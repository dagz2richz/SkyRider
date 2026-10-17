using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// The rider's magic, all automatic: the chosen spell casts itself whenever there's mana and a target,
    /// and the Lightning Storm / Blood Aura passives work on their own.
    public class SpellBook : MonoBehaviour
    {
        static readonly Color SHOCK_COLOR = new Color(0.65f, 0.75f, 1f);

        public Game game;
        public Player player;
        public float cooldown = 1f;
        float stormTimer = 2f;

        // Blood Aura
        Transform auraRing, auraDisc;
        Material auraDiscMat;
        float auraDiscAlpha = 0.08f;
        float auraTick;
        float auraSpin;

        public void Init(Game g, Player p)
        {
            game = g;
            player = p;
            var ring = FX.Part(transform, MeshGen.Torus(0.965f, 1f, 64, 4), FX.Glow(new Color(0.85f, 0.05f, 0.1f, 0.8f), 1.4f), Vector3.zero, Vector3.one);
            FX.NoShadow(ring);
            auraRing = ring.transform;
            auraDiscMat = FX.FadeMat(new Color(0.7f, 0f, 0.05f, 0.08f), false);
            var disc = FX.Part(transform, MeshGen.Disc(), auraDiscMat, Vector3.zero, Vector3.one);
            FX.NoShadow(disc);
            auraDisc = disc.transform;
            for (int i = 0; i < 8; i++)
            {
                float a = M.TAU * i / 8f;
                var drop = FX.Part(auraRing, MeshGen.Sphere(6), FX.Glow(new Color(0.9f, 0.08f, 0.12f), 2f), new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)), new Vector3(0.05f, 0.08f, 0.05f));
                FX.NoShadow(drop);
            }
            auraRing.gameObject.SetActive(false);
            auraDisc.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (auraDiscMat) Destroy(auraDiscMat);
        }

        Data.SpellDef Spell => Data.SPELLS[player.spellId];

        public int Level => player.spells.TryGetValue(player.spellId, out int v) ? v : 1;

        public float Cost => Spell.cost * (1f - 0.04f * (Level - 1)) * player.SpellCostMult;

        /// True once the spell has reached its evolved form (level 5).
        public bool Evolved => Level >= Data.EVOLVE_LEVEL;

        float CooldownTime => Spell.cd * player.CooldownMult;

        public float CooldownFrac => Mathf.Clamp01(cooldown / Mathf.Max(CooldownTime, 0.01f));

        int Lv(string id) => player.spells.TryGetValue(id, out int v) ? v : 1;

        public void Tick(float delta)
        {
            if (player == null || !game.running) return;
            cooldown = Mathf.Max(0f, cooldown - delta);
            Storm(delta);
            BloodAura(delta);
            if (cooldown <= 0f && player.mana >= Cost) AutoCast();
        }

        void AutoCast()
        {
            int L = Level;
            float area = player.AreaMult;
            float dm = player.SpellDamageMult;
            bool ok = false;
            switch (player.spellId)
            {
                case "gravity_well": ok = GravityWellCast(L, area, dm); break;
                case "frost_bolt": ok = FrostBoltCast(L, area, dm); break;
                case "chain_lightning": ok = Lightning(L, dm); break;
                case "meteor": ok = Meteors(L, area, dm); break;
            }
            if (!ok)
            {
                cooldown = 0.25f;   // no target yet; check again shortly
                return;
            }
            player.mana -= Cost;
            cooldown = CooldownTime;
            player.CastAnim();
        }

        // ---------------------------------------------------------------- passive: Lightning Storm

        void Storm(float delta)
        {
            if (player.passiveId != "lightning_storm") return;
            int L = Lv("lightning_storm");
            stormTimer -= delta;
            if (stormTimer > 0f) return;
            stormTimer = (2.2f - 0.2f * L) * player.CooldownMult;
            var used = new List<Enemy>();
            int hits = 0;
            for (int i = 0; i < 1 + L; i++)
            {
                var e = game.RandomEnemyNear(player.Pos, 16f * player.AreaMult);
                if (e == null || used.Contains(e)) continue;
                used.Add(e);
                var top = e.Pos + new Vector3(M.RandRange(-2f, 2f), 24f, M.RandRange(-2f, 2f));
                var hitAt = e.Pos + new Vector3(0, e.size, 0);
                FX.Bolt(top, hitAt, new Color(0.8f, 0.85f, 1f), 0.3f, 0.22f);
                FX.Vfx("electro_hit", e.Pos + new Vector3(0, e.size, 0), 0.8f, 1.5f, null, 0f);
                FX.Flash(hitAt, 1.6f, new Color(0.7f, 0.8f, 1f, 0.7f), 0.2f);
                game.DamageEnemy(e, (14f + 7f * L) * player.DamageMult, Vector3.zero, 0f, true, SHOCK_COLOR);
                if (M.RandF() < 0.55f + 0.07f * L) e.Shock(1f);
                hits++;
            }
            if (hits > 0)
            {
                Sfx.Play("zap", -3f, 0.2f);
                player.AddMana(hits * (2f + 0.5f * L));
            }
        }

        // ---------------------------------------------------------------- passive: Blood Aura

        void BloodAura(float delta)
        {
            bool on = player.passiveId == "blood_aura";
            if (auraRing.gameObject.activeSelf != on)
            {
                auraRing.gameObject.SetActive(on);
                auraDisc.gameObject.SetActive(on);
            }
            if (!on) return;
            int L = Lv("blood_aura");
            float rad = (3.4f + 0.4f * L) * player.AreaMult;
            var basePos = player.Pos;
            basePos.y = game.terrain.SurfaceHeight(basePos.x, basePos.z, player.Pos.y) + 0.12f;
            if (player.flying) basePos = player.Pos + new Vector3(0, 0.2f, 0);
            auraSpin -= delta;
            auraRing.SetPositionAndRotation(basePos, Quaternion.Euler(0, auraSpin * Mathf.Rad2Deg, 0));
            auraRing.localScale = new Vector3(rad, rad, rad);
            auraDisc.position = basePos - new Vector3(0, 0.05f, 0);
            auraDisc.localScale = new Vector3(rad, 1f, rad);
            auraDiscAlpha = Mathf.MoveTowards(auraDiscAlpha, 0.08f, delta * 0.6f);
            FX.SetAlpha(auraDiscMat, auraDiscAlpha);
            auraTick -= delta;
            if (auraTick > 0f) return;
            auraTick = 0.5f;
            float dmg = (6f + 3.5f * L) * player.DamageMult;
            float total = 0f;
            foreach (var e in game.EnemiesInRadius(player.Pos, rad, 3.5f))
            {
                game.DamageEnemy(e, dmg, Vector3.zero, 0f, true, new Color(1f, 0.3f, 0.35f));
                total += dmg;
                if (M.RandF() < 0.3f) FX.Bolt(e.Pos + new Vector3(0, e.size, 0), player.Center, new Color(0.85f, 0.05f, 0.1f, 0.8f), 0.06f, 0.25f, 3);
            }
            if (total > 0f)
            {
                // Life steal in a radius.
                float heal = Mathf.Min(total * (0.048f + 0.012f * L), 2.4f + 1.2f * L) * player.HealMult;
                player.hp = Mathf.Min(player.maxHp, player.hp + heal);
                player.AddMana(Mathf.Min(total * 0.05f, 2f + L));
                auraDiscAlpha = 0.25f;
            }
        }

        // ---------------------------------------------------------------- spells

        bool GravityWellCast(int L, float area, float dm)
        {
            var at = game.DensestPoint(player.Pos, 24f);
            if (M.IsInf(at)) return false;
            SpawnWell(at, L, area, dm);
            if (L >= Data.SPELL_FINAL_LEVEL)
            {
                var at2 = game.DensestPoint(player.Pos, 24f, at, 10f);
                if (!M.IsInf(at2)) SpawnWell(at2, L, area, dm);
            }
            Sfx.Play("whoosh", -2f, 0.05f);
            return true;
        }

        void SpawnWell(Vector3 at, int L, float area, float dm)
        {
            bool evo = L >= Data.EVOLVE_LEVEL;
            GravityWell.Spawn(this, at + new Vector3(0, 1.2f, 0),
                radius: (7f + (L >= 3 ? 1.5f : 0f)) * area * (evo ? 1.5f : 1f),
                pull: (7f + 2f * L) * (evo ? 2f : 1f),
                life: 2.5f + (L >= 3 ? 0.8f : 0f),
                tickDamage: (8f + 3f * L) * dm,
                finalDamage: (60f + 25f * L + (evo ? 60f : 0f)) * dm,
                evolved: evo);
        }

        bool FrostBoltCast(int L, float area, float dm)
        {
            var t = game.NearestEnemy(player.Center, 28f, 20f);
            if (t == null) return false;
            var from = player.HandPosition;
            bool evo = L >= Data.EVOLVE_LEVEL;
            FrostBolt.Spawn(this, from, (t.Pos + new Vector3(0, t.size, 0) - from).normalized * (evo ? 30f : 36f),
                damage: (30f + 12f * L) * dm * (evo ? 1.6f : 1f),
                shards: 6 + (L >= 2 ? 2 : 0) + (L >= 4 ? 2 : 0),
                shardDamage: (12f + 5f * L) * dm,
                freezeChance: 0.2f + (L >= 3 ? 0.15f : 0f) + 0.03f * L,
                pierce: L >= Data.SPELL_FINAL_LEVEL,
                radius: 2.2f * area * (evo ? 1.6f : 1f),
                evolved: evo);
            Sfx.Play("frost", -8f, 0.1f);
            return true;
        }

        bool Lightning(int L, float dm)
        {
            var first = game.NearestEnemy(player.Center, 24f, 20f);
            if (first == null) return false;
            var hit = new List<Enemy>();
            Chain(first, L, dm, hit);
            if (L >= Data.SPELL_FINAL_LEVEL)
            {
                var second = game.NearestEnemy(first.Pos, 14f, 10f, null, hit);
                if (second) Chain(second, L, dm, hit);
            }
            Sfx.Play("zap", 0f, 0.15f);
            return true;
        }

        void Chain(Enemy first, int L, float dm, List<Enemy> hit)
        {
            bool evo = L >= Data.EVOLVE_LEVEL;
            float dmg = (26f + 10f * L) * dm * (evo ? 1.4f : 1f);
            float shockChance = 0.3f + (L >= 3 ? 0.2f : 0f) + 0.04f * L;
            var col = evo ? Data.SPELLS["chain_lightning"].evolvedColor : new Color(0.75f, 0.75f, 1f);
            float w = evo ? 0.45f : 0.25f;
            FX.Bolt(player.HandPosition, first.Pos + new Vector3(0, first.size, 0), col, w, 0.2f);
            if (evo) FX.Bolt(player.HandPosition, first.Pos + new Vector3(0, first.size, 0), new Color(1f, 0.9f, 0.5f), w * 0.4f, 0.25f);
            Zap(first, dmg, shockChance);
            hit.Add(first);
            var cur = first;
            int jumps = 2 + (L >= 2 ? 2 : 0) + (L >= 4 ? 2 : 0);
            for (int i = 0; i < jumps; i++)
            {
                var nxt = game.NearestEnemy(cur.Pos, evo ? 12f : 9f, 8f, null, hit);
                if (nxt == null) break;
                FX.Bolt(cur.Pos + new Vector3(0, cur.size, 0), nxt.Pos + new Vector3(0, nxt.size, 0), col, w * 0.75f, 0.2f);
                Zap(nxt, dmg, shockChance);
                hit.Add(nxt);
                cur = nxt;
            }
            FX.Flash(first.Pos + new Vector3(0, 1, 0), evo ? 2.5f : 1.5f, M.WithA(col, 0.8f), 0.2f);
            // Storm Lord: a crackling orb stays where the chain ended and keeps zapping.
            if (evo) StormOrb.Spawn(this, cur.Pos + new Vector3(0, cur.size + 1f, 0), dmg * 0.4f, col);
        }

        void Zap(Enemy e, float dmg, float shockChance)
        {
            game.DamageEnemy(e, dmg, Vector3.zero, 0f, true, SHOCK_COLOR);
            FX.Vfx("hit_lightning", e.Pos + new Vector3(0, e.size, 0), 0.8f, 1.3f, null, 0.02f);
            if (M.RandF() < shockChance) e.Shock(1f);
        }

        bool Meteors(int L, float area, float dm)
        {
            var center = game.DensestPoint(player.Pos, 26f);
            if (M.IsInf(center)) return false;
            int count = 5 + (L >= 2 ? 2 : 0) + (L >= 4 ? 2 : 0);
            bool evo = L >= Data.EVOLVE_LEVEL;
            float rad = (3f + (L >= 3 ? 1f : 0f)) * area;
            float ignite = 0.4f + (L >= 3 ? 0.2f : 0f) + 0.04f * L;
            float dmg = (32f + 14f * L) * dm * (evo ? 2f : 1f);
            for (int i = 0; i < count; i++)
            {
                var at = center + new Vector3(M.RandRange(-6f, 6f), 0, M.RandRange(-6f, 6f)) * area;
                var near = game.RandomEnemyNear(center, 9f * area);
                if (near && M.RandF() < 0.6f) at = near.Pos;
                DropMeteor(at, rad, dmg, i * 0.12f, 1f, ignite, evo);
            }
            if (L >= Data.SPELL_FINAL_LEVEL) DropMeteor(center, rad * 2.2f, dmg * 3f, count * 0.12f + 0.3f, 2.5f, 1f, evo);
            FX.Telegraph(center, 7f * area, 0.6f, evo ? new Color(0.5f, 0.7f, 1f, 0.35f) : new Color(1f, 0.5f, 0.1f, 0.3f));
            Sfx.Play("whoosh", -2f, 0.05f);
            return true;
        }

        void DropMeteor(Vector3 at, float rad, float dmg, float delay, float scaleK, float ignite, bool evolved)
        {
            at.y = game.terrain.SurfaceHeight(at.x, at.z, at.y + 1f);
            Meteor.Spawn(this, at + new Vector3(M.RandRange(-8f, 8f), 32f, M.RandRange(-8f, 8f)), at, rad, dmg, delay, scaleK, ignite, evolved);
        }
    }

    // ==================================================================== spell objects

    public class GravityWell : MonoBehaviour
    {
        SpellBook book;
        float radius = 7f, pull = 8f, tickDamage = 6f, finalDamage = 50f, life = 2.5f, tick;
        bool evolved;
        Transform core, halo, halo2;
        float haloSpin;

        public static void Spawn(SpellBook book, Vector3 pos, float radius, float pull, float life, float tickDamage, float finalDamage, bool evolved = false)
        {
            var w = new GameObject(evolved ? "EventHorizon" : "GravityWell").AddComponent<GravityWell>();
            w.transform.SetParent(book.transform, false);
            w.transform.position = pos;
            w.book = book;
            w.radius = radius;
            w.pull = pull;
            w.life = life;
            w.tickDamage = tickDamage;
            w.finalDamage = finalDamage;
            w.evolved = evolved;
            w.Build();
        }

        void Build()
        {
            // Event Horizon: a black core with a golden accretion disc instead of the violet ring.
            var ringCol = evolved ? new Color(1f, 0.75f, 0.25f, 0.9f) : new Color(0.6f, 0.25f, 1f, 0.8f);
            var floorCol = evolved ? new Color(1f, 0.6f, 0.15f, 0.4f) : new Color(0.5f, 0.2f, 0.9f, 0.35f);
            core = FX.Part(transform, MeshGen.Sphere(16), FX.Lit(new Color(0.02f, 0f, 0.04f), 0.1f), Vector3.zero, Vector3.one * 0.2f).transform;
            var h = FX.Part(transform, MeshGen.Ring(), FX.Glow(ringCol, evolved ? 3f : 2.5f), Vector3.zero, new Vector3(1.6f, 2f, 1.6f) * (evolved ? 1.8f : 1f));
            FX.NoShadow(h);
            halo = h.transform;
            if (evolved)
            {
                var h2 = FX.Part(transform, MeshGen.Torus(0.55f, 1f, 48, 4), FX.Glow(new Color(1f, 0.9f, 0.6f, 0.5f), 2.2f), Vector3.zero, new Vector3(4.5f, 0.3f, 4.5f));
                FX.NoShadow(h2);
                halo2 = h2.transform;
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.7f, 0.3f);
                l.intensity = 3f;
                l.range = radius * 1.5f;
                l.shadows = LightShadows.None;
            }
            var floorRing = FX.Part(transform, MeshGen.Ring(), FX.Glow(floorCol, 1.2f), new Vector3(0, -1f, 0), new Vector3(radius, 0.4f, radius));
            FX.NoShadow(floorRing);
            FX.VfxLoop("magic_circle", transform, new Vector3(0, -1.1f, 0), radius / 3.5f);
            FX.Particles(transform, new FX.ParticleSpec
            {
                amount = evolved ? 120 : 60, lifetime = 0.8f, size = 0.5f, sphereRadius = radius, radialSpeed = evolved ? -16f : -10f, worldSpace = false,
                startColor = M.WithA(ringCol, 0f), midColor = M.WithA(Color.Lerp(ringCol, Color.white, 0.2f), 1f), midAt = 0.4f, endColor = new Color(0.2f, 0f, 0.4f, 0f),
            });
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = book.game;
            life -= delta;
            core.localScale = Vector3.Lerp(core.localScale, Vector3.one * 2.4f, Mathf.Clamp01(delta * 4f));
            haloSpin += delta * 5f;
            halo.localRotation = Quaternion.Euler(Mathf.Sin(life * 3f) * 0.4f * Mathf.Rad2Deg, haloSpin * Mathf.Rad2Deg, 0);
            if (halo2) halo2.localRotation = Quaternion.Euler(0.35f * Mathf.Rad2Deg, -haloSpin * 0.6f * Mathf.Rad2Deg, 0);
            tick -= delta;
            if (tick <= 0f)
            {
                tick = 0.3f;
                foreach (var e in g.EnemiesInRadius(transform.position, radius, 8f))
                {
                    var to = M.Flat(transform.position - e.Pos);
                    if (!e.isBoss) e.knock += to.normalized * pull * 0.7f;
                    g.DamageEnemy(e, tickDamage, Vector3.zero, 0f, false);
                }
            }
            if (life <= 0f)
            {
                var pos = transform.position;
                var col = evolved ? new Color(1f, 0.75f, 0.3f) : new Color(0.7f, 0.3f, 1f);
                Implode(pos, radius * 0.75f, finalDamage, col);
                if (evolved)
                {
                    // Event Horizon implodes a second time a moment later, and everything it crushes is stunned.
                    var gg = g;
                    float r2 = radius * 0.6f, d2 = finalDamage * 0.7f;
                    Tw.After(0.35f, () => { if (gg != null) ImplodeStatic(gg, pos, r2, d2, new Color(1f, 0.9f, 0.6f)); });
                }
                Destroy(gameObject);
            }
        }

        void Implode(Vector3 pos, float r, float dmg, Color col)
        {
            var g = book.game;
            ImplodeStatic(g, pos, r, dmg, col);
            if (evolved)
                foreach (var e in g.EnemiesInRadius(pos, r, 8f))
                    if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 1.5f);
        }

        static void ImplodeStatic(Game g, Vector3 pos, float r, float dmg, Color col)
        {
            if (!FX.Vfx("hit_magic", pos, r / 2.2f, 1.8f, null, 0f, col))
                FX.Flash(pos, r, M.WithA(col, 0.8f), 0.4f);
            FX.Shockwave(pos - new Vector3(0, 1f, 0), r * 1.33f, M.WithA(col, 0.9f), 0.4f);
            Sfx.Play("boom", pos, 0f);
            foreach (var e in g.EnemiesInRadius(pos, r, 8f))
                g.DamageEnemy(e, dmg, Vector3.zero, 0f, true, Color.Lerp(col, Color.white, 0.3f));
        }
    }

    public class FrostBolt : MonoBehaviour
    {
        SpellBook book;
        Vector3 velocity;
        float damage = 30f, radius = 2.2f, shardDamage = 12f, freezeChance = 0.2f, life = 1.4f;
        int shards = 6;
        bool pierce, evolved;

        public static void Spawn(SpellBook book, Vector3 from, Vector3 velocity, float damage, int shards, float shardDamage, float freezeChance, bool pierce, float radius, bool evolved = false)
        {
            var b = new GameObject(evolved ? "GlacialComet" : "FrostBolt").AddComponent<FrostBolt>();
            b.evolved = evolved;
            b.transform.SetParent(book.transform, false);
            b.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
            b.book = book;
            b.velocity = velocity;
            b.damage = damage;
            b.shards = shards;
            b.shardDamage = shardDamage;
            b.freezeChance = freezeChance;
            b.pierce = pierce;
            b.radius = radius;
            if (evolved)
            {
                // Glacial Comet: a big crystalline head with a long white trail and a cold light.
                FX.NoShadow(FX.Part(b.transform, MeshGen.Sphere(14), FX.Glow(new Color(0.85f, 1f, 1f), 2.8f), Vector3.zero, Vector3.one * 0.9f));
                for (int i = 0; i < 6; i++)
                    FX.NoShadow(FX.Part(b.transform, MeshGen.Cylinder(0f, 0.5f, 4), FX.Glow(new Color(0.6f, 0.9f, 1f), 2.4f), Quaternion.Euler(0, 0, i * 60f) * new Vector3(0.45f, 0, 0.2f),
                        new Vector3(0.22f, 0.9f, 0.22f), new Vector3(M.PI * 0.5f, 0, i * M.PI / 3f)));
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(b.transform, false);
                l.type = LightType.Point;
                l.color = new Color(0.6f, 0.9f, 1f);
                l.intensity = 2.5f;
                l.range = 7f;
                l.shadows = LightShadows.None;
            }
            else
            {
                var mi = FX.Part(b.transform, MeshGen.Cylinder(0f, 0.5f, 6), FX.Glow(new Color(0.65f, 0.92f, 1f), 2.2f), Vector3.zero, new Vector3(0.45f, 1.2f, 0.45f), new Vector3(-M.PI * 0.5f, 0, 0));
                FX.NoShadow(mi);
            }
            FX.Particles(b.transform, new FX.ParticleSpec
            {
                amount = evolved ? 90 : 30, lifetime = evolved ? 0.6f : 0.35f, size = evolved ? 0.8f : 0.4f, speedMin = 0.5f, speedMax = 1.5f, spread = 25f, gravity = new Vector3(0, -3, 0),
                startColor = new Color(0.9f, 1f, 1f, 1f), endColor = new Color(0.4f, 0.7f, 1f, 0f),
            });
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = book.game;
            life -= delta;
            transform.position += velocity * delta;
            if (velocity.magnitude > 0.1f) transform.rotation = M.FaceNegZ(velocity);
            var pos = transform.position;
            bool hit = g.EnemiesInRadius(pos - new Vector3(0, 1f, 0), 0.9f, 2.5f).Count > 0;
            if (hit || life <= 0f || pos.y < g.terrain.Height(pos.x, pos.z) + 0.2f) Burst();
        }

        void Burst()
        {
            var g = book.game;
            var at = transform.position;
            if (!FX.Vfx("snow_hit", at, radius / 2f, 2f))
                FX.Flash(at, radius, new Color(0.6f, 0.9f, 1f, 0.8f), 0.3f);
            FX.Burst(at, new Color(0.85f, 0.97f, 1f), 16, 9f, 0.3f);
            Sfx.Play("frost", at, -2f, 0.1f);
            foreach (var e in g.EnemiesInRadius(at - new Vector3(0, 1f, 0), radius, 3.5f))
            {
                g.DamageEnemy(e, damage, Vector3.zero, 0f, true, new Color(0.7f, 0.95f, 1f));
                if (evolved || M.RandF() < freezeChance) e.Freeze(evolved ? 2f : 1.5f);
            }
            if (evolved)
            {
                var ground = at;
                ground.y = g.terrain.SurfaceHeight(at.x, at.z, at.y + 0.5f);
                IceField.Spawn(book, ground, radius * 1.4f, 3.5f, damage * 0.15f);
                FX.Shockwave(ground + new Vector3(0, 0.2f, 0), radius * 1.6f, new Color(0.8f, 0.97f, 1f, 0.9f), 0.45f);
            }
            // Shards fly out from the center.
            float off = M.RandF() * M.TAU;
            for (int i = 0; i < shards; i++)
            {
                float a = off + M.TAU * i / shards;
                IceShard.Spawn(book, at, new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * 26f, shardDamage, freezeChance, pierce);
            }
            Destroy(gameObject);
        }
    }

    public class IceShard : MonoBehaviour
    {
        SpellBook book;
        Vector3 velocity;
        float damage = 12f, freezeChance = 0.2f, life = 0.4f;
        bool pierce;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();

        public static void Spawn(SpellBook book, Vector3 at, Vector3 velocity, float damage, float freezeChance, bool pierce)
        {
            var s = new GameObject("IceShard").AddComponent<IceShard>();
            s.transform.SetParent(book.transform, false);
            s.transform.SetPositionAndRotation(at, M.FaceNegZ(velocity));
            s.book = book;
            s.velocity = velocity;
            s.damage = damage;
            s.freezeChance = freezeChance;
            s.pierce = pierce;
            var mi = FX.Part(s.transform, MeshGen.Cylinder(0f, 0.5f, 4), FX.Glow(new Color(0.75f, 0.95f, 1f), 2f), Vector3.zero, new Vector3(0.18f, 0.7f, 0.18f), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.NoShadow(mi);
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = book.game;
            life -= delta;
            transform.position += velocity * delta;
            transform.rotation = M.FaceNegZ(velocity);
            foreach (var e in g.EnemiesInRadius(transform.position - new Vector3(0, 1f, 0), 0.7f, 2.5f))
            {
                if (hit.Contains(e)) continue;
                hit.Add(e);
                g.DamageEnemy(e, damage, velocity.normalized, 2f, true, new Color(0.7f, 0.95f, 1f));
                if (M.RandF() < freezeChance) e.Freeze(1.5f);
                if (!pierce)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            if (life <= 0f) Destroy(gameObject);
        }
    }

    public class Meteor : MonoBehaviour
    {
        SpellBook book;
        Vector3 target;
        float radius = 3f, damage = 30f, delay, speed = 55f, size = 1f, igniteChance = 0.4f;
        bool evolved;
        GameObject visual;

        public static void Spawn(SpellBook book, Vector3 from, Vector3 target, float radius, float damage, float delay, float size, float igniteChance, bool evolved = false)
        {
            var m = new GameObject(evolved ? "Star" : "Meteor").AddComponent<Meteor>();
            m.evolved = evolved;
            m.transform.SetParent(book.transform, false);
            m.transform.position = from;
            m.book = book;
            m.target = target;
            m.radius = radius;
            m.damage = damage;
            m.delay = delay;
            m.size = size;
            m.igniteChance = igniteChance;
            m.visual = new GameObject("Visual");
            m.visual.transform.SetParent(m.transform, false);
            var col = evolved ? new Color(0.65f, 0.85f, 1f) : new Color(1f, 0.6f, 0.2f);
            var mi = FX.Part(m.visual.transform, MeshGen.Sphere(10), FX.Glow(col, 3f), Vector3.zero, Vector3.one * 1.1f * size * (evolved ? 1.3f : 1f));
            FX.NoShadow(mi);
            if (evolved) FX.NoShadow(FX.Part(m.visual.transform, MeshGen.Sphere(8), FX.Glow(Color.white, 4f), Vector3.zero, Vector3.one * 0.6f * size));
            var trailNode = FX.Node(m.visual.transform, "Trail");
            trailNode.rotation = Quaternion.LookRotation(from - target);
            FX.Particles(trailNode, new FX.ParticleSpec
            {
                amount = evolved ? 50 : 30, lifetime = 0.4f, size = 1f, speedMin = 1f, speedMax = 2f, spread = 20f,
                startColor = evolved ? new Color(0.9f, 0.95f, 1f, 1f) : new Color(1f, 0.8f, 0.4f, 1f),
                endColor = evolved ? new Color(0.2f, 0.4f, 1f, 0f) : new Color(0.6f, 0.1f, 0f, 0f),
            });
            m.visual.SetActive(delay <= 0f);
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            if (delay > 0f)
            {
                delay -= delta;
                visual.SetActive(delay <= 0f);
                return;
            }
            var to = target - transform.position;
            float step = speed * delta;
            if (to.magnitude <= step)
            {
                Impact();
                return;
            }
            transform.position += to.normalized * step;
        }

        void Impact()
        {
            var g = book.game;
            var col = evolved ? new Color(0.6f, 0.8f, 1f) : new Color(1f, 0.6f, 0.2f);
            if (!FX.Vfx("explosion", target, radius / 2.5f, 2f, null, 0f, evolved ? col : (Color?)null))
                FX.Flash(target + new Vector3(0, 0.5f, 0), radius, M.WithA(col, 0.8f), 0.35f);
            FX.Shockwave(target + new Vector3(0, 0.3f, 0), radius * 1.2f, M.WithA(col, 0.8f), 0.4f);
            FX.Burst(target + new Vector3(0, 0.5f, 0), col, 16, 10f, 0.4f);
            Sfx.Play("boom", target, -3f + size * 2f);
            if (size > 1.5f) g.Shake(0.6f);
            foreach (var e in g.EnemiesInRadius(target, radius, 4f))
            {
                var outDir = M.Flat(e.Pos - target);
                g.DamageEnemy(e, damage, outDir.normalized, 6f, true, Color.Lerp(col, Color.white, 0.3f));
                if (evolved || M.RandF() < igniteChance) e.Ignite(4f, damage * 0.15f);
            }
            // Starfall: each star leaves a pool of blue starfire.
            if (evolved) FirePatch.Spawn(g, target, radius * 0.9f, 3f, damage * 0.35f, new Color(0.45f, 0.7f, 1f));
            Destroy(gameObject);
        }
    }

    /// Fireballs: the Ember Mage's staff bolts (small) and the Azure Wyrm's Inferno Wave (large).
    public class Fireball : MonoBehaviour
    {
        Game game;
        Vector3 velocity;
        float damage = 40f, radius = 2.8f, life = 1.6f, burnMult = 0.12f;
        Player ownerPlayer;
        bool small;
        Color fx = new Color(1f, 0.5f, 0.12f);
        bool custom;
        int pierce;
        readonly HashSet<Enemy> passed = new HashSet<Enemy>();

        public static void Spawn(Game game, Player owner, Vector3 from, Vector3 velocity, float damage, float radius, float burnMult, bool small, Color? color = null, int pierce = 0)
        {
            var f = new GameObject("Fireball").AddComponent<Fireball>();
            f.pierce = pierce;
            f.transform.SetParent(game.spellbook.transform, false);
            f.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
            f.game = game;
            f.ownerPlayer = owner;
            f.velocity = velocity;
            f.damage = damage;
            f.radius = radius;
            f.burnMult = burnMult;
            f.small = small;
            f.custom = color.HasValue;
            f.fx = color ?? new Color(1f, 0.5f, 0.12f);
            var c = f.fx;
            var core0 = f.custom ? Color.Lerp(c, Color.white, 0.7f) : new Color(1f, 0.9f, 0.6f);
            var spark0 = f.custom ? Color.Lerp(c, Color.white, 0.5f) : new Color(1f, 0.85f, 0.4f);
            var spark1 = f.custom ? M.WithA(M.Mul(c, 0.45f), 0f) : new Color(0.7f, 0.1f, 0f, 0f);
            var mi = FX.Part(f.transform, MeshGen.Sphere(12), FX.Glow(new Color(c.r, c.g, c.b, small ? 1f : 0.55f), small ? 3f : 2.2f), Vector3.zero, Vector3.one * (small ? 0.4f : 0.7f));
            FX.NoShadow(mi);
            var core = FX.Part(f.transform, MeshGen.Sphere(8), FX.Glow(new Color(core0.r, core0.g, core0.b, small ? 1f : 0.7f), small ? 3f : 2f), Vector3.zero, Vector3.one * (small ? 0.22f : 0.4f));
            FX.NoShadow(core);
            FX.Particles(f.transform, new FX.ParticleSpec
            {
                amount = small ? 16 : 40, lifetime = 0.35f, size = 0.7f, scaleMin = 0.6f, scaleMax = 1.4f, speedMin = 0.5f, speedMax = 2f, spread = 30f,
                gravity = new Vector3(0, 2, 0), startColor = spark0, endColor = spark1,
            });
            if (!small)
            {
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(f.transform, false);
                light.type = LightType.Point;
                light.color = f.custom ? c : new Color(1f, 0.55f, 0.2f);
                light.intensity = 2f;
                light.range = 6f;
                light.shadows = LightShadows.None;
            }
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            life -= delta;
            transform.position += velocity * delta;
            var pos = transform.position;
            Enemy first = null;
            foreach (var e in game.EnemiesInRadius(pos - new Vector3(0, 1f, 0), 0.9f, 2.2f, ownerPlayer == null))   // WYVERN: the dragon's fireballs can hit wyverns
                if (!passed.Contains(e)) { first = e; break; }
            if (first != null && pierce > 0)
            {
                // Inferno Bolts: burst on the first ogre and keep flying.
                pierce--;
                passed.Add(first);
                Explode(false);
                return;
            }
            // The ground it bursts on includes island tops (judged from where it was before this step).
            float prevY = pos.y - velocity.y * delta;
            if (first != null || life <= 0f || pos.y < game.terrain.SurfaceHeight(pos.x, pos.z, prevY) + 0.2f) Explode(true);
        }

        void Explode(bool final)
        {
            var at = transform.position;
            bool packFx = FX.Vfx("hit_fire", at, small ? 0.7f : radius / 2f, 1.8f, null, small ? 0.04f : 0f, custom ? fx : (Color?)null);
            if (!packFx) FX.Flash(at, radius, new Color(fx.r, fx.g, fx.b, 0.85f), small ? 0.25f : 0.35f);
            if (!small) FX.Shockwave(at - new Vector3(0, 0.5f, 0), radius * 1.2f, custom ? new Color(fx.r, fx.g, fx.b, 0.8f) : new Color(1f, 0.6f, 0.2f, 0.8f), 0.35f);
            FX.Burst(at, custom ? fx : new Color(1f, 0.6f, 0.2f), small ? 8 : 18, 10f, small ? 0.3f : 0.45f);
            Sfx.Play(small ? "hit" : "boom", at, small ? -9f : -5f);
            int hits = 0;
            foreach (var e in game.EnemiesInRadius(at - new Vector3(0, 1f, 0), radius, 3.5f, ownerPlayer == null))
            {
                var outDir = M.Flat(e.Pos - at);
                game.dragonSource = ownerPlayer == null && e.IsWyvern;   // WYVERN: a dragon-launched fireball counts as the dragon's hit
                game.DamageEnemy(e, damage, outDir.normalized, small ? 2.5f : 6f, true, custom ? Color.Lerp(fx, Color.white, 0.4f) : new Color(1f, 0.7f, 0.3f));
                game.dragonSource = false;
                e.Ignite(3f, damage * burnMult);
                hits++;
            }
            if (ownerPlayer) ownerPlayer.WeaponHits(hits, damage);
            if (final) Destroy(gameObject);
        }
    }

    /// Glacial Comet's field of ice: ogres that walk in are frozen.
    public class IceField : MonoBehaviour
    {
        SpellBook book;
        float radius, life, damage, tick;

        public static void Spawn(SpellBook book, Vector3 ground, float radius, float life, float damage)
        {
            var f = new GameObject("IceField").AddComponent<IceField>();
            f.transform.SetParent(book.transform, false);
            f.transform.position = ground + new Vector3(0, 0.06f, 0);
            f.book = book;
            f.radius = radius;
            f.life = life;
            f.damage = damage;
            FX.NoShadow(FX.Part(f.transform, MeshGen.Disc(), FX.Lit(new Color(0.75f, 0.92f, 1f, 0.7f), 0.05f, 0.2f, 0.6f), Vector3.zero, new Vector3(radius, 1f, radius)));
            var crystal = FX.Glow(new Color(0.75f, 0.95f, 1f), 2f);
            for (int i = 0; i < 9; i++)
            {
                float a = M.TAU * i / 9f + M.RandF() * 0.4f;
                float r = radius * M.RandRange(0.25f, 0.9f);
                FX.NoShadow(FX.Part(f.transform, MeshGen.Cylinder(0f, 0.5f, 5), crystal, new Vector3(Mathf.Cos(a) * r, 0.4f, Mathf.Sin(a) * r),
                    new Vector3(0.3f, M.RandRange(0.6f, 1.4f), 0.3f), new Vector3(M.RandRange(-0.3f, 0.3f), 0, M.RandRange(-0.3f, 0.3f))));
            }
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = book.game;
            life -= delta;
            tick -= delta;
            if (tick <= 0f)
            {
                tick = 0.3f;
                foreach (var e in g.EnemiesInRadius(transform.position, radius, 2.5f))
                {
                    e.Freeze(1f);
                    g.DamageEnemy(e, damage, Vector3.zero, 0f, false);
                }
            }
            if (life < 0.5f) transform.localScale = Vector3.one * Mathf.Max(0.01f, life / 0.5f);
            if (life <= 0f) Destroy(gameObject);
        }
    }

    /// Storm Lord's storm orb: hovers where the chain ended and zaps nearby ogres for a few seconds.
    public class StormOrb : MonoBehaviour
    {
        SpellBook book;
        float damage, life = 3f, tick;
        Color color;
        Transform core;

        public static void Spawn(SpellBook book, Vector3 at, float damage, Color color)
        {
            var o = new GameObject("StormOrb").AddComponent<StormOrb>();
            o.transform.SetParent(book.transform, false);
            o.transform.position = at;
            o.book = book;
            o.damage = damage;
            o.color = color;
            o.core = FX.Part(o.transform, MeshGen.Sphere(12), FX.Glow(color, 3f), Vector3.zero, Vector3.one * 0.8f).transform;
            FX.NoShadow(o.core.GetComponent<Renderer>());
            FX.NoShadow(FX.Part(o.transform, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.95f, 0.8f), 3.5f), Vector3.zero, Vector3.one * 0.35f));
            FX.Particles(o.transform, new FX.ParticleSpec
            {
                amount = 30, lifetime = 0.25f, size = 0.3f, speedMin = 3f, speedMax = 6f, spread = 180f, startColor = Color.white, endColor = M.WithA(color, 0f),
            });
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(o.transform, false);
            l.type = LightType.Point;
            l.color = color;
            l.intensity = 2f;
            l.range = 6f;
            l.shadows = LightShadows.None;
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = book.game;
            life -= delta;
            tick -= delta;
            core.localScale = Vector3.one * (0.7f + Mathf.Sin(Time.time * 30f) * 0.15f);
            if (tick <= 0f)
            {
                tick = 0.3f;
                int n = 0;
                foreach (var e in g.EnemiesInRadius(transform.position - Vector3.up, 7f, 6f))
                {
                    if (n++ >= 3) break;
                    FX.Bolt(transform.position, e.Pos + new Vector3(0, e.size, 0), color, 0.18f, 0.15f);
                    g.DamageEnemy(e, damage, Vector3.zero, 0f, true, Color.Lerp(color, Color.white, 0.4f));
                    if (M.RandF() < 0.25f) e.Shock(1f);
                }
            }
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
