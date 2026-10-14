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

        public float Cost => Spell.cost * (1f - 0.04f * (Level - 1));

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
            basePos.y = game.terrain.Height(basePos.x, basePos.z) + 0.12f;
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
                float heal = Mathf.Min(total * (0.08f + 0.02f * L), 4f + 2f * L) * player.HealMult;
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
            if (L >= 5)
            {
                var at2 = game.DensestPoint(player.Pos, 24f, at, 10f);
                if (!M.IsInf(at2)) SpawnWell(at2, L, area, dm);
            }
            Sfx.Play("whoosh", -2f, 0.05f);
            return true;
        }

        void SpawnWell(Vector3 at, int L, float area, float dm)
        {
            GravityWell.Spawn(this, at + new Vector3(0, 1.2f, 0),
                radius: (7f + (L >= 3 ? 1.5f : 0f)) * area,
                pull: 7f + 2f * L,
                life: 2.5f + (L >= 3 ? 0.8f : 0f),
                tickDamage: (8f + 3f * L) * dm,
                finalDamage: (60f + 25f * L + (L >= 4 ? 40f : 0f)) * dm);
        }

        bool FrostBoltCast(int L, float area, float dm)
        {
            var t = game.NearestEnemy(player.Center, 28f, 20f);
            if (t == null) return false;
            var from = player.HandPosition;
            FrostBolt.Spawn(this, from, (t.Pos + new Vector3(0, t.size, 0) - from).normalized * 36f,
                damage: (30f + 12f * L) * dm,
                shards: 6 + (L >= 2 ? 2 : 0) + (L >= 4 ? 2 : 0),
                shardDamage: (12f + 5f * L) * dm,
                freezeChance: 0.2f + (L >= 3 ? 0.15f : 0f) + 0.03f * L,
                pierce: L >= 5,
                radius: 2.2f * area);
            Sfx.Play("frost", -8f, 0.1f);
            return true;
        }

        bool Lightning(int L, float dm)
        {
            var first = game.NearestEnemy(player.Center, 24f, 20f);
            if (first == null) return false;
            var hit = new List<Enemy>();
            Chain(first, L, dm, hit);
            if (L >= 5)
            {
                var second = game.NearestEnemy(first.Pos, 14f, 10f, null, hit);
                if (second) Chain(second, L, dm, hit);
            }
            Sfx.Play("zap", 0f, 0.15f);
            return true;
        }

        void Chain(Enemy first, int L, float dm, List<Enemy> hit)
        {
            float dmg = (26f + 10f * L) * dm;
            float shockChance = 0.3f + (L >= 3 ? 0.2f : 0f) + 0.04f * L;
            var col = new Color(0.75f, 0.75f, 1f);
            FX.Bolt(player.HandPosition, first.Pos + new Vector3(0, first.size, 0), col, 0.25f, 0.2f);
            Zap(first, dmg, shockChance);
            hit.Add(first);
            var cur = first;
            int jumps = 2 + (L >= 2 ? 2 : 0) + (L >= 4 ? 2 : 0);
            for (int i = 0; i < jumps; i++)
            {
                var nxt = game.NearestEnemy(cur.Pos, 9f, 8f, null, hit);
                if (nxt == null) break;
                FX.Bolt(cur.Pos + new Vector3(0, cur.size, 0), nxt.Pos + new Vector3(0, nxt.size, 0), col, 0.18f, 0.2f);
                Zap(nxt, dmg, shockChance);
                hit.Add(nxt);
                cur = nxt;
            }
            FX.Flash(first.Pos + new Vector3(0, 1, 0), 1.5f, new Color(0.7f, 0.7f, 1f, 0.8f), 0.2f);
        }

        void Zap(Enemy e, float dmg, float shockChance)
        {
            game.DamageEnemy(e, dmg, Vector3.zero, 0f, true, SHOCK_COLOR);
            if (M.RandF() < shockChance) e.Shock(1f);
        }

        bool Meteors(int L, float area, float dm)
        {
            var center = game.DensestPoint(player.Pos, 26f);
            if (M.IsInf(center)) return false;
            int count = 5 + (L >= 2 ? 2 : 0) + (L >= 4 ? 2 : 0);
            float rad = (3f + (L >= 3 ? 1f : 0f)) * area;
            float ignite = 0.4f + (L >= 3 ? 0.2f : 0f) + 0.04f * L;
            for (int i = 0; i < count; i++)
            {
                var at = center + new Vector3(M.RandRange(-6f, 6f), 0, M.RandRange(-6f, 6f)) * area;
                var near = game.RandomEnemyNear(center, 9f * area);
                if (near && M.RandF() < 0.6f) at = near.Pos;
                DropMeteor(at, rad, (32f + 14f * L) * dm, i * 0.12f, 1f, ignite);
            }
            if (L >= 5) DropMeteor(center, rad * 2.2f, (32f + 14f * L) * dm * 3f, count * 0.12f + 0.3f, 2.5f, 1f);
            FX.Telegraph(center, 7f * area, 0.6f, new Color(1f, 0.5f, 0.1f, 0.3f));
            Sfx.Play("whoosh", -2f, 0.05f);
            return true;
        }

        void DropMeteor(Vector3 at, float rad, float dmg, float delay, float scaleK, float ignite)
        {
            at.y = game.terrain.Height(at.x, at.z);
            Meteor.Spawn(this, at + new Vector3(M.RandRange(-8f, 8f), 32f, M.RandRange(-8f, 8f)), at, rad, dmg, delay, scaleK, ignite);
        }
    }

    // ==================================================================== spell objects

    public class GravityWell : MonoBehaviour
    {
        SpellBook book;
        float radius = 7f, pull = 8f, tickDamage = 6f, finalDamage = 50f, life = 2.5f, tick;
        Transform core, halo;
        float haloSpin;

        public static void Spawn(SpellBook book, Vector3 pos, float radius, float pull, float life, float tickDamage, float finalDamage)
        {
            var w = new GameObject("GravityWell").AddComponent<GravityWell>();
            w.transform.SetParent(book.transform, false);
            w.transform.position = pos;
            w.book = book;
            w.radius = radius;
            w.pull = pull;
            w.life = life;
            w.tickDamage = tickDamage;
            w.finalDamage = finalDamage;
            w.Build();
        }

        void Build()
        {
            core = FX.Part(transform, MeshGen.Sphere(16), FX.Lit(new Color(0.02f, 0f, 0.04f), 0.1f), Vector3.zero, Vector3.one * 0.2f).transform;
            var h = FX.Part(transform, MeshGen.Ring(), FX.Glow(new Color(0.6f, 0.25f, 1f, 0.8f), 2.5f), Vector3.zero, new Vector3(1.6f, 2f, 1.6f));
            FX.NoShadow(h);
            halo = h.transform;
            var floorRing = FX.Part(transform, MeshGen.Ring(), FX.Glow(new Color(0.5f, 0.2f, 0.9f, 0.35f), 1.2f), new Vector3(0, -1f, 0), new Vector3(radius, 0.4f, radius));
            FX.NoShadow(floorRing);
            FX.Particles(transform, new FX.ParticleSpec
            {
                amount = 60, lifetime = 0.8f, size = 0.5f, sphereRadius = radius, radialSpeed = -10f, worldSpace = false,
                startColor = new Color(0.6f, 0.2f, 1f, 0f), midColor = new Color(0.7f, 0.35f, 1f, 1f), midAt = 0.4f, endColor = new Color(0.2f, 0f, 0.4f, 0f),
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
                FX.Flash(pos, radius * 0.8f, new Color(0.6f, 0.2f, 1f, 0.8f), 0.4f);
                FX.Shockwave(pos - new Vector3(0, 1f, 0), radius, new Color(0.7f, 0.3f, 1f, 0.9f), 0.4f);
                Sfx.Play("boom", pos, 0f);
                foreach (var e in g.EnemiesInRadius(pos, radius * 0.75f, 8f))
                    g.DamageEnemy(e, finalDamage, Vector3.zero, 0f, true, new Color(0.8f, 0.5f, 1f));
                Destroy(gameObject);
            }
        }
    }

    public class FrostBolt : MonoBehaviour
    {
        SpellBook book;
        Vector3 velocity;
        float damage = 30f, radius = 2.2f, shardDamage = 12f, freezeChance = 0.2f, life = 1.4f;
        int shards = 6;
        bool pierce;

        public static void Spawn(SpellBook book, Vector3 from, Vector3 velocity, float damage, int shards, float shardDamage, float freezeChance, bool pierce, float radius)
        {
            var b = new GameObject("FrostBolt").AddComponent<FrostBolt>();
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
            var mi = FX.Part(b.transform, MeshGen.Cylinder(0f, 0.5f, 6), FX.Glow(new Color(0.65f, 0.92f, 1f), 2.2f), Vector3.zero, new Vector3(0.45f, 1.2f, 0.45f), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.NoShadow(mi);
            FX.Particles(b.transform, new FX.ParticleSpec
            {
                amount = 30, lifetime = 0.35f, size = 0.4f, speedMin = 0.5f, speedMax = 1.5f, spread = 25f, gravity = new Vector3(0, -3, 0),
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
            FX.Flash(at, radius, new Color(0.6f, 0.9f, 1f, 0.8f), 0.3f);
            FX.Burst(at, new Color(0.85f, 0.97f, 1f), 16, 9f, 0.3f);
            Sfx.Play("frost", at, -2f, 0.1f);
            foreach (var e in g.EnemiesInRadius(at - new Vector3(0, 1f, 0), radius, 3.5f))
            {
                g.DamageEnemy(e, damage, Vector3.zero, 0f, true, new Color(0.7f, 0.95f, 1f));
                if (M.RandF() < freezeChance) e.Freeze(1.5f);
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
        GameObject visual;

        public static void Spawn(SpellBook book, Vector3 from, Vector3 target, float radius, float damage, float delay, float size, float igniteChance)
        {
            var m = new GameObject("Meteor").AddComponent<Meteor>();
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
            var mi = FX.Part(m.visual.transform, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.6f, 0.2f), 3f), Vector3.zero, Vector3.one * 1.1f * size);
            FX.NoShadow(mi);
            var trailNode = FX.Node(m.visual.transform, "Trail");
            trailNode.rotation = Quaternion.LookRotation(from - target);
            FX.Particles(trailNode, new FX.ParticleSpec
            {
                amount = 30, lifetime = 0.4f, size = 1f, speedMin = 1f, speedMax = 2f, spread = 20f,
                startColor = new Color(1f, 0.8f, 0.4f, 1f), endColor = new Color(0.6f, 0.1f, 0f, 0f),
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
            FX.Flash(target + new Vector3(0, 0.5f, 0), radius, new Color(1f, 0.55f, 0.15f, 0.8f), 0.35f);
            FX.Shockwave(target + new Vector3(0, 0.3f, 0), radius * 1.2f, new Color(1f, 0.6f, 0.2f, 0.8f), 0.4f);
            FX.Burst(target + new Vector3(0, 0.5f, 0), new Color(1f, 0.6f, 0.2f), 16, 10f, 0.4f);
            Sfx.Play("boom", target, -3f + size * 2f);
            if (size > 1.5f) g.Shake(0.6f);
            foreach (var e in g.EnemiesInRadius(target, radius, 4f))
            {
                var outDir = M.Flat(e.Pos - target);
                g.DamageEnemy(e, damage, outDir.normalized, 6f, true, new Color(1f, 0.7f, 0.3f));
                if (M.RandF() < igniteChance) e.Ignite(4f, damage * 0.15f);
            }
            Destroy(gameObject);
        }
    }

    /// Fireballs: the Ember Mage's auto attack (small) and the Azure Wyrm's Inferno Wave (large).
    public class Fireball : MonoBehaviour
    {
        Game game;
        Vector3 velocity;
        float damage = 40f, radius = 2.8f, life = 1.6f, burnMult = 0.12f;
        Player ownerPlayer;
        bool small;

        public static void Spawn(Game game, Player owner, Vector3 from, Vector3 velocity, float damage, float radius, float burnMult, bool small)
        {
            var f = new GameObject("Fireball").AddComponent<Fireball>();
            f.transform.SetParent(game.spellbook.transform, false);
            f.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
            f.game = game;
            f.ownerPlayer = owner;
            f.velocity = velocity;
            f.damage = damage;
            f.radius = radius;
            f.burnMult = burnMult;
            f.small = small;
            var mi = FX.Part(f.transform, MeshGen.Sphere(12), FX.Glow(new Color(1f, 0.55f, 0.15f), 3f), Vector3.zero, Vector3.one * (small ? 0.4f : 0.7f));
            FX.NoShadow(mi);
            var core = FX.Part(f.transform, MeshGen.Sphere(8), FX.Glow(new Color(1f, 0.95f, 0.7f), 3f), Vector3.zero, Vector3.one * (small ? 0.22f : 0.4f));
            FX.NoShadow(core);
            FX.Particles(f.transform, new FX.ParticleSpec
            {
                amount = small ? 16 : 40, lifetime = 0.35f, size = 0.7f, scaleMin = 0.6f, scaleMax = 1.4f, speedMin = 0.5f, speedMax = 2f, spread = 30f,
                gravity = new Vector3(0, 2, 0), startColor = new Color(1f, 0.85f, 0.4f, 1f), endColor = new Color(0.7f, 0.1f, 0f, 0f),
            });
            if (!small)
            {
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(f.transform, false);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.55f, 0.2f);
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
            bool hit = game.EnemiesInRadius(pos - new Vector3(0, 1f, 0), 0.9f, 2.2f).Count > 0;
            if (hit || life <= 0f || pos.y < game.terrain.Height(pos.x, pos.z) + 0.2f) Explode();
        }

        void Explode()
        {
            var at = transform.position;
            FX.Flash(at, radius, new Color(1f, 0.5f, 0.12f, 0.85f), small ? 0.25f : 0.35f);
            if (!small) FX.Shockwave(at - new Vector3(0, 0.5f, 0), radius * 1.2f, new Color(1f, 0.6f, 0.2f, 0.8f), 0.35f);
            FX.Burst(at, new Color(1f, 0.6f, 0.2f), small ? 8 : 18, 10f, small ? 0.3f : 0.45f);
            Sfx.Play(small ? "hit" : "boom", at, small ? -9f : -5f);
            int hits = 0;
            foreach (var e in game.EnemiesInRadius(at - new Vector3(0, 1f, 0), radius, 3.5f))
            {
                var outDir = M.Flat(e.Pos - at);
                game.DamageEnemy(e, damage, outDir.normalized, small ? 2.5f : 6f, true, new Color(1f, 0.7f, 0.3f));
                e.Ignite(3f, damage * burnMult);
                hits++;
            }
            if (ownerPlayer) ownerPlayer.WeaponHits(hits, damage);
            Destroy(gameObject);
        }
    }
}
