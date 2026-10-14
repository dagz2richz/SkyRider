using System.Collections;
using UnityEngine;

namespace Skyfall
{
    /// Ogres of every size, and the giant ogre bosses: model, movement, status effects, boss abilities.
    public class Enemy : MonoBehaviour
    {
        static readonly Color[] SKINS =
        {
            new Color(0.42f, 0.5f, 0.3f), new Color(0.5f, 0.45f, 0.32f), new Color(0.38f, 0.42f, 0.38f),
            new Color(0.47f, 0.52f, 0.36f), new Color(0.55f, 0.42f, 0.33f),
        };

        public Game game;
        public string kind = "";
        public string displayName = "";
        public float size = 1f;
        public float hp = 10f, maxHp = 10f;
        public float speed = 4f, damage = 6f;
        public int xpValue = 1;
        public float radius = 0.5f;
        public bool isBoss;

        float attackCd;
        float slowT, slowF = 0.5f;
        public float stunT;
        public float frozenT;
        float shockT;
        float weakenT, weakenF;
        public bool lanceMarked;
        float bleedT, bleedDps;
        public float buffT;          // empowered by a shaman: faster and hits harder
        string role = "";            // "", "archer", "shaman"
        float shootCd = 2f, buffCd = 1f;
        Transform buffRing;
        float burnT, burnDps;
        public Vector3 knock;
        float pop, flash;
        public bool dead;
        float animT;
        Transform model;
        Material skinMat;
        Joint legL, legR, armL, armR;
        MeshRenderer ice;
        float yaw;

        // Boss state
        string[] abilities = new string[0];
        int abilityI;
        float abilityCd = 4f;
        string state = "";
        float stateT;
        Vector3 chargeDir;

        /// The final boss never leaves the floating island: movement and knockback are clamped to its footprint.
        public bool islandBound;
        WorldTerrain.Island island;
        const float EDGE_MARGIN = 3f;

        public static Enemy Make(Game g, string k, float hpMult, float dmgMult)
        {
            var e = new GameObject("Ogre_" + k).AddComponent<Enemy>();
            e.game = g;
            e.kind = k;
            var d = Data.OGRES[k];
            e.displayName = d.name;
            e.size = d.size * M.RandRange(0.92f, 1.08f);
            float s = e.size;
            e.maxHp = 14f * Mathf.Pow(s, 2.6f) * hpMult;
            e.hp = e.maxHp;
            e.speed = 5.2f / Mathf.Pow(s, 0.7f) * M.RandRange(0.9f, 1.1f);
            e.damage = 6f * s * dmgMult;
            e.xpValue = Mathf.Max(1, Mathf.RoundToInt(Mathf.Pow(s, 2.2f)));
            e.radius = 0.55f * s;
            e.animT = M.RandF() * 10f;
            e.role = d.role;
            if (e.TrySkin(false, -1)) return e;
            switch (e.role)
            {
                case "archer":
                    e.maxHp *= 0.8f;
                    e.hp = e.maxHp;
                    e.xpValue = 2;
                    e.shootCd = M.RandRange(1f, 2.5f);
                    e.BuildOgre(new Color(0.45f, 0.42f, 0.3f), false, false);
                    e.AddBow();
                    break;
                case "shaman":
                    e.maxHp *= 1.2f;
                    e.hp = e.maxHp;
                    e.speed *= 0.8f;
                    e.xpValue = 3;
                    e.BuildOgre(new Color(0.35f, 0.45f, 0.4f), false, false);
                    e.AddStaff();
                    break;
                default:
                    e.BuildOgre(SKINS[Random.Range(0, SKINS.Length)], s >= 1f || M.RandF() < 0.3f, false);
                    break;
            }
            return e;
        }

        public static Enemy MakeBoss(Game g, int idx, float hpMult, float dmgMult = 1f)
        {
            var d = Data.BOSSES[idx];
            var e = new GameObject("Boss_" + idx).AddComponent<Enemy>();
            e.game = g;
            e.kind = "boss";
            e.displayName = d.name;
            e.size = d.size;
            e.maxHp = d.hp * hpMult;
            e.hp = e.maxHp;
            e.speed = d.speed;
            e.damage = d.damage * dmgMult;
            e.radius = 0.55f * e.size;
            e.abilities = d.abilities;
            e.isBoss = true;
            e.xpValue = 60 + idx * 60;
            e.abilityCd = 3f;
            if (!e.TrySkin(true, idx)) e.BuildOgre(d.skin, true, true);
            return e;
        }

        // ---------------------------------------------------------------- Asset Store models

        CharacterRig rig;

        /// Uses an Asset Store model when the skin library has one for this ogre. Returns false to fall back
        /// to the procedural model.
        bool TrySkin(bool boss, int bossIdx)
        {
            var lib = Skins.Lib;
            if (lib == null) return false;
            GameObject prefab;
            RuntimeAnimatorController ctrl = null;
            bool isOgreModel = false;
            if (boss)
            {
                if (bossIdx == Data.MINI_BOSS && lib.golem) prefab = lib.golem;
                else if (bossIdx == Data.FINAL_BOSS && lib.golemKing) prefab = lib.golemKing;
                else { prefab = Skins.Pick(lib.bossOgres) ?? Skins.Pick(lib.ogres); isOgreModel = true; }
            }
            else if (role == "archer") prefab = lib.archer;
            else if (role == "shaman") prefab = lib.shaman;
            else { prefab = Skins.Pick(lib.ogres); isOgreModel = true; }
            if (prefab == null) return false;
            if (isOgreModel) ctrl = lib.ogreController;

            model = FX.Node(transform, "Model");
            rig = Skins.SpawnCharacter(prefab, model, 2.0f * size, ctrl);
            var hand = rig.Bone(HumanBodyBones.RightHand);
            var leftHand = rig.Bone(HumanBodyBones.LeftHand);
            var headBone = rig.Bone(HumanBodyBones.Head);
            float boneScale = rig.transform.localScale.x;
            if (isOgreModel && lib.ogreWeapon && hand)
            {
                // The pack's demo scene places the club loose in the world, so it is attached here by hand:
                // held in the right hand, pointing forward and a little down in the bind pose.
                var w = Instantiate(lib.ogreWeapon, hand, false);
                w.transform.localPosition = Vector3.zero;
                w.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.4f, -1f));
                w.transform.localScale = Vector3.one * boneScale / Mathf.Max(0.0001f, hand.lossyScale.x);
            }
            if (role == "archer" && leftHand)
            {
                var wood = FX.Lit(new Color(0.32f, 0.2f, 0.1f));
                var bow = FX.Node(leftHand, "Bow");
                bow.localScale = Vector3.one * size / Mathf.Max(0.0001f, leftHand.lossyScale.x);
                foreach (int k in new[] { -1, 1 })
                    FX.Part(bow, MeshGen.Box(), wood, new Vector3(0, k * 0.45f, -0.12f), new Vector3(0.07f, 0.95f, 0.07f), new Vector3(k * -0.35f, 0, 0));
                FX.Part(bow, MeshGen.Box(), FX.Lit(new Color(0.85f, 0.85f, 0.8f)), new Vector3(0, 0, 0.02f), new Vector3(0.015f, 1.7f, 0.015f));
            }
            if (role == "shaman")
            {
                if (hand)
                {
                    var st = FX.Node(hand, "Staff");
                    st.localScale = Vector3.one * size / Mathf.Max(0.0001f, hand.lossyScale.x);
                    FX.Part(st, MeshGen.Box(), FX.Lit(Props.BARK), new Vector3(0, 0.3f, 0), new Vector3(0.08f, 2.4f, 0.08f));
                    FX.Part(st, MeshGen.Sphere(8), FX.Lit(new Color(0.9f, 0.86f, 0.75f)), new Vector3(0, 1.55f, 0), new Vector3(0.3f, 0.32f, 0.3f));
                    FX.Part(st, MeshGen.Sphere(8), FX.Glow(new Color(0.3f, 1f, 0.4f), 3f), new Vector3(0, 1.55f, -0.12f), Vector3.one * 0.12f);
                }
                var ring = FX.Part(transform, MeshGen.Ring(), FX.Glow(new Color(0.3f, 1f, 0.4f, 0.5f), 1.3f), new Vector3(0, 0.2f, 0), new Vector3(9f, 0.5f, 9f));
                FX.NoShadow(ring);
                buffRing = ring.transform;
            }
            if (boss && headBone)
            {
                var gold = FX.Lit(new Color(0.95f, 0.75f, 0.25f), 0.3f, 0.9f);
                var crown = FX.Node(headBone, "Crown");
                crown.localScale = Vector3.one * size / Mathf.Max(0.0001f, headBone.lossyScale.x);
                crown.position = headBone.position + Vector3.up * 0.32f * size;
                crown.rotation = Quaternion.identity;
                FX.Part(crown, MeshGen.Cylinder(0.24f, 0.24f, 10), gold, Vector3.zero, new Vector3(1f, 0.12f, 1f));
                for (int i = 0; i < 6; i++)
                {
                    float a = M.TAU * i / 6f;
                    FX.Part(crown, MeshGen.Cylinder(0f, 0.5f, 4), gold, new Vector3(Mathf.Cos(a) * 0.2f, 0.11f, Mathf.Sin(a) * 0.2f), new Vector3(0.06f, 0.16f, 0.06f));
                }
            }
            ice = FX.Part(transform, MeshGen.Sphere(10), FX.Lit(new Color(0.55f, 0.8f, 1.0f, 0.3f), 0.05f, 0f, 0.5f),
                new Vector3(0, 1.2f * size, 0), new Vector3(1.6f, 2.6f, 1.6f) * size);
            FX.NoShadow(ice);
            ice.enabled = false;
            rig.Loop("Idle", "Idle01");
            return true;
        }

        void OnDestroy()
        {
            if (skinMat) Destroy(skinMat);
        }

        public Vector3 Pos
        {
            get => transform.position;
            set => transform.position = value;
        }

        // ---------------------------------------------------------------- model

        MeshRenderer P(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scl, Vector3 rot = default) =>
            FX.Part(parent, mesh, mat, pos, scl, rot);

        void BuildOgre(Color skin, bool club, bool boss)
        {
            model = FX.Node(transform, "Model");
            var body = FX.Node(model, "Body");
            body.localScale = Vector3.one * size;
            skinMat = FX.NewLit(M.Lighten(skin, M.RandRange(-0.06f, 0.06f)), 0.85f);
            var belly = skinMat;
            var cloth = FX.Lit(new Color(0.32f, 0.22f, 0.14f));
            var ivory = FX.Lit(new Color(0.9f, 0.86f, 0.72f), 0.5f);
            var sph = MeshGen.Sphere(12);
            var cap = MeshGen.Capsule();
            // Legs.
            legL = new Joint(FX.Node(body, "LegL", new Vector3(-0.26f, 0.75f, 0)));
            legR = new Joint(FX.Node(body, "LegR", new Vector3(0.26f, 0.75f, 0)));
            foreach (var leg in new[] { legL, legR })
            {
                P(leg.t, cap, skinMat, new Vector3(0, -0.36f, 0), new Vector3(0.36f, 0.38f, 0.36f));
                P(leg.t, sph, skinMat, new Vector3(0, -0.72f, -0.08f), new Vector3(0.34f, 0.16f, 0.42f));
            }
            // Belly, chest, loincloth.
            P(body, sph, belly, new Vector3(0, 1.15f, -0.05f), new Vector3(1.1f, 1.0f, 0.95f));
            P(body, sph, skinMat, new Vector3(0, 1.6f, 0.05f), new Vector3(1.25f, 0.72f, 0.82f));
            P(body, MeshGen.Box(), cloth, new Vector3(0, 0.82f, -0.05f), new Vector3(0.95f, 0.35f, 0.85f));
            P(body, MeshGen.Box(), cloth, new Vector3(0, 0.6f, -0.42f), new Vector3(0.4f, 0.4f, 0.05f));
            // Head hunched forward, with tusks and glowing eyes.
            var head = FX.Node(body, "Head", new Vector3(0, 1.95f, -0.32f));
            P(head, sph, skinMat, Vector3.zero, new Vector3(0.5f, 0.46f, 0.5f));
            P(head, sph, skinMat, new Vector3(0, -0.14f, -0.12f), new Vector3(0.48f, 0.24f, 0.42f));
            P(head, sph, skinMat, new Vector3(0, 0.02f, -0.25f), new Vector3(0.16f, 0.14f, 0.16f));
            foreach (int sd in new[] { -1, 1 })
            {
                P(head, sph, FX.Glow(new Color(1f, 0.75f, 0.2f), 2.5f), new Vector3(sd * 0.11f, 0.07f, -0.22f), Vector3.one * 0.07f);
                P(head, MeshGen.Cylinder(0f, 0.5f, 5), ivory, new Vector3(sd * 0.15f, -0.08f, -0.28f), new Vector3(0.06f, 0.22f, 0.06f), new Vector3(-0.2f, 0, sd * -0.25f));
                P(head, MeshGen.Cylinder(0f, 0.5f, 4), skinMat, new Vector3(sd * 0.26f, 0.05f, 0f), new Vector3(0.08f, 0.2f, 0.12f), new Vector3(0, 0, sd * -1.3f));
            }
            if (boss)
            {
                var gold = FX.Lit(new Color(0.95f, 0.75f, 0.25f), 0.3f, 0.9f);
                P(head, MeshGen.Cylinder(0.24f, 0.24f, 10), gold, new Vector3(0, 0.25f, 0), new Vector3(1f, 0.12f, 1f));
                for (int i = 0; i < 6; i++)
                {
                    float a = M.TAU * i / 6f;
                    P(head, MeshGen.Cylinder(0f, 0.5f, 4), gold, new Vector3(Mathf.Cos(a) * 0.2f, 0.36f, Mathf.Sin(a) * 0.2f), new Vector3(0.06f, 0.16f, 0.06f));
                }
            }
            // Long arms.
            armL = new Joint(FX.Node(body, "ArmL", new Vector3(-0.62f, 1.72f, 0)));
            armR = new Joint(FX.Node(body, "ArmR", new Vector3(0.62f, 1.72f, 0)));
            foreach (var (arm, sd) in new[] { (armL, -1), (armR, 1) })
            {
                P(arm.t, sph, skinMat, Vector3.zero, Vector3.one * 0.42f);
                P(arm.t, cap, skinMat, new Vector3(sd * 0.06f, -0.5f, -0.05f), new Vector3(0.28f, 0.42f, 0.28f), new Vector3(0, 0, sd * 0.12f));
                P(arm.t, sph, skinMat, new Vector3(sd * 0.1f, -1.0f, -0.1f), Vector3.one * 0.3f);
            }
            if (club)
            {
                var c = FX.Node(armR.t, "Club", new Vector3(0.1f, -1.0f, -0.1f), new Vector3(-1.2f, 0, 0));
                P(c, MeshGen.Cylinder(0.5f, 0.25f, 8), FX.Lit(Props.BARK), new Vector3(0, 0.55f, 0), new Vector3(0.24f, 1.3f, 0.24f));
                if (boss || size > 1.3f)
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.6f;
                        P(c, MeshGen.Cylinder(0f, 0.5f, 4), FX.Lit(Props.STEEL, 0.4f, 0.7f), new Vector3(Mathf.Cos(a) * 0.12f, 0.85f + i * 0.1f, Mathf.Sin(a) * 0.12f),
                            new Vector3(0.05f, 0.18f, 0.05f), new Vector3(0, 0, Mathf.Cos(a) * 1.4f));
                    }
            }
            // Ice shell shown while frozen.
            ice = P(transform, MeshGen.Sphere(10), FX.Lit(new Color(0.55f, 0.8f, 1.0f, 0.3f), 0.05f, 0f, 0.5f),
                new Vector3(0, 1.2f * size, 0), new Vector3(1.6f, 2.6f, 1.6f) * size);
            FX.NoShadow(ice);
            ice.enabled = false;
        }

        void AddBow()
        {
            var wood = FX.Lit(new Color(0.32f, 0.2f, 0.1f));
            var bow = FX.Node(armL.t, "Bow", new Vector3(-0.1f, -1.0f, -0.2f));
            foreach (int k in new[] { -1, 1 })
                P(bow, MeshGen.Box(), wood, new Vector3(0, k * 0.45f, -0.12f), new Vector3(0.07f, 0.95f, 0.07f), new Vector3(k * -0.35f, 0, 0));
            P(bow, MeshGen.Box(), FX.Lit(new Color(0.85f, 0.85f, 0.8f)), new Vector3(0, 0, 0.02f), new Vector3(0.015f, 1.7f, 0.015f));
            var quiver = P(model, MeshGen.Cylinder(0.5f, 0.5f, 8), FX.Lit(new Color(0.3f, 0.18f, 0.1f)), new Vector3(0.25f, 1.7f * size, 0.45f * size),
                new Vector3(0.25f, 0.8f, 0.25f) * size, new Vector3(0.3f, 0, -0.3f));
            FX.NoShadow(quiver);
        }

        void AddStaff()
        {
            var st = FX.Node(armR.t, "Staff", new Vector3(0.1f, -1.0f, -0.1f));
            P(st, MeshGen.Box(), FX.Lit(Props.BARK), new Vector3(0, 0.3f, 0), new Vector3(0.08f, 2.4f, 0.08f));
            P(st, MeshGen.Sphere(8), FX.Lit(new Color(0.9f, 0.86f, 0.75f)), new Vector3(0, 1.55f, 0), new Vector3(0.3f, 0.32f, 0.3f));
            P(st, MeshGen.Sphere(8), FX.Glow(new Color(0.3f, 1f, 0.4f), 3f), new Vector3(0, 1.55f, -0.12f), Vector3.one * 0.12f);
            // Bone mask and feathers.
            P(model, MeshGen.Box(), FX.Lit(new Color(0.9f, 0.86f, 0.75f)), new Vector3(0, 1.98f * size, -0.55f * size), new Vector3(0.38f, 0.32f, 0.08f) * size);
            Color[] fc = { new Color(0.8f, 0.2f, 0.1f), new Color(0.2f, 0.6f, 0.3f), new Color(0.9f, 0.8f, 0.2f) };
            for (int k = 0; k < 3; k++)
                P(model, MeshGen.Box(), FX.Lit(fc[k]), new Vector3((k - 1) * 0.12f * size, 2.35f * size, -0.25f * size), new Vector3(0.05f, 0.4f, 0.02f) * size,
                    new Vector3(0, 0, (k - 1) * 0.3f));
            var ring = P(transform, MeshGen.Ring(), FX.Glow(new Color(0.3f, 1f, 0.4f, 0.5f), 1.3f), new Vector3(0, 0.2f, 0), new Vector3(9f, 0.5f, 9f));
            FX.NoShadow(ring);
            buffRing = ring.transform;
        }

        // ---------------------------------------------------------------- status effects

        /// Bleed: the Blood Huntress's javelins open wounds that keep dripping damage.
        public void Bleed(float t, float dps)
        {
            bleedT = Mathf.Max(bleedT, t);
            bleedDps = Mathf.Max(bleedDps, dps);
        }

        public bool Buffed => buffT > 0f;

        public void HitPop()
        {
            pop = 1f;
            flash = 1f;
        }

        /// Shock: a 1-second stun on everything but bosses, with crackling sparks.
        public void Shock(float t)
        {
            shockT = Mathf.Max(shockT, t);
            if (!isBoss) stunT = Mathf.Max(stunT, t);
            if (M.RandF() < 0.5f) FX.Burst(Pos + new Vector3(0, size, 0), new Color(0.7f, 0.8f, 1f), 6, 5f, 0.15f);
        }

        /// Weaken: the ogre's attacks deal less damage for a while.
        public void Weaken(float t, float amount)
        {
            weakenT = Mathf.Max(weakenT, t);
            weakenF = Mathf.Max(weakenF, amount);
        }

        public float AtkMult => 1f - (weakenT > 0f ? weakenF : 0f);

        /// Ignite: burns for a while.
        public void Ignite(float t, float dps)
        {
            burnT = Mathf.Max(burnT, t);
            burnDps = Mathf.Max(burnDps, dps);
        }

        public void Freeze(float t)
        {
            if (isBoss)
            {
                slowT = Mathf.Max(slowT, t);
                slowF = 0.5f;
                return;
            }
            frozenT = Mathf.Max(frozenT, t);
            stunT = Mathf.Max(stunT, t);
        }

        void SetEmission(Color c, float energy)
        {
            if (rig) rig.SetEmission((Color)FX.Lin(c, energy));
            else FX.SetLin(skinMat, "_EmissionColor", c, energy);
        }

        // ---------------------------------------------------------------- update

        public void Tick(float delta, Player player)
        {
            animT += delta;
            if (burnT > 0f)
            {
                burnT -= delta;
                game.DamageEnemy(this, burnDps * delta, Vector3.zero, 0f, false);
            }
            if (bleedT > 0f)
            {
                bleedT -= delta;
                game.DamageEnemy(this, bleedDps * delta, Vector3.zero, 0f, false);
                if (M.RandF() < delta * 6f) FX.Burst(Pos + new Vector3(0, size, 0), new Color(0.7f, 0f, 0.03f), 3, 2f, 0.12f);
            }
            if (dead) return;
            slowT -= delta;
            attackCd -= delta;
            frozenT = Mathf.Max(frozenT - delta, 0f);
            ice.enabled = frozenT > 0f;
            pop = Mathf.MoveTowards(pop, 0f, delta * 6f);
            flash = Mathf.MoveTowards(flash, 0f, delta * 8f);
            shockT = Mathf.Max(shockT - delta, 0f);
            weakenT = Mathf.Max(weakenT - delta, 0f);
            buffT = Mathf.Max(buffT - delta, 0f);
            if (weakenT <= 0f) weakenF = 0f;
            // Status glow: white hit flash, blue crackle when shocked, orange flicker when burning.
            if (flash > 0.05f) SetEmission(Color.white, flash * 1.2f);
            else if (shockT > 0f) SetEmission(new Color(0.5f, 0.65f, 1f), 0.6f + M.RandF() * 0.8f);
            else if (burnT > 0f) SetEmission(new Color(1f, 0.45f, 0.1f), 0.35f + Mathf.Sin(animT * 18f) * 0.2f);
            else if (weakenT > 0f) SetEmission(new Color(0.6f, 0f, 0.05f), 0.5f);
            else if (buffT > 0f) SetEmission(new Color(0.25f, 1f, 0.35f), 0.35f + Mathf.Sin(animT * 6f) * 0.1f);
            else SetEmission(Color.black, 0f);
            model.localScale = Vector3.one * (1f + pop * 0.18f);

            var terrain = game.terrain;
            var to = M.Flat(player.Pos - Pos);
            float dist = to.magnitude;
            var dir = to / Mathf.Max(dist, 0.001f);
            float spd = speed * (slowT > 0f ? slowF : 1f) * (buffT > 0f ? 1.3f : 1f);

            var move = Vector3.zero;
            if (stunT > 0f) stunT -= delta;
            else if (isBoss) move = BossAi(delta, player, dir, dist, spd);
            else if (role == "archer")
            {
                // Keep at bow range and loose arrows.
                if (dist > 18f) move = dir * spd;
                else if (dist < 11f) move = -dir * spd * 0.7f;
                shootCd -= delta;
                if (shootCd <= 0f && dist < 30f)
                {
                    shootCd = M.RandRange(2.2f, 3f) / (buffT > 0f ? 1.3f : 1f);
                    ShootArrow(player);
                }
            }
            else if (role == "shaman")
            {
                // Hang back behind the pack and empower nearby ogres.
                if (dist > 12f) move = dir * spd;
                else if (dist < 8f) move = -dir * spd * 0.6f;
                buffCd -= delta;
                if (buffCd <= 0f)
                {
                    buffCd = 1f;
                    foreach (var o in game.EnemiesInRadius(Pos, 9f, 6f))
                        if (o != this && !o.isBoss) o.buffT = Mathf.Max(o.buffT, 1.6f);
                    FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 9f, new Color(0.3f, 1f, 0.4f, 0.5f), 0.6f);
                }
                if (buffRing) buffRing.Rotate(0f, delta * Mathf.Rad2Deg, 0f, Space.Self);
            }
            else move = dir * spd;

            var p = Pos + (move + knock) * delta;
            knock = Vector3.Lerp(knock, Vector3.zero, Mathf.Clamp01(delta * 7f));
            if (islandBound && island != null)
            {
                bool clamped = ClampToIsland(ref p);
                if (clamped && state == "charge")
                {
                    // The charge ends at the edge with a crash.
                    state = "";
                    FX.Shockwave(p + new Vector3(0, 0.3f, 0), 7f, new Color(0.8f, 0.6f, 0.4f, 0.8f), 0.35f);
                    FX.Burst(p + new Vector3(0, 0.5f, 0), new Color(0.5f, 0.4f, 0.3f), 14, 6f, 0.5f);
                    game.Shake(0.3f);
                }
                p.y = terrain.SurfaceHeight(p.x, p.z, island.topY);
            }
            else p.y = terrain.SurfaceHeight(p.x, p.z, Pos.y);
            Pos = p;
            if (dist > 0.1f && state != "charge" && frozenT <= 0f)
            {
                yaw = M.LerpAngle(yaw, M.YawTo(dir), Mathf.Clamp01(delta * 8f));
                transform.rotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            }

            // Lumbering walk.
            if (rig)
            {
                rig.Speed = frozenT > 0f ? 0f : Mathf.Clamp(spd / 4.5f, 0.6f, 1.6f);
                if (state == "charge") rig.Loop("Run", "Walk");
                else if (stunT > 0f || move.magnitude <= 0.1f) rig.Loop("Idle", "Idle01");
                else if (spd > 5.5f) rig.Loop("Run", "Walk");
                else rig.Loop("Walk", "Run");
            }
            else if (frozenT <= 0f)
            {
                float moving = move.magnitude > 0.1f ? 1f : 0.2f;
                float w = animT * (spd * 1.4f + 1f);
                legL.RotX = Mathf.Sin(w) * 0.5f * moving;
                legR.RotX = -Mathf.Sin(w) * 0.5f * moving;
                armL.RotX = -Mathf.Sin(w) * 0.35f * moving;
                if (state != "windup") armR.RotX = Mathf.Sin(w) * 0.35f * moving;
                model.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(w)) * 0.08f * size, 0);
                model.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(w) * 0.05f * Mathf.Rad2Deg);
            }

            // Melee contact.
            float vdiff = Mathf.Abs(player.Center.y - (Pos.y + size));
            if (dist < radius + player.HitRadius && vdiff < 1.4f + size && attackCd <= 0f && frozenT <= 0f)
            {
                attackCd = 1f;
                player.TakeDamage(damage * AtkMult * (buffT > 0f ? 1.3f : 1f), this);
                if (rig) rig.Action(0.7f, "Attack01", "Attack02");
                else armR.KeysX((-2f, 0.1f), (0.4f, 0.15f));
            }
        }

        void ShootArrow(Player player)
        {
            var from = Pos + new Vector3(0, 1.6f * size, 0);
            var target = player.Center + player.velocity * 0.25f;
            OgreArrow.Spawn(game, from, (target - from).normalized * 34f, damage * AtkMult * (buffT > 0f ? 1.3f : 1f));
            if (rig) rig.Action(0.6f, "Attack02", "Attack01");
            else armL.KeysX((-1.5f, 0.15f), (0f, 0.3f));
            Sfx.Play("bow", Pos, -8f, 0.2f);
        }

        // ---------------------------------------------------------------- bosses

        Vector3 BossAi(float delta, Player player, Vector3 dir, float dist, float spd)
        {
            switch (state)
            {
                case "windup":
                    stateT -= delta;
                    if (armR != null) armR.RotX = Mathf.Lerp(armR.RotX, -2.4f, delta * 8f);
                    if (stateT <= 0f)
                    {
                        state = "charge";
                        stateT = 0.9f;
                    }
                    return Vector3.zero;
                case "charge":
                    stateT -= delta;
                    if (stateT <= 0f) state = "";
                    if (M.RandF() < 0.3f) FX.Burst(Pos + new Vector3(0, 0.5f, 0), new Color(0.5f, 0.4f, 0.3f), 6, 4f, 0.6f);
                    return chargeDir * spd * 7f;
            }
            abilityCd -= delta;
            bool stranded = islandBound && !PlayerOnIsland(player);
            if (abilityCd <= 0f && dist < 45f)
            {
                string ab = abilities[abilityI % abilities.Length];
                abilityI++;
                // With the player below the island only ranged attacks and summons make sense.
                if (stranded && ab != "boulders" && ab != "summon") ab = "boulders";
                abilityCd = M.RandRange(3.5f, 5f);
                UseAbility(ab, player, dir);
            }
            // If the player is off the island the boss waits at the edge, facing them.
            if (stranded && !IslandRoom(Pos, dir)) return Vector3.zero;
            return dir * spd;
        }

        /// Pins this boss to the floating island (final battle).
        public void BindToIsland(WorldTerrain.Island isl)
        {
            island = isl;
            islandBound = isl != null;
        }

        /// True while the player stands on (or hovers above) the boss island.
        public bool PlayerOnIsland(Player player) =>
            island != null && island.Contains(player.Pos.x, player.Pos.z) && player.Pos.y > island.topY - 2f;

        /// Largest distance from the island center this boss may walk to in direction angle a.
        float EdgeLimit(float a) => Mathf.Max(2f, island.EdgeRadius(a) - EDGE_MARGIN);

        /// Clamps p (x/z) to the island footprint. Returns true if it had to be pulled back.
        bool ClampToIsland(ref Vector3 p)
        {
            var c = island.center;
            float dx = p.x - c.x, dz = p.z - c.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float lim = EdgeLimit(Mathf.Atan2(dz, dx));
            if (d <= lim) return false;
            p.x = c.x + dx / d * lim;
            p.z = c.z + dz / d * lim;
            return true;
        }

        /// Whether there is still room to step from `from` in direction `dir` without hitting the island rim.
        bool IslandRoom(Vector3 from, Vector3 dir)
        {
            var q = from + dir * 1.2f;
            float dx = q.x - island.center.x, dz = q.z - island.center.z;
            return Mathf.Sqrt(dx * dx + dz * dz) < EdgeLimit(Mathf.Atan2(dz, dx)) - 0.2f;
        }

        /// Pulls a point back inside the island footprint (for adds summoned by the island boss).
        public Vector3 InsideIsland(Vector3 want)
        {
            if (island != null) ClampToIsland(ref want);
            return want;
        }

        void UseAbility(string ab, Player player, Vector3 dir)
        {
            switch (ab)
            {
                case "slam":
                {
                    var at = player.Pos;
                    float rad = 6f + size;
                    FX.Telegraph(at, rad, 1.1f);
                    if (rig)
                    {
                        rig.Action(1.3f, "Attack02", "Attack01");
                        Tw.Run(SlamRoutine(at, rad, player));
                    }
                    else armR.Play(SlamRoutine(at, rad, player));
                    break;
                }
                case "charge":
                    state = "windup";
                    stateT = 0.7f;
                    chargeDir = dir;
                    if (rig) rig.Action(0.7f, "Victory", "Attack02");
                    FX.Telegraph(Pos, radius + 1.5f, 0.7f, new Color(1f, 0.5f, 0.1f, 0.4f));
                    Sfx.Play("roar", Pos, -4f, 0.15f);
                    break;
                case "boulders":
                {
                    if (rig) rig.Action(0.9f, "Attack01", "Attack02");
                    int count = 3 + (int)size;
                    for (int i = 0; i < count; i++)
                    {
                        var target = player.Center + new Vector3(M.RandRange(-5f, 5f), 0, M.RandRange(-5f, 5f)) * (i == 0 ? 0f : 1f);
                        var from = Pos + new Vector3(0, 2.2f * size, 0);
                        Boulder.Spawn(game, from, target, 1.1f + i * 0.08f, damage * AtkMult);
                    }
                    break;
                }
                case "summon":
                    game.hud.Toast($"{displayName} calls the clan!");
                    Sfx.Play("roar", Pos, 0f, 0.1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = M.TAU * i / 6f;
                        var sp = Pos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (radius + 4f);
                        if (islandBound) game.SpawnEnemy(i % 3 == 0 ? "brute" : "ogre", InsideIsland(sp), island.topY);
                        else game.SpawnEnemy(i % 3 == 0 ? "brute" : "ogre", sp);
                    }
                    break;
            }
        }

        IEnumerator SlamRoutine(Vector3 at, float rad, Player player)
        {
            if (armR != null)
            {
                yield return Tw.To(() => armR.RotX, v => armR.RotX = v, -2.6f, 0.9f, this);
                yield return Tw.To(() => armR.RotX, v => armR.RotX = v, 0.6f, 0.15f, this);
            }
            else yield return Tw.Wait(1.05f);
            if (this == null || dead || player == null) yield break;
            FX.Vfx("ground_explosion", at, rad / 6f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad, new Color(1f, 0.4f, 0.2f, 0.9f), 0.4f);
            FX.Burst(at + new Vector3(0, 0.5f, 0), new Color(0.55f, 0.42f, 0.3f), 30, 12f, 0.7f);
            Sfx.Play("boom", at, 2f);
            game.Shake(0.6f);
            var d = player.Pos - at;
            float gh = game.terrain.SurfaceHeight(player.Pos.x, player.Pos.z, player.Pos.y);
            if (new Vector2(d.x, d.z).magnitude < rad && player.Pos.y - gh < 3f)
                player.TakeDamage(damage * 1.4f * AtkMult);
        }
    }

    /// A boulder hurled by an ogre boss: flies in an arc and bursts on the ground or the player.
    public class Boulder : MonoBehaviour
    {
        Game game;
        float damage = 10f;
        Vector3 velocity;
        float life = 4f;
        const float G = 20f;
        Vector3 spin = new Vector3(3, 2, 1);

        public static void Spawn(Game g, Vector3 from, Vector3 to, float flightTime, float damage)
        {
            var b = new GameObject("Boulder").AddComponent<Boulder>();
            b.game = g;
            b.damage = damage;
            var d = to - from;
            b.velocity = new Vector3(d.x / flightTime, (d.y + 0.5f * G * flightTime * flightTime) / flightTime, d.z / flightTime);
            FX.Telegraph(new Vector3(to.x, g.terrain.SurfaceHeight(to.x, to.z, to.y), to.z), 2.6f, flightTime, new Color(1f, 0.3f, 0.1f, 0.35f));
            FX.Part(b.transform, MeshGen.Sphere(7), FX.Lit(Props.STONE_DARK), Vector3.zero, new Vector3(1.1f, 0.9f, 1f));
            b.transform.position = from;
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            life -= delta;
            velocity.y -= G * delta;
            transform.position += velocity * delta;
            transform.Rotate(spin * delta * Mathf.Rad2Deg, Space.Self);
            var p = game.player;
            var pos = transform.position;
            bool hitPlayer = p != null && Vector3.Distance(p.Center, pos) < p.HitRadius + 0.8f;
            float gh = game.terrain.SurfaceHeight(pos.x, pos.z, pos.y - velocity.y * delta);
            if (hitPlayer || pos.y < gh || life <= 0f)
            {
                FX.Burst(pos, new Color(0.45f, 0.4f, 0.35f), 14, 7f, 0.4f);
                FX.Shockwave(pos + new Vector3(0, 0.2f, 0), 2.6f, new Color(0.7f, 0.5f, 0.3f, 0.7f), 0.3f);
                Sfx.Play("arrow_hit", pos, 2f);
                FX.Vfx("stones_hit", pos, 1f, 2f);
                if (p != null && Vector3.Distance(p.Center, pos) < 2.6f) p.TakeDamage(damage);
                Destroy(gameObject);
            }
        }
    }

    /// An ogre archer's arrow.
    public class OgreArrow : MonoBehaviour
    {
        Game game;
        Vector3 velocity;
        float damage = 6f;
        float life = 2f;

        public static void Spawn(Game g, Vector3 from, Vector3 velocity, float damage)
        {
            var a = new GameObject("Arrow").AddComponent<OgreArrow>();
            a.game = g;
            a.velocity = velocity;
            a.damage = damage;
            FX.Part(a.transform, MeshGen.Box(), FX.Lit(new Color(0.4f, 0.28f, 0.15f)), Vector3.zero, new Vector3(0.06f, 0.06f, 1.1f));
            FX.Part(a.transform, MeshGen.Box(), FX.Glow(new Color(1f, 0.4f, 0.3f), 1.5f), new Vector3(0, 0, -0.6f), new Vector3(0.1f, 0.1f, 0.2f));
            a.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            life -= delta;
            velocity.y -= 3f * delta;
            transform.position += velocity * delta;
            transform.rotation = M.FaceNegZ(velocity);
            var p = game.player;
            var pos = transform.position;
            if (p != null && Vector3.Distance(p.Center, pos) < p.HitRadius + 0.3f)
            {
                p.TakeDamage(damage);
                Destroy(gameObject);
                return;
            }
            if (life <= 0f || pos.y < game.terrain.Height(pos.x, pos.z)) Destroy(gameObject);
        }
    }
}
