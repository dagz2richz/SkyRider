using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Skyfall
{
    /// The hero (Dragonguard Knight, Blood Huntress or Ember Mage): movement, flight on the dragon (Q), camera, stats,
    /// mana, leveling, the auto attack and defenses (block / dodge / parry).
    public class Player : MonoBehaviour
    {
        const float GRAVITY = 34f;
        const float JUMP_VELOCITY = 16.5f;
        const float MAX_ALTITUDE = 26f;
        static readonly Vector3 SEAT = new Vector3(0, 0.98f, -0.6f);      // knight origin offset when mounted
        static readonly Color ARCANE = new Color(0.55f, 0.45f, 1f);

        public Game game;
        public DragonPet pet;
        public Camera cam;
        Transform knight;
        Joint legL, legR, armR, armL, cape;
        MeshRenderer handGlow;
        Transform weaponNode;
        Transform shieldNode;

        public Vector3 velocity;
        bool onGround = true;
        public bool flying;                // mounted on the dragon
        public float flightStamina = 4f;
        public float camYaw, camPitch = 0.32f;
        public float camDist = 9f, camDistTarget = 9f;
        float camH = 2f;
        float walkT;
        float mountT;                      // 0 = on foot, 1 = in the saddle (for blending)
        float yaw;
        Vector3 shakeOffset;

        // Stats
        public float maxHp = 100f, hp = 100f, mana = 40f;
        public int level = 1;
        public float xp, xpNext = 6f;
        public int pendingLevels;
        /// Shop level of the hero's weapon (set at run start).
        public int swordLevel = 1;
        Transform bowNode, bowBone;
        bool leaping;
        public string heroId = "";
        public string dragonId = "blue";
        int airJumps;
        public string spellId = "frost_bolt";
        public string passiveId = "lightning_storm";
        public readonly Dictionary<string, int> spells = new Dictionary<string, int>();   // id -> level (spell, passive, dragon abilities)
        float blockAnim;
        public readonly Dictionary<string, int> relics = new Dictionary<string, int>();   // stat bucket ranks
        float attackTimer = 0.5f;
        float hurtTimer;

        public Vector3 Pos
        {
            get => transform.position;
            set => transform.position = value;
        }

        public float Yaw => yaw;

        public void Init(Game g)
        {
            game = g;
            knight = FX.Node(transform, "Hero");
            weaponNode = null;
            BuildKnight();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            var layerDist = new float[32];
            layerDist[WorldTerrain.PROP_LAYER] = 170f;
            cam.layerCullDistances = layerDist;
            camGo.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            flightStamina = MaxFlight;
        }

        // ---------------------------------------------------------------- derived stats

        /// Stat bucket ranks picked on level-up. Each pick raises all 3 stats in the bucket.
        public int B(string id) => relics.TryGetValue(id, out int v) ? v : 0;

        int SpellLv(string id, int dflt = 1) => spells.TryGetValue(id, out int v) ? v : dflt;

        /// Permanent growth every level: +5% damage, +2% health, +0.5% parry.
        public float LevelDamageBonus => 0.05f * (level - 1);
        public float CritChance => 0.08f + 0.03f * B("offense") + 0.02f * Tier("gloves");
        public float ParryChance => Mathf.Min(0.03f + 0.005f * (level - 1) + 0.02f * B("defense"), 0.4f);
        public bool IsKnight => heroId == "knight";
        public bool IsHuntress => heroId == "huntress";
        public bool IsPyro => heroId == "pyromancer";

        /// Each Defense pick adds a flat +2% block / dodge / ward.
        float DefenseExtra => 0.02f * B("defense");

        /// Armor tier (0..5) of a slot, from the save (set in the shop).
        public int Tier(string slot) => Save.ArmorTier(slot);

        /// Defense level behind block / dodge / ward and their perks at 3 and 5: the Knight's weapon level (his shield) plus
        /// Legs tiers; the Huntress and the Ember Mage start at 1 and gain a level per Legs tier.
        public int shieldLevel => Mathf.Min((IsKnight ? swordLevel : 1) + Tier("legs"), 8);

        /// Knight: shield block. Ember Mage: Flame Ward (same roll, different flavor).
        public float BlockChance
        {
            get
            {
                if (IsKnight) return Mathf.Min(0.12f + 0.05f * (shieldLevel - 1) + DefenseExtra, 0.6f);
                if (IsPyro) return Mathf.Min(Data.WARD.baseChance + 0.05f * (shieldLevel - 1) + DefenseExtra, 0.6f);
                return 0f;
            }
        }

        public float JumpMult => 1f + 0.1f * B("utility");
        public float DamageMult => (1f + 0.08f * B("offense")) * (1f + LevelDamageBonus);
        public float AttackSpeedMult => Mathf.Pow(0.94f, B("offense")) / (1f + 0.05f * Tier("gloves"));

        /// Weapon damage per hit before the shared multipliers: base x weapon level (+15% per level).
        public float WeaponBaseDamage => Weapon.damage * (1f + 0.15f * (swordLevel - 1));

        /// Seconds between attacks: weapon cooldown, faster with weapon level (0.93 per level), Offense and Gloves.
        public float AttackInterval => Weapon.cooldown * AttackSpeedMult * Mathf.Pow(0.93f, swordLevel - 1);

        /// Projectiles per volley: +1 at weapon level 3 and 5 (Javelin, Bow).
        public int ShotCount => 1 + (swordLevel >= 3 ? 1 : 0) + (swordLevel >= 5 ? 1 : 0);
        public float CooldownMult => 1f;
        public float DragonCdMult => Mathf.Pow(0.94f, B("dragon"));
        public float DragonDamageMult => 1f + 0.12f * B("dragon");
        public float AreaMult => 1f;

        /// Level of a passive, but only if it is the chosen passive.
        public int Plv(string id) => passiveId == id ? SpellLv(id, 0) : 0;

        public float MoveMult => 1f + 0.07f * Plv("windwalker") + 0.06f * B("utility") + 0.05f * Tier("boots");

        public float DodgeChance
        {
            get
            {
                float b = IsHuntress ? Data.EVASION.baseChance + 0.05f * (shieldLevel - 1) + DefenseExtra : 0f;
                return Mathf.Min(b + 0.05f * Plv("windwalker"), 0.65f);
            }
        }

        public float SpellDamageMult => DamageMult * (1f + 0.08f * Plv("spellblade"));
        public float WeaponDamageMult => DamageMult * (1f + 0.05f * Plv("bloodthirst"));
        public float HealMult => 1f + 0.1f * B("health");
        public float Lifesteal => Plv("bloodthirst") == 0 ? 0f : (0.02f + 0.012f * Plv("bloodthirst")) * HealMult;

        public float ManaPerHit =>
            3f * (1f + 0.15f * B("mana")) * (1f + (Plv("spellblade") > 0 ? 0.6f + 0.25f * Plv("spellblade") : 0f));

        public float MagnetRadius => 5.5f * (1f + 0.25f * B("utility"));
        public float MaxFlight => 4f * (1f + 0.2f * B("dragon") + 0.1f * Plv("windwalker"));
        public float Armor => 1.5f * B("defense") + 2f * Tier("chest");
        public float Regen => 0.5f * B("health");
        public float XpMult => 1f;
        public float MaxMana => 100f + 15f * B("mana");

        /// No base mana regen: it only comes from the Mana bucket.
        public float ManaRegen => 0.8f * B("mana");

        /// Apply a stat bucket pick.
        public void AddBucket(string id)
        {
            relics[id] = B(id) + 1;
            switch (id)
            {
                case "health":
                    float gain = maxHp * 0.12f;
                    maxHp += gain;
                    hp += gain;
                    break;
            }
        }

        public Vector3 Center => Pos + new Vector3(0, Mathf.Lerp(1.1f, 1.7f, mountT), 0);
        public float HitRadius => Mathf.Lerp(0.9f, 1.6f, mountT);
        public Data.WeaponDef Weapon => Data.WEAPONS[Data.HEROES[heroId].weapon];

        public void AddMana(float amount) => mana = Mathf.Min(MaxMana, mana + amount);

        /// Camera forward on the ground plane.
        public Vector3 CamFwd => new Vector3(-Mathf.Sin(camYaw), 0, -Mathf.Cos(camYaw));

        /// Screen-right on the ground plane.
        public Vector3 CamRight => new Vector3(-Mathf.Cos(camYaw), 0, Mathf.Sin(camYaw));

        /// Where the rider is aiming: camera-forward, snapped to an enemy near the crosshair line.
        public Enemy AimTarget()
        {
            var fwd = CamFwd;
            Enemy best = null;
            float bestScore = 0f;
            var c = Center;
            foreach (var e in game.enemies)
            {
                if (e.dead) continue;
                var to = M.Flat(e.Pos - c);
                float d = to.magnitude;
                if (d > 40f || d < 0.5f) continue;
                float dot = Vector3.Dot(to.normalized, fwd);
                if (dot < 0.86f) continue;
                float score = dot * 2f - d / 40f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            return best;
        }

        public Vector3 AimPoint()
        {
            var t = AimTarget();
            if (t) return t.Pos;
            var p = Pos + CamFwd * 14f;
            p.y = game.terrain.Height(p.x, p.z);
            return p;
        }

        // ---------------------------------------------------------------- hero model

        void BuildKnight()
        {
            bool huntress = heroId == "huntress";
            bool pyro = heroId == "pyromancer";
            var steel = !huntress ? FX.Lit(new Color(0.62f, 0.66f, 0.72f), 0.3f, 0.85f) : FX.Lit(new Color(0.22f, 0.12f, 0.13f), 0.55f, 0.3f);
            var dark = FX.Lit(new Color(0.12f, 0.12f, 0.14f), 0.6f, 0.4f);
            var tabard = !huntress ? FX.Lit(new Color(0.08f, 0.2f, 0.55f), 0.8f) : FX.Lit(new Color(0.5f, 0.04f, 0.07f), 0.7f);
            if (pyro)
            {
                steel = FX.Lit(new Color(0.35f, 0.12f, 0.05f), 0.7f, 0.1f);
                tabard = FX.Lit(new Color(0.85f, 0.35f, 0.08f), 0.7f);
            }
            var gold = !huntress ? FX.Lit(new Color(0.9f, 0.72f, 0.3f), 0.35f, 0.9f) : FX.Lit(new Color(0.75f, 0.75f, 0.78f), 0.3f, 0.9f);
            var skin = !huntress ? FX.Lit(new Color(0.85f, 0.66f, 0.52f), 0.8f) : FX.Lit(new Color(0.88f, 0.76f, 0.7f), 0.8f);
            var leather = !huntress ? FX.Lit(new Color(0.28f, 0.18f, 0.1f), 0.8f) : FX.Lit(new Color(0.15f, 0.07f, 0.07f), 0.8f);
            var cloth = !huntress ? FX.Lit(new Color(0.6f, 0.06f, 0.06f), 0.85f) : FX.Lit(new Color(0.12f, 0.02f, 0.03f), 0.85f);
            if (pyro) cloth = FX.Lit(new Color(0.3f, 0.06f, 0.02f), 0.85f);
            var cap = MeshGen.Capsule();
            var sph = MeshGen.Sphere(12);
            // Legs.
            legL = new Joint(FX.Node(knight, "LegL", new Vector3(-0.14f, 0.95f, 0)));
            legR = new Joint(FX.Node(knight, "LegR", new Vector3(0.14f, 0.95f, 0)));
            foreach (var leg in new[] { legL, legR })
            {
                FX.Part(leg.t, cap, leather, new Vector3(0, -0.42f, 0), new Vector3(0.2f, 0.44f, 0.2f));
                FX.Part(leg.t, sph, steel, new Vector3(0, -0.45f, -0.08f), new Vector3(0.16f, 0.14f, 0.12f));
                FX.Part(leg.t, MeshGen.Box(), dark, new Vector3(0, -0.88f, -0.05f), new Vector3(0.2f, 0.15f, 0.34f));
            }
            // Torso and armor.
            FX.Part(knight, cap, steel, new Vector3(0, 1.32f, 0), new Vector3(0.5f, 0.42f, 0.36f));
            FX.Part(knight, MeshGen.Box(), tabard, new Vector3(0, 1.12f, -0.17f), new Vector3(0.42f, 0.72f, 0.06f));
            FX.Part(knight, MeshGen.Box(), gold, new Vector3(0, 1.3f, -0.205f), new Vector3(0.14f, 0.14f, 0.02f), new Vector3(0, 0, M.PI * 0.25f));
            FX.Part(knight, MeshGen.Box(), leather, new Vector3(0, 0.98f, 0), new Vector3(0.52f, 0.09f, 0.4f));
            foreach (int s in new[] { -1, 1 })
                FX.Part(knight, sph, steel, new Vector3(s * 0.3f, 1.6f, 0), new Vector3(0.3f, 0.22f, 0.32f));
            FX.Part(knight, sph, skin, new Vector3(0, 1.85f, 0), Vector3.one * 0.28f);
            if (pyro)
            {
                // Wide-brimmed, pointed wizard hat and a robe skirt.
                FX.Part(knight, MeshGen.Cylinder(0.5f, 0.5f, 16), tabard, new Vector3(0, 2.0f, 0), new Vector3(0.7f, 0.03f, 0.7f));
                FX.Part(knight, MeshGen.Cylinder(0f, 0.5f, 12), tabard, new Vector3(0, 2.32f, 0.05f), new Vector3(0.36f, 0.62f, 0.36f), new Vector3(-0.15f, 0, 0));
                FX.Part(knight, MeshGen.Cylinder(0.5f, 0.5f, 12), FX.Glow(new Color(1f, 0.5f, 0.1f), 1.5f), new Vector3(0, 2.04f, 0), new Vector3(0.38f, 0.04f, 0.38f));
                FX.Part(knight, MeshGen.Cylinder(0.32f, 0.5f, 12), tabard, new Vector3(0, 0.62f, 0), new Vector3(1f, 0.75f, 1f));
                foreach (int e in new[] { -1, 1 })
                    FX.Part(knight, MeshGen.Sphere(8), FX.Glow(new Color(1f, 0.6f, 0.15f), 3f), new Vector3(e * 0.06f, 1.88f, -0.14f), Vector3.one * 0.05f);
            }
            else if (!huntress)
            {
                // Great helm with plume.
                FX.Part(knight, MeshGen.Cylinder(0.5f, 0.5f, 12), steel, new Vector3(0, 1.9f, 0), new Vector3(0.34f, 0.36f, 0.34f));
                FX.Part(knight, sph, steel, new Vector3(0, 2.08f, 0), new Vector3(0.34f, 0.18f, 0.34f));
                FX.Part(knight, MeshGen.Box(), dark, new Vector3(0, 1.92f, -0.17f), new Vector3(0.24f, 0.04f, 0.02f));
                FX.Part(knight, MeshGen.Box(), gold, new Vector3(0, 1.9f, -0.172f), new Vector3(0.03f, 0.24f, 0.02f));
                FX.Part(knight, MeshGen.Box(), FX.Lit(new Color(0.85f, 0.85f, 0.9f)), new Vector3(0, 2.23f, 0.1f), new Vector3(0.06f, 0.22f, 0.42f), new Vector3(0.35f, 0, 0));
            }
            else
            {
                // Deep crimson hood with glowing eyes and a mask.
                FX.Part(knight, sph, tabard, new Vector3(0, 1.9f, 0.03f), new Vector3(0.36f, 0.38f, 0.38f));
                FX.Part(knight, MeshGen.Cylinder(0f, 0.5f, 8), tabard, new Vector3(0, 2.12f, 0.1f), new Vector3(0.3f, 0.3f, 0.3f), new Vector3(-0.5f, 0, 0));
                FX.Part(knight, MeshGen.Box(), dark, new Vector3(0, 1.8f, -0.15f), new Vector3(0.22f, 0.12f, 0.04f));
                foreach (int e in new[] { -1, 1 })
                    FX.Part(knight, sph, FX.Glow(new Color(1f, 0.15f, 0.15f), 3f), new Vector3(e * 0.06f, 1.88f, -0.14f), Vector3.one * 0.05f);
            }
            // Cape.
            cape = new Joint(FX.Node(knight, "Cape", new Vector3(0, 1.64f, 0.2f)));
            FX.Part(cape.t, MeshGen.Box(), cloth, new Vector3(0, -0.6f, 0.05f), new Vector3(0.62f, 1.2f, 0.03f));
            // Left arm: the casting hand.
            armL = new Joint(FX.Node(knight, "ArmL", new Vector3(-0.36f, 1.58f, 0)));
            FX.Part(armL.t, cap, steel, new Vector3(0, -0.3f, 0), new Vector3(0.13f, 0.3f, 0.13f));
            handGlow = FX.Part(armL.t, sph, FX.Glow(GlowColor, 2.5f), new Vector3(0, -0.62f, 0), Vector3.one * 0.16f);
            // Right arm holds the weapon.
            armR = new Joint(FX.Node(knight, "ArmR", new Vector3(0.36f, 1.58f, 0)));
            FX.Part(armR.t, cap, steel, new Vector3(0, -0.3f, 0), new Vector3(0.13f, 0.3f, 0.13f));
            weaponNode = FX.Node(armR.t, "Weapon", new Vector3(0, -0.6f, 0));
            armR.Rot = new Vector3(-0.5f, 0, 0);
        }

        public Color GlowColor
        {
            get
            {
                switch (heroId)
                {
                    case "huntress": return new Color(1f, 0.12f, 0.15f);
                    case "pyromancer": return new Color(1f, 0.5f, 0.1f);
                }
                return ARCANE;
            }
        }

        public void SetupLoadout(string spell, string passive, string hero = "knight", string dragon = "blue")
        {
            spellId = spell;
            passiveId = passive;
            dragonId = dragon;
            spells.Clear();
            spells[spell] = 1;
            spells[passive] = 1;
            spells["dragonfire"] = 1;
            spells["ride"] = 1;
            if (hero != heroId || weaponNode == null || weaponNode.childCount == 0)
            {
                heroId = hero;
                for (int i = knight.childCount - 1; i >= 0; i--) DestroyImmediate(knight.GetChild(i).gameObject);
                shieldNode = null;
                BuildKnight();
            }
            for (int i = weaponNode.childCount - 1; i >= 0; i--) DestroyImmediate(weaponNode.GetChild(i).gameObject);
            swordLevel = Save.WeaponLevel(Data.HEROES[heroId].weapon);
            maxHp = Data.HEROES[heroId].hp * (1f + 0.06f * Tier("helm"));
            hp = maxHp;
            if (pet) pet.SetDragon(dragon);
            BuildWeapon();
            ApplySkin();
            AttachBow();
            AnimateKnight(0f, 0f);
        }

        // ---------------------------------------------------------------- Asset Store hero model

        CharacterRig rig;
        Transform skinRoot;
        string skinHero = "";

        static readonly string[] IDLE = { "Idle_SwordShield", "Idle01", "Idle" };
        static readonly string[] WALK = { "Walk_SwordShield", "WalkForward", "BattleWalkForward", "Walk" };
        static readonly string[] RUN = { "Run_SwordShield", "BattleRunForward", "Run" };
        static readonly string[] AIR = { "JumpAir", "Idle_SwordShield", "Idle01" };
        static readonly string[] SWING = { "NormalAttack01_SwordShield", "Attack01" };
        static readonly string[] CAST = { "Attack01" };
        static readonly string[] HURT = { "GetHit_SwordShield", "GetHit" };
        static readonly string[] LEAP = { "JumpUpAttack", "NormalAttack02_SwordShield" };

        /// Swaps the procedural hero for the Asset Store model when the skin library has one for this hero.
        void ApplySkin()
        {
            var prefab = Skins.Lib ? Skins.Lib.Hero(heroId) : null;
            if (skinHero != heroId || (prefab == null && skinRoot != null))
            {
                if (skinRoot) DestroyImmediate(skinRoot.gameObject);
                skinRoot = null;
                rig = null;
                skinHero = heroId;
                if (prefab)
                {
                    skinRoot = FX.Node(transform, "HeroSkin");
                    rig = Skins.SpawnCharacter(prefab, skinRoot, 1.8f);
                    rig.Loop(IDLE);
                }
            }
            // With a skin, the procedural body stays (its joints still drive the casting hand) but is hidden.
            bool hide = rig != null;
            foreach (var r in knight.GetComponentsInChildren<Renderer>(true))
                if (r != handGlow) r.enabled = !hide;
        }

        void AnimateSkin(float hspeed)
        {
            if (rig == null) return;
            skinRoot.localPosition = knight.localPosition;
            rig.Speed = hspeed > 0.6f && onGround ? Mathf.Clamp(hspeed / 9f, 0.7f, 1.6f) : 1f;
            if (flying) rig.Loop(IDLE);
            else if (!onGround) rig.Loop(AIR);
            else if (hspeed > 6f) rig.Loop(RUN);
            else if (hspeed > 0.6f) rig.Loop(WALK);
            else rig.Loop(IDLE);
        }

        void BuildWeapon()
        {
            var steel = FX.Lit(new Color(0.78f, 0.8f, 0.85f), 0.2f, 0.95f);
            var gold = FX.Lit(new Color(0.9f, 0.72f, 0.3f), 0.35f, 0.9f);
            var w = FX.Node(weaponNode, "Held", Vector3.zero, new Vector3(-1.1f, 0, 0));
            if (IsPyro) return;   // her bow is attached to the left hand (AttachBow)
            if (!IsKnight)
            {
                // A blood javelin, glowing.
                FX.Part(w, MeshGen.Box(), FX.Lit(new Color(0.2f, 0.05f, 0.06f), 0.5f), new Vector3(0, 0.6f, 0), new Vector3(0.05f, 1.9f, 0.05f));
                FX.Part(w, MeshGen.Cylinder(0f, 0.5f, 4), FX.Glow(new Color(1f, 0.1f, 0.12f), 2.5f), new Vector3(0, 1.75f, 0), new Vector3(0.12f, 0.45f, 0.12f));
                return;
            }
            FX.Part(w, MeshGen.Box(), steel, new Vector3(0, 0.95f, 0), new Vector3(0.1f, 1.4f, 0.03f));
            FX.Part(w, MeshGen.Box(), gold, new Vector3(0, 0.22f, 0), new Vector3(0.42f, 0.07f, 0.1f));
            FX.Part(w, MeshGen.Box(), FX.Lit(new Color(0.2f, 0.1f, 0.06f)), new Vector3(0, 0.05f, 0), new Vector3(0.07f, 0.3f, 0.07f));
            FX.Part(w, MeshGen.Sphere(8), gold, new Vector3(0, -0.12f, 0), Vector3.one * 0.11f);
            // Kite shield on the off arm.
            if (shieldNode == null)
            {
                shieldNode = FX.Node(armL.t, "Shield", new Vector3(-0.1f, -0.35f, -0.18f), new Vector3(0, 0.25f, 0));
                var face = FX.Lit(new Color(0.1f, 0.22f, 0.55f), 0.6f);
                var rim = FX.Lit(new Color(0.85f, 0.7f, 0.3f), 0.3f, 0.9f);
                FX.Part(shieldNode, MeshGen.Box(), rim, new Vector3(0, 0.05f, 0), new Vector3(0.62f, 0.78f, 0.06f));
                FX.Part(shieldNode, MeshGen.Box(), face, new Vector3(0, 0.05f, -0.02f), new Vector3(0.54f, 0.7f, 0.05f));
                FX.Part(shieldNode, MeshGen.Cylinder(0f, 0.5f, 4), rim, new Vector3(0, -0.42f, 0), new Vector3(0.6f, 0.3f, 0.06f), new Vector3(M.PI, 0, 0));
                FX.Part(shieldNode, MeshGen.Cylinder(0f, 0.5f, 4), face, new Vector3(0, -0.4f, -0.02f), new Vector3(0.52f, 0.26f, 0.05f), new Vector3(M.PI, 0, 0));
                FX.Part(shieldNode, MeshGen.Sphere(8), rim, new Vector3(0, 0.1f, -0.06f), new Vector3(0.16f, 0.16f, 0.08f));
                FX.Part(shieldNode, MeshGen.Box(), rim, new Vector3(0, 0.05f, -0.05f), new Vector3(0.06f, 0.6f, 0.02f));
            }
        }

        /// Where Ember Mage arrows leave from: the bow, or her hand.
        Vector3 ShotOrigin => bowNode ? bowNode.position + Vector3.up * 0.15f : HandPosition;

        /// The Ember Mage's bow, held in the left hand: on the Asset Store model's hand bone when there is one, else on the procedural arm.
        void AttachBow()
        {
            if (bowNode) DestroyImmediate(bowNode.gameObject);
            bowNode = null;
            bowBone = null;
            if (!IsPyro) return;
            bowBone = rig != null ? rig.Bone(HumanBodyBones.LeftHand) : null;
            // On the model the bow follows the hand bone each frame (LateUpdate) but stays upright; on the procedural arm it is a child.
            bowNode = FX.Node(bowBone != null ? transform : armL.t, "Bow");
            if (bowBone == null) bowNode.localPosition = new Vector3(0f, -0.62f, -0.05f);
            else
            {
                LateUpdate();
                // The wizard model carries a staff of its own; her weapon is the bow now.
                foreach (var t in rig.model.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Staff")) t.gameObject.SetActive(false);
            }
            var wood = FX.Lit(new Color(0.36f, 0.2f, 0.09f), 0.6f);
            var dark = FX.Lit(new Color(0.16f, 0.08f, 0.05f), 0.8f);
            var ember = FX.Glow(new Color(1f, 0.5f, 0.12f), 2.6f);
            const int segs = 7;
            const float half = 0.68f, bend = 0.2f;
            Vector3 P(float k) => new Vector3(0f, k * half, bend * k * k);
            for (int i = -segs; i < segs; i++)
            {
                float k0 = (float)i / segs, k1 = (float)(i + 1) / segs;
                float r0 = Mathf.Lerp(0.045f, 0.022f, Mathf.Abs(k0)), r1 = Mathf.Lerp(0.045f, 0.022f, Mathf.Abs(k1));
                FX.NoShadow(FX.Bone(bowNode, P(k0), P(k1), r0, r1, wood));
            }
            FX.Part(bowNode, MeshGen.Box(), dark, Vector3.zero, new Vector3(0.09f, 0.22f, 0.09f));
            foreach (int s in new[] { -1, 1 })
            {
                FX.NoShadow(FX.Part(bowNode, MeshGen.Sphere(8), ember, P(s), Vector3.one * 0.07f));
                FX.NoShadow(FX.Part(bowNode, MeshGen.Sphere(8), ember, P(s * 0.62f), Vector3.one * 0.04f));
            }
            FX.NoShadow(FX.Bone(bowNode, P(-1f), P(1f), 0.008f, 0.008f, FX.Lit(new Color(0.9f, 0.85f, 0.7f), 0.5f)));
            foreach (var r in bowNode.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }

        void LateUpdate()
        {
            if (bowNode == null || bowBone == null) return;
            bowNode.SetPositionAndRotation(bowBone.position + transform.rotation * BOW_BONE_OFFSET, transform.rotation);
        }

        /// Offset of the bow's grip from the hand bone, in the hero's own axes (right, up, forward is -Z).
        static readonly Vector3 BOW_BONE_OFFSET = new Vector3(0f, 0f, 0f);

        void SwingAnim()
        {
            armR.Keys((new Vector3(-2.2f, 0.4f, -0.3f), 0.08f), (new Vector3(0.6f, -0.7f, 0.3f), 0.12f), (new Vector3(-0.5f, 0, 0), 0.25f));
            if (rig) rig.Action(0.45f, SWING);
        }

        public void CastAnim()
        {
            armL.Keys((new Vector3(-1.6f, 0, 0.2f), 0.07f), (Vector3.zero, 0.3f));
            if (rig && IsPyro && !rig.InAction) rig.Action(0.4f, CAST);
            var hg = handGlow.transform;
            hg.localScale = Vector3.one * 0.4f;
            Tw.Run(Tw.To(() => hg.localScale, v => hg.localScale = v, Vector3.one * 0.16f, 0.3f, hg));
        }

        public Vector3 HandPosition => handGlow.transform.position;

        void AnimateKnight(float delta, float hspeed)
        {
            mountT = Mathf.MoveTowards(mountT, flying ? 1f : 0f, delta * 5f);
            float moving = Mathf.Clamp(hspeed / 10f, 0f, 1f);
            walkT += delta * (3f + hspeed * 0.9f);
            float stride = onGround ? Mathf.Sin(walkT) * 0.75f * moving : 0.35f;
            var walkL = new Vector3(stride, 0, 0);
            var walkR = new Vector3(onGround ? -stride : 0.6f, 0, 0);
            var rideL = new Vector3(-1.2f, 0, -0.55f);
            var rideR = new Vector3(-1.2f, 0, 0.55f);
            legL.Rot = Vector3.Lerp(walkL, rideL, mountT);
            legR.Rot = Vector3.Lerp(walkR, rideR, mountT);
            float bob = onGround ? Mathf.Abs(Mathf.Sin(walkT)) * 0.06f * moving * (1f - mountT) : 0f;
            var seat = SEAT;
            if (pet != null && pet.SeatWorld.HasValue) seat = transform.InverseTransformPoint(pet.SeatWorld.Value);
            knight.localPosition = Vector3.Lerp(new Vector3(0, bob, 0), seat, mountT);
            AnimateSkin(hspeed);
            cape.RotX = Mathf.Lerp(cape.RotX, Mathf.Clamp(hspeed * 0.06f, 0f, 1.2f) + Mathf.Sin(Time.time * 12f) * 0.05f, delta * 6f);
            blockAnim = Mathf.MoveTowards(blockAnim, 0f, delta * 4f);
            if (blockAnim > 0f) armL.Rot = new Vector3(-1.3f * blockAnim, 0.4f * blockAnim, 0);
            float glow = 1.5f + Mathf.Round(Mathf.Clamp01(mana / MaxMana) * 2.5f / 0.5f) * 0.5f;
            handGlow.sharedMaterial = FX.Glow(GlowColor, glow);
        }

        // ---------------------------------------------------------------- dragon mount

        void SummonDragon()
        {
            flying = true;
            pet.Mount();
        }

        void ReleaseDragon(string msg = "")
        {
            if (!flying) return;
            flying = false;
            if (msg != "") game.hud.Toast(msg);
            pet.Dismount();
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (game == null) return;
            float delta = Time.deltaTime;
            if (!game.Paused && GameInput.MouseCaptured)
            {
                var md = GameInput.MouseDelta;
                camYaw += md.x * 0.0035f * Save.mouse_sens;
                camPitch = Mathf.Clamp(camPitch - md.y * 0.003f * Save.mouse_sens, -1.35f, 1.48f);
                float sc = GameInput.Scroll;
                if (sc > 0f) camDistTarget = Mathf.Clamp(camDistTarget * 0.87f, 2.5f, 60f);
                else if (sc < 0f) camDistTarget = Mathf.Clamp(camDistTarget * 1.15f, 2.5f, 60f);
            }
            if (game.Paused) return;
            if (!game.running)
            {
                AnimateKnight(delta, 0f);
                UpdateCamera(delta);
                return;
            }
            Move(delta);
            UpdateCamera(delta);
            hurtTimer = Mathf.Max(hurtTimer - delta, 0f);
            hp = Mathf.Min(maxHp, hp + Regen * delta);
            AddMana(ManaRegen * delta);
            attackTimer -= delta;
            if (attackTimer <= 0f)
            {
                bool attacked = IsKnight ? Attack() : (IsHuntress ? Throw() : Shoot());
                attackTimer = attacked ? AttackInterval : 0.1f;
            }
            // Q: call the dragon and take flight (Q again to drop off). E: dragon signature (ground or air).
            if (GameInput.FlyPressed && !leaping)
            {
                if (flying) ReleaseDragon();
                else if (flightStamina > 0.3f)
                {
                    SummonDragon();
                    velocity.y = 10f;
                    onGround = false;
                }
                else game.hud.Toast("Your dragon needs to rest...");
            }
            if (GameInput.AbilityPressed && !leaping)
                if (pet.RideCommand()) CastAnim();
        }

        void Move(float delta)
        {
            var terrain = game.terrain;
            var input = GameInput.Move;
            var fwd = CamFwd;
            var right = CamRight;
            var wish = right * input.x - fwd * input.y;
            if (wish.magnitude > 1f) wish = wish.normalized;
            if (leaping) wish = Vector3.zero;
            float speed = 9.5f * MoveMult;
            if (flying) speed = 17f * MoveMult;
            float accel = (onGround || flying) ? 10f : (leaping ? 0f : 4f);
            var hv = Vector3.Lerp(new Vector3(velocity.x, 0, velocity.z), wish * speed, Mathf.Clamp01(accel * delta));
            velocity.x = hv.x;
            velocity.z = hv.z;

            float ground = terrain.SurfaceHeight(Pos.x, Pos.z, Pos.y);
            if (GameInput.JumpPressed && !leaping && !flying)
            {
                if (onGround)
                {
                    velocity.y = JUMP_VELOCITY * Mathf.Sqrt(JumpMult);
                    onGround = false;
                    airJumps = 0;
                }
                else if (airJumps < 1)
                {
                    // Double jump.
                    airJumps++;
                    velocity.y = JUMP_VELOCITY * 0.9f * Mathf.Sqrt(JumpMult);
                    FX.Shockwave(Pos + new Vector3(0, 0.2f, 0), 2f, new Color(0.8f, 0.85f, 1f, 0.5f), 0.25f);
                }
            }
            if (flying)
            {
                flightStamina -= delta;
                if (GameInput.JumpHeld) velocity.y = Mathf.MoveTowards(velocity.y, 10f, 30f * delta);
                else if (GameInput.DescendHeld) velocity.y = Mathf.MoveTowards(velocity.y, -16f, 45f * delta);
                else velocity.y = Mathf.MoveTowards(velocity.y, -1.5f, 14f * delta);
                if (Pos.y > ground + MAX_ALTITUDE) velocity.y = Mathf.Min(velocity.y, 0f);
                if (flightStamina <= 0f) ReleaseDragon("Your dragon tires and sets you down...");
            }
            else velocity.y -= GRAVITY * delta;

            var pos = Pos + velocity * delta;
            float lim = WorldTerrain.PLAY_LIMIT + 8f;
            pos.x = Mathf.Clamp(pos.x, -lim, lim);
            pos.z = Mathf.Clamp(pos.z, -lim, lim);
            float g = terrain.SurfaceHeight(pos.x, pos.z, Pos.y);
            if (pos.y <= g)
            {
                pos.y = g;
                if (velocity.y < 0f) velocity.y = 0f;
                if (flying && velocity.y <= 0f) ReleaseDragon();
                if (leaping) Slam();
                onGround = true;
            }
            else onGround = false;
            Pos = pos;
            if (onGround) flightStamina = Mathf.Min(MaxFlight, flightStamina + delta * 1.2f);

            float hspeed = new Vector2(velocity.x, velocity.z).magnitude;
            if (hspeed > 0.6f)
            {
                float targetYaw = Mathf.Atan2(-velocity.x, -velocity.z);
                yaw = M.LerpAngle(yaw, targetYaw, Mathf.Clamp01(delta * 10f));
            }
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            AnimateKnight(delta, hspeed);
        }

        // ---------------------------------------------------------------- leap slam

        /// From the dragon's back: leap off toward the target and slam the ground.
        public void LeapSlam(Vector3 target)
        {
            ReleaseDragon();
            leaping = true;
            var flat = M.Flat(target - Pos);
            if (flat.magnitude > 22f) flat = flat.normalized * 22f;
            const float t = 0.45f;
            float drop = Pos.y - game.terrain.SurfaceHeight(Pos.x + flat.x, Pos.z + flat.z, Pos.y);
            velocity = new Vector3(flat.x / t, (0.5f * GRAVITY * t * t - drop) / t, flat.z / t);
            if (flat.magnitude > 0.5f) yaw = Mathf.Atan2(-flat.x, -flat.z);
            armR.Keys((new Vector3(-2.8f, 0, 0), 0.25f));
            if (rig) rig.Action(0.7f, LEAP);
            Sfx.Play("whoosh", 0f);
        }

        void Slam()
        {
            leaping = false;
            int L = SpellLv("ride");
            float rad = (5f + 0.5f * L) * AreaMult;
            float dmg = (75f + 32f * L) * WeaponDamageMult;
            var at = Pos;
            velocity = Vector3.zero;
            armR.Play(Tw.Seq(
                Tw.To(() => armR.Rot, v => armR.Rot = v, new Vector3(0.9f, 0, 0), 0.06f, armR.t),
                Tw.Wait(0.25f),
                Tw.To(() => armR.Rot, v => armR.Rot = v, new Vector3(-0.5f, 0, 0), 0.25f, armR.t)));
            if (!FX.Vfx("ground_explosion", at, rad / 6f, 2.5f))
                FX.Flash(at + new Vector3(0, 0.8f, 0), rad * 0.8f, new Color(0.75f, 0.85f, 1f, 0.8f), 0.35f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad * 1.3f, new Color(0.8f, 0.9f, 1f, 0.9f), 0.45f);
            FX.Burst(at + new Vector3(0, 0.4f, 0), new Color(0.55f, 0.45f, 0.35f), 30, 13f, 0.6f);
            game.Shake(0.8f);
            Sfx.Play("boom", 3f, 0.05f);
            Sfx.Play("clang", 0f, 0.05f);
            int hits = 0;
            foreach (var e in game.EnemiesInRadius(at, rad, 5f))
            {
                var outDir = M.Flat(e.Pos - at);
                game.DamageEnemy(e, dmg, outDir.normalized, 12f, true, new Color(0.85f, 0.92f, 1f));
                if (!e.isBoss) e.stunT = Mathf.Max(e.stunT, 1f);
                hits++;
            }
            WeaponHits(hits, dmg * 0.25f);
        }

        // ---------------------------------------------------------------- camera

        public void SetShake(Vector2 offset) => shakeOffset = new Vector3(offset.x, offset.y, 0f);

        void UpdateCamera(float delta)
        {
            if (cam == null) return;
            camDist = Mathf.Lerp(camDist, camDistTarget, Mathf.Clamp01(delta * 8f));
            camH = Mathf.Lerp(camH, flying ? 2.8f : 2f, Mathf.Clamp01(delta * 4f));
            var target = Pos + new Vector3(0, camH, 0);
            var offset = new Vector3(Mathf.Sin(camYaw) * Mathf.Cos(camPitch), Mathf.Sin(camPitch), Mathf.Cos(camYaw) * Mathf.Cos(camPitch)) * (camDist + mountT * 3f);
            var want = target + offset;
            if (game && game.terrain)
            {
                var terrain = game.terrain;
                // The camera stops at the ground instead of going under it...
                float gh = terrain.Height(want.x, want.z) + 0.6f;
                if (want.y < gh) want.y = gh;
                // ...and slides in when a hill gets between it and you.
                const int steps = 24;
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    var p = Vector3.Lerp(target, want, t);
                    if (p.y < terrain.Height(p.x, p.z) + 0.4f)
                    {
                        want = Vector3.Lerp(target, want, Mathf.Max((float)(i - 1) / steps, 0.08f));
                        float g2 = terrain.Height(want.x, want.z) + 0.6f;
                        want.y = Mathf.Max(want.y, g2);
                        break;
                    }
                }
            }
            var ct = cam.transform;
            var basePos = ct.position - ct.rotation * shakeOffsetApplied;
            basePos = Vector3.Distance(basePos, want) < 40f ? Vector3.Lerp(basePos, want, Mathf.Clamp01(delta * 12f)) : want;
            if (Vector3.Distance(basePos, target) > 0.05f)
                ct.rotation = Quaternion.LookRotation(target - basePos, Vector3.up);
            shakeOffsetApplied = shakeOffset;
            ct.position = basePos + ct.rotation * shakeOffsetApplied;
        }

        Vector3 shakeOffsetApplied;

        /// Puts the camera straight at its target (used when the scene starts).
        public void SnapCamera()
        {
            if (cam == null) return;
            camDist = camDistTarget;
            var target = Pos + new Vector3(0, camH, 0);
            var offset = new Vector3(Mathf.Sin(camYaw) * Mathf.Cos(camPitch), Mathf.Sin(camPitch), Mathf.Cos(camYaw) * Mathf.Cos(camPitch)) * camDist;
            cam.transform.position = target + offset;
            cam.transform.rotation = Quaternion.LookRotation(-offset, Vector3.up);
        }

        // ---------------------------------------------------------------- combat

        bool Attack()
        {
            var w = Weapon;
            float rngMult = AreaMult * (1f + 0.06f * (swordLevel - 1));
            float reach = w.range * rngMult;
            var c = Center;
            var target = game.NearestEnemy(c, reach + 1.5f, 4.5f);
            if (target == null) return false;
            var dir = M.Flat(target.Pos - Pos).normalized;
            float arc = w.arc;
            float dmg = WeaponBaseDamage * WeaponDamageMult;
            float cosHalf = Mathf.Cos(arc * 0.5f * Mathf.Deg2Rad);
            int hits = 0;
            var list = game.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.dead) continue;
                var to = e.Pos - Pos;
                float vy = Mathf.Abs(e.Pos.y + 1f - c.y);
                if (vy > 4.5f + e.radius) continue;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > reach + e.radius) continue;
                if (arc < 359f && dist > 0.5f && Vector3.Dot(to.normalized, dir) < cosHalf) continue;
                var knockDir = dist > 0.01f ? to.normalized : dir;
                game.DamageEnemy(e, dmg, knockDir, w.knock);
                if (hits >= 2 || !FX.Vfx("hit", e.Pos + new Vector3(0, e.size, 0), 0.8f, 1.2f, null, 0f))
                    FX.Burst(e.Pos + new Vector3(0, e.size, 0), new Color(1f, 0.9f, 0.7f), 5, 6f, 0.15f);
                hits++;
            }
            WeaponHits(hits, dmg);
            Sfx.Play("slash", -8f);
            if (hits > 0) Sfx.Play("hit", -3f, 0.12f);
            SwingFx(dir, reach, arc, w.color);
            SwingAnim();
            return true;
        }

        void SwingFx(Vector3 dir, float reach, float arc, Color color)
        {
            var basePos = Pos + new Vector3(0, 0.4f + mountT * 0.8f, 0);
            var m = FX.FadeMat(new Color(color.r, color.g, color.b, 0.75f));
            var r = FX.Part(FX.root, ArcMesh(reach, arc), m, Vector3.zero, Vector3.one);
            FX.NoShadow(r);
            var t = r.transform;
            t.position = basePos + new Vector3(0, 0.6f, 0);
            float y0 = Mathf.Atan2(-dir.x, -dir.z);
            float spin = arc >= 359f ? M.TAU : 0.5f;
            Tw.Run(Tw.Seq(
                Tw.Lerp(0.25f, k =>
                {
                    FX.SetAlpha(m, 0.75f * (1f - k));
                    t.rotation = Quaternion.Euler(0, (y0 + spin * k) * Mathf.Rad2Deg, 0);
                }, t),
                Tw.Call(() => { if (t) Destroy(t.gameObject); Destroy(m); })));
        }

        static readonly Dictionary<string, Mesh> arcCache = new Dictionary<string, Mesh>();

        static Mesh ArcMesh(float reach, float arc)
        {
            string key = $"{reach:F1}_{arc:F0}";
            if (arcCache.TryGetValue(key, out var mesh) && mesh) return mesh;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            const int segs = 24;
            float half = arc * 0.5f * Mathf.Deg2Rad;
            float inner = reach * 0.35f;
            for (int i = 0; i < segs; i++)
            {
                float a0 = -half + 2f * half * i / segs;
                float a1 = -half + 2f * half * (i + 1) / segs;
                var d0 = new Vector3(-Mathf.Sin(a0), 0, -Mathf.Cos(a0));
                var d1 = new Vector3(-Mathf.Sin(a1), 0, -Mathf.Cos(a1));
                verts.AddRange(new[] { d0 * inner, d0 * reach, d1 * reach, d0 * inner, d1 * reach, d1 * inner });
            }
            for (int i = 0; i < verts.Count; i++) norms.Add(Vector3.up);
            mesh = MeshGen.FromTriangles(verts, norms, null, "SwingArc");
            arcCache[key] = mesh;
            return mesh;
        }

        /// Weapon hits feed mana (and life, with Bloodthirst).
        public void WeaponHits(int hits, float dmg)
        {
            if (hits <= 0) return;
            AddMana(Mathf.Min(hits * ManaPerHit, 10f + 6f * Plv("spellblade")));
            float ls = Lifesteal;
            if (ls > 0f) hp = Mathf.Min(maxHp, hp + dmg * hits * ls);
        }

        /// Ember Mage auto attack: fast fire-tipped arrows. Extra arrows (Lv 3, 5) pick other targets, or fan out at the only one.
        bool Shoot()
        {
            var w = Weapon;
            var origin = ShotOrigin;
            int count = ShotCount;
            var used = new List<Enemy>();
            Enemy first = null;
            for (int i = 0; i < count; i++)
            {
                var t = game.NearestEnemy(Center, w.range, 16f, null, used);
                Vector3 dir;
                if (t != null)
                {
                    used.Add(t);
                    if (first == null) first = t;
                    dir = (t.Pos + new Vector3(0, t.size, 0) - origin).normalized;
                }
                else if (first != null)
                {
                    dir = (first.Pos + new Vector3(0, first.size, 0) - origin).normalized;
                    dir = (dir + CamRight * (i % 2 == 0 ? 0.07f : -0.07f) * ((i + 1) / 2)).normalized;
                }
                else break;
                FireArrow.Spawn(this, origin, dir * FireArrow.SPEED, WeaponBaseDamage * WeaponDamageMult, swordLevel >= 5 ? 1 : 0, w.range);
            }
            if (first == null) return false;
            Sfx.Play("bow", -6f, 0.15f);
            CastAnim();
            return true;
        }

        /// Blood Huntress auto attack: piercing Blood Javelins paid for with 2% of current health.
        bool Throw()
        {
            var w = Weapon;
            var c = Center + new Vector3(0, 0.4f, 0);
            int count = ShotCount;
            var used = new List<Enemy>();
            for (int i = 0; i < count; i++)
            {
                var t = game.NearestEnemy(c, w.range, 16f, null, used);
                if (t == null) break;
                used.Add(t);
                var aim = t.Pos + new Vector3(0, t.size, 0);
                BloodJavelin.Spawn(this, c, (aim - c).normalized * 52f, WeaponBaseDamage * WeaponDamageMult);
            }
            if (used.Count == 0) return false;
            // The blood price: 2% of current health (never lethal).
            hp = Mathf.Max(1f, hp - hp * w.hp_cost);
            Sfx.Play("whoosh", -8f, 0.15f);
            armR.Keys((new Vector3(-2.6f, 0, 0), 0.08f), (new Vector3(0.4f, 0, 0), 0.1f), (new Vector3(-0.5f, 0, 0), 0.2f));
            return true;
        }

        public void TakeDamage(float amount, Enemy attacker = null)
        {
            if (game == null || !game.running || hurtTimer > 0f || leaping) return;
            var at = Center + new Vector3(0, 1.5f, 0);
            bool attackerAlive = attacker != null && !attacker.dead;
            if (M.RandF() < DodgeChance)
            {
                hurtTimer = 0.3f;
                FX.Text(at, "Dodge", IsHuntress ? new Color(1f, 0.5f, 0.55f) : new Color(0.7f, 1f, 0.75f));
                if (IsHuntress)
                {
                    if (shieldLevel >= 3) hp = Mathf.Min(maxHp, hp + maxHp * 0.03f);
                    if (shieldLevel >= 5)
                    {
                        var t = attackerAlive ? attacker : game.NearestEnemy(Center, 20f);
                        if (t)
                            BloodJavelin.Spawn(this, Center, (t.Pos + new Vector3(0, t.size, 0) - Center).normalized * 52f, Weapon.damage * WeaponDamageMult * 1.5f);
                    }
                }
                return;
            }
            // Parry: turn a melee blow aside and riposte.
            if (attackerAlive && M.RandF() < ParryChance)
            {
                hurtTimer = 0.35f;
                FX.Text(at, "Parry!", new Color(1f, 0.9f, 0.4f));
                Sfx.Play("parry", 0f, 0.08f);
                FX.Burst(attacker.Pos + new Vector3(0, attacker.size, 0), new Color(1f, 0.9f, 0.5f), 12, 8f, 0.2f);
                attacker.stunT = Mathf.Max(attacker.stunT, !attacker.isBoss ? 0.8f : 0.2f);
                var dir = M.Flat(attacker.Pos - Pos);
                game.DamageEnemy(attacker, Weapon.damage * WeaponDamageMult * 1.5f, dir.normalized, 6f, true, new Color(1f, 0.9f, 0.4f));
                armR.Keys((new Vector3(-1.2f, -0.8f, 0.4f), 0.06f), (new Vector3(-0.5f, 0, 0), 0.2f));
                return;
            }
            // Flame Ward (Ember Mage).
            if (IsPyro && M.RandF() < BlockChance)
            {
                hurtTimer = 0.3f;
                FX.Text(at, "Ward", new Color(1f, 0.6f, 0.2f));
                Sfx.Play("block", -2f, 0.15f);
                FX.Flash(Center, 1.8f, new Color(1f, 0.5f, 0.1f, 0.6f), 0.25f);
                if (attackerAlive) attacker.Ignite(4f, 6f + 3f * shieldLevel);
                if (shieldLevel >= 3)
                {
                    FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 4f, new Color(1f, 0.5f, 0.1f, 0.9f), 0.3f);
                    foreach (var e in game.EnemiesInRadius(Pos, 4f, 3f))
                        game.DamageEnemy(e, amount * 1.5f, (e.Pos - Pos).normalized, 6f, true, new Color(1f, 0.6f, 0.2f));
                }
                if (shieldLevel >= 5) AddMana(5f);
                return;
            }
            // Shield block (Knight only).
            if (IsKnight && M.RandF() < BlockChance)
            {
                hurtTimer = 0.3f;
                FX.Text(at, "Block", new Color(0.6f, 0.8f, 1f));
                Sfx.Play("block", 0f, 0.08f);
                if (shieldNode) FX.Burst(shieldNode.position, new Color(1f, 0.95f, 0.8f), 10, 7f, 0.15f);
                blockAnim = 1f;
                if (attackerAlive)
                {
                    if (shieldLevel >= 3) attacker.stunT = Mathf.Max(attacker.stunT, !attacker.isBoss ? 0.6f : 0f);
                    if (shieldLevel >= 5) game.DamageEnemy(attacker, amount * 2f, Vector3.zero, 0f, true, new Color(0.6f, 0.8f, 1f));
                }
                return;
            }
            float dmg = Mathf.Max(1f, amount - Armor);
            hp -= dmg;
            hurtTimer = 0.12f;
            game.hud.HurtFlash();
            if (rig && !rig.InAction) rig.Action(0.3f, HURT);
            game.Shake(0.25f);
            Sfx.Play("hurt", 0f);
            FX.Number(at, dmg, new Color(1f, 0.3f, 0.3f));
            if (hp <= 0f)
            {
                hp = 0f;
                game.OnPlayerDied();
            }
        }

        public void Heal(float amount)
        {
            amount *= HealMult;
            hp = Mathf.Min(maxHp, hp + amount);
            FX.Number(Center + new Vector3(0, 1.5f, 0), amount, new Color(0.4f, 1f, 0.4f));
        }

        public void AddXp(float amount)
        {
            xp += amount * XpMult;
            while (xp >= xpNext)
            {
                xp -= xpNext;
                level++;
                pendingLevels++;
                // +2% max health each level (and heal that much).
                float gain = maxHp * 0.02f;
                maxHp += gain;
                hp += gain;
                xpNext = 6f + (level - 1) * 7f + Mathf.Pow(level - 1, 1.5f) * 1.5f;
            }
        }
    }

    /// The Blood Huntress's javelin: pierces every ogre in its path and makes them bleed.
    public class BloodJavelin : MonoBehaviour
    {
        Player player;
        Vector3 velocity;
        float damage = 25f;
        float life = 0.75f;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();

        public static void Spawn(Player p, Vector3 from, Vector3 velocity, float damage)
        {
            var j = new GameObject("BloodJavelin").AddComponent<BloodJavelin>();
            j.player = p;
            j.velocity = velocity;
            j.damage = damage;
            j.transform.SetParent(FX.root, false);
            j.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
            var shaft = FX.Part(j.transform, MeshGen.Box(), FX.Glow(new Color(0.85f, 0.05f, 0.1f), 1.8f), Vector3.zero, new Vector3(0.08f, 0.08f, 1.6f));
            FX.NoShadow(shaft);
            var tip = FX.Part(j.transform, MeshGen.Cylinder(0f, 0.5f, 4), FX.Glow(new Color(1f, 0.35f, 0.35f), 2.5f), new Vector3(0, 0, -0.95f),
                new Vector3(0.18f, 0.4f, 0.18f), new Vector3(-M.PI * 0.5f, 0, 0));
            FX.NoShadow(tip);
            FX.Particles(j.transform, new FX.ParticleSpec
            {
                amount = 24, lifetime = 0.3f, size = 0.3f, speedMin = 0.5f, speedMax = 1.5f, spread = 20f, gravity = new Vector3(0, -6, 0),
                startColor = new Color(0.8f, 0.02f, 0.05f, 1f), endColor = new Color(0.3f, 0f, 0f, 0f), additive = false,
            });
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = player.game;
            life -= delta;
            transform.position += velocity * delta;
            transform.rotation = M.FaceNegZ(velocity);
            var pos = transform.position;
            // Pierces through every ogre in its path.
            var list = g.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.dead || hit.Contains(e)) continue;
                var c = e.Pos + new Vector3(0, e.size, 0);
                if (Vector3.Distance(c, pos) < e.radius + 0.6f + 0.5f * e.size)
                {
                    hit.Add(e);
                    e.lanceMarked = true;
                    e.Bleed(3f, damage * Data.WEAPONS["blood_lance"].bleed / 3f);
                    g.DamageEnemy(e, damage, velocity.normalized, 2.5f, true, new Color(1f, 0.35f, 0.4f));
                    player.WeaponHits(1, damage);
                    FX.Burst(pos, new Color(0.85f, 0.05f, 0.1f), 6, 4f, 0.18f);
                    Sfx.Play("hit", pos, -6f, 0.2f);
                }
            }
            if (life <= 0f || pos.y < g.terrain.Height(pos.x, pos.z)) Destroy(gameObject);
        }
    }

    /// The Ember Mage's arrow: fast, long range, a flame trail and a light burn on hit. Stops at the first foe it
    /// hits, or passes through `pierce` extra ones (Bow Lv 5).
    public class FireArrow : MonoBehaviour
    {
        public const float SPEED = 62f;
        Player player;
        Vector3 velocity;
        float damage = 20f;
        float life = 0.6f;
        int pierce;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();

        public static void Spawn(Player p, Vector3 from, Vector3 velocity, float damage, int pierce, float range)
        {
            var a = new GameObject("FireArrow").AddComponent<FireArrow>();
            a.player = p;
            a.velocity = velocity;
            a.damage = damage;
            a.pierce = pierce;
            a.life = range / velocity.magnitude + 0.05f;
            a.transform.SetParent(FX.root, false);
            a.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
            var wood = FX.Lit(new Color(0.45f, 0.28f, 0.12f), 0.7f);
            FX.NoShadow(FX.Part(a.transform, MeshGen.Box(), wood, Vector3.zero, new Vector3(0.045f, 0.045f, 1.0f)));
            FX.NoShadow(FX.Part(a.transform, MeshGen.Cylinder(0f, 0.5f, 4), FX.Glow(new Color(1f, 0.55f, 0.12f), 3f), new Vector3(0, 0, -0.58f),
                new Vector3(0.12f, 0.26f, 0.12f), new Vector3(-M.PI * 0.5f, 0, 0)));
            FX.NoShadow(FX.Part(a.transform, MeshGen.Sphere(6), FX.Glow(new Color(1f, 0.85f, 0.45f), 3f), new Vector3(0, 0, -0.5f), Vector3.one * 0.1f));
            var fletch = FX.Lit(new Color(0.9f, 0.35f, 0.1f), 0.8f);
            foreach (float rz in new[] { 0f, M.PI * 0.5f })
                FX.NoShadow(FX.Part(a.transform, MeshGen.Box(), fletch, new Vector3(0, 0, 0.42f), new Vector3(0.2f, 0.012f, 0.2f), new Vector3(0, 0, rz)));
            FX.Particles(a.transform, new FX.ParticleSpec
            {
                amount = 26, lifetime = 0.28f, size = 0.34f, scaleMin = 0.5f, scaleMax = 1.2f, speedMin = 0.2f, speedMax = 1f, spread = 25f,
                gravity = new Vector3(0, 3, 0), startColor = new Color(1f, 0.75f, 0.3f, 1f), endColor = new Color(0.8f, 0.15f, 0f, 0f), worldSpace = true,
            });
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            var g = player.game;
            life -= delta;
            transform.position += velocity * delta;
            var pos = transform.position;
            var list = g.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.dead || hit.Contains(e)) continue;
                var c = e.Pos + new Vector3(0, e.size, 0);
                if (Vector3.Distance(c, pos) < e.radius + 0.4f + 0.5f * e.size)
                {
                    hit.Add(e);
                    g.DamageEnemy(e, damage, velocity.normalized, 1.5f, true, new Color(1f, 0.7f, 0.3f));
                    e.Ignite(2.5f, damage * 0.12f);
                    player.WeaponHits(1, damage);
                    FX.Burst(pos, new Color(1f, 0.6f, 0.2f), 6, 5f, 0.16f);
                    Sfx.Play("arrow_hit", pos, -6f, 0.2f);
                    if (hit.Count > pierce)
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
            }
            if (life <= 0f || pos.y < g.terrain.Height(pos.x, pos.z)) Destroy(gameObject);
        }
    }
}
