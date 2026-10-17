using System.Reflection;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// -skyfallFeatureTest <mode> (use with -skyfallSaveFile): scripted checks and photographs for power-ups, the Q lockout,
    /// flight regen, red enemy explosions and dragon-colored abilities. Modes: dragonview, fx, logic.
    public static partial class SkyfallTests
    {
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        static void SnapAt(string name, Vector3 camPos, Vector3 look)
        {
            var ct = Game.I.player.cam.transform;
            ct.position = camPos;
            ct.LookAt(look);
            Snap(name);
        }

        static void FeatureTest(Game game, string mode)
        {
            var pl = game.player;
            pl.hp = pl.maxHp = 99999f;
            typeof(Game).GetField("spawnAccum", NP).SetValue(game, -1e9f);
            if (frame == 1) { game.timeElapsed = 10f; ClearEnemies(game); }
            if (mode == "dragonview") DragonView(game);
            else if (mode == "fx") FxView(game);
            else if (mode == "logic") LogicTest(game);
            else if (mode == "vfxcmp") VfxCompare(game);
            else if (mode == "pickups") PickupView(game);
            else if (mode == "gamepad") GamepadTest(game);
            else if (mode == "padprobe") PadProbe(game);
        }

        static Vector3 FlatFwd(Player pl) { var d = pl.CamFwd; d.y = 0; return d.normalized; }

        static Vector3 OnGround(Game g, Vector3 p) { p.y = g.terrain.Height(p.x, p.z); return p; }

        static void Crowd(Game g, Vector3 center, int n, string kind = "runt")
        {
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.399f, r = 0.8f + 2.6f * Mathf.Sqrt(i / (float)Mathf.Max(1, n));
                SpawnFixed(g, kind, center + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), true);
            }
        }

        /// A side view of the dragon pet, for comparing colors before and after.
        static void DragonView(Game game)
        {
            if (frame == 90 || frame == 150) DragonSnap(game);
            if (frame == 151) Finish(0);
        }

        static void DragonSnap(Game game, string suffix = "")
        {
            var pet = game.pet.transform;
            var f = -pet.forward;
            f.y = 0;
            var p = game.pet.Pos + f.normalized * 6.5f + pet.right * 3f + Vector3.up * 2.5f;
            SnapAt($"feat_dragon_{game.pet.dragonId}{suffix}", p, game.pet.Pos + Vector3.up * 1.8f);
        }

        /// The three power-up models side by side, outside the magnet radius.
        static void PickupView(Game game)
        {
            var pl = game.player;
            var d = FlatFwd(pl);
            var side = new Vector3(-d.z, 0, d.x);
            if (frame == 1) Tele(pl, OnGround(game, Vector3.zero));
            var at = pl.Pos + d * 11f;
            if (frame == 20)
                for (int i = 0; i < 3; i++)
                    game.DropPowerUp(Data.POWERUP_ORDER[i], OnGround(game, at + side * ((i - 1) * 2.6f)));
            foreach (int k in new[] { 30, 45, 60 })
                if (frame == k) SnapAt($"feat_pickups_{k}", at - d * 5f + Vector3.up * 2.2f, at + Vector3.up * 1.3f);
            if (frame == 62) SnapAt("feat_pickups_wide", pl.Pos - d * 3f + Vector3.up * 4f, at + Vector3.up * 1f);
            if (frame == 70) Finish(0);
        }

        /// Untinted effects (back row) next to their tinted versions (front row), photographed a few frames after they start.
        static void VfxCompare(Game game)
        {
            var pl = game.player;
            var d = FlatFwd(pl);
            var side = new Vector3(-d.z, 0, d.x);
            if (frame == 1) Tele(pl, OnGround(game, Vector3.zero));
            var deepRed = Data.DRAGONS["red"].breath;
            var blue = Data.DRAGONS["blue"].breath;
            var list = new (string name, float scale, Color? tint)[]
            {
                ("explosion", 1.4f, null), ("explosion", 1.4f, blue), ("red_explosion", 1.4f, null), ("red_explosion", 1.4f, deepRed),
                ("dust_puff", 1f, null), ("dust_puff", 1f, FX.EnemyRed), ("ground_explosion", 1f, null), ("ground_explosion", 1f, FX.EnemyRed),
                ("stones_hit", 1f, null), ("stones_hit", 1f, FX.EnemyRed), ("hit_fire", 1.5f, null), ("hit_fire", 1.5f, blue),
            };
            for (int pair = 0; pair < list.Length / 2; pair++)
            {
                int f0 = 40 + pair * 40;
                if (frame == f0)
                    for (int k = 0; k < 2; k++)
                    {
                        var it = list[pair * 2 + k];
                        var p = OnGround(game, pl.Pos + d * 12f + side * ((k - 0.5f) * 5f));
                        FX.Vfx(it.name, p, it.scale, 3f, null, 0f, it.tint);
                    }
                foreach (int k in new[] { 8, 20 })
                    if (frame == f0 + k) SnapAt($"feat_vfxcmp_{list[pair * 2].name}_{k}", pl.Pos + d * 2f + Vector3.up * 3f, pl.Pos + d * 12f + Vector3.up * 0.8f);
            }
            if (frame == 40 + 40 * (list.Length / 2)) Finish(0);
        }

        // ------------------------------------------------------------------ visuals: red enemy effects, dragon-colored abilities

        static void FxView(Game game)
        {
            var pl = game.player;
            var tr = game.terrain;
            string dr = game.pet.dragonId;
            pl.flightStamina = 99f;
            var d = FlatFwd(pl);
            if (frame == 1) Tele(pl, OnGround(game, new Vector3(0, 0, 0)));
            if (frame == 20) { game.pet.transform.position = pl.Pos + new Vector3(0, 5, 0) + d * 3f; }
            if (frame == 40) DragonSnap(game, "_fx");

            // 1. death puff: kill a small cluster 9 m ahead.
            Vector3 c1 = OnGround(game, pl.Pos + d * 9f);
            if (frame == 60) { ClearEnemies(game); Crowd(game, c1, 4); }
            if (frame == 70)
            {
                foreach (var e in game.enemies.ToArray()) game.DamageEnemy(e, 1e9f);
                Debug.Log($"SKYFALL_TEST fx death: kills={game.kills}");
            }
            if (frame == 76) SnapAt($"feat_{dr}_1_death_a", c1 - d * 2f + new Vector3(-d.z, 0, d.x) * 5f + Vector3.up * 2.5f, c1 + Vector3.up * 1.2f);
            if (frame == 96) SnapAt($"feat_{dr}_1_death_b", c1 - d * 2f + new Vector3(-d.z, 0, d.x) * 5f + Vector3.up * 2.5f, c1 + Vector3.up * 1.2f);

            // 2. boulder impact on the same spot.
            if (frame == 120)
                Boulder.Spawn(game, c1 + new Vector3(0, 14f, 0) - d * 3f, c1, 0.7f, 1f);
            if (frame == 120 + 45) SnapAt($"feat_{dr}_2_boulder_a", c1 - d * 2f + new Vector3(-d.z, 0, d.x) * 5f + Vector3.up * 2.5f, c1 + Vector3.up * 1f);
            if (frame == 120 + 55) SnapAt($"feat_{dr}_2_boulder_b", c1 - d * 2f + new Vector3(-d.z, 0, d.x) * 5f + Vector3.up * 2.5f, c1 + Vector3.up * 1f);

            // 3. boss slam on the player's spot (the enemy ground explosion).
            if (frame == 200)
            {
                var b = Enemy.MakeBoss(game, Data.MINI_BOSS, 1f, 1f);
                b.transform.SetParent(game.transform, false);
                b.Pos = OnGround(game, pl.Pos + d * 14f);
                b.stunT = 1e6f;
                game.enemies.Add(b);
                typeof(Enemy).GetMethod("UseAbility", NP).Invoke(b, new object[] { "slam", pl, -d });
                SnapAt($"feat_{dr}_3_slam_tele", pl.Pos - d * 4f + Vector3.up * 5f, pl.Pos);
            }
            if (frame == 200 + 78) SnapAt($"feat_{dr}_3_slam_a", pl.Pos - d * 4f + Vector3.up * 5f, pl.Pos);
            if (frame == 200 + 90) SnapAt($"feat_{dr}_3_slam_b", pl.Pos - d * 4f + Vector3.up * 5f, pl.Pos);

            // 4. dragon dive impact on a crowd.
            Vector3 c2 = OnGround(game, pl.Pos + d * 16f);
            if (frame == 330) { ClearEnemies(game); Crowd(game, c2, 8); }
            if (frame == 335)
                typeof(DragonPet).GetMethod("StartDive", NP).Invoke(game.pet, new object[] { c2 });
            if (frame == 335 + 24) SnapAt($"feat_{dr}_4_dive_tele", pl.Pos - d * 3f + Vector3.up * 5f, c2 + Vector3.up * 3f);
            if (frame == 335 + 49) SnapAt($"feat_{dr}_4_dive_a", pl.Pos - d * 3f + Vector3.up * 5f, c2 + Vector3.up * 1.5f);
            if (frame == 335 + 58) SnapAt($"feat_{dr}_4_dive_b", pl.Pos - d * 3f + Vector3.up * 5f, c2 + Vector3.up * 1.5f);

            // 5. the dragon's signature (E) on a crowd, while the hero stays on foot.
            if (frame == 470) { ClearEnemies(game); Crowd(game, c2, 8); }
            if (frame == 480)
            {
                game.pet.RideCommand();
                Debug.Log($"SKYFALL_TEST fx signature fired for {dr}");
            }
            foreach (int k in new[] { 12, 28, 40, 52, 70 })
                if (frame == 480 + k) SnapAt($"feat_{dr}_5_sig_{k}", pl.Pos - d * 3f + Vector3.up * 5f, c2 + Vector3.up * 2f);
            if (frame == 480 + 90)
            {
                Finish(0);
            }
        }

        // ------------------------------------------------------------------ logic: power-ups, Q lockout, flight regen

        static float baseMove, baseDmg;
        static int dropsBefore;

        static string ToastNow(Game g)
        {
            var t = typeof(Hud);
            return $"'{t.GetField("toastText", NP).GetValue(g.hud)}' (age {t.GetField("toastT", NP).GetValue(g.hud):0.00}s)";
        }

        static string PU(Player p) =>
            $"move={p.MoveMult:0.00} dmgMult={p.DamageMult:0.000} hitDmg={p.WeaponBaseDamage * p.WeaponDamageMult:0.0} magnet={p.MagnetRadius:0.0}m " +
            $"left[speed={p.PowerLeft("speed"):0.0} damage={p.PowerLeft("damage"):0.0} magnet={p.PowerLeft("magnet"):0.0}]";

        static void LogicTest(Game game)
        {
            var pl = game.player;
            var tr = game.terrain;
            int f = frame;
            var d = FlatFwd(pl);
            if (f == 1) Tele(pl, OnGround(game, Vector3.zero));
            if (f == 2)
            {
                // Final boss health: stage 2 is double and stage 3 triple what the stage's own multiplier alone would give.
                for (int s = 0; s < Data.STAGE_COUNT; s++)
                {
                    var st = Data.STAGES[s];
                    float plain = Data.BOSSES[Data.FINAL_BOSS].hp * Data.BOSS_STAT_MULT * st.enemyMult;
                    float hp = Data.BOSSES[Data.FINAL_BOSS].hp * Data.FinalBossHpMult(st);
                    Debug.Log($"SKYFALL_TEST finalboss stage {s + 1}: hp={hp:0} (x{hp / plain:0.0} of {plain:0}) model='{st.finalBossModel}' ability='{st.finalBossAbility}'");
                    if (Mathf.Abs(hp / plain - (s == 0 ? 1f : s + 1f)) > 0.001f) Debug.LogError($"SKYFALL_TEST finalboss stage {s + 1}: wrong health multiplier");
                    string tiers = "";
                    for (int t = 0; t < Data.KNOCK_DIST.Length; t++) tiers += $" {Data.KnockDistance(t, st.enemyMult):0.0}m";
                    Debug.Log($"SKYFALL_TEST knockback stage {s + 1} (regular, brute, hulk, mini, final):{tiers}");
                }
            }
            if (f < 1700 && f > 3 && !GameInput.TestMove.HasValue)
            {
                // Keep the hero grounded and still for the power-up part.
                pl.velocity = new Vector3(0, pl.velocity.y, 0);
            }

            // ---- power-ups: collection and effects
            if (f == 5)
            {
                baseMove = pl.MoveMult; baseDmg = pl.WeaponBaseDamage * pl.WeaponDamageMult;
                Debug.Log($"SKYFALL_TEST pu base: {PU(pl)} dropChance={pl.PowerUpDropChance:0.0000}");
            }
            if (f == 50)
            {
                game.DropPowerUp("speed", OnGround(game, pl.Pos + d * 3f));
                game.DropPowerUp("damage", OnGround(game, pl.Pos + new Vector3(-d.z, 0, d.x) * 3f));
                game.DropPowerUp("magnet", OnGround(game, pl.Pos - new Vector3(-d.z, 0, d.x) * 3f));
                Debug.Log($"SKYFALL_TEST pu spawned: pickups in world={Object.FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude).Length}");
                pl.camYaw = Mathf.Atan2(-d.x, -d.z) + 0.5f; pl.camDistTarget = 7f; pl.camPitch = 0.3f;
            }
            if (f == 52) SnapAt("feat_pu_world", pl.Pos - d * 2f + Vector3.up * 3.2f + new Vector3(-d.z, 0, d.x) * 2f, pl.Pos + d * 2f + Vector3.up * 1.2f);
            if (f == 55) SnapAt("feat_pu_world2", pl.Pos + d * 6f + Vector3.up * 2f, pl.Pos + d * 3f + Vector3.up * 1.2f);
            if (f == 110)
            {
                Debug.Log($"SKYFALL_TEST pu collected: pickups left={Object.FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude).Length}  {PU(pl)}");
                Debug.Log($"SKYFALL_TEST pu toast: {ToastNow(game)}");
                Debug.Log($"SKYFALL_TEST pu effect: moveBase={baseMove:0.00} -> {pl.MoveMult:0.00} (x{pl.MoveMult / baseMove:0.00}), hit damage {baseDmg:0.0} -> {pl.WeaponBaseDamage * pl.WeaponDamageMult:0.0} (x{pl.WeaponBaseDamage * pl.WeaponDamageMult / baseDmg:0.00})");
            }
            // Move speed under the Speed power-up: actual ground speed.
            if (f == 160) GameInput.TestMove = new Vector2(0f, -1f);
            if (f == 200) { Debug.Log($"SKYFALL_TEST pu speed measured: hspeed={new Vector2(pl.velocity.x, pl.velocity.z).magnitude:0.0} expected={9.5f * pl.MoveMult:0.0}"); GameInput.TestMove = null; }
            // Refresh: pick up another Speed 8 s in (frame 50 + ~60 collected, so 8 s ~ f 570).
            if (f == 480) Debug.Log($"SKYFALL_TEST pu before refresh: {PU(pl)}");
            if (f == 500) game.DropPowerUp("speed", OnGround(game, pl.Pos + d * 2f));
            if (f == 560) Debug.Log($"SKYFALL_TEST pu after refresh: {PU(pl)}");
            // Expiry: damage/magnet run out ~15 s after f~60 (f~960); speed ~15 s after the refresh (f~1420).
            foreach (int ff in new[] { 930, 960, 1000, 1400, 1430, 1450 })
                if (f == ff) Debug.Log($"SKYFALL_TEST pu t={f}: {PU(pl)}");
            if (f == 1010)
            {
            }

            // ---- drop chance by Utility rank, and an empirical kill test
            if (f == 1460)
            {
                for (int r = 0; r <= 5; r++)
                {
                    pl.relics["utility"] = r;
                    Debug.Log($"SKYFALL_TEST pu chance utility rank {r}: {pl.PowerUpDropChance * 100f:0.0}%  ({pl.PowerUpDropChance:0.0000})");
                }
                pl.relics["utility"] = 100;   // 10.1% for the empirical run
                Debug.Log($"SKYFALL_TEST pu empirical: utility rank 100 -> chance {pl.PowerUpDropChance:0.0000}; killing 300 runts");
                dropsBefore = game.powerUpDrops;
                Crowd(game, OnGround(game, pl.Pos + d * 12f), 300);
            }
            if (f == 1465)
            {
                int n = 0;
                foreach (var e in game.enemies.ToArray()) { game.DamageEnemy(e, 1e9f, Vector3.zero, 0f, false); n++; }
                int drops = game.powerUpDrops - dropsBefore;
                Debug.Log($"SKYFALL_TEST pu empirical: killed {n}, drops={drops} (expected ~{n * 0.101f:0})");
                pl.relics["utility"] = 0;
                foreach (var p in Object.FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude)) Object.Destroy(p.gameObject);
            }

            // ---- bosses drop two each
            if (f == 1480)
            {
                typeof(Game).GetMethod("SpawnBoss", NP).Invoke(game, new object[] { Data.MINI_BOSS });
                dropsBefore = game.powerUpDrops;
            }
            if (f == 1485)
            {
                var b = game.bosses[0];
                b.Pos = OnGround(game, pl.Pos + d * 60f);
                game.DamageEnemy(b, 1e9f);
                int pickups = Object.FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude).Length;
                Debug.Log($"SKYFALL_TEST pu mini boss killed: drops={game.powerUpDrops - dropsBefore} (bossPowerUps={game.bossPowerUps}) pickups in world={pickups}");
                dropsBefore = game.powerUpDrops;
                game.BeginFinalBattle();
            }
            if (f == 1490)
            {
                var b = game.bosses[0];
                game.DamageEnemy(b, 1e9f);
                int pickups = Object.FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude).Length;
                Debug.Log($"SKYFALL_TEST pu final boss killed: drops={game.powerUpDrops - dropsBefore} (bossPowerUps={game.bossPowerUps} total) pickups in world={pickups}");
            }
            if (f == 1495)
            {
                foreach (var p in Object.FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude)) Object.Destroy(p.gameObject);
                var cap = Object.FindFirstObjectByType<DragonCaptive>();
                if (cap) cap.hold = true;
                ClearEnemies(game);
                Tele(pl, OnGround(game, Vector3.zero));
                pl.flightStamina = pl.MaxFlight;
            }

            // ---- Remounting after E on the dragon (1 s on solid ground), and flight regen from the moment you land.
            const int Q0 = 1600;
            if (f == Q0)
            {
                GameInput.TestFlyPress = true;   // Q: take flight
            }
            if (f == Q0 + 40)
            {
                Debug.Log($"SKYFALL_TEST q mounted: flying={pl.flying} stamina={pl.flightStamina:0.00}/{pl.MaxFlight:0.0}");
                GameInput.TestAbilityPress = true;   // E while mounted: leap off and slam
            }
            int e0 = Q0 + 41;   // frame of the E effect
            if (f == e0 + 1) Debug.Log($"SKYFALL_TEST q after E: flying={pl.flying} remountWait={pl.RemountWait:0.00}s onGround={pl.onGround}");
            if (f == e0 + 60)   // 1 s after E
            {
                GameInput.TestFlyPress = true;
            }
            if (f == e0 + 62) Debug.Log($"SKYFALL_TEST q attempt at ~1s: flying={pl.flying} remountWait={pl.RemountWait:0.00}s toast={ToastNow(game)} onGround={pl.Pos.y - tr.Height(pl.Pos.x, pl.Pos.z) < 0.2f}");
            if (f == e0 + 130)   // ~2.2 s after E
            {
                GameInput.TestFlyPress = true;
            }
            if (f == e0 + 132) Debug.Log($"SKYFALL_TEST q attempt at ~2.2s: flying={pl.flying} remountWait={pl.RemountWait:0.00}s toast={ToastNow(game)}");
            if (f == e0 + 210)   // 3.5 s after E
            {
                Debug.Log($"SKYFALL_TEST q before attempt at 3.5s: remountWait={pl.RemountWait:0.00}s stamina={pl.flightStamina:0.00}");
                GameInput.TestFlyPress = true;
            }
            if (f == e0 + 214) Debug.Log($"SKYFALL_TEST q attempt at ~3.5s: flying={pl.flying} remountWait={pl.RemountWait:0.00}s toast={ToastNow(game)}");

            // Flight regen: fly for ~1 s (stamina drops), drop off with Q, then log the stamina every 0.5 s.
            int r0 = e0 + 214 + 60;
            if (f == r0)
            {
                Debug.Log($"SKYFALL_TEST flight: stamina after ~1 s of flight = {pl.flightStamina:0.00} flying={pl.flying}");
                GameInput.TestFlyPress = true;   // Q again: drop off
            }
            if (f > r0 && f <= r0 + 60 * 8 && (f - r0) % 30 == 0)
                Debug.Log($"SKYFALL_TEST flight regen +{(f - r0) / 60f:0.0}s: stamina={pl.flightStamina:0.00} remountWait={pl.RemountWait:0.00} flying={pl.flying} onGround={pl.Pos.y - tr.Height(pl.Pos.x, pl.Pos.z) < 0.2f}");
            // A new flight resets the delay.
            int n0 = r0 + 60 * 8;
            if (f == n0) GameInput.TestFlyPress = true;
            if (f == n0 + 30) { Debug.Log($"SKYFALL_TEST flight 2: flying={pl.flying} stamina={pl.flightStamina:0.00} remountWait={pl.RemountWait:0.00}"); GameInput.TestFlyPress = true; }
            if (f > n0 + 30 && f <= n0 + 30 + 60 * 5 && (f - n0 - 30) % 30 == 0)
                Debug.Log($"SKYFALL_TEST flight2 regen +{(f - n0 - 30) / 60f:0.0}s: stamina={pl.flightStamina:0.00} remountWait={pl.RemountWait:0.00} flying={pl.flying}");
            if (f == n0 + 30 + 60 * 5 + 5)
            {
                LogStats(game, "end");
                Finish(0);
            }
        }
    }
}
