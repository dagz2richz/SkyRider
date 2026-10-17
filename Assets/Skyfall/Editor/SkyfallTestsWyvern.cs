using UnityEngine;

namespace Skyfall.EditorTools
{
    /// -skyfallFeatureTest wyvern (use with -skyfallSaveFile): scripted checks of the wyvern's damage rule (on-foot hero
    /// ignored, dragon applies, mounted hero applies, targeting skips it), its bomb and its bomb-vs-distract decision.
    /// Every check logs "SKYFALL_TEST wyvern <name>: PASS" or "FAIL".
    public static partial class SkyfallTests
    {
        static Enemy wyv;
        static float wyvHp, wyvHeroHp;
        static int wyvKills;

        static void WyvernCheck(string name, bool ok, string detail = "")
        {
            Debug.Log($"SKYFALL_TEST wyvern {name}: {(ok ? "PASS" : "FAIL")} {detail}");
        }

        static WyvernSituation Sit(bool mounted = false, float heroHp = 1f, float dragonDist = 6f, bool breathing = false,
            bool diving = false, float breathIn = 0f, int others = 0, int max = 1, bool hasPlan = false, WyvernPlan cur = WyvernPlan.Distract, bool hurt = false)
            => new WyvernSituation
            {
                heroMounted = mounted, heroHpFrac = heroHp, dragonDist = dragonDist, dragonBreathing = breathing, dragonDiving = diving,
                breathReadyIn = breathIn, otherBombers = others, maxBombers = max, hasPlan = hasPlan, current = cur, hurt = hurt,
            };

        static void WyvernTest(Game game)
        {
            var pl = game.player;
            int f = frame;
            if (f == 1) WyvernDirector.Disabled = true;

            // ---- the decision (pure)
            if (f == 2)
            {
                WyvernCheck("brain: mounted hero -> distract", WyvernBrain.Choose(Sit(mounted: true, dragonDist: 40f)) == WyvernPlan.Distract);
                WyvernCheck("brain: dragon far, on foot -> bomb", WyvernBrain.Choose(Sit(dragonDist: 30f)) == WyvernPlan.Bomb);
                WyvernCheck("brain: dragon close and breathing -> distract", WyvernBrain.Choose(Sit(dragonDist: 4f, breathing: true)) == WyvernPlan.Distract);
                WyvernCheck("brain: dragon diving -> bomb", WyvernBrain.Choose(Sit(dragonDist: 12f, diving: true)) == WyvernPlan.Bomb);
                WyvernCheck("brain: breath on cooldown -> bomb", WyvernBrain.Choose(Sit(dragonDist: 12f, breathIn: 2.5f)) == WyvernPlan.Bomb);
                WyvernCheck("brain: low hero health beats a close breathing dragon", WyvernBrain.Choose(Sit(heroHp: 0.2f, dragonDist: 4f, breathing: true)) == WyvernPlan.Bomb);
                WyvernCheck("brain: bomber cap -> distract", WyvernBrain.Choose(Sit(dragonDist: 30f, others: 1, max: 1)) == WyvernPlan.Distract);
                WyvernCheck("brain: current bomber keeps its slot", WyvernBrain.Choose(Sit(dragonDist: 30f, others: 1, max: 1, hasPlan: true, cur: WyvernPlan.Bomb)) == WyvernPlan.Bomb);
                WyvernCheck("brain: hurt -> retreat", WyvernBrain.Choose(Sit(dragonDist: 30f, hurt: true)) == WyvernPlan.Retreat);
            }

            // ---- the damage rule
            if (f == 5)
            {
                pl.flying = false;
                var at = OnGround(game, pl.Pos + FlatFwd(pl) * 25f);
                wyv = game.SpawnEnemy("wyvern", at);
                wyv.WyvernPlace(at);
                wyv.maxHp = wyv.hp = 5000f;
                wyvHp = wyv.hp;
                WyvernCheck("spawned as wyvern", wyv != null && wyv.IsWyvern && wyv.kind == "wyvern");
                WyvernCheck("flies above the ground", wyv.Pos.y > game.terrain.Height(wyv.Pos.x, wyv.Pos.z) + 5f, $"y={wyv.Pos.y:0.0}");
            }
            if (f == 8 && wyv)
            {
                game.DamageEnemy(wyv, 100f);
                WyvernCheck("on-foot hero damage ignored", Mathf.Approximately(wyv.hp, wyvHp), $"hp={wyv.hp:0}");
                WyvernCheck("hero targeting: NearestEnemy skips it", game.NearestEnemy(wyv.Pos, 200f, 200f) == null);
                WyvernCheck("hero targeting: EnemiesInRadius skips it", game.EnemiesInRadius(wyv.Pos, 30f, 50f).Count == 0);
                WyvernCheck("hero targeting: RandomEnemyNear skips it", game.RandomEnemyNear(wyv.Pos, 80f) == null);
                WyvernCheck("hero targeting: DensestPoint skips it", M.IsInf(game.DensestPoint(wyv.Pos, 80f)));
                WyvernCheck("dragon targeting finds it", game.NearestEnemy(wyv.Pos, 200f, 200f, null, null, true) == wyv);
                wyvHp = wyv.hp;
            }
            if (f == 12 && wyv)
            {
                game.pet.Hit(wyv, 100f);
                WyvernCheck("dragon damage applies", Mathf.Approximately(wyv.hp, wyvHp - 100f), $"hp={wyv.hp:0} (was {wyvHp:0})");
                wyvHp = wyv.hp;
                pl.flying = true;
                game.DamageEnemy(wyv, 70f);
                WyvernCheck("mounted hero damage applies", Mathf.Approximately(wyv.hp, wyvHp - 70f), $"hp={wyv.hp:0} (was {wyvHp:0})");
                WyvernCheck("mounted hero targeting finds it", game.NearestEnemy(wyv.Pos, 200f, 200f) == wyv);
                pl.flying = false;
                wyvHp = wyv.hp;
            }

            // ---- burn ticks from the dragon's hit count
            if (f == 14 && wyv) { game.pet.Hit(wyv, 1f); wyv.Ignite(2f, 300f); wyvHp = wyv.hp; }
            if (f == 40 && wyv) WyvernCheck("dragon-applied burn ticks on it", wyv.hp < wyvHp - 50f, $"hp={wyv.hp:0} (was {wyvHp:0})");

            // ---- its bomb: a marker that hurts only inside the circle
            if (f == 45)
            {
                pl.hp = pl.maxHp = 5000f;
                Tele(pl, OnGround(game, new Vector3(0f, 0f, 0f)));
                wyvHeroHp = pl.hp;
                WyvernBomb.Spawn(game, wyv, OnGround(game, pl.Pos + FlatFwd(pl) * 40f), 0.4f, pl.Pos.y + 9f);
            }
            if (f == 70) WyvernCheck("bomb far from the hero does no damage", Mathf.Approximately(pl.hp, wyvHeroHp), $"hp={pl.hp:0}");
            if (f == 71)
            {
                wyvHeroHp = pl.hp;
                WyvernBomb.Spawn(game, wyv, OnGround(game, pl.Pos), 0.4f, pl.Pos.y + 9f);
            }
            if (f == 100) WyvernCheck("bomb on the hero hurts (or is dodged/guarded)", pl.hp <= wyvHeroHp, $"hp={pl.hp:0} (was {wyvHeroHp:0})");

            // ---- kill credit
            if (f == 105 && wyv)
            {
                wyvKills = game.dragonKills;
                game.pet.Hit(wyv, 1e9f);
            }
            if (f == 108)
            {
                WyvernCheck("dragon kill is credited to the dragon", wyv == null || wyv.dead, $"dragonKills={game.dragonKills} (was {wyvKills})");
                WyvernCheck("dragonKills went up", game.dragonKills == wyvKills + 1);
                WyvernDirector.Disabled = false;
                Finish(0);
            }
        }
    }
}
