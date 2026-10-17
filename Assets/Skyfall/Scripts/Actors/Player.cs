using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Skyfall
{
    /// The hero (Dragonguard Knight, Blood Huntress or Ember Mage): movement, flight on the dragon (Q), camera, stats,
    /// mana, leveling, the auto attack and defenses (block / dodge / parry). Talents live in PlayerTalents.cs.
    public partial class Player : MonoBehaviour
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
        public bool onGround = true;
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
        /// Damage absorbed before health (talent barriers).
        public float barrier;
        public int level = 1;
        public float xp, xpNext = 6f;
        public int pendingLevels;
        bool leaping;
        public string heroId = "";
        public string dragonId = "blue";
        int airJumps;
        public string spellId = "frost_bolt";
        public string passiveId = "lightning_storm";
        public readonly Dictionary<string, int> spells = new Dictionary<string, int>();   // id -> level (weapon, spell, passive, dragon abilities)
        float blockAnim;
        public readonly Dictionary<string, int> relics = new Dictionary<string, int>();   // stat bucket ranks
        /// Extra ranks per stat key ("bucket:index") from the armor worn.
        readonly Dictionary<string, float> gear = new Dictionary<string, float>();
        /// Armor worn, at most one piece per slot.
        public readonly List<Save.ArmorPiece> armor = new List<Save.ArmorPiece>();
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
            cam.farClipPlane = 700f;
            var layerDist = new float[32];
            layerDist[WorldTerrain.PROP_LAYER] = 190f;
            cam.layerCullDistances = layerDist;
            camGo.AddComponent<AudioListener>();
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            flightStamina = MaxFlight;
        }

        // ---------------------------------------------------------------- derived stats

        /// Stat bucket ranks picked on level-up. Each pick raises all the stats in the bucket.
        public int B(string id) => relics.TryGetValue(id, out int v) ? v : 0;

        /// Ranks of one bucket stat: the bucket's picks plus what the armor adds to that stat.
        public float S(string bucket, int idx) => B(bucket) + (gear.TryGetValue(Data.StatKey(bucket, idx), out float g) ? g : 0f);

        int SpellLv(string id, int dflt = 1) => spells.TryGetValue(id, out int v) ? v : dflt;

        /// Permanent growth every level: +1% damage, +1% health, +0.5% parry.
        /// Damage gained per hero level: 2%, or 5% for the Ember Mage.
        public float DamagePerLevel => IsPyro ? 0.05f : 0.02f;
        public float LevelDamageBonus => DamagePerLevel * (level - 1);
        public float CritChance => 0.08f + 0.03f * S("offense", 1);
        public float ParryChance => Mathf.Min(0.03f + 0.005f * (level - 1) + 0.02f * S("defense", 2), 0.4f);
        public bool IsKnight => heroId == "knight";
        public bool IsHuntress => heroId == "huntress";
        public bool IsPyro => heroId == "pyromancer";

        /// Each Defense rank adds a flat +2% block / dodge / ward.
        float DefenseExtra => 0.02f * S("defense", 0);

        /// Weapon level (1..5), raised on level-up.
        public int weaponLevel => SpellLv("weapon");

        /// Defense level behind block / dodge / ward and their perks at 3 and 5: it grows with the weapon.
        public int shieldLevel => Mathf.Min(weaponLevel, 8);

        /// Knight: shield block. Ember Mage: Flame Ward (same roll, different flavor).
        public float BlockChance
        {
            get
            {
                if (IsKnight) return Mathf.Min(Data.SHIELD.baseChance + 0.05f * (shieldLevel - 1) + DefenseExtra + (Has("k_bulwark") ? 0.12f : 0f) + Pct("block%"), 0.7f);
                if (IsPyro) return Mathf.Min(Data.WARD.baseChance + 0.05f * (shieldLevel - 1) + DefenseExtra + (Has("p_emberward") ? 0.12f : 0f) + Pct("block%"), 0.7f);
                return 0f;
            }
        }

        public float JumpMult => 1f + 0.1f * S("utility", 0);
        public float DamageMult => (1f + 0.08f * S("offense", 0)) * (1f + LevelDamageBonus) * (PowerActive("damage") ? 1f + Data.POWERUP_DAMAGE : 1f)
            * (1f + Pct("dmg%") + (flying ? Pct("mountdmg%") : 0f));
        public float AttackSpeedMult => Mathf.Pow(0.94f, S("offense", 2)) / (1f + Pct("atkspd%"));

        /// Weapon damage per hit before the shared multipliers: base x weapon level (+15% per level) x talents.
        public float WeaponBaseDamage => Weapon.damage * (1f + 0.15f * (weaponLevel - 1)) * (Has("k_wyrmsteel") ? 1.3f : 1f) * (Has("h_pact") ? 1.1f : 1f) * (1f + Pct("wdmg%"));

        /// Seconds between attacks: weapon cooldown, faster with weapon level (0.93 per level) and Offense.
        public float AttackInterval => Weapon.cooldown * AttackSpeedMult * Mathf.Pow(0.93f, weaponLevel - 1) * (Has("h_phantom") && flying ? 0.5f : 1f);

        /// Projectiles per volley: +1 at weapon level 3 and 5 (Javelin, Staff), plus talents.
        public int ShotCount
        {
            get
            {
                int n = 1 + (weaponLevel >= 3 ? 1 : 0) + (weaponLevel >= 5 ? 1 : 0) + (Has("h_volley") ? 2 : 0) + (Has("p_twin") ? 1 : 0);
                return BloodMoon || SpecterActive ? n * 3 : n;
            }
        }

        /// Spell cooldown and mana cost multipliers (Spell Weaver).
        public float CooldownMult => (Has("p_weaver") ? 0.75f : 1f) * (1f - Pct("spellcd%"));
        public float SpellCostMult => Has("p_weaver") ? 0.8f : 1f;
        /// Dragon cooldowns: the Dragon stat, Restless Wings (dragon card), talents and tree picks.
        public float DragonCdMult => Mathf.Pow(0.94f, S("dragon", 2)) * Mathf.Pow(0.92f, SpellLv("d_haste", 0)) * (Has("k_oath") ? 0.75f : 1f) * (1f - Pct("dragoncd%"));
        public float DragonDamageMult => 1f + 0.12f * S("dragon", 0) + (Has("k_oath") ? 0.4f : 0f) + (Has("h_wyrmbond") ? 0.3f : 0f) + (Has("p_dragonkin") ? 0.35f : 0f) + Pct("dragondmg%");
        /// Breath reach: Dragon Kin, the Dragon stat, the dragon's size and tree picks.
        public float DragonReachMult => (Has("p_dragonkin") ? 1.3f : 1f) * (1f + 0.08f * S("dragon", 1) + Pct("reach%")) * (1f + 0.67f * (DragonSizeMult - 1f));
        /// How big the dragon is: Ancient Growth (dragon card) and tree picks.
        public float DragonSizeMult => 1f + 0.12f * SpellLv("d_size", 0) + Pct("size%");
        public float AreaMult => 1f;
        /// Burn and bleed damage multipliers (Kindling, Hemorrhage).
        public float BurnMult => Has("p_kindling") ? 1.6f : 1f;
        public float BleedMult => Has("h_hemorrhage") ? 1.6f : 1f;

        /// Level of a passive, but only if it is the chosen passive.
        public int Plv(string id) => passiveId == id ? SpellLv(id, 0) : 0;

        public float MoveMult => 1f + 0.06f * S("utility", 1) + Pct("move%") + (SpecterActive ? 0.4f : 0f) + (PowerActive("speed") ? Data.POWERUP_SPEED : 0f) + (Has("h_fleet") ? 0.15f : 0f);

        public float DodgeChance
        {
            get
            {
                float b = IsHuntress ? Data.EVASION.baseChance + 0.05f * (shieldLevel - 1) + DefenseExtra + (Has("h_fleet") ? 0.15f : 0f) : 0f;
                return Mathf.Min(b + (IsHuntress ? Pct("block%") : 0f), 0.7f);
            }
        }

        public float SpellDamageMult => DamageMult * (1f + 0.08f * Plv("spellblade") + Pct("spelldmg%"));
        public float WeaponDamageMult => DamageMult * (1f + 0.05f * Plv("bloodthirst"));
        public float HealMult => 1f + 0.1f * S("health", 2) + Pct("heal%");
        public float Lifesteal => (Plv("bloodthirst") == 0 ? 0f : (0.02f + 0.012f * Plv("bloodthirst")) * HealMult) + (BloodMoon ? 0.1f : 0f) + (SpecterActive ? 0.08f : 0f);

        public float ManaPerHit =>
            3f * (1f + 0.15f * S("mana", 2)) * (1f + (Plv("spellblade") > 0 ? 0.6f + 0.25f * Plv("spellblade") : 0f));

        public float MagnetRadius => 5.5f * (1f + 0.25f * S("utility", 2)) * (PowerActive("magnet") ? Data.POWERUP_MAGNET : 1f);

        // ---- power-ups (run only): seconds left per kind, in Data.POWERUP_ORDER

        readonly float[] powerT = new float[3];

        static int PowerIndex(string id) => System.Array.IndexOf(Data.POWERUP_ORDER, id);
        public float PowerLeft(string id) { int i = PowerIndex(id); return i < 0 ? 0f : powerT[i]; }
        public bool PowerActive(string id) => PowerLeft(id) > 0f;

        /// Chance that a regular ogre drops a power-up: 1 in 500 plus 0.1% per Utility rank.
        public float PowerUpDropChance => Data.POWERUP_DROP + Data.POWERUP_DROP_RANK * S("utility", 3);

        /// Starts (or refreshes to the full time) a power-up.
        public void GrantPowerUp(string id)
        {
            int i = PowerIndex(id);
            if (i < 0) return;
            powerT[i] = Data.POWERUP_TIME;
            var d = Data.POWERUPS[id];
            Sfx.Play("levelup", -6f, 0.05f);
            Sfx.Play("pickup", -6f, 0f);
            FX.Flash(Center, 2.6f, new Color(d.color.r, d.color.g, d.color.b, 0.6f), 0.35f);
            FX.Burst(Center, d.color, 16, 8f, 0.25f);
            if (id == "magnet")
            {
                game.VacuumPickups();
                FX.Shockwave(Pos + new Vector3(0, 0.3f, 0), 14f, M.WithA(d.color, 0.8f), 0.6f);
            }
            game.hud.Toast($"{d.name}! {d.desc} for {Mathf.RoundToInt(Data.POWERUP_TIME)} s");
        }

        void TickPowerUps(float delta)
        {
            for (int i = 0; i < powerT.Length; i++) powerT[i] = Mathf.Max(0f, powerT[i] - delta);
        }

        // ---- dragon flight rules

        /// After leaving the dragon any way at all, Q works again this long after the hero first touches solid ground.
        /// Stamina refills while on the ground.
        public const float REMOUNT_GROUND_TIME = 1f;
        /// How far below the feet the ground can drop in a frame and still count as standing on it (slopes).
        const float GROUND_SNAP = 0.35f;
        public const float FLIGHT_REGEN_RATE = 1.2f;
        float remountWait;
        /// True once the hero has touched the ground since leaving the dragon (the remount timer is running).
        bool remountLanded = true;
        public float RemountWait => remountWait;
        public bool RemountLanded => remountLanded;
        /// Dragon flight time: 10 s in worlds 1-2, 60 s with the adult dragon of world 3 (Data.FlightTimeFor; no upgrades lengthen it).
        public float MaxFlight => Data.FlightTimeFor(game != null ? game.stage : 1);
        /// Stamina refills at this rate, scaled so an empty bar takes the same time to refill whatever the flight time.
        float FlightRegen => FLIGHT_REGEN_RATE * MaxFlight / Data.FLIGHT_TIME;
        /// The hatchling (world 1) is too small to carry you; the young (world 2) and adult (world 3) dragons both can.
        public bool CanFly => game == null || Data.CanFlyAtStage(game.stage);
        /// From 0:00 (the final battle) the dragon never tires.
        public bool UnlimitedFlight => game != null && game.finalStarted;
        /// True after jumping off the ground (until landing): flight recharges in a jump as well as on the ground,
        /// but not while falling after leaving the dragon.
        bool jumpedFromGround;
        public float Armor => 1.5f * S("defense", 1) + (Has("p_molten") ? 6f : 0f) + Mod("armor");
        public float Regen => 0.5f * S("health", 1) + Mod("regen");
        /// Experience multiplier: the Utility stat's +8% per rank (and the dev menu's 10x).
        public float XpMult => (1f + 0.08f * S("utility", 4))
            * (DevMenu.FastLevels ? 10f : 1f)   // DEV
            ;
        public float MaxMana => 100f + 15f * S("mana", 0) + (Has("p_font") ? 40f : 0f);

        /// No base mana regen: it only comes from the Mana bucket (and Mana Font).
        public float ManaRegen => 0.8f * S("mana", 1) + (Has("p_font") ? 2f : 0f) + Mod("manaregen");

        /// Max health from the hero's base, +2% per level, the Health stat and talents.
        public float MaxHpTarget => Data.HEROES[heroId].hp * Mathf.Pow(1.01f, level - 1) * (1f + 0.12f * S("health", 0)) * (Has("k_rally") ? 1.2f : 1f) * (1f + Pct("hp%"));

        /// Recomputes max health; gains also heal by the same amount.
        public void RecalcMaxHp()
        {
            if (!Data.HEROES.ContainsKey(heroId)) return;
            float target = MaxHpTarget;
            float gain = target - maxHp;
            maxHp = target;
            if (gain > 0f) hp += gain;
            hp = Mathf.Min(hp, maxHp);
        }

        /// Apply a stat bucket pick.
        public void AddBucket(string id)
        {
            relics[id] = B(id) + 1;
            RecalcMaxHp();
        }

        /// Wears an armor piece (replacing whatever was in its slot) and rebuilds the armor stat table.
        public void EquipArmor(Save.ArmorPiece piece)
        {
            armor.RemoveAll(a => a.slot == piece.slot);
            armor.Add(piece);
            RebuildGear();
        }

        void RebuildGear()
        {
            gear.Clear();
            foreach (var a in armor)
            {
                float m = Data.ARMOR_TIER_MULT[Mathf.Clamp(a.tier, 0, 2)];
                foreach (var k in a.stats) gear[k] = (gear.TryGetValue(k, out float v) ? v : 0f) + m;
            }
            RecalcMaxHp();
            mana = Mathf.Min(mana, MaxMana);
        }

        public Save.ArmorPiece ArmorIn(string slot) => armor.Find(a => a.slot == slot);

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
                if (e.dead || !game.CanTarget(e)) continue;   // WYVERN: no aim-snap onto what you cannot hurt
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
            p.y = game.terrain.SurfaceHeight(p.x, p.z, Pos.y);   // an island top when you stand on one
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
            spells["weapon"] = 1;
            spells["dragonfire"] = 1;
            spells["ride"] = 1;
            relics.Clear();
            armor.Clear();
            gear.Clear();
            talents.Clear();
            level = 1;
            xp = 0f;
            xpNext = 6f;
            barrier = 0f;
            System.Array.Clear(powerT, 0, powerT.Length);
            remountWait = 0f;
            ResetTalentState();
            if (hero != heroId || weaponNode == null || weaponNode.childCount == 0)
            {
                heroId = hero;
                for (int i = knight.childCount - 1; i >= 0; i--) DestroyImmediate(knight.GetChild(i).gameObject);
                shieldNode = null;
                BuildKnight();
            }
            for (int i = weaponNode.childCount - 1; i >= 0; i--) DestroyImmediate(weaponNode.GetChild(i).gameObject);
            // The save slot's permanent hero and dragon tree picks.
            LearnPermanentTalents();
            OnTalentsChanged();
            maxHp = Data.HEROES[heroId].hp;
            RecalcMaxHp();
            hp = maxHp;
            if (pet) pet.SetDragon(dragon);
            BuildWeapon();
            ApplySkin();
            FindStaff();
            AnimateKnight(0f, 0f);
        }

        /// Restores a saved hero (Continue): levels, buckets, talents, armor, health and mana.
        public void ApplyRunState(Save.RunState s)
        {
            level = Mathf.Max(1, s.level);
            xp = s.xp;
            xpNext = Mathf.Max(6f, s.xpNext);
            foreach (var e in s.spells) spells[e.key] = Mathf.Clamp(e.value, 1, Data.SPELL_MAX_LEVEL);
            foreach (var e in s.buckets) relics[e.key] = Mathf.Max(0, e.value);
            foreach (var t in s.talents) if (Data.IsRunTalent(t)) talents.Add(t);
            armor.Clear();
            foreach (var a in s.armor) armor.Add(a);
            OnTalentsChanged();
            RebuildGear();
            hp = Mathf.Clamp(maxHp * s.hpFrac, 1f, maxHp);
            mana = Mathf.Min(s.mana, MaxMana);
        }

        public void WriteRunState(Save.RunState s)
        {
            s.level = level;
            s.xp = xp;
            s.xpNext = xpNext;
            s.hpFrac = Mathf.Clamp01(hp / Mathf.Max(1f, maxHp));
            s.mana = mana;
            s.spells.Clear();
            foreach (var kv in spells) s.spells.Add(new Save.IntEntry { key = kv.Key, value = kv.Value });
            s.buckets.Clear();
            foreach (var kv in relics) s.buckets.Add(new Save.IntEntry { key = kv.Key, value = kv.Value });
            s.talents = new List<string>(talents);
            s.talents.RemoveAll(t => !Data.IsRunTalent(t));
            s.armor = new List<Save.ArmorPiece>(armor);
        }

        // ---------------------------------------------------------------- Asset Store hero model

        CharacterRig rig;
        Transform skinRoot;
        string skinHero = "";

        static readonly string[] IDLE = { "Idle_SwordShield", "Idle01", "Idle" };
        static readonly string[] WALK = { "Walk_SwordShield", "WalkForward", "BattleWalkForward", "Walk" };
        static readonly string[] RUN = { "Run_SwordShield", "BattleRunForward", "Run" };
        static readonly string[] AIR = { "JumpAir", "Idle_SwordShield", "Idle01", "Idle" };
        static readonly string[] SWING = { "NormalAttack01_SwordShield", "Attack01" };
        static readonly string[] CAST = { "Attack01" };
        static readonly string[] THROW = { "Attack01" };
        static readonly string[] HURT = { "GetHit_SwordShield", "GetHit" };
        static readonly string[] LEAP = { "JumpUpAttack", "NormalAttack02_SwordShield", "Attack01" };
        static readonly string[] DIE = { "Die_SwordShield", "Die" };

        /// Swaps the procedural hero for the Asset Store model when the skin library has one for this hero.
        void ApplySkin()
        {
            var lib = Skins.Lib;
            var prefab = lib ? lib.Hero(heroId) : null;
            if (skinHero != heroId || (prefab == null && skinRoot != null))
            {
                if (skinRoot) DestroyImmediate(skinRoot.gameObject);
                skinRoot = null;
                rig = null;
                skinHero = heroId;
                if (prefab)
                {
                    skinRoot = FX.Node(transform, "HeroSkin");
                    rig = Skins.SpawnCharacter(prefab, skinRoot, 1.8f, lib.HeroController(heroId));
                    if (IsHuntress) DressHuntress();
                    rig.Loop(IDLE);
                }
            }
            SetHeroVisible(true);
        }

        /// The Blood Huntress model (the Kevin Iglesias female mannequin) is dressed for the part: a dark leather
        /// bodysuit, a crimson cuirass, pauldrons, belt and tassets, steel bracers and greaves, a cape, a hood with
        /// glowing eyes, and a glowing Blood Javelin held where the pack's polearm sits (that polearm is hidden).
        void DressHuntress()
        {
            var suit = FX.Lit(new Color(0.16f, 0.07f, 0.08f), 0.75f);
            var crimson = FX.Lit(new Color(0.55f, 0.04f, 0.08f), 0.45f, 0.35f);
            var darkSteel = FX.Lit(new Color(0.22f, 0.2f, 0.22f), 0.35f, 0.85f);
            var leather = FX.Lit(new Color(0.1f, 0.05f, 0.04f), 0.8f);
            var capeMat = FX.Lit(new Color(0.32f, 0.02f, 0.05f), 0.8f);
            Transform polearm = null;
            foreach (var r in rig.model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (r.name.IndexOf("Polearm", System.StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("Weapon", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    polearm = r.transform;
                    r.enabled = false;
                    r.gameObject.SetActive(false);
                    continue;
                }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = suit;
                r.sharedMaterials = mats;
            }
            var faceRot = transform.rotation;   // the model faces -Z in the hero's frame
            // A node on a bone in world units (bones carry the model's import scale), turned to the hero's frame.
            Transform On(HumanBodyBones b, string name, Vector3 offset = default)
            {
                var bone = rig.Bone(b);
                if (bone == null) return null;
                var n = FX.Node(bone, name);
                n.localScale = Vector3.one / Mathf.Max(0.0001f, bone.lossyScale.x);
                n.SetPositionAndRotation(bone.position + faceRot * offset, faceRot);
                return n;
            }
            // A piece running down a limb from bone a toward bone b (between fractions t0 and t1 of the way).
            void Limb(HumanBodyBones a, HumanBodyBones b, Material m, float radius, float t0, float t1, string name)
            {
                var ta = rig.Bone(a);
                var tb = rig.Bone(b);
                if (ta == null || tb == null) return;
                var dir = tb.position - ta.position;
                var n = FX.Node(ta, name);
                n.localScale = Vector3.one / Mathf.Max(0.0001f, ta.lossyScale.x);
                n.SetPositionAndRotation(ta.position + dir * (t0 + t1) * 0.5f, Quaternion.FromToRotation(Vector3.up, dir.normalized));
                FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 10), m, Vector3.zero, new Vector3(radius * 2f, dir.magnitude * (t1 - t0), radius * 2f));
            }
            var chestBone = rig.Bone(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : HumanBodyBones.Chest;
            var chest = On(chestBone, "Cuirass");
            if (chest)
            {
                FX.Part(chest, MeshGen.Sphere(14), crimson, new Vector3(0, -0.02f, -0.02f), new Vector3(0.34f, 0.36f, 0.25f));
                FX.Part(chest, MeshGen.Box(), darkSteel, new Vector3(0, -0.02f, -0.14f), new Vector3(0.05f, 0.24f, 0.03f));
                // Cape from the shoulders down the back.
                FX.Part(chest, MeshGen.Box(), capeMat, new Vector3(0, -0.42f, 0.17f), new Vector3(0.44f, 0.9f, 0.025f), new Vector3(-0.12f, 0, 0));
                FX.Part(chest, MeshGen.Box(), crimson, new Vector3(0, 0.05f, 0.15f), new Vector3(0.46f, 0.08f, 0.06f));
            }
            var hips = On(HumanBodyBones.Hips, "Belt");
            if (hips)
            {
                FX.Part(hips, MeshGen.Cylinder(0.5f, 0.5f, 12), leather, new Vector3(0, 0.06f, 0), new Vector3(0.33f, 0.07f, 0.24f));
                FX.Part(hips, MeshGen.Sphere(8), darkSteel, new Vector3(0, 0.06f, -0.12f), new Vector3(0.07f, 0.06f, 0.03f));
                foreach (int s in new[] { -1, 1 })
                    FX.Part(hips, MeshGen.Box(), crimson, new Vector3(s * 0.11f, -0.07f, -0.04f), new Vector3(0.12f, 0.2f, 0.03f), new Vector3(0.15f, 0, s * 0.15f));
            }
            foreach (var (bone, s) in new[] { (HumanBodyBones.LeftUpperArm, -1), (HumanBodyBones.RightUpperArm, 1) })
            {
                var p = On(bone, "Pauldron", new Vector3(0, 0.03f, 0));
                if (p) FX.Part(p, MeshGen.Sphere(10), crimson, Vector3.zero, new Vector3(0.15f, 0.1f, 0.15f));
            }
            Limb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, darkSteel, 0.05f, 0.35f, 0.9f, "Bracer");
            Limb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, darkSteel, 0.05f, 0.35f, 0.9f, "Bracer");
            Limb(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, darkSteel, 0.06f, 0.3f, 0.95f, "Greave");
            Limb(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, darkSteel, 0.06f, 0.3f, 0.95f, "Greave");
            BuildHuntressJavelin(polearm);
            var head = rig.Bone(HumanBodyBones.Head);
            if (head)
            {
                var hood = FX.Node(head, "Hood");
                hood.localScale = Vector3.one / Mathf.Max(0.0001f, head.lossyScale.x);
                hood.rotation = transform.rotation;
                var cloth = FX.Lit(new Color(0.45f, 0.03f, 0.06f), 0.75f);
                FX.Part(hood, MeshGen.Sphere(12), cloth, new Vector3(0, 0.1f, 0.03f), new Vector3(0.3f, 0.32f, 0.32f));
                FX.Part(hood, MeshGen.Cylinder(0f, 0.5f, 8), cloth, new Vector3(0, 0.28f, 0.1f), new Vector3(0.24f, 0.26f, 0.24f), new Vector3(-0.5f, 0, 0));
                foreach (int e in new[] { -1, 1 })
                    FX.NoShadow(FX.Part(hood, MeshGen.Sphere(8), FX.Glow(new Color(1f, 0.15f, 0.15f), 3f), new Vector3(e * 0.05f, 0.08f, -0.13f), Vector3.one * 0.04f));
            }
        }

        /// The Blood Javelin in the Huntress's hand: laid along the pack polearm's long axis (so the pack's grip and
        /// animations hold it right), its glowing blood tip at the end away from her hand.
        void BuildHuntressJavelin(Transform polearm)
        {
            var hand = rig.Bone(HumanBodyBones.RightHand);
            Transform parent = polearm ? polearm.parent : hand;
            if (parent == null) return;
            Vector3 center, axis;
            float length = 1.9f;
            var mf = polearm ? polearm.GetComponent<MeshFilter>() : null;
            var smr = polearm ? polearm.GetComponent<SkinnedMeshRenderer>() : null;
            var mesh = mf ? mf.sharedMesh : smr ? smr.sharedMesh : null;
            if (mesh != null)
            {
                var b = mesh.bounds;
                int k = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
                var local = Vector3.zero;
                local[k] = 1f;
                center = polearm.TransformPoint(b.center);
                axis = polearm.TransformVector(local * b.size[k]);
                length = Mathf.Clamp(axis.magnitude, 1.5f, 2.2f);
                axis = axis.normalized;
                // Point the tip away from the grip.
                if (Vector3.Dot(center - parent.position, axis) < 0f) axis = -axis;
            }
            else
            {
                center = parent.position;
                axis = transform.rotation * new Vector3(0, 1f, -0.2f).normalized;
            }
            var n = FX.Node(parent, "BloodJavelin");
            n.localScale = Vector3.one / Mathf.Max(0.0001f, parent.lossyScale.x);
            n.SetPositionAndRotation(center, Quaternion.FromToRotation(Vector3.up, axis));
            var shaft = FX.Lit(new Color(0.18f, 0.06f, 0.07f), 0.5f, 0.3f);
            var glow = FX.Glow(new Color(1f, 0.1f, 0.12f), 2.8f);
            FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 8), shaft, Vector3.zero, new Vector3(0.05f, length, 0.05f));
            FX.NoShadow(FX.Part(n, MeshGen.Cylinder(0f, 0.5f, 6), glow, new Vector3(0, length * 0.5f + 0.18f, 0), new Vector3(0.11f, 0.38f, 0.11f)));
            FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 8), FX.Lit(new Color(0.75f, 0.75f, 0.78f), 0.3f, 0.9f), new Vector3(0, length * 0.5f - 0.02f, 0), new Vector3(0.08f, 0.05f, 0.08f));
            FX.Part(n, MeshGen.Cylinder(0.5f, 0.5f, 8), FX.Lit(new Color(0.08f, 0.04f, 0.03f), 0.9f), new Vector3(0, -length * 0.1f, 0), new Vector3(0.065f, 0.3f, 0.065f));
            FX.NoShadow(FX.Part(n, MeshGen.Cylinder(0.5f, 0f, 6), glow, new Vector3(0, -length * 0.5f - 0.06f, 0), new Vector3(0.07f, 0.14f, 0.07f)));
        }

        /// Shows the hero (procedural body or the skin) or hides it (Dragon Form). With a skin, the procedural body stays
        /// hidden: its joints still drive the casting hand.
        void SetHeroVisible(bool on)
        {
            bool skinned = rig != null;
            foreach (var r in knight.GetComponentsInChildren<Renderer>(true))
                r.enabled = on && (!skinned || r == handGlow);
            if (skinRoot)
                foreach (var r in skinRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
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
            if (IsPyro)
            {
                // The Ember Staff: a dark shaft with gold bands and a glowing ember crystal at the top.
                var wood = FX.Lit(new Color(0.22f, 0.12f, 0.07f), 0.7f);
                FX.Part(w, MeshGen.Cylinder(0.5f, 0.5f, 8), wood, new Vector3(0, 0.55f, 0), new Vector3(0.07f, 2.1f, 0.07f));
                FX.Part(w, MeshGen.Cylinder(0.5f, 0.5f, 8), gold, new Vector3(0, 1.45f, 0), new Vector3(0.1f, 0.08f, 0.1f));
                FX.Part(w, MeshGen.Cylinder(0.5f, 0.5f, 8), gold, new Vector3(0, -0.2f, 0), new Vector3(0.1f, 0.08f, 0.1f));
                staffTipProc = FX.Part(w, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.5f, 0.12f), 3f), new Vector3(0, 1.72f, 0), new Vector3(0.2f, 0.3f, 0.2f)).transform;
                FX.NoShadow(staffTipProc.GetComponent<Renderer>());
                return;
            }
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

        // ---------------------------------------------------------------- Ember Staff

        Transform staffTipProc;
        Renderer staffSkin;
        Transform staffGem;

        /// The wizard model carries its own staff: it is the Ember Mage's weapon now. Bolts leave from its top, where a
        /// glowing ember crystal is added.
        void FindStaff()
        {
            staffSkin = null;
            if (staffGem) DestroyImmediate(staffGem.gameObject);
            staffGem = null;
            if (!IsPyro || rig == null) return;
            foreach (var r in rig.model.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Staff")) continue;
                r.gameObject.SetActive(true);
                if (staffSkin == null || r.bounds.size.y > staffSkin.bounds.size.y) staffSkin = r;
            }
            if (staffSkin == null) return;
            staffGem = FX.Node(transform, "StaffGem");
            FX.NoShadow(FX.Part(staffGem, MeshGen.Sphere(10), FX.Glow(new Color(1f, 0.5f, 0.12f), 3f), Vector3.zero, Vector3.one * 0.13f));
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(staffGem, false);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.2f);
            l.intensity = 1.2f;
            l.range = 3.5f;
            l.shadows = LightShadows.None;
            LateUpdate();
        }

        /// Where Ember Mage bolts leave from: the top of the staff.
        Vector3 StaffTip
        {
            get
            {
                if (staffSkin)
                {
                    // The staff is held upright: its top is the top of its bounds.
                    var b = staffSkin.bounds;
                    return new Vector3(b.center.x, b.max.y - 0.12f, b.center.z);
                }
                if (staffTipProc) return staffTipProc.position;
                return HandPosition;
            }
        }

        void LateUpdate()
        {
            if (staffGem) staffGem.position = StaffTip;
        }

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
            mountT = Mathf.MoveTowards(mountT, flying || SoaringForm ? 1f : 0f, delta * 5f);
            float moving = Mathf.Clamp(hspeed / 10f, 0f, 1f);
            walkT += delta * (3f + hspeed * 0.9f);
            float stride = onGround ? Mathf.Sin(walkT) * 0.75f * moving : 0.35f;
            var walkL = new Vector3(stride, 0, 0);
            var walkR = new Vector3(onGround ? -stride : 0.6f, 0, 0);
            var rideL = new Vector3(-1.2f, 0, -0.55f);
            var rideR = new Vector3(-1.2f, 0, 0.55f);
            float ride = flying ? mountT : 0f;
            legL.Rot = Vector3.Lerp(walkL, rideL, ride);
            legR.Rot = Vector3.Lerp(walkR, rideR, ride);
            float bob = onGround ? Mathf.Abs(Mathf.Sin(walkT)) * 0.06f * moving * (1f - mountT) : 0f;
            var seat = SEAT;
            if (pet != null && pet.SeatWorld.HasValue) seat = transform.InverseTransformPoint(pet.SeatWorld.Value);
            knight.localPosition = Vector3.Lerp(new Vector3(0, bob, 0), seat, ride);
            AnimateSkin(hspeed);
            cape.RotX = Mathf.Lerp(cape.RotX, Mathf.Clamp(hspeed * 0.06f, 0f, 1.2f) + Mathf.Sin(Time.time * 12f) * 0.05f, delta * 6f);
            blockAnim = Mathf.MoveTowards(blockAnim, 0f, delta * 4f);
            if (blockAnim > 0f) armL.Rot = new Vector3(-1.3f * blockAnim, 0.4f * blockAnim, 0);
            float glow = 1.5f + Mathf.Round(Mathf.Clamp01(mana / MaxMana) * 2.5f / 0.5f) * 0.5f;
            handGlow.sharedMaterial = FX.Glow(GlowColor, glow);
            AnimateDragonForm(delta, hspeed);
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
            remountWait = REMOUNT_GROUND_TIME;
            remountLanded = false;
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
            if (!game.Paused && game.running)
            {
                // Gamepad: the right stick looks around (frame-rate independent, unaffected by the game speed), D-pad zooms.
                var look = GameInput.LookStick;
                if (look.sqrMagnitude > 0f)
                {
                    float ud = Time.timeScale > 0f ? Time.deltaTime / Time.timeScale : 0f;   // real seconds, whatever the game speed
                    camYaw += look.x * 2.8f * Save.mouse_sens * ud;
                    camPitch = Mathf.Clamp(camPitch - look.y * 1.8f * Save.mouse_sens * ud, -1.35f, 1.48f);
                    Cursor.visible = false;
                }
                if (!GameInput.MouseCaptured)
                {
                    float sc = GameInput.Scroll;
                    if (sc > 0f) camDistTarget = Mathf.Clamp(camDistTarget * 0.87f, 2.5f, 60f);
                    else if (sc < 0f) camDistTarget = Mathf.Clamp(camDistTarget * 1.15f, 2.5f, 60f);
                }
                if (GameInput.ZoomResetPressed) camDistTarget = 9f;
            }
            if (game.Paused) return;
            if (!game.running || game.traveling)
            {
                AnimateKnight(delta, 0f);
                UpdateCamera(delta);
                return;
            }
            Move(delta);
            UpdateCamera(delta);
            hurtTimer = Mathf.Max(hurtTimer - delta, 0f);
            TickPowerUps(delta);
            TickTalents(delta);
            hp = Mathf.Min(maxHp, hp + Regen * delta);
            AddMana(ManaRegen * delta);
            attackTimer -= delta;
            if (attackTimer <= 0f && !SoaringForm)
            {
                bool attacked = IsKnight ? Attack() : (IsHuntress ? Throw() : CastBolts());
                attackTimer = attacked ? AttackInterval : 0.1f;
            }
            // Q: call the dragon and take flight (Q again to drop off). E: dragon signature (ground or air).
            if (GameInput.FlyPressed && !leaping && !SoaringForm)
            {
                if (flying) ReleaseDragon();
                else if (!CanFly) game.hud.Toast($"Your {Data.DRAGON_AGE[0].ToLower()} is too small to carry you yet: it grows in the next world");
                else if (remountWait > 0f) game.hud.Toast(remountLanded ? "Your dragon is circling back..." : "Land first: your dragon can only pick you up from solid ground");
                else if (flightStamina > 0.3f)
                {
                    SummonDragon();
                    velocity.y = 10f;
                    onGround = false;
                    jumpedFromGround = false;
                }
                else game.hud.Toast("Your dragon needs to rest...");
            }
            if (GameInput.AbilityPressed && !leaping && !SoaringForm)
                if (pet.RideCommand()) CastAnim();
            if (GameInput.TalentPressed) TryDragonForm();
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
            bool soaring = flying || SoaringForm;
            float speed = 9.5f * MoveMult;
            if (flying) speed = 17f * MoveMult * (hookedT > 0f ? Data.HARPOON_SLOW : 1f);
            hookedT = flying ? Mathf.Max(0f, hookedT - delta) : 0f;
            if (SoaringForm) speed = 19f * MoveMult;
            float accel = (onGround || soaring) ? 10f : (leaping ? 0f : 4f);
            var hv = Vector3.Lerp(new Vector3(velocity.x, 0, velocity.z), wish * speed, Mathf.Clamp01(accel * delta));
            if (chargeT > 0f) hv = chargeDir * 42f;
            velocity.x = hv.x;
            velocity.z = hv.z;

            float ground = terrain.GroundHeight(Pos.x, Pos.z, Pos.y);
            if (GameInput.JumpPressed && !leaping && !soaring)
            {
                if (onGround)
                {
                    velocity.y = JUMP_VELOCITY * Mathf.Sqrt(JumpMult);
                    onGround = false;
                    jumpedFromGround = true;
                    airJumps = 0;
                }
                else if (airJumps < MaxAirJumps)
                {
                    // Double jump (Wings of Blood adds two more).
                    airJumps++;
                    velocity.y = JUMP_VELOCITY * 0.9f * Mathf.Sqrt(JumpMult);
                    FX.Shockwave(Pos + new Vector3(0, 0.2f, 0), 2f, Has("h_wings") ? new Color(1f, 0.2f, 0.25f, 0.6f) : new Color(0.8f, 0.85f, 1f, 0.5f), 0.25f);
                    FlapWings();
                }
            }
            if (soaring)
            {
                if (flying && !UnlimitedFlight) flightStamina -= delta;
                if (GameInput.JumpHeld) velocity.y = Mathf.MoveTowards(velocity.y, 10f, 30f * delta);
                else if (GameInput.DescendHeld) velocity.y = Mathf.MoveTowards(velocity.y, -16f, 45f * delta);
                else velocity.y = Mathf.MoveTowards(velocity.y, 0f, 14f * delta);   // the dragon holds its height until told otherwise
                if (Pos.y > ground + MAX_ALTITUDE) velocity.y = Mathf.Min(velocity.y, 0f);
                if (flying && flightStamina <= 0f) ReleaseDragon("Your dragon tires and sets you down...");
            }
            else
            {
                velocity.y -= GRAVITY * delta;
                // Wings of Blood: holding jump while falling glides.
                if (Has("h_wings") && !onGround && GameInput.JumpHeld && velocity.y < -3f) velocity.y = -3f;
            }

            var pos = Pos + velocity * delta + KnockStep(delta);
            float lim = WorldTerrain.PLAY_LIMIT + 8f;
            pos.x = Mathf.Clamp(pos.x, -lim, lim);
            pos.z = Mathf.Clamp(pos.z, -lim, lim);
            // Solid islands and ruins, then the ogres; a push keeps only the speed along the surface (so you slide along walls).
            var wanted = pos;
            if (terrain.CollidePlayer(Pos, ref pos, PhysRadius, PhysHeight, ref velocity.y)) knockVel = Vector3.zero;   // a wall ends the shove
            CollideEnemies(ref pos, delta);
            if (delta > 0f && (Mathf.Abs(pos.x - wanted.x) + Mathf.Abs(pos.z - wanted.z)) > 1e-5f)
            {
                velocity.x = Mathf.Lerp(velocity.x, (pos.x - Pos.x) / delta, 0.8f);
                velocity.z = Mathf.Lerp(velocity.z, (pos.z - Pos.z) / delta, 0.8f);
            }
            float g = terrain.GroundHeight(pos.x, pos.z, Pos.y);
            // Walking down a slope the ground falls away faster than gravity pulls: stay stuck to it (unless jumping,
            // flying or leaping) instead of floating a few centimeters up and flickering into the air animation.
            if (onGround && !soaring && !leaping && velocity.y <= 0f && pos.y > g)
            {
                float hstep = new Vector2(pos.x - Pos.x, pos.z - Pos.z).magnitude;
                if (pos.y - g <= GROUND_SNAP + hstep * 1.2f) pos.y = g;
            }
            // On the dragon, brushing the ground (rising terrain) just skims over it; only descending (Shift) lands you.
            if (flying && pos.y <= g + 0.6f && !GameInput.DescendHeld)
            {
                pos.y = g + 0.6f;
                if (velocity.y < 0f) velocity.y = 0f;
            }
            if (pos.y <= g)
            {
                pos.y = g;
                if (velocity.y < 0f) velocity.y = 0f;
                if (flying && velocity.y <= 0f) ReleaseDragon();
                if (leaping) Slam();
                if (!onGround) OnLanded();
                onGround = true;
            }
            else onGround = false;
            Pos = pos;
            // Flight stamina refills while you're on the ground or in a jump from it (not while falling off the dragon);
            // from 0:00 it stays full. The remount timer starts the moment you first touch solid ground after leaving
            // the dragon, and keeps running from then on (jumping doesn't reset it).
            if (onGround)
            {
                jumpedFromGround = false;
                remountLanded = true;
            }
            if (UnlimitedFlight) flightStamina = MaxFlight;
            else if (onGround || (jumpedFromGround && !flying && !SoaringForm))
                flightStamina = Mathf.Min(MaxFlight, flightStamina + delta * FlightRegen);
            if (remountLanded) remountWait = Mathf.Max(0f, remountWait - delta);

            float hspeed = new Vector2(velocity.x, velocity.z).magnitude;
            if (hspeed > 0.6f)
            {
                float targetYaw = Mathf.Atan2(-velocity.x, -velocity.z);
                yaw = M.LerpAngle(yaw, targetYaw, Mathf.Clamp01(delta * 10f));
            }
            transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
            AnimateKnight(delta, hspeed);
        }

        /// Air jumps after the first jump: one, or three with Wings of Blood.
        int MaxAirJumps => Has("h_wings") ? 3 : 1;

        // ---------------------------------------------------------------- solid world

        /// Body size used against islands, ruins and ogres (bigger on the dragon).
        public float PhysRadius => flying || SoaringForm ? 1.2f : 0.5f;
        public float PhysHeight => flying || SoaringForm ? 2.6f : 1.9f;

        /// Small ogres get shoved aside (and slow you a little), big ogres and bosses are solid. Flying above them ignores them.
        void CollideEnemies(ref Vector3 pos, float delta)
        {
            if (SpecterActive) return;   // the Crimson Specter passes straight through
            float pr = PhysRadius;
            var back = Vector3.zero;
            int shoved = 0;
            var list = game.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.dead || e.IsWyvern) continue;   // WYVERN: flies far above, never blocks you
                var ep = e.transform.position;
                float dx = pos.x - ep.x, dz = pos.z - ep.z;
                float rr = pr + e.radius;
                float d2 = dx * dx + dz * dz;
                if (d2 >= rr * rr) continue;
                if (pos.y > ep.y + e.size * 1.7f || pos.y + PhysHeight < ep.y + 0.2f) continue;
                float d = Mathf.Sqrt(d2);
                float nx, nz;
                if (d > 0.001f) { nx = dx / d; nz = dz / d; }
                else { float a = M.RandF() * M.TAU; nx = Mathf.Cos(a); nz = Mathf.Sin(a); }
                float o = rr - d;
                BumpInto(e, nx, nz);
                if (e.Solid)
                {
                    pos.x += nx * o;
                    pos.z += nz * o;
                }
                else
                {
                    e.Shove(new Vector3(-nx, 0f, -nz) * (o * 0.9f));
                    back += new Vector3(nx, 0f, nz) * (o * 0.2f);
                    shoved++;
                }
            }
            if (shoved == 0 || chargeT > 0f) return;
            // Pushing through a crowd costs speed, but never stops you completely.
            float cap = 0.5f * new Vector2(velocity.x, velocity.z).magnitude * delta + 0.012f;
            if (back.magnitude > cap) back = back.normalized * cap;
            pos += back;
            float keep = Mathf.Pow(0.3f, delta * Mathf.Min(shoved, 5));
            velocity.x *= keep;
            velocity.z *= keep;
        }

        /// Puts the hero at a spot (stage start, teleports): stops all motion and leaves the saddle.
        public void Teleport(Vector3 at)
        {
            ReleaseDragon();
            EndDragonForm();
            leaping = false;
            chargeT = 0f;
            velocity = Vector3.zero;
            knockVel = Vector3.zero;   // a shove left over from before the portal must not carry into the new stage
            knockCd = 0f;
            hookedT = 0f;
            Pos = at;
            onGround = true;
            flightStamina = MaxFlight;
            remountWait = 0f;
            SnapCamera();
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
            float drop = Pos.y - game.terrain.GroundHeight(Pos.x + flat.x, Pos.z + flat.z, Pos.y);
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
            // The slam is part of the dragon's signature, so it takes the dragon's color.
            var dc = Data.DRAGONS[dragonId].breath;
            if (!FX.Vfx("ground_explosion", at, rad / 6f, 2.5f, null, 0.04f, dc))
                FX.Flash(at + new Vector3(0, 0.8f, 0), rad * 0.8f, new Color(dc.r, dc.g, dc.b, 0.8f), 0.35f);
            FX.Shockwave(at + new Vector3(0, 0.3f, 0), rad * 1.3f, new Color(dc.r, dc.g, dc.b, 0.9f), 0.45f, dragonId != "black");
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
            camH = Mathf.Lerp(camH, flying || SoaringForm ? 2.8f : 2f, Mathf.Clamp01(delta * 4f));
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
                    bool hitTerrain = p.y < terrain.Height(p.x, p.z) + 0.4f;
                    if (hitTerrain || terrain.CameraBlocked(p))
                    {
                        want = Vector3.Lerp(target, want, Mathf.Max((float)(i - 1) / steps, 0.08f));
                        if (hitTerrain)
                        {
                            float g2 = terrain.Height(want.x, want.z) + 0.6f;
                            want.y = Mathf.Max(want.y, g2);
                        }
                        break;
                    }
                }
            }
            var ct = cam.transform;
            var basePos = ct.position - ct.rotation * shakeOffsetApplied;
            basePos = Vector3.Distance(basePos, want) < 40f ? Vector3.Lerp(basePos, want, Mathf.Clamp01(delta * 12f)) : want;
            if (game && game.terrain && game.terrain.CameraBlocked(basePos)) basePos = want;
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

        int swingCount;
        /// How far the sword reaches above an ogre's head and below its feet (from the hero's chest).
        const float SWORD_REACH_UP = 6f, SWORD_REACH_DOWN = 6f;

        bool Attack()
        {
            var w = Weapon;
            float rngMult = AreaMult * (1f + 0.06f * (weaponLevel - 1)) * (Has("k_reach") ? 1.35f : 1f);
            float reach = w.range * rngMult;
            var c = Center;
            var target = game.NearestEnemy(c, reach + 1.5f, SWORD_REACH_DOWN + 2f);
            if (target == null) return false;
            swingCount++;
            bool spin = Has("k_whirlwind") && swingCount % 5 == 0;
            var dir = M.Flat(target.Pos - Pos).normalized;
            float arc = spin ? 360f : w.arc;
            float dmg = WeaponBaseDamage * WeaponDamageMult * (spin ? 2f : 1f);
            float knock = w.knock * (Has("k_reach") ? 1.5f : 1f);
            float cosHalf = Mathf.Cos(arc * 0.5f * Mathf.Deg2Rad);
            int hits = 0;
            var list = game.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.dead || !game.CanTarget(e)) continue;   // WYVERN: the sword passes through it on foot
                var to = e.Pos - Pos;
                // Tall hitbox: anything whose body (feet to head) comes within reach above or below the blade.
                if (c.y > e.Pos.y + 2f * e.size + SWORD_REACH_UP || c.y < e.Pos.y - SWORD_REACH_DOWN) continue;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > reach + e.radius) continue;
                if (arc < 359f && dist > 0.5f && Vector3.Dot(to.normalized, dir) < cosHalf) continue;
                var knockDir = dist > 0.01f ? to.normalized : dir;
                game.DamageEnemy(e, dmg, knockDir, knock);
                if (Has("k_wyrmsteel")) e.Ignite(3f, dmg * 0.12f);
                if (hits >= 2 || !FX.Vfx("hit", e.Pos + new Vector3(0, e.size, 0), 0.8f, 1.2f, null, 0f))
                    FX.Burst(e.Pos + new Vector3(0, e.size, 0), new Color(1f, 0.9f, 0.7f), 5, 6f, 0.15f);
                hits++;
            }
            WeaponHits(hits, dmg);
            Sfx.Play("slash", -8f);
            if (hits > 0) Sfx.Play("hit", -3f, 0.12f);
            var col = Has("k_wyrmsteel") ? new Color(1f, 0.6f, 0.2f) : w.color;
            SwingFx(dir, reach, arc, col);
            SwingAnim();
            if (Has("k_firewave")) FireWave.Spawn(this, Pos + dir * 1.2f, dir, WeaponBaseDamage * WeaponDamageMult * 0.8f);
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

        /// Weapon hits feed mana (and life, with Bloodthirst or the Blood Moon).
        public void WeaponHits(int hits, float dmg)
        {
            if (hits <= 0) return;
            AddMana(Mathf.Min(hits * ManaPerHit, 10f + 6f * Plv("spellblade")));
            float ls = Lifesteal;
            if (ls > 0f) hp = Mathf.Min(maxHp, hp + dmg * hits * ls);
        }

        /// Ember Mage auto attack: bursting fire bolts from the staff. Extra bolts pick other targets, or fan out at the only one.
        bool CastBolts()
        {
            var w = Weapon;
            var origin = StaffTip;
            int count = ShotCount;
            var used = new List<Enemy>();
            Enemy first = null;
            float radius = w.radius * (weaponLevel >= 5 ? 1.35f : 1f) * (Has("p_inferno") ? 1.8f : 1f);
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
                    dir = (dir + CamRight * (i % 2 == 0 ? 0.09f : -0.09f) * ((i + 1) / 2)).normalized;
                }
                else break;
                Fireball.Spawn(game, this, origin, dir * 44f, WeaponBaseDamage * WeaponDamageMult, radius, 0.15f * BurnMult, true, null, Has("p_inferno") ? 1 : 0);
            }
            if (first == null) return false;
            Sfx.Play("flame", -10f, 0.15f);
            FX.Flash(origin, 0.8f, new Color(1f, 0.55f, 0.15f, 0.6f), 0.12f);
            CastAnim();
            return true;
        }

        int throwCount;

        /// Blood Huntress auto attack: piercing Blood Javelins paid for with 2% of current health.
        bool Throw()
        {
            var w = Weapon;
            var c = Center + new Vector3(0, 0.4f, 0);
            int count = ShotCount;
            var used = new List<Enemy>();
            Enemy first = null;
            for (int i = 0; i < count; i++)
            {
                var t = game.NearestEnemy(c, w.range, 16f, null, used);
                Vector3 aim;
                if (t != null)
                {
                    used.Add(t);
                    if (first == null) first = t;
                    aim = t.Pos + new Vector3(0, t.size, 0);
                }
                else if (first != null)
                {
                    aim = first.Pos + new Vector3(0, first.size, 0) + CamRight * ((i % 2 == 0 ? 1f : -1f) * 1.4f * ((i + 1) / 2));
                }
                else break;
                BloodJavelin.Spawn(this, c, (aim - c).normalized * 52f, WeaponBaseDamage * WeaponDamageMult);
            }
            if (first == null) return false;
            throwCount++;
            if (Has("h_lance") && throwCount % 8 == 0)
                BloodJavelin.Spawn(this, c, (first.Pos + new Vector3(0, first.size, 0) - c).normalized * 46f, WeaponBaseDamage * WeaponDamageMult * 3f, BloodJavelin.Kind.Lance);
            // The blood price: 2% of current health (never lethal; Blood Pact waives it).
            if (!Has("h_pact")) hp = Mathf.Max(1f, hp - hp * w.hp_cost);
            Sfx.Play("whoosh", -8f, 0.15f);
            armR.Keys((new Vector3(-2.6f, 0, 0), 0.08f), (new Vector3(0.4f, 0, 0), 0.1f), (new Vector3(-0.5f, 0, 0), 0.2f));
            if (rig && !rig.InAction) rig.Action(0.35f, THROW);
            return true;
        }

        float invulnT;

        /// Seconds left with a harpoon still hooked in the dragon (it flies slower; a second hit drags you down).
        public float hookedT;

        /// An Ogre Harpooner's harpoon struck you in the air: it drains flight and slows the dragon. Only a second
        /// harpoon while the first is still hooked pulls you out of the sky.
        public void HarpoonHit(float damage, Enemy thrower = null)
        {
            if (!flying || game == null || !game.running || game.traveling) return;
            if (pet.TryWingGuard(Center)) return;
            TakeDamage(damage);
            FX.Burst(Center, new Color(1f, 0.45f, 0.2f), 14, 7f, 0.2f);
            Sfx.Play("clang", -2f, 0.1f);
            // A harpoon landing right after the first (a volley) only hurts: a dismount needs a real second hit.
            if (hookedT > Data.HARPOON_HOOK_TIME - Data.HARPOON_GRACE)
            {
                game.hud.Toast("Harpooned again! Shake it off before the next one");
                return;
            }
            if (hookedT > 0f)
            {
                hookedT = 0f;
                ReleaseDragon();
                velocity.y = Mathf.Min(velocity.y, -8f);
                // Make the dismount unmistakable: banner, red flash, a heavy shake and the chain yanking you down.
                game.hud.Banner("DRAGGED FROM THE SKY", "A second harpoon pulled you off your dragon");
                game.hud.HurtFlash();
                game.Shake(0.9f);
                Sfx.Play("roar", -2f, 0f);
                if (thrower != null && !thrower.dead)
                    FX.Bolt(Center, thrower.Pos + new Vector3(0, 2f * thrower.size, 0), new Color(1f, 0.35f, 0.15f), 0.3f, 0.6f, 4);
                FX.Shockwave(Center, 4f, new Color(1f, 0.3f, 0.15f, 0.8f), 0.4f);
                return;
            }
            hookedT = Data.HARPOON_HOOK_TIME;
            flightStamina = Mathf.Max(0.5f, flightStamina - Data.HARPOON_FLIGHT_DRAIN);
            game.hud.Splash("Harpooned!", "(One more and you're grounded!)", new Color(1f, 0.45f, 0.2f));
            game.hud.Toast("Your dragon is slowed: shake off the harpoon before another one hits");
        }

        public void TakeDamage(float amount, Enemy attacker = null)
        {
            if (game == null || !game.running || game.traveling || hurtTimer > 0f || leaping || invulnT > 0f) return;
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
                    OnDodge();
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
                if (Has("p_emberward")) Heal(maxHp * 0.03f);
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
                OnBlock();
                return;
            }
            float dmg = Mathf.Max(1f, amount - Armor) * DamageTakenMult;
            if (attackerAlive && Has("p_molten")) attacker.Ignite(4f, 10f + 2f * level);
            if (attackerAlive) DragonWrath(attacker);
            if (Has("k_retribution")) Retribution(amount);
            // Barriers soak damage first.
            if (barrier > 0f)
            {
                float soak = Mathf.Min(barrier, dmg);
                barrier -= soak;
                dmg -= soak;
                FX.Text(at + Vector3.up * 0.4f, "Absorbed", new Color(0.7f, 0.85f, 1f));
                if (dmg <= 0f)
                {
                    hurtTimer = 0.12f;
                    return;
                }
            }
            hp -= dmg;
            hurtTimer = 0.12f;
            game.hud.HurtFlash();
            if (rig && !rig.InAction) rig.Action(0.3f, HURT);
            game.Shake(0.25f);
            Sfx.Play("hurt", 0f);
            FX.Number(at, dmg, new Color(1f, 0.3f, 0.3f));
            if (hp <= 0f && !CheatDeath())
            {
                hp = 0f;
                if (rig && !flying) rig.Action(60f, DIE);
                game.OnPlayerDied();
                return;
            }
            AfterHurt();
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
                // +1% max health each level (and heal that much).
                RecalcMaxHp();
                // Each level costs more than the last, easing off at the top (level 10: ~165, level 30: ~855, level 50: ~1950).
                xpNext = 8f + (level - 1) * 8f + Mathf.Pow(level - 1, 1.7f) * 2f;
            }
        }
    }

    /// The Blood Huntress's javelin: pierces every ogre in its path and makes them bleed. Talents add an execute,
    /// a split into three on the first hit (Crimson Tempest) and the giant exploding Bloodstorm Lance.
    public class BloodJavelin : MonoBehaviour
    {
        public enum Kind { Normal, Lance, Shard }

        Player player;
        Vector3 velocity;
        float damage = 25f;
        float life = 0.75f;
        Kind kind;
        bool split;
        readonly HashSet<Enemy> hit = new HashSet<Enemy>();

        public static void Spawn(Player p, Vector3 from, Vector3 velocity, float damage, Kind kind = Kind.Normal)
        {
            var j = new GameObject(kind == Kind.Lance ? "BloodLance" : "BloodJavelin").AddComponent<BloodJavelin>();
            j.player = p;
            j.velocity = velocity;
            j.damage = damage;
            j.kind = kind;
            j.split = kind != Kind.Normal;
            if (kind == Kind.Shard) j.life = 0.35f;
            j.transform.SetParent(FX.root, false);
            j.transform.SetPositionAndRotation(from, M.FaceNegZ(velocity));
            float s = kind == Kind.Lance ? 2.6f : (kind == Kind.Shard ? 0.6f : 1f);
            var shaft = FX.Part(j.transform, MeshGen.Box(), FX.Glow(new Color(0.85f, 0.05f, 0.1f), 1.8f), Vector3.zero, new Vector3(0.08f, 0.08f, 1.6f) * s);
            FX.NoShadow(shaft);
            var tip = FX.Part(j.transform, MeshGen.Cylinder(0f, 0.5f, 4), FX.Glow(new Color(1f, 0.35f, 0.35f), 2.5f), new Vector3(0, 0, -0.95f * s),
                new Vector3(0.18f, 0.4f, 0.18f) * s, new Vector3(-M.PI * 0.5f, 0, 0));
            FX.NoShadow(tip);
            FX.Particles(j.transform, new FX.ParticleSpec
            {
                amount = kind == Kind.Lance ? 80 : 24, lifetime = 0.3f, size = 0.3f * s, speedMin = 0.5f, speedMax = 1.5f, spread = 20f, gravity = new Vector3(0, -6, 0),
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
            float reachK = kind == Kind.Lance ? 1.8f : 1f;
            // Pierces through every ogre in its path.
            var list = g.enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.dead || hit.Contains(e) || !g.CanTarget(e)) continue;   // WYVERN
                var c = e.Pos + new Vector3(0, e.size, 0);
                if (Vector3.Distance(c, pos) < (e.radius + 0.6f + 0.5f * e.size) * reachK)
                {
                    hit.Add(e);
                    e.lanceMarked = true;
                    // Exsanguinate: a bleeding ogre below 25% health dies outright.
                    if (player.Has("h_execute") && !e.isBoss && e.Bleeding && e.hp < e.maxHp * 0.25f)
                    {
                        FX.Text(c + Vector3.up, "Executed", new Color(1f, 0.25f, 0.3f));
                        g.DamageEnemy(e, e.hp + 1f, velocity.normalized, 4f, true, new Color(1f, 0.2f, 0.25f));
                    }
                    else
                    {
                        float bleedT = player.Has("h_hemorrhage") ? 4.5f : 3f;
                        e.Bleed(bleedT, damage * Data.WEAPONS["blood_lance"].bleed / 3f);
                        g.DamageEnemy(e, damage, velocity.normalized, 2.5f, true, new Color(1f, 0.35f, 0.4f));
                    }
                    player.WeaponHits(1, damage);
                    FX.Burst(pos, new Color(0.85f, 0.05f, 0.1f), 6, 4f, 0.18f);
                    Sfx.Play("hit", pos, -6f, 0.2f);
                    if (kind == Kind.Lance)
                    {
                        // The Bloodstorm Lance bursts at every ogre it passes through.
                        FX.Flash(c, 3f, new Color(0.9f, 0.05f, 0.1f, 0.7f), 0.25f);
                        FX.Shockwave(e.Pos + new Vector3(0, 0.3f, 0), 3.5f, new Color(0.9f, 0.05f, 0.1f, 0.8f), 0.3f, false);
                        foreach (var o in g.EnemiesInRadius(e.Pos, 3.5f, 4f))
                            if (o != e) g.DamageEnemy(o, damage * 0.5f, (o.Pos - e.Pos).normalized, 6f, true, new Color(1f, 0.3f, 0.35f));
                    }
                    if (!split && player.Has("h_tempest"))
                    {
                        // Crimson Tempest: three smaller javelins fan out from the first ogre hit.
                        split = true;
                        var fwd = velocity.normalized;
                        for (int k = -1; k <= 1; k++)
                        {
                            var d = Quaternion.Euler(0f, k * 28f, 0f) * fwd;
                            BloodJavelin.Spawn(player, c + fwd * (e.radius + 0.6f), d * 48f, damage * 0.45f, Kind.Shard);
                        }
                    }
                }
            }
            if (life <= 0f || pos.y < g.terrain.Height(pos.x, pos.z)) Destroy(gameObject);
        }
    }
}
