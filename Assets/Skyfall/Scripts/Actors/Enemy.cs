using System.Collections;
using UnityEngine;

namespace Skyfall
{
    /// Ogres of every size, and the giant ogre bosses: model, movement, status effects, boss abilities.
    public partial class Enemy : MonoBehaviour   // WYVERN logic lives in Wyvern.cs
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
        /// For bosses: Data.MINI_BOSS or Data.FINAL_BOSS, and the model the stage gave it ("", "ogre", "dragon").
        public int bossIdx = -1;
        string bossModel = "";

        float attackCd;
        float slowT, slowF = 0.5f;
        public float stunT;
        public float frozenT;
        float shockT;
        float weakenT, weakenF;
        public bool lanceMarked;
        float bleedT, bleedDps;
        public float buffT;          // empowered by a shaman: faster and hits harder (no glow: the buff is stats only)
        string role = "";            // "", "archer", "shaman", "harpooner"
        float shootCd = 2f, buffCd = 1f;
        float harpoonWind;           // harpooner: seconds left in the telegraphed wind-up before a throw
        Vector3 harpoonAim;
        /// When any harpooner last started a wind-up (they take turns, Data.HARPOON_STAGGER apart).
        static float lastHarpoonWindup = -99f;
        float burnT, burnDps;
        float poisonT, poisonDps;
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
            if (k == "wyvern") return MakeWyvern(g, hpMult, dmgMult);
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
                    e.shootCd = M.RandRange(2.5f, Data.POTION_COOLDOWN_MAX);
                    e.BuildOgre(new Color(0.45f, 0.42f, 0.3f), false, false);
                    e.AddBow();
                    break;
                case "harpooner":
                    e.maxHp *= 1.3f;
                    e.hp = e.maxHp;
                    e.xpValue = 4;
                    e.shootCd = M.RandRange(2f, 4f);
                    e.BuildOgre(new Color(0.4f, 0.38f, 0.42f), false, false);
                    e.AddHarpoon(e.armR.t, 1f);
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

        /// model: "" (the stage 1 golems), "ogre" or "dragon" for the later final bosses. extraAbility is added to the
        /// boss's usual rotation ("thorns", "gravehands").
        public static Enemy MakeBoss(Game g, int idx, float hpMult, float dmgMult = 1f, string name = null, Color? tint = null,
            string model = "", string extraAbility = "")
        {
            var d = Data.BOSSES[idx];
            var e = new GameObject("Boss_" + idx).AddComponent<Enemy>();
            e.game = g;
            e.kind = "boss";
            e.displayName = name ?? d.name;
            e.size = d.size;
            e.maxHp = d.hp * hpMult;
            e.hp = e.maxHp;
            e.speed = d.speed;
            e.damage = d.damage * dmgMult;
            e.radius = 0.55f * e.size;
            e.abilities = d.abilities;
            if (!string.IsNullOrEmpty(extraAbility))
            {
                // The signature attack comes up twice per cycle, so it is seen often.
                var list = new System.Collections.Generic.List<string>(d.abilities);
                list.Insert(2, extraAbility);
                list.Add(extraAbility);
                e.abilities = list.ToArray();
            }
            e.isBoss = true;
            e.bossIdx = idx;
            e.bossModel = model ?? "";
            e.xpValue = 60 + idx * 60;
            e.abilityCd = 3f;
            if (!e.TrySkin(true, idx)) e.BuildOgre(tint.HasValue ? d.skin * tint.Value : d.skin, true, true);
            else if (tint.HasValue && tint.Value != Color.white && e.bossModel != "dragon") e.TintModel(tint.Value);
            return e;
        }

        // ---------------------------------------------------------------- Asset Store models

        CharacterRig rig;

        /// "skeleton", "human" or "" (ogre) for regular foes.
        string ModelKind => !isBoss && Data.OGRES.TryGetValue(kind, out var d) ? d.model : "";

        /// Uses an Asset Store model when the skin library has one for this ogre. Returns false to fall back
        /// to the procedural model.
        bool TrySkin(bool boss, int bossIdx)
        {
            var lib = Skins.Lib;
            if (lib == null) return false;
            GameObject prefab;
            RuntimeAnimatorController ctrl = null;
            bool isOgreModel = false, isDragon = false;
            if (boss)
            {
                if (bossIdx == Data.FINAL_BOSS && bossModel == "ogre" && lib.forestBoss) { prefab = lib.forestBoss; isOgreModel = true; }
                else if (bossIdx == Data.FINAL_BOSS && bossModel == "dragon" && lib.cryptBoss) { prefab = lib.cryptBoss; isDragon = true; }
                else if (bossIdx == Data.MINI_BOSS && lib.golem) prefab = lib.golem;
                else if (bossIdx == Data.FINAL_BOSS && lib.golemKing) prefab = lib.golemKing;
                else { prefab = Skins.Pick(lib.bossOgres) ?? Skins.Pick(lib.ogres); isOgreModel = true; }
            }
            else if (role == "archer") prefab = lib.archer;
            else if (role == "shaman") prefab = lib.shaman;
            else if (ModelKind == "skeleton" && lib.skeleton) { prefab = lib.skeleton; ctrl = lib.skeletonController; }
            else if (ModelKind == "human" && lib.humanSoldier) { prefab = lib.humanSoldier; ctrl = lib.humanController; }
            else { prefab = Skins.Pick(lib.ogres); isOgreModel = true; }
            if (prefab == null) return false;
            if (isOgreModel) ctrl = lib.ogreController;

            model = FX.Node(transform, "Model");
            // Skeletons and knights stand at human height (scaled with their size roll); ogres tower at 2 m per size.
            // A dragon is sized by its body height, about half an ogre's head height.
            float headHeight = !boss && ModelKind != "" && !isOgreModel ? 1.75f * size / Data.OGRES[kind].size : (isDragon ? 1.0f : 2.0f) * size;
            rig = Skins.SpawnCharacter(prefab, model, headHeight, ctrl);
            var hand = rig.Bone(HumanBodyBones.RightHand);
            var leftHand = rig.Bone(HumanBodyBones.LeftHand);
            var headBone = rig.Bone(HumanBodyBones.Head);
            float boneScale = rig.transform.localScale.x;
            if (isOgreModel && role == "harpooner" && hand)
            {
                var h = AddHarpoon(hand, size / Mathf.Max(0.0001f, hand.lossyScale.x));
                h.localPosition = Vector3.zero;
                h.rotation = Quaternion.LookRotation(new Vector3(0f, 0.3f, -1f));
            }
            else if (isOgreModel && lib.ogreWeapon && hand)
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

        readonly System.Collections.Generic.List<Material> tintMats = new System.Collections.Generic.List<Material>();

        void OnDestroy()
        {
            if (skinMat) Destroy(skinMat);
            foreach (var m in tintMats) if (m) Destroy(m);
        }

        /// Recolors a pack model (stage bosses): per-instance material copies multiplied by the tint.
        void TintModel(Color tint)
        {
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.materials;
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    tintMats.Add(m);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * tint);
                }
                r.materials = mats;
            }
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

        /// A barbed iron harpoon, held in the given hand (bone).
        Transform AddHarpoon(Transform hand, float scale)
        {
            var iron = FX.Lit(new Color(0.3f, 0.3f, 0.33f), 0.4f, 0.8f);
            var hp = FX.Node(hand, "Harpoon", new Vector3(0.1f, -1.0f, -0.1f));
            hp.localScale = Vector3.one * scale;
            FX.Part(hp, MeshGen.Box(), FX.Lit(Props.BARK), new Vector3(0, 0.4f, 0), new Vector3(0.09f, 2.6f, 0.09f));
            FX.Part(hp, MeshGen.Cylinder(0f, 0.22f, 4), iron, new Vector3(0, 1.9f, 0), new Vector3(1f, 0.5f, 1f));
            foreach (int k in new[] { -1, 1 })
                FX.Part(hp, MeshGen.Box(), iron, new Vector3(k * 0.12f, 1.62f, 0), new Vector3(0.05f, 0.3f, 0.05f), new Vector3(0, 0, k * 0.6f));
            FX.Part(hp, MeshGen.Cylinder(0.14f, 0.14f, 8), iron, new Vector3(0, -0.6f, 0), new Vector3(1f, 0.25f, 1f));
            return hp;
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
        }

        // ---------------------------------------------------------------- status effects

        /// Bleed: the Blood Huntress's javelins open wounds that keep dripping damage.
        public void Bleed(float t, float dps)
        {
            bleedT = Mathf.Max(bleedT, t);
            bleedDps = Mathf.Max(bleedDps, dps);
        }

        public bool Buffed => buffT > 0f;
        public bool Bleeding => bleedT > 0f;
        public bool Burning => burnT > 0f;
        public bool Poisoned => poisonT > 0f;

        /// Poison (Venom Wyrm): damage over time; an ogre that dies poisoned bursts and poisons its neighbors.
        public void Poison(float t, float dps)
        {
            poisonT = Mathf.Max(poisonT, t);
            poisonDps = Mathf.Max(poisonDps, dps);
        }

        /// Slows movement to frac of normal speed for t seconds (bosses too).
        public void Slow(float t, float frac)
        {
            slowT = Mathf.Max(slowT, t);
            slowF = frac;
        }

        float flinchCd;

        /// A hit landed: pop and flash; a heavy blow (a big chunk of health, or a knockback) also makes a pack model
        /// flinch, at most every 1.5 s and never in the middle of its own attack.
        public void HitPop(float amount = 0f, bool knocked = false)
        {
            pop = 1f;
            flash = 1f;
            if (rig == null || isBoss || rig.InAction || Time.time < flinchCd) return;
            if (!knocked && amount < maxHp * 0.08f) return;
            if (rig.Action(0.4f, "GetHit", "Get Hit")) flinchCd = Time.time + 1.5f;
        }

        /// On death, a pack model with a death animation is left behind to fall (it sinks into the ground and goes
        /// away); everything else just vanishes in the death burst. Regular foes' corpses are capped so big waves stay
        /// cheap. Returns true if a corpse was left.
        public bool LeaveCorpse()
        {
            if (IsWyvern) return LeaveWyvernCorpse();
            if (rig == null || model == null || !rig.Has("Die") || frozenT > 0f) return false;
            if (!isBoss && EnemyCorpse.Active >= EnemyCorpse.MAX_REGULAR) return false;
            var go = new GameObject("Corpse_" + displayName);
            if (FX.root) go.transform.SetParent(FX.root, false);   // cleared with the stage
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);
            model.SetParent(go.transform, true);
            rig.SetEmission(Color.black);
            rig.Speed = 1f;
            rig.Action(10f, "Die");
            var c = go.AddComponent<EnemyCorpse>();
            c.Init(isBoss ? 4f : 1.4f, (isBoss ? 2.5f : 1.2f) * size, tintMats);
            tintMats.Clear();   // the corpse owns the boss tint materials now
            model = null;
            rig = null;
            return true;
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
                DotDamage(burnDps * player.BurnMult * delta);
            }
            if (bleedT > 0f)
            {
                bleedT -= delta;
                DotDamage(bleedDps * player.BleedMult * delta);
                if (M.RandF() < delta * 6f) FX.Burst(Pos + new Vector3(0, size, 0), new Color(0.7f, 0f, 0.03f), 3, 2f, 0.12f);
            }
            if (poisonT > 0f)
            {
                poisonT -= delta;
                DotDamage(poisonDps * delta);
                if (M.RandF() < delta * 5f) FX.Burst(Pos + new Vector3(0, size * 1.2f, 0), new Color(0.5f, 1f, 0.2f), 3, 1.5f, 0.15f);
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
            else if (IsWyvern && WyvernShieldGlow()) { }   // WYVERN: violet shimmer while only the dragon can hurt it
            else SetEmission(Color.black, 0f);
            if (IsWyvern) { WyvernTick(delta, player); return; }   // WYVERN: its own flight AI (Wyvern.cs)
            // Crouch before a jump, stretch in the air, squash on landing.
            squash = Mathf.MoveTowards(squash, jumpWind > 0f ? -0.25f : (airborne ? 0.15f : 0f), delta * 2.2f);
            model.localScale = Vector3.one * (1f + pop * 0.18f) + new Vector3(-squash * 0.5f, squash, -squash * 0.5f);
            jumpCd -= delta;

            var terrain = game.terrain;
            var to = M.Flat(player.Pos - Pos);
            float dist = to.magnitude;
            var dir = to / Mathf.Max(dist, 0.001f);
            float spd = speed * (slowT > 0f ? slowF : 1f) * (buffT > 0f ? 1.3f : 1f);

            var move = Vector3.zero;
            if (stunT > 0f)
            {
                stunT -= delta;
                jumpWind = 0f;
            }
            else if (airborne) { }
            else if (jumpWind > 0f)
            {
                jumpWind -= delta;
                if (jumpWind <= 0f) LaunchJump();
            }
            else if (isBoss) move = BossAi(delta, player, dir, dist, spd);
            else if (role == "archer")
            {
                // Keep at throwing range and lob potions.
                if (dist > 18f) move = dir * spd;
                else if (dist < 11f) move = -dir * spd * 0.7f;
                shootCd -= delta;
                if (shootCd <= 0f && dist < 30f)
                {
                    shootCd = M.RandRange(Data.POTION_COOLDOWN_MIN, Data.POTION_COOLDOWN_MAX) / (buffT > 0f ? 1.3f : 1f);
                    ThrowPotion(player);
                }
            }
            else if (role == "harpooner")
            {
                // Hunts riders: while you are on your dragon it keeps its distance and, after a loud, glowing wind-up,
                // throws a chained harpoon. On foot it just closes in like any other ogre.
                shootCd -= delta;
                if (harpoonWind > 0f)
                {
                    harpoonWind -= delta;
                    // Aim where you'll be about halfway through its flight: steady flight gets hit, turning away dodges.
                    float lead = Vector3.Distance(HarpoonHand, player.Center) / Data.HARPOON_SPEED * 0.5f;
                    harpoonAim = player.Center + player.velocity * lead;
                    if (M.RandF() < delta * 14f) FX.Bolt(HarpoonHand, harpoonAim, new Color(1f, 0.35f, 0.15f, 0.5f), 0.06f, 0.08f, 3);
                    if (harpoonWind <= 0f) ThrowHarpoon();
                }
                else if (player.flying)
                {
                    if (dist > 30f) move = dir * spd;
                    else if (dist < 14f) move = -dir * spd * 0.7f;
                    if (shootCd <= 0f && dist < Data.HARPOON_RANGE && Mathf.Abs(Time.time - lastHarpoonWindup) > Data.HARPOON_STAGGER)
                    {
                        lastHarpoonWindup = Time.time;
                        harpoonWind = Data.HARPOON_WINDUP;
                        if (rig) rig.Action(Data.HARPOON_WINDUP, "Attack02", "Attack01");
                        else armR.KeysX((-2.6f, Data.HARPOON_WINDUP * 0.8f));
                        Sfx.Play("roar", Pos, -4f, 0.15f);
                        FX.Text(Pos + new Vector3(0, 3f * size, 0), "Harpoon!", new Color(1f, 0.45f, 0.2f));
                    }
                }
                else
                {
                    move = dir * spd;
                    var climb = IslandClimb(player);
                    if (climb != Vector3.zero) move = climb * spd;
                    if (jumpCd <= 0f && frozenT <= 0f && !TryIslandLeap(player)) TryJump(player, dir, dist);
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
                }
            }
            else
            {
                move = dir * spd;
                var climb = IslandClimb(player);
                if (climb != Vector3.zero) move = climb * spd;
                if (role == "" && jumpCd <= 0f && frozenT <= 0f && !TryIslandLeap(player)) TryJump(player, dir, dist);
            }

            if (airborne) FlyStep(delta);
            else
            {
                var p = Pos + (move + knock) * delta;
                knock = Vector3.Lerp(knock, Vector3.zero, Mathf.Clamp01(delta * 7f));
                float g;
                if (islandBound && island != null)
                {
                    bool clamped = ClampToIsland(ref p);
                    if (clamped && state == "charge")
                    {
                        // The charge ends at the edge with a crash.
                        state = "";
                        FX.Shockwave(p + new Vector3(0, 0.3f, 0), 7f, M.WithA(FX.EnemyRed, 0.8f), 0.35f, false);
                        FX.Burst(p + new Vector3(0, 0.5f, 0), FX.EnemyRed, 14, 6f, 0.5f);
                        game.Shake(0.3f);
                    }
                    g = terrain.SurfaceHeight(p.x, p.z, island.topY);
                }
                else g = terrain.SurfaceHeight(p.x, p.z, Pos.y);
                if (perched)
                {
                    // Landed on a ruin top: stay up there until walking off its edge.
                    float og = terrain.ObstacleTop(p.x, p.z, Pos.y);
                    if (og > g) g = og;
                    else perched = false;
                }
                if (Pos.y - g > 0.7f)
                {
                    // Walked off a ledge: fall.
                    fallV -= JUMP_G * delta;
                    p.y = Mathf.Max(g, Pos.y + fallV * delta);
                    if (p.y <= g) fallV = 0f;
                }
                else
                {
                    fallV = 0f;
                    p.y = g;
                }
                Pos = p;
            }
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
                float healthBefore = player.hp + player.barrier;
                player.TakeDamage(damage * AtkMult * (buffT > 0f ? 1.3f : 1f), this);
                // A blow that lands (not one dodged, blocked or parried) shoves the hero away.
                if (player.hp + player.barrier < healthBefore) player.Knockback(M.Flat(player.Pos - Pos), KnockDistance);
                // Thorns or a reflect can kill it mid-swing (its model then already lies as a corpse).
                if (dead) return;
                if (rig) rig.Action(0.7f, "Attack01", "Attack02");
                else if (armR != null) armR.KeysX((-2f, 0.1f), (0.4f, 0.15f));
            }
        }

        // ---------------------------------------------------------------- jumping up to the player

        /// Big ogres and bosses are solid for the player; the small ones get shoved aside.
        public bool Solid => isBoss || kind == "brute" || kind == "hulk";
        public bool Airborne => airborne || IsWyvern;   // a wyvern flies: no ground separation

        /// How hard this foe's blows (and body) shove the hero: regular < brute < hulk < mini boss < final boss, growing per stage.
        public int KnockTier => isBoss ? (bossIdx == Data.FINAL_BOSS ? Data.KNOCK_FINAL_BOSS : Data.KNOCK_MINI_BOSS)
            : kind == "hulk" ? Data.KNOCK_HULK : kind == "brute" ? Data.KNOCK_BRUTE : Data.KNOCK_REGULAR;
        public float KnockDistance => Data.KnockDistance(KnockTier, game != null ? game.Stage.enemyMult : 1f);

        /// Moves this ogre sideways (the player or another body pushing it).
        public void Shove(Vector3 flat) => transform.position += flat;

        /// True while this ogre stands on a floating island (it is bound to one, or its feet are on an island top).
        public bool OnFloatingIsland()
        {
            if (islandBound) return true;
            var isl = game.terrain.IslandAt(Pos.x, Pos.z, Pos.y);
            return isl != null && Pos.y - isl.topY < 1f;
        }

        public static int jumpCount;
        public static float highestJump;
        const float JUMP_G = 24f;
        float jumpCd = 1.5f, jumpWind, takeoffY, jumpLife, fallV, squash;
        bool airborne, perched;
        Vector3 jumpVel;
        /// Top of the island this ogre is leaping onto (so the landing counts it as ground), or -infinity.
        float leapTopY = float.NegativeInfinity;

        /// The floating island the player is standing on (not flying over), or null.
        WorldTerrain.Island PlayerIsland(Player player)
        {
            if (player.flying) return null;
            var isl = game.terrain.IslandAt(player.Pos.x, player.Pos.z, player.Pos.y);
            return isl != null && player.Pos.y - isl.topY < 1.5f ? isl : null;
        }

        /// While the player stands on a floating island, ogres caught underneath it walk out from under the rock toward
        /// the rim (where they can leap up). Zero when not needed.
        Vector3 IslandClimb(Player player)
        {
            if (isBoss || islandBound) return Vector3.zero;
            var isl = PlayerIsland(player);
            if (isl == null || Pos.y >= isl.topY - 0.5f) return Vector3.zero;
            var flat = new Vector3(Pos.x - isl.center.x, 0f, Pos.z - isl.center.z);
            float d = flat.magnitude;
            float edge = isl.EdgeRadius(Mathf.Atan2(flat.z, flat.x));
            if (d > edge * 1.05f) return Vector3.zero;
            return d > 0.01f ? flat / d : Vector3.forward;
        }

        /// The player is up on a floating island: an ogre near its rim crouches, then makes a huge leap up onto it.
        bool TryIslandLeap(Player player)
        {
            if (isBoss || islandBound || airborne || jumpWind > 0f) return false;
            var isl = PlayerIsland(player);
            if (isl == null || Pos.y >= isl.topY - 0.5f) return false;
            var flat = new Vector3(Pos.x - isl.center.x, 0f, Pos.z - isl.center.z);
            float d = flat.magnitude;
            if (d < 0.01f) return false;
            float edge = isl.EdgeRadius(Mathf.Atan2(flat.z, flat.x));
            if (d < edge * 1.05f || d > edge + 14f) return false;   // still under the rock, or too far out
            if (M.RandF() > 0.5f) { jumpCd = 0.6f; return true; }   // stagger the crowd a little
            var land = isl.center + flat / d * (edge * 0.82f);
            float rise = isl.topY - Pos.y;
            float vy0 = Mathf.Sqrt(2f * JUMP_G * (rise + 2.5f));
            float flight = (vy0 + Mathf.Sqrt(Mathf.Max(0f, vy0 * vy0 - 2f * JUMP_G * rise))) / JUMP_G;
            var hd = new Vector3(land.x - Pos.x, 0f, land.z - Pos.z);
            jumpVel = new Vector3(hd.x / flight, vy0, hd.z / flight);
            jumpWind = 0.6f;
            jumpLife = flight + 0.8f;
            leapTopY = isl.topY;
            jumpCd = 2f;
            if (rig) rig.Loop("Idle", "Idle01");
            else armR.KeysX((-0.6f, 0.2f), (0.2f, 0.3f));
            return true;
        }

        /// Leaps at a player who is above this ogre (on a ruin, a ledge or hovering low on the dragon). The landing spot is
        /// always the terrain or a ruin top within about 4.6 m of the take-off height; floating islands are reached with
        /// the bigger TryIslandLeap instead.
        void TryJump(Player player, Vector3 dir, float dist)
        {
            jumpCd = 0.3f;
            if (isBoss || islandBound || dist > 6.5f) return;
            var terrain = game.terrain;
            float py = player.Pos.y, dy = py - Pos.y;
            if (dy < 1.1f + 0.3f * dist || dy > (player.flying ? 3.6f : 4.6f)) return;
            var isl = terrain.IslandAt(player.Pos.x, player.Pos.z, py);
            if (isl != null && py - isl.topY < 0.8f && !OnFloatingIsland()) return;   // the player is up on an island
            float reachY = Pos.y + 4.1f;
            float Land(Vector3 q) => Mathf.Max(terrain.SurfaceHeight(q.x, q.z, Pos.y), terrain.ObstacleTop(q.x, q.z, reachY));
            var near = player.Pos - dir * (radius + 0.5f);
            Vector3 target;
            float yL, h;
            if (player.flying)
            {
                target = near;
                yL = Land(near);
                h = dy + 0.6f;
            }
            else
            {
                float y1 = Land(near);
                float y2 = Land(player.Pos);
                if (y1 >= py - 1f) { target = near; yL = y1; }
                else if (y2 >= py - 1f) { target = player.Pos; yL = y2; }
                else return;   // the player stands on something an ogre cannot get onto
                h = Mathf.Max(yL - Pos.y, 0f) + 0.9f;
            }
            if (yL - Pos.y > 4.7f) return;
            float vy0 = Mathf.Sqrt(2f * JUMP_G * h);
            float disc = vy0 * vy0 - 2f * JUMP_G * (yL - Pos.y);
            if (disc < 0f) return;
            float flight = (vy0 + Mathf.Sqrt(disc)) / JUMP_G;
            var d = new Vector3(target.x - Pos.x, 0f, target.z - Pos.z);
            if (d.magnitude / flight > 15f) return;
            jumpVel = new Vector3(d.x / flight, vy0, d.z / flight);
            jumpWind = 0.4f;
            jumpLife = flight + 0.6f;
            if (rig) rig.Loop("Idle", "Idle01");
            else armR.KeysX((-0.6f, 0.15f), (0.2f, 0.25f));
        }

        void LaunchJump()
        {
            airborne = true;
            perched = false;
            takeoffY = Pos.y;
            jumpCd = M.RandRange(2.8f, 4.5f);
            jumpCount++;
            if (rig) rig.Action(0.9f, "Attack02", "Attack01");
            else armR.KeysX((-2.4f, 0.1f), (0.4f, 0.5f));
            FX.Burst(Pos + new Vector3(0, 0.3f, 0), new Color(0.5f, 0.42f, 0.32f), 6, 3f, 0.3f);
        }

        void FlyStep(float delta)
        {
            var terrain = game.terrain;
            jumpVel.y -= JUMP_G * delta;
            jumpLife -= delta;
            var p = Pos + (jumpVel + new Vector3(knock.x, 0f, knock.z)) * delta;
            knock = Vector3.Lerp(knock, Vector3.zero, Mathf.Clamp01(delta * 7f));
            highestJump = Mathf.Max(highestJump, p.y - takeoffY);
            float surface = terrain.SurfaceHeight(p.x, p.z, Mathf.Max(takeoffY, leapTopY));
            float og = terrain.ObstacleTop(p.x, p.z, Pos.y);
            float g = Mathf.Max(surface, og);
            if ((jumpVel.y < 0f && p.y <= g) || jumpLife <= 0f)
            {
                p.y = g;
                airborne = false;
                leapTopY = float.NegativeInfinity;
                perched = og > surface + 0.05f;
                squash = -0.3f;
                fallV = 0f;
                jumpCd = Mathf.Max(jumpCd, 1.5f);
                FX.Burst(p + new Vector3(0, 0.2f, 0), new Color(0.5f, 0.42f, 0.32f), 8, 4f * size, 0.35f);
                if (Vector3.Distance(p, game.player.Pos) < 18f) game.Shake(0.08f * size);
            }
            Pos = p;
        }

        Vector3 HarpoonHand => Pos + new Vector3(0, 2.2f * size, 0);

        void ThrowHarpoon()
        {
            shootCd = Data.HARPOON_COOLDOWN * M.RandRange(0.85f, 1.15f);
            var from = HarpoonHand;
            Harpoon.Spawn(game, this, from, (harpoonAim - from).normalized * Data.HARPOON_SPEED, damage * AtkMult * 1.5f);
            Sfx.Play("whoosh", Pos, -2f, 0.1f);
        }

        /// Lobs a potion in a slow arc at where the player is heading; it shatters in a splash (telegraphed on the ground).
        void ThrowPotion(Player player)
        {
            var from = Pos + new Vector3(0, 1.8f * size, 0);
            float dist = M.Flat(player.Pos - Pos).magnitude;
            float flightTime = Mathf.Lerp(Data.POTION_FLIGHT_MIN, Data.POTION_FLIGHT_MAX, Mathf.InverseLerp(6f, 30f, dist));
            // Lead the target by half its movement over the flight: running in a straight line gets you hit.
            var target = (player.flying ? player.Center : player.Pos) + M.Flat(player.velocity) * flightTime * 0.5f;
            ThrownPotion.Spawn(game, from, target, flightTime, damage * AtkMult * (buffT > 0f ? 1.3f : 1f), !player.flying);
            if (rig) rig.Action(0.6f, "Attack02", "Attack01");
            else armR.KeysX((-2.2f, 0.18f), (0f, 0.3f));
            Sfx.Play("whoosh", Pos, -10f, 0.2f);
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
                    FX.Telegraph(Pos, radius + 1.5f, 0.7f, new Color(FX.EnemyRed.r, FX.EnemyRed.g, FX.EnemyRed.b, 0.4f));
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
                case "thorns":
                {
                    // Ulgrim: a fan of root lines. Each is outlined on the ground and fills toward its far end; when
                    // the fill completes, thorns burst up along it from the boss outward. Step out of the strip.
                    if (rig) rig.Action(1.3f, "Attack02", "Attack01");
                    else if (armR != null) armR.KeysX((-2.4f, 0.5f), (0.5f, 0.3f));
                    Sfx.Play("roar", Pos, -2f, 0.15f);
                    FX.Text(Pos + new Vector3(0, 3f * size, 0), "Thorns!", THORN_GREEN);
                    var origin = Pos;
                    var lines = new Vector3[Data.THORNS_LINES];
                    var starts = new Vector3[Data.THORNS_LINES];
                    var lengths = new float[Data.THORNS_LINES];
                    for (int i = 0; i < lines.Length; i++)
                    {
                        float off = (i - (lines.Length - 1) * 0.5f) * Data.THORNS_SPREAD;
                        lines[i] = Quaternion.AngleAxis(off * Mathf.Rad2Deg, Vector3.up) * dir;
                        starts[i] = origin + lines[i] * (radius * 0.7f);
                        lengths[i] = LineLength(starts[i], lines[i], Data.THORNS_LENGTH);
                        FX.TelegraphLine(starts[i], lines[i], lengths[i], Data.THORNS_WIDTH, Data.THORNS_WARN, new Color(0.35f, 0.9f, 0.2f, 0.4f));
                    }
                    Tw.Run(ThornsRoutine(starts, lines, lengths, player));
                    break;
                }
                case "gravehands":
                {
                    // Skarnath: bony hands claw out of the ground. One is aimed under the hero, one where they are heading,
                    // the rest are scattered around them; every spot is ringed and fills for GRAVE_WARN seconds first.
                    if (rig) rig.Action(1.4f, "Victory", "Attack01");
                    else if (armR != null) armR.KeysX((-2.6f, 0.4f), (0.4f, 0.4f));
                    Sfx.Play("roar", Pos, -2f, 0.15f);
                    FX.Text(Pos + new Vector3(0, 3f * size, 0), "Grave Hands!", GRAVE_VIOLET);
                    var spots = new System.Collections.Generic.List<Vector3>();
                    var vel = M.Flat(player.velocity);
                    var pp = player.Pos;
                    spots.Add(pp);
                    if (vel.magnitude > 1.5f) spots.Add(pp + vel.normalized * Mathf.Min(vel.magnitude * Data.GRAVE_WARN, Data.GRAVE_SPREAD));
                    while (spots.Count < Data.GRAVE_HANDS)
                    {
                        float a = M.RandF() * M.TAU;
                        float r = M.RandRange(2f, Data.GRAVE_SPREAD);
                        spots.Add(pp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
                    }
                    for (int i = 0; i < spots.Count; i++)
                    {
                        var s = spots[i];
                        if (islandBound) s = InsideIsland(s);
                        s.y = game.terrain.SurfaceHeight(s.x, s.z, islandBound ? island.topY : pp.y + 1f);
                        spots[i] = s;
                        FX.Telegraph(s, Data.GRAVE_RADIUS, Data.GRAVE_WARN, new Color(GRAVE_VIOLET.r, GRAVE_VIOLET.g, GRAVE_VIOLET.b, 0.4f));
                    }
                    Tw.Run(GraveHandsRoutine(spots, player));
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
            FX.Vfx("ground_explosion", at, rad / 6f, 2f, null, 0.04f, FX.EnemyRed);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad, M.WithA(FX.EnemyRed, 0.9f), 0.4f, false);
            FX.Burst(at + new Vector3(0, 0.5f, 0), FX.EnemyRed, 30, 12f, 0.7f);
            Sfx.Play("boom", at, 2f);
            game.Shake(0.6f);
            var d = player.Pos - at;
            float gh = game.terrain.GroundHeight(player.Pos.x, player.Pos.z, player.Pos.y);
            if (new Vector2(d.x, d.z).magnitude < rad && player.Pos.y - gh < 3f)
                player.TakeDamage(damage * 1.4f * AtkMult);
        }

        static readonly Color THORN_GREEN = new Color(0.45f, 0.95f, 0.25f);
        static readonly Color GRAVE_VIOLET = new Color(0.7f, 0.4f, 1f);

        /// How far a strip starting at `from` can run along flat direction d before it leaves the boss island (or maxLen).
        float LineLength(Vector3 from, Vector3 d, float maxLen)
        {
            if (!islandBound || island == null) return maxLen;
            float l = 0f;
            while (l < maxLen && island.Contains(from.x + d.x * (l + 1f), from.z + d.z * (l + 1f))) l += 1f;
            return Mathf.Max(l, 3f);
        }

        /// The player stands at (flat) distance < r of `at` and on the ground there (not flying high above it).
        bool PlayerInReach(Player player, Vector3 at, float r)
        {
            var d = player.Pos - at;
            float gh = game.terrain.GroundHeight(player.Pos.x, player.Pos.z, player.Pos.y);
            return new Vector2(d.x, d.z).magnitude < r && player.Pos.y - gh < 3f;
        }

        IEnumerator ThornsRoutine(Vector3[] starts, Vector3[] dirs, float[] lengths, Player player)
        {
            yield return Tw.Wait(Data.THORNS_WARN);
            if (this == null || dead || player == null) yield break;
            const float STEP = 3f;
            bool hit = false;
            float longest = 0f;
            foreach (float l in lengths) longest = Mathf.Max(longest, l);
            Sfx.Play("boom", Pos, -2f);
            game.Shake(0.4f);
            for (float s = STEP * 0.5f; s < longest; s += STEP)
            {
                for (int i = 0; i < dirs.Length; i++)
                {
                    if (s > lengths[i]) continue;
                    var at = starts[i] + dirs[i] * s;
                    at.y = game.terrain.SurfaceHeight(at.x, at.z, at.y + 1f);
                    ThornSpikes(at);
                    // Inside this 3 m cell of the strip (along it +-STEP/2, across it the half-width plus the hero's body).
                    var rel = player.Pos - at;
                    float along = rel.x * dirs[i].x + rel.z * dirs[i].z;
                    float across = Mathf.Abs(-rel.x * dirs[i].z + rel.z * dirs[i].x);
                    if (!hit && Mathf.Abs(along) <= STEP * 0.5f + 0.3f && across <= Data.THORNS_WIDTH + player.HitRadius * 0.6f && PlayerInReach(player, at, 99f))
                    {
                        hit = true;
                        player.TakeDamage(damage * Data.THORNS_DAMAGE * AtkMult);
                    }
                }
                yield return Tw.Wait(0.03f);
                if (this == null || player == null) yield break;
            }
        }

        /// A cluster of thorns that stabs up out of the ground, stays a moment and sinks back.
        static void ThornSpikes(Vector3 at)
        {
            if (FX.root == null) return;
            var holder = FX.Node(FX.root, "Thorns", at);
            holder.position = at;
            var bark = FX.Lit(new Color(0.3f, 0.2f, 0.1f));
            var leaf = FX.Lit(new Color(0.25f, 0.5f, 0.12f));
            for (int i = 0; i < 3; i++)
            {
                float h = M.RandRange(1.6f, 2.8f);
                var off = new Vector3(M.RandRange(-0.8f, 0.8f), h * 0.5f, M.RandRange(-0.8f, 0.8f));
                FX.Part(holder, MeshGen.Cylinder(0f, 0.5f, 5), i == 1 ? leaf : bark, off, new Vector3(0.45f, h, 0.45f),
                    new Vector3(M.RandRange(-0.25f, 0.25f), 0f, M.RandRange(-0.25f, 0.25f)));
            }
            FX.Burst(at + new Vector3(0, 0.4f, 0), THORN_GREEN, 5, 4f, 0.2f);
            holder.localScale = new Vector3(1f, 0.05f, 1f);
            Tw.Run(Tw.Seq(
                Tw.Lerp(0.12f, k => { if (holder) holder.localScale = new Vector3(1f, Mathf.Max(0.05f, k), 1f); }, holder),
                Tw.Wait(0.6f),
                Tw.Lerp(0.25f, k => { if (holder) holder.localScale = new Vector3(1f, Mathf.Max(0.05f, 1f - k), 1f); }, holder),
                Tw.Call(() => { if (holder) Destroy(holder.gameObject); })));
        }

        IEnumerator GraveHandsRoutine(System.Collections.Generic.List<Vector3> spots, Player player)
        {
            yield return Tw.Wait(Data.GRAVE_WARN);
            if (this == null || dead || player == null) yield break;
            bool hit = false;
            Sfx.Play("boom", Pos, -2f);
            game.Shake(0.4f);
            foreach (var at in spots)
            {
                GraveHand(at);
                FX.Shockwave(at + new Vector3(0, 0.2f, 0), Data.GRAVE_RADIUS, M.WithA(GRAVE_VIOLET, 0.8f), 0.3f, true);
                FX.Vfx("dark_explosion", at, Data.GRAVE_RADIUS / 5f, 1.5f, null, 0.04f, GRAVE_VIOLET);
                if (!hit && PlayerInReach(player, at, Data.GRAVE_RADIUS + player.HitRadius * 0.4f))
                {
                    hit = true;
                    player.TakeDamage(damage * Data.GRAVE_DAMAGE * AtkMult);
                }
            }
        }

        /// A skeletal hand that claws up out of the ground, grips at the air and sinks back.
        static void GraveHand(Vector3 at)
        {
            if (FX.root == null) return;
            var holder = FX.Node(FX.root, "GraveHand", at);
            holder.position = at;
            holder.rotation = Quaternion.Euler(0f, M.RandRange(0f, 360f), 0f);
            var bone = FX.Lit(new Color(0.86f, 0.83f, 0.72f));
            FX.Part(holder, MeshGen.Box(), bone, new Vector3(0, 1.1f, 0), new Vector3(0.4f, 2.2f, 0.4f));
            var palm = FX.Node(holder, "Palm", new Vector3(0, 2.3f, 0));
            FX.Part(palm, MeshGen.Box(), bone, Vector3.zero, new Vector3(1.1f, 0.3f, 0.9f));
            var fingers = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                fingers[i] = FX.Node(palm, "Finger", new Vector3(-0.4f + i * 0.27f, 0.1f, 0f));
                FX.Part(fingers[i], MeshGen.Box(), bone, new Vector3(0, 0.55f, 0), new Vector3(0.16f, 1.1f, 0.16f));
            }
            FX.Part(palm, MeshGen.Sphere(8), FX.Glow(GRAVE_VIOLET, 2.5f), new Vector3(0, 0.25f, 0), Vector3.one * 0.35f);
            holder.localScale = new Vector3(1f, 0.05f, 1f);
            Tw.Run(Tw.Seq(
                Tw.Lerp(0.15f, k => { if (holder) holder.localScale = new Vector3(1f, Mathf.Max(0.05f, k), 1f); }, holder),
                // The fingers curl shut.
                Tw.Lerp(0.35f, k => { foreach (var f in fingers) if (f) f.localRotation = Quaternion.Euler(-70f * k, 0f, 0f); }, holder),
                Tw.Wait(0.4f),
                Tw.Lerp(0.25f, k => { if (holder) holder.localScale = new Vector3(1f, Mathf.Max(0.05f, 1f - k), 1f); }, holder),
                Tw.Call(() => { if (holder) Destroy(holder.gameObject); })));
        }
    }

    /// A fallen foe playing its death animation: it lies there a moment, then sinks into the ground and is removed.
    public class EnemyCorpse : MonoBehaviour
    {
        public const int MAX_REGULAR = 24;
        public static int Active;
        float hold, depth, t;
        const float SINK_TIME = 0.8f;
        System.Collections.Generic.List<Material> mats;

        public void Init(float holdTime, float sinkDepth, System.Collections.Generic.List<Material> ownedMats)
        {
            hold = holdTime;
            depth = sinkDepth;
            mats = new System.Collections.Generic.List<Material>(ownedMats);
            Active++;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (t > hold) transform.position += Vector3.down * (depth / SINK_TIME) * Time.deltaTime;
            if (t > hold + SINK_TIME) Destroy(gameObject);
        }

        void OnDestroy()
        {
            Active--;
            if (mats != null) foreach (var m in mats) if (m) Destroy(m);
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
            FX.Telegraph(new Vector3(to.x, g.terrain.SurfaceHeight(to.x, to.z, to.y), to.z), 2.6f, flightTime, new Color(FX.EnemyRed.r, FX.EnemyRed.g, FX.EnemyRed.b, 0.35f));
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
                FX.Burst(pos, FX.EnemyRed, 14, 7f, 0.4f);
                FX.Shockwave(pos + new Vector3(0, 0.2f, 0), 2.6f, M.WithA(FX.EnemyRed, 0.7f), 0.3f, false);
                Sfx.Play("arrow_hit", pos, 2f);
                FX.Vfx("stones_hit", pos, 1f, 2f, null, 0.04f, FX.EnemyRed);
                if (p != null && Vector3.Distance(p.Center, pos) < 2.6f && !p.pet.TryWingGuard(p.Center)) p.TakeDamage(damage);
                Destroy(gameObject);
            }
        }
    }

    /// An Ogre Harpooner's chained harpoon. It only matters to a rider in the air (Player.HarpoonHit); the chain back
    /// to the thrower makes it easy to see where it came from.
    public class Harpoon : MonoBehaviour
    {
        Game game;
        Enemy thrower;
        Vector3 velocity;
        float damage;
        float life = 3.5f;
        LineRenderer chain;

        public static void Spawn(Game g, Enemy thrower, Vector3 from, Vector3 velocity, float damage)
        {
            var h = new GameObject("Harpoon").AddComponent<Harpoon>();
            h.game = g;
            h.thrower = thrower;
            h.velocity = velocity;
            h.damage = damage;
            var iron = FX.Lit(new Color(0.3f, 0.3f, 0.33f), 0.4f, 0.8f);
            FX.Part(h.transform, MeshGen.Box(), FX.Lit(Props.BARK), Vector3.zero, new Vector3(0.1f, 0.1f, 2.2f));
            FX.Part(h.transform, MeshGen.Box(), FX.Glow(new Color(1f, 0.4f, 0.15f), 2.5f), new Vector3(0, 0, -1.2f), new Vector3(0.22f, 0.22f, 0.45f));
            FX.Part(h.transform, MeshGen.Box(), iron, new Vector3(0, 0, -0.95f), new Vector3(0.5f, 0.06f, 0.12f));
            h.chain = h.gameObject.AddComponent<LineRenderer>();
            h.chain.material = FX.Glow(new Color(0.55f, 0.5f, 0.45f), 0.6f);
            h.chain.startWidth = h.chain.endWidth = 0.07f;
            h.chain.positionCount = 2;
            // A burning trail so it's easy to track in the air.
            FX.Particles(h.transform, new FX.ParticleSpec
            {
                amount = 40, lifetime = 0.45f, size = 0.45f, speedMin = 0.2f, speedMax = 0.8f, spread = 25f, worldSpace = true,
                startColor = new Color(1f, 0.55f, 0.2f, 0.9f), endColor = new Color(0.4f, 0.1f, 0f, 0f),
            });
            h.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            life -= delta;
            transform.position += velocity * delta;
            var pos = transform.position;
            chain.SetPosition(0, thrower && !thrower.dead ? thrower.Pos + new Vector3(0, 2f * thrower.size, 0) : pos);
            chain.SetPosition(1, pos);
            var p = game.player;
            if (p != null && p.flying && Vector3.Distance(p.Center, pos) < p.HitRadius + 0.6f)
            {
                p.HarpoonHit(damage, thrower);
                Destroy(gameObject);
                return;
            }
            if (life <= 0f || pos.y < game.terrain.Height(pos.x, pos.z)) Destroy(gameObject);
        }
    }

    /// An alchemist's potion: a glowing flask lobbed in a slow, high arc that shatters in a splash where it lands
    /// (or on the player, if it hits them in flight). Its landing spot is marked on the ground so it can be dodged.
    public class ThrownPotion : MonoBehaviour
    {
        static readonly Color BREW = new Color(0.45f, 1f, 0.3f);
        const float G = 18f;
        Game game;
        Vector3 velocity;
        float damage;
        float life;
        Vector3 spin;

        public static void Spawn(Game g, Vector3 from, Vector3 to, float flightTime, float damage, bool telegraph)
        {
            var b = new GameObject("Potion").AddComponent<ThrownPotion>();
            b.game = g;
            b.damage = damage;
            b.life = flightTime + 2f;
            b.spin = new Vector3(M.RandRange(4f, 8f), M.RandRange(-3f, 3f), M.RandRange(2f, 5f));
            var d = to - from;
            b.velocity = new Vector3(d.x / flightTime, (d.y + 0.5f * G * flightTime * flightTime) / flightTime, d.z / flightTime);
            if (telegraph)
                FX.Telegraph(new Vector3(to.x, g.terrain.SurfaceHeight(to.x, to.z, to.y + 1f), to.z), Data.POTION_SPLASH, flightTime,
                    new Color(BREW.r, BREW.g, BREW.b, 0.3f));
            // Round glass flask with a cork, full of glowing brew.
            FX.Part(b.transform, MeshGen.Sphere(10), FX.Glow(BREW, 2.2f), Vector3.zero, Vector3.one * 0.42f);
            FX.Part(b.transform, MeshGen.Sphere(10), FX.Lit(new Color(0.8f, 0.95f, 1f, 0.35f), 0.05f, 0f, 0.35f), Vector3.zero, Vector3.one * 0.5f);
            FX.Part(b.transform, MeshGen.Cylinder(0.5f, 0.5f, 8), FX.Lit(new Color(0.8f, 0.95f, 1f, 0.5f), 0.05f), new Vector3(0, 0.3f, 0), new Vector3(0.14f, 0.2f, 0.14f));
            FX.Part(b.transform, MeshGen.Cylinder(0.5f, 0.5f, 8), FX.Lit(Props.BARK), new Vector3(0, 0.43f, 0), new Vector3(0.16f, 0.1f, 0.16f));
            FX.Particles(b.transform, new FX.ParticleSpec
            {
                amount = 30, lifetime = 0.5f, size = 0.25f, speedMin = 0.1f, speedMax = 0.5f, spread = 40f, worldSpace = true,
                startColor = new Color(BREW.r, BREW.g, BREW.b, 0.8f), endColor = new Color(0.1f, 0.4f, 0.05f, 0f),
            });
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
            bool hitPlayer = p != null && Vector3.Distance(p.Center, pos) < p.HitRadius + 0.5f;
            float gh = game.terrain.SurfaceHeight(pos.x, pos.z, pos.y - velocity.y * delta);
            if (hitPlayer || pos.y < gh || life <= 0f) Shatter(hitPlayer ? p.Center : new Vector3(pos.x, Mathf.Max(pos.y, gh), pos.z));
        }

        void Shatter(Vector3 at)
        {
            FX.Burst(at, BREW, 18, 6f, 0.45f);
            FX.Burst(at, new Color(0.85f, 0.95f, 1f), 8, 5f, 0.25f);
            FX.Shockwave(at + new Vector3(0, 0.15f, 0), Data.POTION_SPLASH, M.WithA(BREW, 0.7f), 0.35f, true);
            FX.Vfx("hit_magic", at, 1f, 1.5f, null, 0.02f, BREW);
            Sfx.Play("arrow_hit", at, 0f);
            Sfx.Play("frost", at, -12f, 0.3f);
            var p = game.player;
            if (p != null)
            {
                var v = p.Center - at;
                bool inSplash = new Vector2(v.x, v.z).magnitude < Data.POTION_SPLASH + p.HitRadius && Mathf.Abs(v.y) < 2.6f;
                if (inSplash && !p.pet.TryWingGuard(p.Center)) p.TakeDamage(damage);
            }
            Destroy(gameObject);
        }
    }
}
