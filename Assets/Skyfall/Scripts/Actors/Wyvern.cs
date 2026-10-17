using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    // The WYVERN: a two-legged, bat-winged drake that harasses the hero from the air (world 2 onward).
    //
    // Damage rule. Only the dragon can hurt a wyvern: Game.DamageEnemy ignores any hit on it unless Game.dragonSource is set
    // (DragonPet.Hit and dragon-launched fireballs set it; a wyvern's own burn/bleed ticks do too) or the hero is mounted /
    // in a soaring dragon form (Game.HeroCanHitWyverns). On top of that, the hero's targeting queries (Game.NearestEnemy,
    // EnemiesInRadius, RandomEnemyNear, DensestPoint) skip wyverns while the hero can't hurt them, so chain/bounce/homing/
    // auto-target spells never waste a shot on one; the dragon's own queries pass asDragon: true.

    /// What a wyvern is doing: harrying the dragon, bombing the hero, or fleeing while hurt.
    public enum WyvernPlan { Distract, Bomb, Retreat }

    /// Everything a wyvern looks at when it chooses what to do (a plain struct so the choice can be tested).
    public struct WyvernSituation
    {
        public bool heroMounted;          // flying on the dragon (or in a soaring form): bombs can't catch a flier
        public float heroHpFrac;          // 0..1
        public float dragonDist;          // dragon to hero
        public bool dragonBreathing;      // actively breathing at something
        public bool dragonDiving;         // in an automatic dive (busy)
        public float breathReadyIn;       // seconds until its passive breath can start again
        public int otherBombers;          // other wyverns currently bombing
        public int maxBombers;            // the cap for this world
        public WyvernPlan current;        // what it is doing now (small bonus for staying with it)
        public bool hasPlan;              // false for a newborn wyvern (no bonus)
        public bool hurt;                 // below Data.WYVERN_RETREAT_HP and hasn't retreated yet
    }

    public static class WyvernBrain
    {
        const float STAY_BONUS = 0.75f;

        /// Bomb when the hero is on foot and the dragon is far, busy diving or waiting for its breath, or the hero is
        /// low on health; harry the dragon when it is close to the hero, actively breathing, or the hero is riding it.
        /// At most maxBombers wyverns bomb at once (the rest harry), and a hurt wyvern retreats.
        public static WyvernPlan Choose(WyvernSituation s)
        {
            if (s.hurt) return WyvernPlan.Retreat;
            if (s.heroMounted) return WyvernPlan.Distract;
            bool wasBomber = s.hasPlan && s.current == WyvernPlan.Bomb;
            if (!wasBomber && s.otherBombers >= s.maxBombers) return WyvernPlan.Distract;

            float bomb = 0f, distract = 0.5f;
            if (s.heroHpFrac < Data.WYVERN_LOW_HERO_HP) bomb += 4f;
            if (s.dragonDist > Data.WYVERN_DRAGON_FAR) bomb += 2f;
            else if (s.dragonDist > Data.WYVERN_DRAGON_NEAR) bomb += 1f;
            else distract += 1.5f;
            if (s.dragonDiving) bomb += 1.5f;
            else if (s.dragonBreathing) distract += 2f;
            else if (s.breathReadyIn > Data.WYVERN_BREATH_COOLDOWN) bomb += 1f;
            if (s.hasPlan)
            {
                if (s.current == WyvernPlan.Bomb) bomb += STAY_BONUS;
                else if (s.current == WyvernPlan.Distract) distract += STAY_BONUS;
            }
            return bomb > distract ? WyvernPlan.Bomb : WyvernPlan.Distract;
        }
    }

    /// Adds wyverns to the field: a few at a time from world 2 (more and sooner in world 3), outside the wave table.
    public static class WyvernDirector
    {
        /// Tests switch the director off to place wyverns by hand.
        public static bool Disabled;

        static Game owner;
        static int ownerStage;
        static float lastT, nextSpawnT;
        static Game announcedFor;

        public static int Alive(Game g)
        {
            int n = 0;
            foreach (var e in g.enemies) if (e.IsWyvern && !e.dead) n++;
            return n;
        }

        public static void Tick(Game g, float delta)
        {
            if (Disabled || g.finalStarted || g.stage < Data.FLIGHT_STAGE || g.player == null) return;
            int si = Mathf.Clamp(g.stage - 1, 0, Data.WYVERN_MAX_ALIVE.Length - 1);
            float t = g.timeElapsed;
            if (owner != g || ownerStage != g.stage || t < lastT - 0.5f)
            {
                owner = g;
                ownerStage = g.stage;
                nextSpawnT = Data.WYVERN_FIRST_SPAWN[si];
            }
            lastT = t;
            if (t < nextSpawnT) return;
            if (Alive(g) >= Data.WYVERN_MAX_ALIVE[si])
            {
                nextSpawnT = t + 5f;   // full: look again shortly
                return;
            }
            Spawn(g);
            float ramp = Mathf.Lerp(1f, Data.WYVERN_SPAWN_RAMP, Mathf.Clamp01(t / Data.RUN_LENGTH));
            nextSpawnT = t + Data.WYVERN_SPAWN_EVERY[si] * ramp * M.RandRange(0.85f, 1.15f);
        }

        /// One wyvern coming in over the horizon.
        public static Enemy Spawn(Game g)
        {
            float a = M.RandF() * M.TAU;
            var p = g.player.Pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * M.RandRange(40f, 55f);
            var e = g.SpawnEnemy("wyvern", p);
            e.WyvernPlace(e.Pos);
            if (announcedFor != g && g.hud != null)
            {
                announcedFor = g;
                g.hud.Toast("A wyvern circles overhead: only your dragon can hurt it (or ride your dragon with Q)");
            }
            FX.Text(e.Pos + Vector3.up * 3f, "Wyvern: dragon only!", new Color(0.75f, 0.6f, 1f));
            Sfx.Play("roar", e.Pos, -4f, 0.15f);
            return e;
        }
    }

    public partial class Enemy
    {
        /// A wyvern: flies, shrugs off everything but the dragon (see the notes at the top of this file).
        public bool IsWyvern { get; private set; }
        /// Set while a wyvern's own burn/bleed/poison tick is applied, which game.DamageEnemy lets through.
        public bool dotTick;

        // ---- wyvern state
        WyvernPlan wPlan = WyvernPlan.Distract;
        bool wHasPlan;
        string wState = "stage";             // bomb: stage (circle the hero), run (the bombing run), recover
        float wThinkT = 0.5f, wCommitT, wBombCd = 2f, wHarryCd = 1.5f, wSwoopT, wRetreatT, wOrbit, wOrbitDir = 1f;
        bool wRetreated;
        Vector3 wVel, wRunDir;
        float wRunT, wRunTime, wRunSpeed, wRecoverT, yawRate, bank, wImmuneMsgT, wWingT;
        Transform wShadow, wingL, wingR;

        static readonly string[] W_FLY = { "Fly Forward", "Fly Glide", "Fly Float", "Idle", "Idle01" };
        static readonly string[] W_GLIDE = { "Fly Glide", "Fly Forward", "Fly Float" };
        static readonly string[] W_HOVER = { "Fly Float", "Fly Forward", "Fly Glide" };
        static readonly string[] W_ATTACK = { "Fly Flame Attack", "Fly Fireball Shoot", "Fly Attack", "Basic Attack", "Fly Forward" };
        static readonly string[] W_DIE = { "Die", "die", "Death" };

        /// Where the wyvern would land: the ground (or island top) straight below it.
        public Vector3 GroundPos => new Vector3(Pos.x, game.terrain.SurfaceHeight(Pos.x, Pos.z, Pos.y), Pos.z);

        // ---------------------------------------------------------------- creation

        static Enemy MakeWyvern(Game g, float hpMult, float dmgMult)
        {
            var e = new GameObject("Wyvern").AddComponent<Enemy>();
            e.game = g;
            e.kind = "wyvern";
            e.displayName = "Wyvern";
            e.IsWyvern = true;
            e.size = Data.WYVERN_SIZE * M.RandRange(0.95f, 1.05f);
            e.maxHp = 14f * Mathf.Pow(e.size, 2.6f) * hpMult * Data.WYVERN_HP_MULT;
            e.hp = e.maxHp;
            e.speed = Data.WYVERN_SPEED * M.RandRange(0.92f, 1.08f);
            e.damage = 6f * e.size * dmgMult * Data.WYVERN_DMG_MULT;
            e.xpValue = Data.WYVERN_XP;
            e.radius = Data.WYVERN_RADIUS;
            e.animT = M.RandF() * 10f;
            e.wOrbit = M.RandF() * M.TAU;
            e.wOrbitDir = M.RandF() < 0.5f ? -1f : 1f;
            e.wBombCd = M.RandRange(1f, 3f);
            e.wHarryCd = M.RandRange(1f, 2.5f);
            e.wThinkT = M.RandRange(0.3f, 1.2f);
            e.BuildWyvernModel();
            return e;
        }

        /// Puts a freshly spawned wyvern at cruise height above the ground below it.
        public void WyvernPlace(Vector3 p)
        {
            float g = game.terrain.SurfaceHeight(p.x, p.z, 100000f);
            Pos = new Vector3(p.x, Mathf.Max(g, game.player.Pos.y) + Data.WYVERN_ALTITUDE, p.z);
            wVel = Vector3.zero;
        }

        void BuildWyvernModel()
        {
            model = FX.Node(transform, "Model");
            var lib = Skins.Lib;
            var prefab = lib != null ? lib.wyvern : null;
            if (prefab != null)
            {
                var holder = FX.Node(model, "WyvernSkin");
                var go = Instantiate(prefab, holder, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                go.transform.localScale = Vector3.one;
                rig = holder.gameObject.AddComponent<CharacterRig>();
                rig.Init(go, null);
                // Scale to the wyvern's length and center the body on the enemy's position (the pack models stand on y = 0).
                var b = rig.Bounds();
                float s = Mathf.Clamp(Data.WYVERN_LENGTH / Mathf.Max(b.size.z, 0.01f), 0.02f, 50f);
                holder.localScale = Vector3.one * s;
                holder.localPosition = -(b.center - transform.position) * s;
                if (lib.wyvernFallback) TintModel(new Color(0.62f, 0.5f, 0.9f));   // the stand-in evil dragon, recolored
                rig.Loop(W_FLY);
            }
            else BuildProceduralWyvern();
            ice = FX.Part(transform, MeshGen.Sphere(10), FX.Lit(new Color(0.55f, 0.8f, 1.0f, 0.3f), 0.05f, 0f, 0.5f), Vector3.zero, new Vector3(5f, 3f, 6f));
            FX.NoShadow(ice);
            ice.enabled = false;
            // A ground shadow so you can read where it is.
            var shadowMat = FX.Lit(new Color(0.02f, 0.02f, 0.04f, 0.42f), 1f);
            wShadow = FX.Part(transform, MeshGen.Disc(), shadowMat, Vector3.zero, new Vector3(3f, 1f, 3f)).transform;
            FX.NoShadow(wShadow.GetComponent<Renderer>());
            wShadow.gameObject.name = "WyvernShadow";
        }

        /// Last resort when the skin library has no wyvern model at all: a simple bat-winged drake from primitives.
        void BuildProceduralWyvern()
        {
            var skin = FX.NewLit(new Color(0.28f, 0.22f, 0.36f), 0.7f);
            var membrane = FX.NewLit(new Color(0.45f, 0.18f, 0.3f), 0.8f);
            tintMats.Add(skin);
            tintMats.Add(membrane);
            membrane.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            var sph = MeshGen.Sphere(10);
            var cone = MeshGen.Cylinder(0f, 0.5f, 6);
            FX.Part(model, sph, skin, new Vector3(0f, 0f, 0.2f), new Vector3(1.3f, 1.2f, 2.8f));
            FX.Part(model, sph, skin, new Vector3(0f, 0.5f, -1.5f), new Vector3(0.55f, 0.55f, 1.3f), new Vector3(0.5f, 0f, 0f));
            var head = FX.Node(model, "Head", new Vector3(0f, 0.95f, -2.2f));
            FX.Part(head, sph, skin, Vector3.zero, new Vector3(0.7f, 0.6f, 0.95f));
            FX.Part(head, sph, FX.Glow(new Color(1f, 0.6f, 0.2f), 2.5f), new Vector3(0.22f, 0.1f, -0.3f), Vector3.one * 0.12f);
            FX.Part(head, sph, FX.Glow(new Color(1f, 0.6f, 0.2f), 2.5f), new Vector3(-0.22f, 0.1f, -0.3f), Vector3.one * 0.12f);
            FX.Part(model, cone, skin, new Vector3(0f, -0.1f, 2.3f), new Vector3(0.5f, 2.6f, 0.5f), new Vector3(M.PI * 0.5f, 0f, 0f));
            foreach (int sd in new[] { -1, 1 })
            {
                var wing = FX.Node(model, sd < 0 ? "WingL" : "WingR", new Vector3(sd * 0.6f, 0.45f, -0.2f));
                FX.Part(wing, MeshGen.Box(), membrane, new Vector3(sd * 1.7f, 0f, 0.3f), new Vector3(3.4f, 0.05f, 1.9f), new Vector3(0f, sd * 0.25f, 0f));
                FX.Part(wing, MeshGen.Box(), skin, new Vector3(sd * 1.7f, 0.02f, -0.55f), new Vector3(3.5f, 0.1f, 0.1f));
                if (sd < 0) wingL = wing; else wingR = wing;
                FX.Part(model, cone, skin, new Vector3(sd * 0.35f, -0.85f, 0.2f), new Vector3(0.18f, 0.9f, 0.18f));
            }
        }

        // ---------------------------------------------------------------- damage feedback

        /// "Immune" feedback when something that can't hurt it tries (at most every few seconds per wyvern).
        public void ShowImmune()
        {
            if (wImmuneMsgT > 0f) return;
            wImmuneMsgT = 4f;
            FX.Text(Pos + new Vector3(0f, 2.5f, 0f), "Immune: dragon only", new Color(0.75f, 0.6f, 1f));
            Sfx.Play("clang", Pos, -8f, 0.2f);
        }

        /// A steady violet shimmer while only the dragon can hurt it; gone while the hero rides and can.
        bool WyvernShieldGlow()
        {
            if (!IsWyvern || game.HeroCanHitWyverns) return false;
            SetEmission(new Color(0.55f, 0.35f, 1f), 0.45f + Mathf.Sin(animT * 3f) * 0.12f);
            return true;
        }

        /// Burn/bleed/poison ticks: a wyvern's only get applied by hits that were allowed, so they always count (as the dragon's).
        void DotDamage(float amount)
        {
            if (!IsWyvern)
            {
                game.DamageEnemy(this, amount, Vector3.zero, 0f, false);
                return;
            }
            dotTick = true;
            game.dragonSource = true;
            game.DamageEnemy(this, amount, Vector3.zero, 0f, false);
            game.dragonSource = false;
            dotTick = false;
        }

        // ---------------------------------------------------------------- AI

        void WyvernTick(float delta, Player player)
        {
            var pet = game.pet;
            wImmuneMsgT -= delta;
            wBombCd -= delta;
            wHarryCd -= delta;
            wThinkT -= delta;
            wCommitT -= delta;
            model.localScale = Vector3.one * (1f + pop * 0.18f);
            bool grounded = stunT > 0f || frozenT > 0f;
            float spd = speed * (slowT > 0f ? slowF : 1f);

            Vector3 want = Pos;
            float wantY = Pos.y;
            float topSpeed = spd;
            if (stunT > 0f) stunT -= delta;
            if (!grounded)
            {
                // A hurt wyvern breaks off (once) unless it is mid-run.
                if (!wRetreated && hp < maxHp * Data.WYVERN_RETREAT_HP && !(wPlan == WyvernPlan.Bomb && wState == "run")) BeginPlan(WyvernPlan.Retreat);
                else if (wThinkT <= 0f && !(wPlan == WyvernPlan.Bomb && wState != "stage")) Think(player, pet);
                switch (wPlan)
                {
                    case WyvernPlan.Distract: DistractAi(delta, player, pet, ref want, ref wantY, ref topSpeed); break;
                    case WyvernPlan.Bomb: BombAi(delta, player, ref want, ref wantY, ref topSpeed); break;
                    default: RetreatAi(delta, player, ref want, ref wantY, ref topSpeed); break;
                }
            }
            if (frozenT > 0f) wVel = Vector3.MoveTowards(wVel, Vector3.zero, 30f * delta);
            else Steer(want, wantY, topSpeed, wPlan == WyvernPlan.Bomb && wState == "run", delta, grounded);

            // Move.
            var p = Pos + wVel * delta;
            float lim = WorldTerrain.PLAY_LIMIT;
            p.x = Mathf.Clamp(p.x, -lim, lim);
            p.z = Mathf.Clamp(p.z, -lim, lim);
            float gh = game.terrain.SurfaceHeight(p.x, p.z, p.y);
            p.y = Mathf.Max(p.y, gh + Data.WYVERN_MIN_AGL * 0.6f);
            Pos = p;
            knock = Vector3.zero;   // it flies: nothing shoves it
            Face(delta);
            Visuals(delta, grounded, gh);
        }

        void Think(Player player, DragonPet pet)
        {
            wThinkT = Data.WYVERN_THINK_EVERY * M.RandRange(0.85f, 1.2f);
            if (wPlan == WyvernPlan.Retreat) return;   // it leaves retreat on its own timer
            if (wHasPlan && wCommitT > 0f) return;
            int others = 0;
            foreach (var o in game.enemies)
                if (o != this && o.IsWyvern && !o.dead && o.wPlan == WyvernPlan.Bomb) others++;
            int si = Mathf.Clamp(game.stage - 1, 0, Data.WYVERN_MAX_BOMBERS.Length - 1);
            var s = new WyvernSituation
            {
                heroMounted = game.HeroCanHitWyverns,
                heroHpFrac = player.hp / Mathf.Max(1f, player.maxHp),
                dragonDist = Vector3.Distance(pet.Pos, player.Pos),
                dragonBreathing = pet.IsBreathing,
                dragonDiving = pet.IsDiving,
                breathReadyIn = pet.BreathReadyIn,
                otherBombers = others,
                maxBombers = Data.WYVERN_MAX_BOMBERS[si],
                current = wPlan,
                hasPlan = wHasPlan,
                hurt = false,
            };
            var plan = WyvernBrain.Choose(s);
            if (!wHasPlan || plan != wPlan) BeginPlan(plan);
        }

        void BeginPlan(WyvernPlan plan)
        {
            wPlan = plan;
            wHasPlan = true;
            wCommitT = Data.WYVERN_COMMIT;
            if (plan == WyvernPlan.Bomb)
            {
                wState = "stage";
                wBombCd = Mathf.Max(wBombCd, 1f);
            }
            else if (plan == WyvernPlan.Retreat)
            {
                wRetreated = true;
                wRetreatT = Data.WYVERN_RETREAT_TIME;
                wOrbit = Mathf.Atan2(Pos.z - game.player.Pos.z, Pos.x - game.player.Pos.x);
                FX.Text(Pos + new Vector3(0f, 2.5f, 0f), "Wyvern flees!", new Color(1f, 0.8f, 0.4f));
            }
            else wSwoopT = 0f;
        }

        /// Ground height under the hero (or the wyvern, if higher): the reference for cruise altitude.
        float RefGround(Player player, Vector3 at)
        {
            float g = game.terrain.SurfaceHeight(at.x, at.z, Mathf.Max(Pos.y, at.y));
            return Mathf.Max(g, player.Pos.y);
        }

        // (a) Harry the dragon: circle it and swoop at it, pulling its breath onto this wyvern.
        void DistractAi(float delta, Player player, DragonPet pet, ref Vector3 want, ref float wantY, ref float topSpeed)
        {
            wOrbit += wOrbitDir * delta * 0.9f;
            var center = pet.Pos;
            float rad = 9f + Mathf.Sin(animT * 0.7f) * 1.5f;
            want = center + new Vector3(Mathf.Cos(wOrbit), 0f, Mathf.Sin(wOrbit)) * rad;
            wantY = Mathf.Max(center.y + 2f, game.terrain.SurfaceHeight(Pos.x, Pos.z, Pos.y) + Data.WYVERN_MIN_AGL);
            topSpeed = speed * 1.15f;
            if (wSwoopT > 0f)
            {
                wSwoopT -= delta;
                want = center;
                wantY = center.y + 1f;
                topSpeed = Data.WYVERN_RUN_SPEED * 0.85f;
            }
            else if (wHarryCd <= 0f && M.Flat(center - Pos).magnitude < 24f)
            {
                wSwoopT = 0.75f;
                wHarryCd = Data.WYVERN_HARRY_EVERY * M.RandRange(0.9f, 1.25f);
                pet.Harry(this, Data.WYVERN_HARRY_TIME);
                if (rig) rig.Action(0.7f, W_ATTACK);
                Sfx.Play("roar", Pos, -7f, 0.2f);
            }
        }

        // (b) Bomb the hero: circle at a distance, then a straight run over a marked spot, dropping fire bombs.
        void BombAi(float delta, Player player, ref Vector3 want, ref float wantY, ref float topSpeed)
        {
            float refY = RefGround(player, player.Pos);
            switch (wState)
            {
                case "run":
                {
                    wRunT += delta;
                    want = Pos + wRunDir * 50f;
                    wantY = refY + Data.WYVERN_BOMB_ALTITUDE;
                    topSpeed = wRunSpeed;
                    if (wRunT >= wRunTime && wRunT - delta < wRunTime)
                    {
                        if (rig) rig.Action(0.6f, W_ATTACK);
                        Sfx.Play("flame", Pos, -4f, 0.1f);
                    }
                    if (wRunT >= wRunTime + 0.9f)
                    {
                        wState = "recover";
                        wRecoverT = 1.3f;
                        wBombCd = Data.WYVERN_BOMB_COOLDOWN[Mathf.Clamp(game.stage - 1, 0, Data.WYVERN_BOMB_COOLDOWN.Length - 1)] * M.RandRange(0.9f, 1.15f);
                    }
                    break;
                }
                case "recover":
                {
                    wRecoverT -= delta;
                    want = Pos + wRunDir * 50f;
                    wantY = refY + Data.WYVERN_ALTITUDE + 2f;
                    topSpeed = speed * 1.2f;
                    if (wRecoverT <= 0f) wState = "stage";
                    break;
                }
                default:
                {
                    // Circle the hero at the distance a run needs, then go when the bombs are ready.
                    wOrbit += wOrbitDir * delta * 0.45f;
                    float ring = Data.WYVERN_RUN_SPEED * 1.2f;
                    want = player.Pos + new Vector3(Mathf.Cos(wOrbit), 0f, Mathf.Sin(wOrbit)) * ring;
                    wantY = refY + Data.WYVERN_ALTITUDE;
                    topSpeed = speed * 1.1f;
                    float d = M.Flat(player.Pos - Pos).magnitude;
                    if (wBombCd <= 0f && d > 18f && d < 34f && !game.HeroCanHitWyverns) StartRun(player);
                    break;
                }
            }
        }

        void StartRun(Player player)
        {
            // Aim where the hero is heading; the marker stays put, so walking out of it dodges the bomb.
            var m = player.Pos + M.Flat(player.velocity) * Data.WYVERN_BOMB_LEAD;
            m.y = game.terrain.SurfaceHeight(m.x, m.z, player.Pos.y + 1f);
            var flat = M.Flat(m - Pos);
            float dist = Mathf.Max(flat.magnitude, 1f);
            wRunDir = flat / dist;
            float delay = Mathf.Max(Data.WYVERN_BOMB_MIN_WARNING, dist / Data.WYVERN_RUN_SPEED + Data.WYVERN_BOMB_FALL);
            wRunTime = delay - Data.WYVERN_BOMB_FALL;
            wRunSpeed = dist / wRunTime;
            wRunT = 0f;
            wState = "run";
            float dropY = Pos.y - 1f;
            WyvernBomb.Spawn(game, this, m, delay, dropY);
            // World 3: a second bomb further along the same line.
            if (game.stage >= 3)
            {
                var m2 = m + wRunDir * Data.WYVERN_BOMB_SECOND_GAP;
                m2.y = game.terrain.SurfaceHeight(m2.x, m2.z, player.Pos.y + 1f);
                WyvernBomb.Spawn(game, this, m2, delay + Data.WYVERN_BOMB_SECOND_GAP / wRunSpeed, dropY);
            }
            if (rig) rig.Action(0.5f, W_GLIDE);
            Sfx.Play("roar", Pos, -5f, 0.15f);
        }

        // Retreat: fly out and circle far from the hero until the timer runs out.
        void RetreatAi(float delta, Player player, ref Vector3 want, ref float wantY, ref float topSpeed)
        {
            wRetreatT -= delta;
            wOrbit += wOrbitDir * delta * 0.25f;
            want = player.Pos + new Vector3(Mathf.Cos(wOrbit), 0f, Mathf.Sin(wOrbit)) * 55f;
            wantY = RefGround(player, player.Pos) + Data.WYVERN_ALTITUDE + 6f;
            topSpeed = speed * 1.15f;
            if (wRetreatT <= 0f)
            {
                wHasPlan = false;   // choose afresh
                wPlan = WyvernPlan.Distract;
                wThinkT = 0f;
                wCommitT = 0f;
            }
        }

        // ---------------------------------------------------------------- movement and looks

        /// Smoothly accelerates toward a point (horizontal speed up to topSpeed, vertical toward wantY), keeping clear of other wyverns.
        void Steer(Vector3 want, float wantY, float topSpeed, bool straight, float delta, bool hover)
        {
            var to = M.Flat(want - Pos);
            float dist = to.magnitude;
            var desired = dist > 0.05f ? to / dist * topSpeed : Vector3.zero;
            if (!straight)
            {
                // Keep apart from other wyverns.
                foreach (var o in game.enemies)
                {
                    if (o == this || !o.IsWyvern || o.dead) continue;
                    var away = M.Flat(Pos - o.Pos);
                    float d = away.magnitude;
                    if (d < 9f && d > 0.01f) desired += away / d * (9f - d) * 1.6f;
                }
                if (dist < 3f) desired *= dist / 3f;
            }
            if (hover) desired = Vector3.zero;
            float accel = straight ? 70f : 16f;
            var flatVel = new Vector3(wVel.x, 0f, wVel.z);
            flatVel = Vector3.MoveTowards(flatVel, desired, accel * delta);
            float vy = hover ? 0f : Mathf.Clamp((wantY - Pos.y) * 1.6f, -9f, 9f);
            wVel = new Vector3(flatVel.x, Mathf.Lerp(wVel.y, vy, Mathf.Clamp01(delta * 4f)), flatVel.z);
        }

        void Face(float delta)
        {
            var flat = new Vector3(wVel.x, 0f, wVel.z);
            float prevYaw = yaw;
            if (flat.magnitude > 1.5f) yaw = M.LerpAngle(yaw, M.YawTo(flat), Mathf.Clamp01(delta * 5f));
            else
            {
                var toHero = M.Flat(game.player.Pos - Pos);
                if (toHero.magnitude > 0.5f) yaw = M.LerpAngle(yaw, M.YawTo(toHero), Mathf.Clamp01(delta * 2f));
            }
            transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(prevYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) / Mathf.Max(delta, 0.0001f), Mathf.Clamp01(delta * 6f));
        }

        void Visuals(float delta, bool grounded, float groundY)
        {
            // Lean into turns and pitch with climbing.
            bank = Mathf.Lerp(bank, Mathf.Clamp(yawRate * 0.35f, -35f, 35f), Mathf.Clamp01(delta * 5f));
            float pitch = Mathf.Clamp(-wVel.y * 2.5f, -25f, 25f);
            model.localRotation = Quaternion.Euler(pitch, 0f, bank);
            if (rig)
            {
                rig.Speed = frozenT > 0f ? 0f : 1f;
                float flat = new Vector2(wVel.x, wVel.z).magnitude;
                if (flat > 12f) rig.Loop(W_GLIDE);
                else if (flat > 3f) rig.Loop(W_FLY);
                else rig.Loop(W_HOVER);
            }
            else if (wingL != null && frozenT <= 0f)
            {
                wWingT += delta * 7f;
                float f = Mathf.Sin(wWingT) * 28f;
                wingL.localRotation = Quaternion.Euler(0f, 0f, -f);
                wingR.localRotation = Quaternion.Euler(0f, 0f, f);
            }
            // The shadow lies on the ground below, larger and fainter the higher it flies.
            if (wShadow != null)
            {
                float agl = Mathf.Max(0f, Pos.y - groundY);
                wShadow.SetPositionAndRotation(new Vector3(Pos.x, groundY + 0.08f, Pos.z), Quaternion.identity);
                float k = Mathf.Clamp01(agl / 30f);
                wShadow.localScale = new Vector3(Mathf.Lerp(3.4f, 2.2f, k), 1f, Mathf.Lerp(3.4f, 2.2f, k));
            }
        }

        // ---------------------------------------------------------------- death

        /// A wyvern's body drops out of the sky (the model is handed to a WyvernFall); returns true when it left one.
        bool LeaveWyvernCorpse()
        {
            if (model == null) return false;
            var go = new GameObject("Corpse_Wyvern");
            if (FX.root) go.transform.SetParent(FX.root, false);   // cleared with the stage
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);
            model.SetParent(go.transform, true);
            if (rig)
            {
                rig.SetEmission(Color.black);
                rig.Speed = 1f;
                rig.Action(10f, W_DIE);
            }
            var fall = go.AddComponent<WyvernFall>();
            fall.Init(game, wVel, tintMats);
            tintMats.Clear();   // the corpse owns the tint materials now
            model = null;
            rig = null;
            return true;
        }
    }

    /// A fire bomb dropped by a wyvern: a ground marker shows the whole run-up, then the bomb falls in view and bursts.
    /// It is dodgeable by simply leaving the marked circle, and it commits at once, so killing the wyvern does not cancel it.
    public class WyvernBomb : MonoBehaviour
    {
        Game game;
        Vector3 at;
        float delay, dropY, damage, t;
        Transform ball;
        bool falling;

        public static void Spawn(Game g, Enemy wyvern, Vector3 at, float delay, float dropY)
        {
            var b = new GameObject("WyvernBomb").AddComponent<WyvernBomb>();
            b.transform.SetParent(FX.root ? FX.root : g.transform, false);
            b.game = g;
            b.at = at;
            b.delay = delay;
            b.dropY = Mathf.Max(dropY, at.y + 4f);
            b.damage = wyvern.damage * Data.WYVERN_BOMB_DMG;
            FX.Telegraph(at, Data.WYVERN_BOMB_RADIUS, delay, new Color(1f, 0.35f, 0.08f, 0.45f));
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            if (game == null) { Destroy(gameObject); return; }
            t += delta;
            if (!falling && t >= delay - Data.WYVERN_BOMB_FALL)
            {
                falling = true;
                ball = FX.Node(transform, "Bomb");
                FX.NoShadow(FX.Part(ball, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.5f, 0.12f, 0.9f), 3f), Vector3.zero, Vector3.one * 1.1f));
                FX.NoShadow(FX.Part(ball, MeshGen.Sphere(8), FX.Glow(new Color(1f, 0.92f, 0.6f), 3.5f), Vector3.zero, Vector3.one * 0.6f));
                Sfx.Play("whoosh", at, -4f, 0.15f);
            }
            if (falling)
            {
                float k = Mathf.Clamp01((t - (delay - Data.WYVERN_BOMB_FALL)) / Data.WYVERN_BOMB_FALL);
                ball.position = new Vector3(at.x, Mathf.Lerp(dropY, at.y + 0.5f, k * k), at.z);
                if (M.RandF() < delta * 40f) FX.Burst(ball.position, new Color(1f, 0.5f, 0.1f), 2, 2f, 0.2f);
            }
            if (t >= delay) Explode();
        }

        void Explode()
        {
            float r = Data.WYVERN_BOMB_RADIUS;
            if (!FX.Vfx("explosion", at, r / 4f, 2.2f, null, 0.04f, new Color(1f, 0.5f, 0.15f)))
                FX.Flash(at + Vector3.up, r, new Color(1f, 0.5f, 0.12f, 0.85f), 0.4f);
            FX.Shockwave(at + new Vector3(0f, 0.3f, 0f), r * 1.2f, new Color(1f, 0.45f, 0.1f, 0.9f), 0.4f);
            FX.Burst(at + new Vector3(0f, 0.6f, 0f), new Color(1f, 0.55f, 0.15f), 22, 11f, 0.5f);
            Sfx.Play("boom", at, 0f, 0.08f);
            game.Shake(0.35f);
            var p = game.player;
            if (p != null && game.running && !game.traveling)
            {
                // A hero flying high above the ground spot is out of reach.
                bool low = p.Pos.y - at.y < Data.WYVERN_BOMB_REACH_Y;
                if (low && M.Flat(p.Pos - at).magnitude < r + p.HitRadius * 0.5f && !p.pet.TryWingGuard(p.Center))
                    p.TakeDamage(damage);   // no attacker: reflect/ignite effects must not touch a wyvern the hero can't hurt
            }
            Destroy(gameObject);
        }
    }

    /// A dead wyvern's body: tumbles out of the sky, crashes, lies there a moment and sinks away.
    public class WyvernFall : MonoBehaviour
    {
        Game game;
        Vector3 vel;
        float vy, t, landedAt = -1f;
        List<Material> mats;
        const float HOLD = 1.6f, SINK = 0.8f;

        public void Init(Game g, Vector3 velocity, List<Material> ownedMats)
        {
            game = g;
            vel = new Vector3(velocity.x, 0f, velocity.z) * 0.5f;
            vy = Mathf.Min(velocity.y, 0f);
            mats = new List<Material>(ownedMats);
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            if (game == null) { Destroy(gameObject); return; }
            t += delta;
            var p = transform.position;
            if (landedAt < 0f)
            {
                vy -= 30f * delta;
                p += (vel + new Vector3(0f, vy, 0f)) * delta;
                vel = Vector3.MoveTowards(vel, Vector3.zero, 4f * delta);
                transform.Rotate(0f, 0f, 160f * delta, Space.Self);   // tumbling
                float gh = game.terrain.SurfaceHeight(p.x, p.z, p.y + 1f);
                if (p.y <= gh + 0.5f)
                {
                    p.y = gh + 0.5f;
                    landedAt = t;
                    transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                    FX.Burst(p, new Color(0.6f, 0.5f, 0.4f), 20, 9f, 0.5f);
                    FX.Shockwave(p + new Vector3(0f, 0.2f, 0f), 4f, new Color(0.9f, 0.7f, 0.4f, 0.7f), 0.35f, false);
                    Sfx.Play("boom", p, -2f, 0.1f);
                    game.Shake(0.3f);
                }
                transform.position = p;
            }
            else
            {
                float since = t - landedAt;
                if (since > HOLD) transform.position += Vector3.down * (2.5f / SINK) * delta;
                if (since > HOLD + SINK) Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (mats != null) foreach (var m in mats) if (m) Destroy(m);
        }
    }
}
