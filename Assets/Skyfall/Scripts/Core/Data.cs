using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Static game data: heroes, weapons, spells, passives, dragons, stat buckets, armor, talents, ogres, bosses, stages.
    /// Balance lives almost entirely here.
    public static class Data
    {
        /// Length of one stage; the mini boss arrives at 4:00 and the stage's final boss at 0:00 (on the floating island in
        /// worlds 2 and 3; world 1 has no islands and fights on the ground).
        public const float RUN_LENGTH = 480f;
        public const float WAVE_LENGTH = 120f;
        public const float MINI_BOSS_TIME = 240f;
        /// Weapons and passives top out at level 5; spells at level 10.
        public const int MAX_LEVEL = 5;
        public const int SPELL_MAX_LEVEL = 10;
        /// Spells reach their evolved form (a unique, visually distinct upgrade) at level 5, and gain a second unique
        /// upgrade at level 10.
        public const int EVOLVE_LEVEL = 5;
        public const int SPELL_FINAL_LEVEL = 10;

        static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        // ------------------------------------------------------------ heroes

        public class HeroDef
        {
            public string name, weapon, defense, desc;
            public Color color;
            public float hp;
        }

        public static readonly string[] HERO_ORDER = { "knight", "huntress", "pyromancer" };
        public static readonly Dictionary<string, HeroDef> HEROES = new Dictionary<string, HeroDef>
        {
            ["knight"] = new HeroDef { name = "Dragonguard Knight", color = C(0.55f, 0.7f, 1.0f), weapon = "sword", defense = "shield",
                desc = "Armored melee battlemage. His longsword cuts wide arcs and his shield blocks blows. While riding his dragon he hurls piercing Skyblades instead.", hp = 120f },
            ["huntress"] = new HeroDef { name = "Blood Huntress", color = C(0.9f, 0.2f, 0.25f), weapon = "blood_lance", defense = "evasion",
                desc = "Ranged blood mage. She hurls piercing Blood Javelins that make foes bleed, paying for each throw with her own blood. She dodges blows instead of blocking them.", hp = 90f },
            ["pyromancer"] = new HeroDef { name = "Ember Mage", color = C(1.0f, 0.55f, 0.15f), weapon = "staff", defense = "ward",
                desc = "Fire caster. His Ember Staff hurls bursting fire bolts that set foes alight, and a Flame Ward turns blows aside and burns the attacker. He gains 5% damage per level instead of 2%.", hp = 85f },
        };

        // ------------------------------------------------------------ weapons (auto attacks, upgraded on level-up)

        public class WeaponDef
        {
            public string name, desc, up;
            public Color color;
            public float damage, cooldown, range, arc, knock, hp_cost, orb_bonus, bleed, radius;
        }

        public static readonly Dictionary<string, WeaponDef> WEAPONS = new Dictionary<string, WeaponDef>
        {
            ["sword"] = new WeaponDef { name = "Dragonguard Longsword", color = C(0.75f, 0.88f, 1.0f),
                damage = 27.5f, cooldown = 0.6f, range = 5.0f, arc = 170f, knock = 5f,
                desc = "Wide sweeping cuts that knock foes back. While riding your dragon you hurl piercing Skyblades instead (70% damage each, 32 m range).",
                up = "+15% damage, faster swings, +6% reach, +5% block; +1 Skyblade at Lv 3 and 5 (while mounted). Lv 3: blocks stun the attacker. Lv 5: blocks reflect double damage" },
            ["blood_lance"] = new WeaponDef { name = "Blood Javelin", color = C(0.95f, 0.15f, 0.2f),
                damage = 27.5f, cooldown = 0.75f, range = 28f, hp_cost = 0.02f, orb_bonus = 0.05f, bleed = 0.35f,
                desc = "Blood Javelins that pierce every foe in their path and make them bleed. Each throw costs 2% of your current health (never lethal) and no mana. Foes a javelin hit have +5% extra chance to drop a health orb.",
                up = "+15% damage, faster throws, +5% dodge; +1 javelin at Lv 3 and 5. Lv 3: dodging heals 1.8% health. Lv 5: dodging hurls a counter-javelin" },
            ["staff"] = new WeaponDef { name = "Ember Staff", color = C(1.0f, 0.55f, 0.15f),
                damage = 24f, cooldown = 0.5f, range = 30f, radius = 1.6f,
                desc = "Fire bolts from the staff's crystal that burst on impact and set foes alight. They cost no mana.",
                up = "+15% damage, faster casts, +5% ward; +1 bolt at Lv 3 and 5, bigger bursts at Lv 5. Lv 3: wards explode in fire. Lv 5: wards restore 5 mana" },
        };

        /// Dragonguard Knight, mounted only: Skyblades (thrown spectral swords) replace the sword swings. Each deals this
        /// fraction of a sword hit to every ogre it pierces, flies at SKYBLADE_SPEED and reaches SKYBLADE_RANGE metres.
        public const float SKYBLADE_DAMAGE = 0.7f;
        public const float SKYBLADE_RANGE = 32f;
        public const float SKYBLADE_SPEED = 50f;

        /// Weapon stats at a level (1..5), one line each; used by the level-up card and the stats screen.
        public static string[] WeaponStats(string id, int lvl)
        {
            var w = WEAPONS[id];
            float dmg = w.damage * (1f + 0.15f * (lvl - 1));
            float perSec = 1f / (w.cooldown * Mathf.Pow(0.93f, lvl - 1));
            int shots = 1 + (lvl >= 3 ? 1 : 0) + (lvl >= 5 ? 1 : 0);
            switch (id)
            {
                case "sword":
                    return new[] { $"Damage {dmg:0}   ·   {perSec:0.0} swings/s", $"Reach {w.range * (1f + 0.06f * (lvl - 1)):0.0} m  ·  mounted: {shots} Skyblade{(shots > 1 ? "s" : "")}" };
                case "blood_lance":
                    return new[] { $"Damage {dmg:0}   ·   {perSec:0.0} throws/s", $"{shots} javelin{(shots > 1 ? "s" : "")} per throw  ·  bleeds, costs 2% health" };
                default:
                    return new[] { $"Damage {dmg:0}   ·   {perSec:0.0} casts/s", $"{shots} bolt{(shots > 1 ? "s" : "")} per cast  ·  bursts, ignites" };
            }
        }

        // ------------------------------------------------------------ defenses

        public class DefenseDef
        {
            public string name, desc;
            public Color color;
            public float baseChance;
            public string[] ups;
        }

        public static readonly DefenseDef WARD = new DefenseDef { name = "Flame Ward", color = C(1.0f, 0.5f, 0.15f), baseChance = 0.10f,
            desc = "A chance to turn any blow aside; the attacker catches fire. It levels up with your weapon.",
            ups = new[] { "", "+5% ward chance", "+5% ward chance; warded blows also explode in fire around you", "+5% ward chance", "+5% ward chance; each ward restores 5 mana" } };

        public static readonly DefenseDef EVASION = new DefenseDef { name = "Evasion", color = C(0.95f, 0.4f, 0.45f), baseChance = 0.15f,
            desc = "A chance to dodge any blow entirely. It levels up with your weapon.",
            ups = new[] { "", "+5% dodge chance", "+5% dodge chance; each dodge heals 1.8% of max health", "+5% dodge chance", "+5% dodge chance; each dodge hurls a counter-javelin" } };

        public static readonly DefenseDef SHIELD = new DefenseDef { name = "Dragonguard Shield", color = C(0.45f, 0.65f, 1.0f), baseChance = 0.25f,
            desc = "A chance to block any blow entirely. It levels up with your weapon.",
            ups = new[] { "", "+5% block chance", "+5% block chance; blocks stun the attacker (not bosses)", "+5% block chance", "+5% block chance; blocks reflect double the blow's damage" } };

        /// Health orb drop chance (bigger foes drop them more often) and how much they heal.
        public const float ORB_CHANCE = 0.03f;
        public const float ORB_HEAL = 0.10f;

        // ------------------------------------------------------------ spells (auto-cast)

        public class SpellDef
        {
            public string name, desc, evolved;
            public Color color, evolvedColor;
            public float cost, cd;
            public string[] ups;
        }

        public static readonly string[] SPELL_ORDER = { "gravity_well", "frost_bolt", "chain_lightning", "meteor" };
        /// Level-up texts for a spell, indexed by the current level (ups[1] is the upgrade to level 2): three early
        /// upgrades, the evolution at 5, four steady upgrades, then the level-10 unique upgrade.
        static string[] SpellUps(string l2, string l3, string l4, string evolve, string steady, string l10) =>
            new[] { "", l2, l3, l4, evolve, steady, steady, steady, steady, l10 };

        public static readonly Dictionary<string, SpellDef> SPELLS = new Dictionary<string, SpellDef>
        {
            ["gravity_well"] = new SpellDef { name = "Gravity Well", color = C(0.6f, 0.3f, 1.0f), cost = 40f, cd = 4f,
                evolved = "Event Horizon", evolvedColor = C(1f, 0.78f, 0.3f),
                desc = "Opens a crushing vortex in the thickest crowd, dragging foes in before it implodes.",
                ups = SpellUps("+pull, +damage", "+radius, longer vortex", "+pull, +damage",
                    "EVOLVES into Event Horizon: a golden-ringed black hole with 1.5x the radius and double the pull. It implodes twice and stuns everything it crushes (not bosses)",
                    "+pull, +damage, cheaper to cast", "UNIQUE: opens a second well") },
            ["frost_bolt"] = new SpellDef { name = "Frost Bolt", color = C(0.6f, 0.9f, 1.0f), cost = 16f, cd = 0.8f,
                evolved = "Glacial Comet", evolvedColor = C(0.85f, 1f, 1f),
                desc = "A bolt of ice that bursts on contact, sending shards out from the center. The bolt and its shards can freeze foes (bosses are only slowed).",
                ups = SpellUps("+2 shards, +damage", "+freeze chance", "+2 shards, +damage",
                    "EVOLVES into Glacial Comet: a huge comet that always freezes what it hits (bosses are slowed) and leaves a field of ice that freezes foes walking in",
                    "+damage, +freeze chance, cheaper to cast", "UNIQUE: shards pierce through foes") },
            ["chain_lightning"] = new SpellDef { name = "Chain Lightning", color = C(0.75f, 0.75f, 1.0f), cost = 24f, cd = 1.4f,
                evolved = "Storm Lord", evolvedColor = C(0.85f, 0.55f, 1f),
                desc = "Lightning leaps from foe to foe, with a chance to shock each one it hits (1 s stun, not on bosses).",
                ups = SpellUps("+2 jumps, +damage", "+shock chance", "+2 jumps, +damage",
                    "EVOLVES into Storm Lord: violet lightning with +40% damage that jumps farther, and leaves a crackling storm orb where the chain ends that keeps zapping",
                    "+damage, +shock chance, cheaper to cast", "UNIQUE: forks into two chains") },
            ["meteor"] = new SpellDef { name = "Meteor Storm", color = C(1.0f, 0.65f, 0.25f), cost = 48f, cd = 5f,
                evolved = "Starfall", evolvedColor = C(0.6f, 0.8f, 1f),
                desc = "Burning stars rain on the thickest crowd, each with a chance to ignite what it hits.",
                ups = SpellUps("+2 meteors, +damage", "+ignite chance, bigger impacts", "+2 meteors, +damage",
                    "EVOLVES into Starfall: blue-white stars that hit twice as hard, always ignite and leave pools of starfire burning on the ground",
                    "+damage, +ignite chance, cheaper to cast", "UNIQUE: every barrage ends with a giant meteor (3x damage)") },
        };

        // ------------------------------------------------------------ passives

        public class PassiveDef
        {
            public string name, desc, up;
            public Color color;
        }

        public static readonly string[] PASSIVE_ORDER = { "blood_aura", "lightning_storm", "bloodthirst", "spellblade" };
        public static readonly Dictionary<string, PassiveDef> PASSIVES = new Dictionary<string, PassiveDef>
        {
            ["blood_aura"] = new PassiveDef { name = "Blood Aura", color = C(0.85f, 0.1f, 0.15f),
                desc = "A crimson ring around you drains every foe inside it twice a second, healing you for part of the damage and restoring a little mana.",
                up = "+damage, +radius, +lifesteal" },
            ["lightning_storm"] = new PassiveDef { name = "Lightning Storm", color = C(0.65f, 0.75f, 1.0f),
                desc = "Lightning strikes random foes within 16 m every couple of seconds, with a high chance to shock them (1 s stun, not on bosses). Each strike feeds your mana.",
                up = "+1 bolt, +damage, +shock chance, strikes more often" },
            ["bloodthirst"] = new PassiveDef { name = "Bloodthirst", color = C(0.9f, 0.2f, 0.2f),
                desc = "Your weapons drink life: you heal for a share of the damage each weapon hit deals.",
                up = "+lifesteal, +5% weapon damage" },
            ["spellblade"] = new PassiveDef { name = "Spellblade", color = C(0.4f, 0.8f, 1.0f),
                desc = "Weapon hits charge far more mana, and your spells hit harder.",
                up = "+mana per hit, +8% spell damage" },
        };

        // ------------------------------------------------------------ dragons

        public class DragonDef
        {
            public string name, focus, desc, sig, sig_desc;
            public float damage;
            public Color body, edge, belly, membrane, horn, eye, breath;
            /// Every dragon uses the same Asset Store model (the Usurper); skin picks its color variant and skinTint
            /// multiplies its texture so each dragon's hide matches its ability color.
            public string skin;
            public Color skinTint = Color.white;
            /// Breath particles are additive (glowing) unless false.
            public bool glowBreath = true;
        }

        public static readonly string[] DRAGON_ORDER = { "blue", "red", "black" };
        public static readonly Dictionary<string, DragonDef> DRAGONS = new Dictionary<string, DragonDef>
        {
            ["blue"] = new DragonDef { name = "Azure Wyrm", focus = "Fire", damage = 1.0f, skin = "blue",
                body = C(0.1f, 0.42f, 1.0f), edge = C(0.02f, 0.07f, 0.3f), belly = C(0.62f, 0.82f, 1.0f),
                membrane = C(0.1f, 0.3f, 0.75f), horn = C(0.92f, 0.88f, 0.78f), eye = C(1.0f, 0.85f, 0.25f),
                breath = C(0.25f, 0.6f, 1.0f),
                desc = "Fire focus. Its blue flames leave a heavy burn that lasts 4 s.",
                sig = "Inferno Wave", sig_desc = "A torrent of fireballs sets the crowd ablaze." },
            ["red"] = new DragonDef { name = "Crimson Drake", focus = "Explosive", damage = 1.25f, skin = "red", skinTint = C(0.82f, 0.3f, 0.3f),
                body = C(0.5f, 0.03f, 0.05f), edge = C(0.12f, 0.01f, 0.02f), belly = C(0.72f, 0.38f, 0.22f),
                membrane = C(0.32f, 0.02f, 0.04f), horn = C(0.25f, 0.2f, 0.18f), eye = C(1.0f, 0.9f, 0.3f),
                breath = C(0.85f, 0.08f, 0.06f),
                desc = "Explosive and physical. Deals 1.25x the damage of other dragons; its breath knocks foes back harder and its dives hit 20% wider.",
                sig = "Cataclysm", sig_desc = "A massive blast at the heart of the crowd that stuns, then a ring of aftershocks." },
            ["black"] = new DragonDef { name = "Night Wyrm", focus = "Blood", damage = 1.0f, skin = "black", glowBreath = false,
                body = C(0.08f, 0.07f, 0.08f), edge = C(0.45f, 0.02f, 0.04f), belly = C(0.3f, 0.05f, 0.07f),
                membrane = C(0.18f, 0.02f, 0.03f), horn = C(0.6f, 0.55f, 0.5f), eye = C(1.0f, 0.15f, 0.1f),
                breath = C(0.75f, 0.05f, 0.1f),
                desc = "Blood focus. Its breath, dives and blood rain weaken foes so they hit for 40% less.",
                sig = "Blood Rain", sig_desc = "A storm of blood over the crowd: steady damage, and foes inside hit for 40% less." },
        };

        /// The dragon's signature (E) cooldown before upgrades.
        public const float SIGNATURE_CD = 10f;

        /// The dragon grows with each world: a hatchling in world 1 (too small to carry you), a young dragon in world 2
        /// (unlimited flight), an adult in world 3 (flight and Guardian Wings). Index by stage - 1.
        public static readonly string[] DRAGON_AGE = { "Hatchling", "Young Dragon", "Adult Dragon" };
        public static readonly float[] DRAGON_AGE_SCALE = { 0.6f, 0.85f, 1f };
        public static readonly float[] DRAGON_AGE_DAMAGE = { 0.7f, 1f, 1.3f };
        /// First world in which the dragon can carry you.
        public const int FLIGHT_STAGE = 2;
        /// Extra size for the rigged Dragonguard Knight model (it is fitted to the same head height as the others).
        public const float ART_KNIGHT_SCALE = 1.35f;
        /// Adult dragon (world 3) passive: below this health it shields you with its wings and heals you.
        public const int GUARDIAN_WINGS_STAGE = 3;
        public const float GUARDIAN_WINGS_HP = 0.3f;
        public const float GUARDIAN_WINGS_HEAL = 0.5f;
        public const float GUARDIAN_WINGS_SHIELD = 3f;
        public const float GUARDIAN_WINGS_CD = 60f;

        /// Nominal flight bar length in seconds. Flight itself is unlimited wherever the dragon can carry you (worlds 2-3);
        /// this only scales the stamina bar and its refill (the hatchling of world 1 cannot fly at all).
        public const float FLIGHT_TIME = 10f;
        /// First world in which the dragon is an adult (longer flight, Guardian Wings).
        public const int ADULT_STAGE = 3;

        /// True when the dragon of this world can carry the hero: the young dragon (world 2) and the adult (world 3) both can.
        public static bool CanFlyAtStage(int stage) => stage >= FLIGHT_STAGE;

        /// Seconds Q (mount / dismount / summon) is locked after the E signature is used.
        public const float Q_LOCKOUT_AFTER_E = 5f;

        /// Flight never runs out in worlds 2 and 3 (no drain, no forced landing).
        public static bool UnlimitedFlightAtStage(int stage) => CanFlyAtStage(stage);

        /// Nominal flight bar length in this world (see FLIGHT_TIME).
        public static float FlightTimeFor(int stage) => FLIGHT_TIME;

        /// Rescue: the stage's final boss held one of the dragons you don't own captive. The first rescue ever is guaranteed;
        /// after that it is a coin flip (while locked dragons remain).
        public const float RESCUE_CHANCE = 0.5f;

        /// The still shown after a world's final boss dies: the hero's dragon devours the warlord and grows into its next age.
        /// Index by the world just finished (0 = world 1). Format args: {0} = the warlord, {1} = the new age.
        public static readonly string[] FEAST_CAPTION =
        {
            "Your dragon devours {0} and grows into a {1}",
            "Your dragon devours {0} and grows into an {1}",
        };

        // ------------------------------------------------------------ stat buckets

        /// One stat inside a bucket: per-rank amount and a format string for it ({0} is replaced by the amount).
        public class StatDef
        {
            public float amount;
            public string fmt;
            public string Text(float ranks = 1f)
            {
                float v = amount * ranks;
                string n = Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? Mathf.Round(v).ToString("0") : v.ToString("0.#");
                return string.Format(fmt, n);
            }
        }

        public class BucketDef
        {
            public string name;
            public Color color;
            public StatDef[] stats;
        }

        static StatDef St(float amount, string fmt) => new StatDef { amount = amount, fmt = fmt };

        public static readonly string[] BUCKET_ORDER = { "health", "mana", "offense", "defense", "dragon", "utility" };
        public static readonly Dictionary<string, BucketDef> BUCKETS = new Dictionary<string, BucketDef>
        {
            ["health"] = new BucketDef { name = "Health", color = C(0.9f, 0.25f, 0.25f),
                stats = new[] { St(12f, "+{0}% max health"), St(0.5f, "+{0} health regen / s"), St(10f, "+{0}% healing received") } },
            ["mana"] = new BucketDef { name = "Mana", color = C(0.5f, 0.45f, 1.0f),
                stats = new[] { St(15f, "+{0} max mana"), St(0.8f, "+{0} mana regen / s"), St(15f, "+{0}% mana per hit") } },
            ["offense"] = new BucketDef { name = "Offense", color = C(1.0f, 0.6f, 0.25f),
                stats = new[] { St(8f, "+{0}% damage"), St(3f, "+{0}% critical chance"), St(6f, "+{0}% attack speed") } },
            ["defense"] = new BucketDef { name = "Defense", color = C(0.5f, 0.75f, 1.0f),
                stats = new[] { St(2f, "+{0}% block / dodge / ward"), St(1.5f, "+{0} armor"), St(2f, "+{0}% parry") } },
            ["dragon"] = new BucketDef { name = "Dragon", color = C(0.35f, 0.65f, 1.0f),
                stats = new[] { St(12f, "+{0}% dragon damage"), St(8f, "+{0}% breath reach"), St(6f, "-{0}% dragon cooldowns") } },
            ["utility"] = new BucketDef { name = "Utility", color = C(0.6f, 1.0f, 0.6f),
                stats = new[] { St(10f, "+{0}% jump height"), St(6f, "+{0}% movement speed"), St(25f, "+{0}% orb attract range"), St(0.1f, "+{0}% power-up drop chance"),
                    St(8f, "+{0}% experience gained") } },
        };

        /// Stat key used by armor and the player's stat table: "bucket:index".
        public static string StatKey(string bucket, int idx) => bucket + ":" + idx;

        // ------------------------------------------------------------ armor (chosen every 5 levels)

        /// One armor piece is offered every ARMOR_EVERY levels, for the slots in ARMOR_SLOTS order (helm first, then around
        /// again to upgrade). The tier is rolled once per offer, so all three choices share it: same slot, same rarity,
        /// different stats. Tiers: Common (3 stats), Superior (4), Mythic (6, rare); each 15% stronger per stat.
        public const int ARMOR_EVERY = 5;
        public static readonly string[] ARMOR_SLOTS = { "helm", "chest", "gloves", "legs", "boots" };
        public static readonly Dictionary<string, string> ARMOR_SLOT_NAMES = new Dictionary<string, string>
        {
            ["helm"] = "Helm", ["chest"] = "Breastplate", ["gloves"] = "Gauntlets", ["legs"] = "Greaves", ["boots"] = "Boots",
        };
        public static readonly string[] ARMOR_TIER_NAMES = { "Common", "Superior", "Mythic" };
        public static readonly int[] ARMOR_TIER_STATS = { 3, 4, 6 };
        public static readonly float[] ARMOR_TIER_MULT = { 1f, 1.15f, 1.3225f };
        public static readonly float[] ARMOR_TIER_ODDS = { 0.62f, 0.30f, 0.08f };
        public static readonly Color[] ARMOR_TIER_COLORS = { C(0.75f, 0.78f, 0.82f), C(0.35f, 0.65f, 1f), C(1f, 0.6f, 0.15f) };
        /// Fancy names per bucket for the piece's main stat.
        public static readonly Dictionary<string, string> ARMOR_SUFFIX = new Dictionary<string, string>
        {
            ["health"] = "of Vigor", ["mana"] = "of the Arcane", ["offense"] = "of Fury", ["defense"] = "of the Bulwark",
            ["dragon"] = "of the Wyrm", ["utility"] = "of the Wind",
        };

        // ------------------------------------------------------------ talents

        /// A talent. Simple stat talents carry their effect as mods ("key:amount" pairs, summed by Player.Mod);
        /// the rest are looked up by id where they act (PlayerTalents.cs, DragonPet.cs).
        public class TalentDef
        {
            public string id, name, desc;
            public Dictionary<string, float> mods = new Dictionary<string, float>();
        }

        static TalentDef T(string id, string name, string desc, string mods = "")
        {
            var t = new TalentDef { id = id, name = name, desc = desc };
            foreach (var part in mods.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length == 2) t.mods[kv[0].Trim()] = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            return t;
        }

        // ---- in-run talents: two rows per hero, one pick per row as the hero levels up during a run

        /// Hero levels at which an in-run talent row opens.
        public static readonly int[] TALENT_LEVELS = { 3, 7 };

        public static readonly Dictionary<string, TalentDef[][]> RUN_TALENTS = new Dictionary<string, TalentDef[][]>
        {
            ["knight"] = new[]
            {
                new[] { T("k_wyrmsteel", "Wyrmsteel Edge", "Weapon damage +30% (sword and Skyblades); every hit sets the foe ablaze."),
                        T("k_bulwark", "Bulwark", "+12% block chance; each block heals you for 1.2% of your max health."),
                        T("k_rally", "Rallying Cry", "+20% max health. Every 10 s your barrier refills to 15% of your max health (it does not stack).") },
                new[] { T("k_whirlwind", "Whirlwind", "On foot, every 5th swing becomes a full 360-degree spin that deals double damage."),
                        T("k_bash", "Shield Bash", "Each block sends out a 5 m shockwave that damages nearby foes, hurls them back and stuns them for 1 s (not bosses)."),
                        T("k_reach", "Dragon's Reach", "+35% sword and Skyblade reach, and +50% sword knockback.") },
            },
            ["huntress"] = new[]
            {
                new[] { T("h_hemorrhage", "Hemorrhage", "All bleeds deal 60% more damage, and javelin bleeds last 4.5 s instead of 3 s."),
                        T("h_pact", "Blood Pact", "Javelins no longer cost health and deal 10% more damage."),
                        T("h_fleet", "Fleetfoot", "+15% dodge chance and +15% movement speed.") },
                new[] { T("h_volley", "Crimson Volley", "+2 javelins per throw."),
                        T("h_sanguine", "Sanguine Shield", "Dodging grants a blood barrier that absorbs 10% of your max health (6 s cooldown)."),
                        T("h_orbs", "Blood Harvest", "Foes drop health orbs twice as often, and each orb also restores 20 mana.") },
            },
            ["pyromancer"] = new[]
            {
                new[] { T("p_kindling", "Kindling", "All burns deal 60% more damage."),
                        T("p_emberward", "Ember Ward", "+12% ward chance; each ward heals you for 1.8% of your max health."),
                        T("p_font", "Mana Font", "+40 max mana and +2 mana regen per second.") },
                new[] { T("p_twin", "Twin Embers", "+1 fire bolt per cast."),
                        T("p_firewalk", "Fire Walk", "While you run on the ground you leave a trail of flames that burns foes who follow you."),
                        T("p_combustion", "Combustion", "Burning foes explode when they die, hurting and igniting foes within 4 m.") },
            },
        };

        // ---- permanent trees (main menu): six rows of three, one pick per row; rows are bought with hero or bond XP

        /// XP needed to open each row of a permanent tree (hero XP for hero trees, bond XP for dragon trees; both share this curve).
        /// "Faster early, steeper late": the first rows open after a single short run, the last rows take many wins.
        /// Old curve: 100, 200, 350, 500, 700, 900 (total 2750). New curve: 40, 90, 200, 450, 900, 1700 (total 3380);
        /// each of the last three rows costs about double the one before it. XP gain is unchanged
        /// (hero: 0.25/kill + 0.1/s + 75 per world cleared + 150 for a win; a typical full win is roughly 800-900).
        public static readonly int[] TREE_ROW_COST = { 40, 90, 200, 450, 900, 1700 };
        public const int TREE_ROWS = 6;

        /// Mod keys (all summed over the talents you have):
        ///   hero:   hp% armor regen manaregen dmg% wdmg% atkspd% move% block% heal% mountdmg% spelldmg% spellcd% dmgtaken%
        ///   dragon: dragondmg% dragoncd% sigcd% sigdmg% sigarea% size% reach% cone breathtime% divedmg% diverad% divestun
        ///           mountdr% burn% weaken leech% killheal% raintime rainarea% fireballs aftershocks wing rescue wrath
        public static readonly Dictionary<string, TalentDef[][]> HERO_TREES = new Dictionary<string, TalentDef[][]>
        {
            ["knight"] = new[]
            {
                new[] { T("k_iron", "Iron Hide", "+4 armor and +10% max health.", "armor:4,hp%:10"),
                        T("k_temper", "Tempered Blade", "+12% damage and +8% attack speed.", "dmg%:12,atkspd%:8"),
                        T("k_squire", "Dragonsworn", "Your dragon deals +20% damage and its cooldowns are 10% shorter.", "dragondmg%:20,dragoncd%:10") },
                new[] { T("k_aegis", "Aegis of Scales", "When a hit drops you below 30% health you become invulnerable for 3 s and heal 20% of your max health (60 s cooldown)."),
                        T("k_charge", "Ember Charge", "On foot, every 8 s you charge up to 18 m into the thickest crowd, hitting for 130% weapon damage, burning and scattering it."),
                        T("k_oath", "Oathbound", "Your dragon deals +40% damage and all its cooldowns (breath, dives, signature) are 25% shorter.") },
                new[] { T("k_guard", "Shield Wall", "+6% block chance and +1 health regen per second.", "block%:6,regen:1"),
                        T("k_cleave", "Cleave", "+20% weapon damage.", "wdmg%:20"),
                        T("k_rider", "Lancer", "+40% damage while riding your dragon.", "mountdmg%:40") },
                new[] { T("k_thunderclap", "Thunderclap", "Landing from a double jump slams the ground: 250% weapon damage in 6 m, stunning foes for 0.8 s (not bosses)."),
                        T("k_retribution", "Retribution", "Every hit that lands on you (not a block or dodge) lashes out at all foes within 6 m for 90% of its damage."),
                        T("k_sky", "Sky Sovereign", "While riding your dragon you take 30% less damage, and its breath deals 25% more.") },
                new[] { T("k_bastion", "Bastion", "You take 12% less damage.", "dmgtaken%:12"),
                        T("k_fervor", "Battle Fervor", "+25% healing received and +2 health regen per second.", "heal%:25,regen:2"),
                        T("k_wyrmcall", "Wyrmcaller", "Your dragon's signature deals 35% more damage over a 20% larger area.", "sigdmg%:35,sigarea%:20") },
                new[] { T("k_dragonform", "Dragon Form", "Press F (gamepad LB) to become a dragon for 12 s: fly freely, breathe fire at the nearest foes (your weapon rests) and take 50% less damage. 45 s cooldown."),
                        T("k_firewave", "Dragonfire Blade", "Every sword swing also launches a wave of dragonfire (80% weapon damage) that rolls forward through the crowd. Not while riding."),
                        T("k_vow", "Undying Vow", "Once per world, a killing blow instead leaves you at 50% health and invulnerable for 2 s, in a nova of holy fire.") },
            },
            ["huntress"] = new[]
            {
                new[] { T("h_keen", "Keen Eye", "+10% damage and +6% attack speed.", "dmg%:10,atkspd%:6"),
                        T("h_lithe", "Lithe", "+8% dodge chance and +8% movement speed.", "block%:8,move%:8"),
                        T("h_bondblood", "Blood Bond", "Your dragon deals +20% damage and its cooldowns are 10% shorter.", "dragondmg%:20,dragoncd%:10") },
                new[] { T("h_execute", "Exsanguinate", "Javelins instantly kill bleeding foes below 25% health (not bosses)."),
                        T("h_shadowstep", "Shadowstep", "On foot, when a foe gets within 3 m you blink 8 m away and leave a blood decoy that explodes 0.7 s later for heavy damage and bleeding (7 s cooldown)."),
                        T("h_wyrmbond", "Wyrm Bond", "Your dragon's breath and dives make foes bleed; dragon damage +30%.") },
                new[] { T("h_wings", "Wings of Blood", "Crimson wings: +2 air jumps, and holding jump while falling lets you glide."),
                        T("h_quiver", "Deep Quiver", "+15% attack speed.", "atkspd%:15"),
                        T("h_hunter", "Wyrm Hunter", "+40% damage while riding your dragon.", "mountdmg%:40") },
                new[] { T("h_lance", "Bloodstorm Lance", "Every 8th throw also hurls a giant lance (3x damage) that bursts at every foe it pierces."),
                        T("h_thirst", "Thirst", "Every kill heals 0.9% of your max health."),
                        T("h_phantom", "Phantom Hunt", "While riding your dragon you throw twice as fast and take 25% less damage.") },
                new[] { T("h_vital", "Vital Essence", "+20% max health and +1.5 health regen per second.", "hp%:20,regen:1.5"),
                        T("h_predator", "Apex Predator", "+18% damage.", "dmg%:18"),
                        T("h_skyblood", "Skyblood", "Your dragon's signature deals 35% more damage over a 20% larger area.", "sigdmg%:35,sigarea%:20") },
                new[] { T("h_bloodmoon", "Blood Moon", "Every 30 s, once foes are near, a blood moon rises for 8 s: triple javelins, +6% lifesteal, and every foe within 12 m bleeds."),
                        T("h_tempest", "Crimson Tempest", "When a javelin first hits a foe it splits into three smaller javelins (45% damage each)."),
                        T("h_specter", "Crimson Specter", "Press F (gamepad LB) to become a blood wraith for 10 s: pass through foes, move 40% faster, throw triple javelins, gain +5% lifesteal and take 60% less damage. 45 s cooldown.") },
            },
            ["pyromancer"] = new[]
            {
                new[] { T("p_focus", "Arcane Focus", "+12% spell damage and +1 mana regen per second.", "spelldmg%:12,manaregen:1"),
                        T("p_ember", "Emberskin", "+3 armor and +10% max health.", "armor:3,hp%:10"),
                        T("p_kinship", "Ember Kinship", "Your dragon deals +20% damage and its cooldowns are 10% shorter.", "dragondmg%:20,dragoncd%:10") },
                new[] { T("p_phoenix", "Ember Rebirth", "Once per world, a killing blow instead revives you at full health, invulnerable for 2 s, in a burst of fire that burns nearby foes."),
                        T("p_blink", "Blink", "On foot, when 5 or more foes crowd within 4 m you teleport 10 m away to the emptiest spot, leaving an explosion behind (6 s cooldown)."),
                        T("p_dragonkin", "Dragon Kin", "Dragon damage +35% and its breath reaches 30% farther.") },
                new[] { T("p_pillars", "Pillars of Flame", "Every 12 s, when foes are near, a ring of eight fire pillars erupts 7 m around you."),
                        T("p_haste", "Quickcast", "+12% attack speed and spells cool down 10% faster.", "atkspd%:12,spellcd%:10"),
                        T("p_skyfire", "Skyfire", "+40% damage while riding your dragon.", "mountdmg%:40") },
                new[] { T("p_inferno", "Inferno Bolts", "Fire bolts burst 80% wider and pass through the first foe they hit."),
                        T("p_molten", "Molten Armor", "+6 armor; foes that hit you in melee catch fire."),
                        T("p_weaver", "Spell Weaver", "Spell cooldowns are 25% shorter and spells cost 20% less mana.") },
                new[] { T("p_ascend", "Ascendance", "+15% damage.", "dmg%:15"),
                        T("p_warding", "Warding Flames", "You take 12% less damage and gain +5% ward chance.", "dmgtaken%:12,block%:5"),
                        T("p_flamecaller", "Flamecaller", "Your dragon's signature deals 35% more damage over a 20% larger area.", "sigdmg%:35,sigarea%:20") },
                new[] { T("p_supernova", "Supernova", "Every 35 s, once foes are within 18 m, you erupt in a supernova that scorches every foe in range."),
                        T("p_sunorbs", "Sunfire Orbs", "Three blazing orbs circle you at 3 m, burning any foe they touch."),
                        T("p_phoenixform", "Phoenix Form", "Press F (gamepad LB) to become a phoenix for 12 s: fly freely, rain fire on the ground around you (your staff rests) and take 50% less damage. 45 s cooldown.") },
            },
        };

        /// Dragon trees: rows 1, 3 and 5 are shared by every dragon; rows 2, 4 and 6 are the dragon's own.
        static readonly TalentDef[][] DRAGON_SHARED_ROWS =
        {
            new[] { T("dt_fury", "Fury", "Your dragon deals 15% more damage.", "dragondmg%:15"),
                    T("dt_swift", "Swiftwing", "Your dragon's cooldowns are 12% shorter.", "dragoncd%:12"),
                    T("dt_vast", "Vast", "Your dragon grows 15% larger: its breath reaches farther and its dives hit wider.", "size%:15") },
            new[] { T("dt_shelter", "Sheltering Wings", "+1 rank of Sheltering Wing (12% chance per rank): its wing stops a potion, boulder, harpoon or wyvern bomb meant for you.", "wing:1"),
                    T("dt_talons", "Watchful Talons", "+1 rank of Guardian's Talons: below 35% health on foot, your dragon swoops in and carries you out (a hatchling crashes down beside you instead).", "rescue:1"),
                    T("dt_wrath", "Wrathful", "+1 rank of Wrathful Guardian (20% chance per rank): your dragon scorches whatever hurts you.", "wrath:1") },
            new[] { T("dt_skyrider", "Skyrider", "While riding your dragon you take 25% less damage.", "mountdr%:25"),
                    T("dt_unbound", "Unbound", "Your dragon's signature cools down 20% faster.", "sigcd%:20"),
                    T("dt_ancient", "Ancient Power", "Your dragon deals 30% more damage.", "dragondmg%:30") },
        };

        static TalentDef[][] DragonTree(TalentDef[] own2, TalentDef[] own4, TalentDef[] own6) =>
            new[] { DRAGON_SHARED_ROWS[0], own2, DRAGON_SHARED_ROWS[1], own4, DRAGON_SHARED_ROWS[2], own6 };

        public static readonly Dictionary<string, TalentDef[][]> DRAGON_TREES = new Dictionary<string, TalentDef[][]>
        {
            ["blue"] = DragonTree(
                new[] { T("db_kindle", "Blue Flame", "Its burns deal 40% more damage.", "burn%:40"),
                        T("db_maw", "Roaring Maw", "Its breath cone is 8° wider and reaches 15% farther.", "cone:8,reach%:15"),
                        T("db_ash", "Ashfall", "Its dives leave the ground burning for 4 s.") },
                new[] { T("db_inferno", "Greater Inferno", "Inferno Wave hurls 4 more fireballs. Needs world 2 or later.", "fireballs:4"),
                        T("db_kiln", "Kiln Heart", "Each breath lasts 40% longer.", "breathtime%:40"),
                        T("db_cinder", "Cinder Guard", "+1 rank of Wrathful Guardian.", "wrath:1") },
                new[] { T("db_azure", "Azure Inferno", "Its breath sometimes (35% per pulse) sets the ground ablaze where it hits."),
                        T("db_rekindle", "Rekindle", "Once per world, a killing blow instead has your dragon pull you from the fire, leaving you at 40% health and invulnerable for 2.5 s."),
                        T("db_sunburst", "Sunburst", "Its dives hit 30% wider and 40% harder.", "diverad%:30,divedmg%:40") }),
            ["red"] = DragonTree(
                new[] { T("dr_volatile", "Volatile", "Breath hits have a 20% chance to burst in a small explosion for double damage."),
                        T("dr_crater", "Crater", "Its dives hit 25% wider.", "diverad%:25"),
                        T("dr_hide", "Molten Hide", "While riding you take 20% less damage.", "mountdr%:20") },
                new[] { T("dr_aftershock", "Aftershocks", "Cataclysm sets off 3 more aftershocks (8 instead of 5). Needs world 2 or later.", "aftershocks:3"),
                        T("dr_quake", "Quake", "Its dives stun 1.5 s longer (2.3 s in all; not bosses).", "divestun:1.5"),
                        T("dr_fuse", "Short Fuse", "Your dragon's cooldowns are 15% shorter.", "dragoncd%:15") },
                new[] { T("dr_meteor", "Meteor Dive", "Its dives deal 60% more damage.", "divedmg%:60"),
                        T("dr_bulwark", "Living Bulwark", "When a hit drops you below 30% health your dragon erupts around you, blasting and stunning everything near (45 s cooldown)."),
                        T("dr_chain", "Chain Reaction", "Foes your dragon kills explode, hurting nearby foes for 30% of their max health.") }),
            ["black"] = DragonTree(
                new[] { T("dk_leech", "Sanguine Breath", "You heal for 1.8% of the damage your dragon deals.", "leech%:1.8"),
                        T("dk_dread", "Dread", "Its weakening is stronger: weakened foes hit for 60% less instead of 40%.", "weaken:0.2"),
                        T("dk_veil", "Shadow Veil", "While riding you take 15% less damage.", "mountdr%:15") },
                new[] { T("dk_monsoon", "Monsoon", "Blood Rain covers 40% more ground and lasts 2 s longer. Needs world 2 or later.", "rainarea%:40,raintime:2"),
                        T("dk_hunger", "Hunger", "Every foe your dragon kills heals you for 0.6% of your max health.", "killheal%:0.6"),
                        T("dk_night", "Nightfall", "Your dragon's cooldowns are 15% shorter.", "dragoncd%:15") },
                new[] { T("dk_tide", "Crimson Tide", "Standing in Blood Rain heals you for 1.2% of your max health per second. Needs world 2 or later."),
                        T("dk_pact", "Pact of Wings", "Once per world, a killing blow instead has your dragon shield you, leaving you at 40% health and invulnerable for 2.5 s."),
                        T("dk_vampire", "Vampire Wyrm", "Your dragon deals 40% more damage.", "dragondmg%:40") }),
        };

        /// Every talent of every kind, by id.
        static Dictionary<string, TalentDef> allTalents;

        public static TalentDef FindTalent(string id)
        {
            if (allTalents == null)
            {
                allTalents = new Dictionary<string, TalentDef>();
                foreach (var set in new[] { RUN_TALENTS, HERO_TREES, DRAGON_TREES })
                    foreach (var tree in set.Values)
                        foreach (var row in tree)
                            foreach (var t in row)
                                allTalents[t.id] = t;
            }
            return id != null && allTalents.TryGetValue(id, out var d) ? d : null;
        }

        /// True for the in-run talents (saved with the run); permanent tree picks live in the save slot.
        public static bool IsRunTalent(string id)
        {
            foreach (var tree in RUN_TALENTS.Values)
                foreach (var row in tree)
                    foreach (var t in row)
                        if (t.id == id) return true;
            return false;
        }

        /// The permanent tree for "hero:knight" or "dragon:blue" style keys.
        public static TalentDef[][] Tree(string key)
        {
            var parts = key.Split(':');
            if (parts.Length != 2) return null;
            var set = parts[0] == "hero" ? HERO_TREES : DRAGON_TREES;
            return set.TryGetValue(parts[1], out var tree) ? tree : null;
        }

        // ------------------------------------------------------------ dragon cards (the third card on every level-up)

        /// One dragon upgrade offered on the dragon card. Breath ("dragonfire") and the signature ("ride") start at rank 1;
        /// the rest start at 0. only = the dragon it belongs to ("" for every dragon).
        public class DragonCardDef
        {
            public string id, name, desc, only = "";
            public int start, max = 5;   // breath and signature go to 10
            public bool guardian;
        }

        public static readonly DragonCardDef[] DRAGON_CARDS =
        {
            new DragonCardDef { id = "dragonfire", name = "Breath", start = 1, max = 10, desc = "+damage, +reach, longer breath; breathes and dives more often" },
            new DragonCardDef { id = "ride", name = "Signature", start = 1, max = 10, desc = "+damage, bigger area and a shorter cooldown for your signature (E)" },
            new DragonCardDef { id = "d_size", name = "Ancient Growth", desc = "Your dragon grows 12% larger: its breath reaches 8% farther and its dives hit 8% wider" },
            new DragonCardDef { id = "d_cone", name = "Wide Maw", desc = "Its breath cone is 6° wider" },
            new DragonCardDef { id = "d_haste", name = "Restless Wings", desc = "Its breath, dives and signature have 8% shorter cooldowns" },
            new DragonCardDef { id = "d_rescue", name = "Guardian's Talons", guardian = true,
                desc = "When a hit drops you below 35% health on foot, your dragon swoops in and carries you off (a hatchling crashes down beside you instead). 90 s cooldown, 10 s shorter per rank" },
            new DragonCardDef { id = "d_wing", name = "Sheltering Wing", guardian = true,
                desc = "12% chance per rank that your dragon's wing stops a potion, boulder, harpoon or wyvern bomb meant for you" },
            new DragonCardDef { id = "d_wrath", name = "Wrathful Guardian", guardian = true,
                desc = "20% chance per rank that your dragon scorches whatever hurts you" },
            new DragonCardDef { id = "d_blue", name = "Everburning", only = "blue", desc = "Its burns deal 30% more damage per rank" },
            new DragonCardDef { id = "d_red", name = "Shatterscale", only = "red", desc = "Its signature and dives hit 10% wider per rank" },
            new DragonCardDef { id = "d_black", name = "Blood Thirst", only = "black", desc = "You heal for 1.2% per rank of the damage your dragon deals" },
        };

        public static DragonCardDef FindDragonCard(string id) => System.Array.Find(DRAGON_CARDS, c => c.id == id);

        // ------------------------------------------------------------ meta XP (earned after every run)

        /// Hero XP: for the hero you played. Bond XP: for the dragon you rode.
        public static int HeroXpFor(int heroKills, float timeS, int stage, bool won) =>
            Mathf.RoundToInt(heroKills * 0.25f + timeS * 0.1f + 75f * (stage - 1) + (won ? 150f : 0f));

        public static int BondXpFor(int dragonKills, float rideS, int signatures, int rescues, int stage, bool won) =>
            Mathf.RoundToInt(dragonKills * 0.5f + rideS * 1f + signatures * 3f + rescues * 15f + 50f * (stage - 1) + (won ? 100f : 0f));

        // ------------------------------------------------------------ power-ups

        /// Temporary run-only boosts dropped by ogres. Each lasts POWERUP_TIME seconds; picking up the same kind refreshes it.
        public class PowerUpDef
        {
            public string name, desc, glyph;
            public Color color;
        }

        public const float POWERUP_TIME = 15f;
        public const float POWERUP_SPEED = 0.4f;     // +40% move speed
        public const float POWERUP_DAMAGE = 0.5f;    // +50% damage
        public const float POWERUP_MAGNET = 12f;     // magnet radius x12 (and everything on the map flies to you on pickup)
        /// Chance that a regular ogre drops a power-up: 1 in 500 kills (bosses always drop BOSS_POWERUPS); each Utility rank adds POWERUP_DROP_RANK.
        public const float POWERUP_DROP = 0.002f;
        public const float POWERUP_DROP_RANK = 0.001f;
        public const int BOSS_POWERUPS = 2;

        public static readonly string[] POWERUP_ORDER = { "speed", "damage", "magnet" };
        public static readonly Dictionary<string, PowerUpDef> POWERUPS = new Dictionary<string, PowerUpDef>
        {
            ["speed"] = new PowerUpDef { name = "Swiftness", glyph = "»", color = C(0.25f, 0.9f, 1f), desc = "+40% movement speed" },
            ["damage"] = new PowerUpDef { name = "Fury", glyph = "†", color = C(1f, 0.2f, 0.18f), desc = "+50% damage" },
            ["magnet"] = new PowerUpDef { name = "Lodestone", glyph = "U", color = C(0.78f, 0.45f, 1f), desc = "Every gem and orb on the field flies to you, and your pickup range is huge" },
        };

        /// XP: half as many ogres drop a gem, but each gem is worth twice as much.
        public const float GEM_DROP_CHANCE = 0.5f;
        public const float GEM_VALUE_MULT = 2f;

        /// Level-up rerolls the player gets at the start of every world (unused ones don't carry over).
        public const int REROLLS_PER_WORLD = 5;

        /// Chance that a level-up swaps its usual type (ability <-> stats).
        public const float LEVEL_SWAP_CHANCE = 0.25f;

        // ------------------------------------------------------------ ogres

        /// A regular foe. The swarm is skeletons and renegade knights; ogres are the big ones (brutes, hulks, harpooners)
        /// and the bosses; grunts throw potions and empower others. role: "", "archer" (potion thrower), "shaman",
        /// "harpooner". model: "" for an ogre, "skeleton" or "human" (they are drawn at a human height instead).
        public class OgreDef
        {
            public string name, role = "", model = "";
            public float size;
        }

        public static readonly Dictionary<string, OgreDef> OGRES = new Dictionary<string, OgreDef>
        {
            ["runt"] = new OgreDef { name = "Skeleton", size = 0.7f, model = "skeleton" },
            ["ogre"] = new OgreDef { name = "Renegade Knight", size = 1.0f, model = "human" },
            ["brute"] = new OgreDef { name = "Ogre Brute", size = 1.45f },
            ["hulk"] = new OgreDef { name = "Ogre Hulk", size = 2.0f },
            ["archer"] = new OgreDef { name = "Grunt Alchemist", size = 0.95f, role = "archer" },
            ["harpooner"] = new OgreDef { name = "Ogre Harpooner", size = 1.15f, role = "harpooner" },
            ["shaman"] = new OgreDef { name = "Grunt Shaman", size = 1.1f, role = "shaman" },
        };

        /// Global enemy tuning (health and damage raised 25% twice: 1.3 -> 1.625 -> 2.03 and 1.5 -> 1.875 -> 2.34).
        /// Monster density was raised 25% (spawn rate 0.7 -> 0.875 and the on-field cap x1.25).
        public const float SPAWN_RATE_MULT = 0.875f;
        public const float SPAWN_CAP_MULT = 1.25f;
        /// Regular monsters (not bosses) get this much extra health on top of everything else.
        public const float ENEMY_HP_MULT = 1.5f;
        public const float ENEMY_STAT_MULT = 2.03125f;
        public const float BOSS_STAT_MULT = 2.34375f;

        /// Spawn weights per wave (four 2-minute waves, the hardest mix last), as (kind, weight) pairs in a fixed order.
        /// Ranged ogres were halved; harpooners (they only hunt riders in the air) are about one in ten of those left.
        /// The potion-throwing "archer" share was halved again (20/40/48/48 -> 10/20/24/24).
        public static readonly (string kind, int weight)[][] WAVE_WEIGHTS =
        {
            new[] { ("runt", 650), ("ogre", 300), ("archer", 10), ("harpooner", 2) },
            new[] { ("runt", 450), ("ogre", 350), ("brute", 80), ("archer", 20), ("harpooner", 5), ("shaman", 20) },
            new[] { ("runt", 350), ("ogre", 300), ("brute", 150), ("hulk", 40), ("archer", 24), ("harpooner", 6), ("shaman", 40) },
            new[] { ("runt", 200), ("ogre", 270), ("brute", 230), ("hulk", 120), ("archer", 24), ("harpooner", 6), ("shaman", 60) },
        };

        /// Before the dragon can fly (stage 1) a harpooner roll becomes an archer this often, otherwise a runt, so the
        /// converted rolls don't undo the halved archer share.
        public const float HARPOONER_AS_ARCHER = 0.5f;

        // ------------------------------------------------------------ knockback on the player

        /// A monster's melee hit (and the player running into one) shoves the hero a short way. The distance in metres
        /// by tier on stage 1: regular, brute, hulk, mini boss, final boss. It grows with the stage like the rest of the
        /// game (the stage's enemyMult to the power KNOCK_STAGE_POW: x1.0 / x1.22 / x1.43 on stages 1 / 2 / 3).
        public static readonly float[] KNOCK_DIST = { 1.4f, 2.2f, 2.8f, 3.6f, 4.5f };
        public const int KNOCK_REGULAR = 0, KNOCK_BRUTE = 1, KNOCK_HULK = 2, KNOCK_MINI_BOSS = 3, KNOCK_FINAL_BOSS = 4;
        public const float KNOCK_STAGE_POW = 0.25f;
        /// Running into a monster pushes this fraction of a melee hit's distance, and only when closing at KNOCK_BUMP_SPEED m/s or more.
        public const float KNOCK_BUMP_MULT = 0.5f;
        public const float KNOCK_BUMP_SPEED = 4f;
        /// The shove dies out with exp(-KNOCK_DECAY t): about 0.35 s of motion. Hard cap on any single shove, in metres.
        public const float KNOCK_DECAY = 9f;
        public const float KNOCK_MAX = 7f;
        /// Seconds before another shove of similar strength can start (a clearly stronger blow still lands at once).
        public const float KNOCK_COOLDOWN = 0.6f;
        /// A shove is cancelled rather than push the hero over a drop of more than this many metres (island rims, cliffs).
        public const float KNOCK_MAX_DROP = 0.8f;

        /// Knockback distance in metres for a tier on a stage with the given enemyMult.
        public static float KnockDistance(int tier, float stageEnemyMult) =>
            Mathf.Min(KNOCK_MAX, KNOCK_DIST[Mathf.Clamp(tier, 0, KNOCK_DIST.Length - 1)] * Mathf.Pow(Mathf.Max(stageEnemyMult, 0.01f), KNOCK_STAGE_POW));

        /// Alchemists lob potions in a slow arc (flight time grows with distance) that splash where they land.
        public const float POTION_FLIGHT_MIN = 1.1f;
        public const float POTION_FLIGHT_MAX = 1.7f;
        public const float POTION_SPLASH = 2.4f;
        /// Seconds between one alchemist's throws (a shaman's buff shortens it by 30%).
        public const float POTION_COOLDOWN_MIN = 5f;
        public const float POTION_COOLDOWN_MAX = 6.5f;

        /// Ogre Harpooner: wind-up (telegraphed) before each throw, seconds between throws, and what a hit does.
        /// One hit drains flight and slows the dragon; only a second hit while still hooked drags the rider down.
        public const float HARPOON_WINDUP = 1.1f;
        public const float HARPOON_COOLDOWN = 6f;
        public const float HARPOON_RANGE = 42f;
        /// Harpoons fly slowly enough to see coming and dodge.
        public const float HARPOON_SPEED = 24f;
        public const float HARPOON_FLIGHT_DRAIN = 3f;
        public const float HARPOON_HOOK_TIME = 3f;
        public const float HARPOON_SLOW = 0.55f;
        /// After a hook, harpoons in this window only deal damage (so one volley can never drag you down).
        public const float HARPOON_GRACE = 1.5f;
        /// Harpooners take turns: no wind-up starts within this many seconds of another one's.
        public const float HARPOON_STAGGER = 2f;

        // ---------------------------------------------------------------- WYVERN (flying harasser, world 2 onward)
        // Only the dragon can hurt a wyvern (breath, dives, signature, guardian blasts), unless the hero is riding it
        // (mounted or in a soaring dragon form): then the hero's own weapons and spells count too. See Wyvern.cs.

        /// Regular-but-rare: a spawn director (not the wave table) adds one at a time, indexed by stage - 1 (world 1: none).
        /// Most alive at once, first spawn after this many seconds into the stage, and seconds between spawns.
        public static readonly int[] WYVERN_MAX_ALIVE = { 0, 2, 4 };
        public static readonly float[] WYVERN_FIRST_SPAWN = { 0f, 50f, 30f };
        public static readonly float[] WYVERN_SPAWN_EVERY = { 0f, 42f, 26f };
        /// The spawn gap shrinks to this fraction of itself by the end of the run's clock.
        public const float WYVERN_SPAWN_RAMP = 0.7f;
        /// Body size feeds the usual health/damage formula (a brute is 1.45); multipliers on top of the shared stage/time scaling.
        public const float WYVERN_SIZE = 1.5f;
        public const float WYVERN_HP_MULT = 1.5f;
        public const float WYVERN_DMG_MULT = 1.15f;
        public const int WYVERN_XP = 6;
        /// Hit radius for the dragon's breath/blasts (the model is about WYVERN_LENGTH long, nose to tail).
        public const float WYVERN_RADIUS = 2.2f;
        public const float WYVERN_LENGTH = 5.5f;
        /// Cruise speed and the speed of a bombing run (the hero walks at ~9).
        public const float WYVERN_SPEED = 13f;
        public const float WYVERN_RUN_SPEED = 21f;
        /// Cruise height above the higher of the ground and the hero, a bombing run's height, and the minimum above ground.
        public const float WYVERN_ALTITUDE = 10f;
        public const float WYVERN_BOMB_ALTITUDE = 8f;
        public const float WYVERN_MIN_AGL = 6f;
        /// Re-evaluate bomb vs distract this often (seconds), and never switch sooner than the commit time.
        public const float WYVERN_THINK_EVERY = 3f;
        public const float WYVERN_COMMIT = 5f;
        /// Bombing: at most this many wyverns bomb at once (indexed by stage - 1), seconds between a wyvern's runs
        /// (indexed by stage - 1), the blast radius, and the bomb's fall time (the ground marker shows for the whole run-up plus this).
        public static readonly int[] WYVERN_MAX_BOMBERS = { 0, 1, 2 };
        public static readonly float[] WYVERN_BOMB_COOLDOWN = { 0f, 6f, 4.5f };
        public const float WYVERN_BOMB_RADIUS = 4.2f;
        public const float WYVERN_BOMB_FALL = 0.45f;
        /// The marker is placed where the hero will be this many seconds ahead; at least this long before the blast.
        public const float WYVERN_BOMB_LEAD = 0.45f;
        public const float WYVERN_BOMB_MIN_WARNING = 1.15f;
        /// World 3 wyverns carpet the run: a second bomb this far further along the run line.
        public const float WYVERN_BOMB_SECOND_GAP = 7f;
        /// A bomb's damage is the wyvern's damage times this; it can't catch a hero flying higher than this above the ground.
        public const float WYVERN_BOMB_DMG = 1.4f;
        public const float WYVERN_BOMB_REACH_Y = 3.5f;
        /// Decision weights: below this much hero health the wyverns press the bombing; the dragon counts as "close" to
        /// the hero inside NEAR and "far" beyond FAR; a breath that is this many seconds from ready counts as "on cooldown".
        public const float WYVERN_LOW_HERO_HP = 0.4f;
        public const float WYVERN_DRAGON_NEAR = 9f;
        public const float WYVERN_DRAGON_FAR = 18f;
        public const float WYVERN_BREATH_COOLDOWN = 1.5f;
        /// Harrying the dragon: a swoop at it every few seconds; each makes its breath prefer this wyvern for a while,
        /// and (at most once per DIVE_LOCK seconds) holds off its next automatic dive for a moment.
        public const float WYVERN_HARRY_EVERY = 2.6f;
        public const float WYVERN_HARRY_TIME = 2.2f;
        public const float WYVERN_HARRY_DIVE_DELAY = 1.5f;
        public const float WYVERN_HARRY_DIVE_LOCK = 10f;
        /// Hurt wyverns (below this health fraction) break off, flying away for a while before coming back.
        public const float WYVERN_RETREAT_HP = 0.3f;
        public const float WYVERN_RETREAT_TIME = 7f;

        public class BossDef
        {
            public string name;
            public float size, hp, speed, damage;
            public string[] abilities;
            public Color skin;
        }

        /// Index 0 is the mini boss (4:00); index 1 is the final boss on the floating island (0:00). Stages rename and
        /// strengthen them (StageDef).
        public const int MINI_BOSS = 0, FINAL_BOSS = 1;
        public static readonly BossDef[] BOSSES =
        {
            new BossDef { name = "Krag the Stonehide", size = 4.4f, hp = 3600f, speed = 3.2f, damage = 24f,
                abilities = new[] { "charge", "slam", "boulders" }, skin = C(0.42f, 0.42f, 0.45f) },
            new BossDef { name = "Gorrak, the Mountain King", size = 6.5f, hp = 12000f, speed = 3.0f, damage = 34f,
                abilities = new[] { "slam", "boulders", "charge", "summon", "boulders" }, skin = C(0.32f, 0.38f, 0.28f) },
        };

        /// Signature attacks of the later final bosses (tuned against the boss's own damage stat).
        /// Ulgrim's "thorns": a fan of root lines erupts from him; each line is marked on the ground for THORNS_WARN seconds.
        public const int THORNS_LINES = 5;
        public const float THORNS_SPREAD = 0.5f;     // radians between neighbouring lines (the middle one points at the hero)
        public const float THORNS_LENGTH = 34f;
        public const float THORNS_WIDTH = 1.7f;      // half-width of a line
        public const float THORNS_WARN = 1.3f;
        public const float THORNS_DAMAGE = 1.3f;     // x boss damage
        /// Skarnath's "gravehands": bony hands claw out of the ground around the hero after GRAVE_WARN seconds.
        public const int GRAVE_HANDS = 9;
        public const float GRAVE_RADIUS = 2.8f;
        public const float GRAVE_WARN = 1.4f;
        public const float GRAVE_SPREAD = 13f;       // how far from the hero the stray hands may rise
        public const float GRAVE_DAMAGE = 1.2f;      // x boss damage

        // ------------------------------------------------------------ stages

        /// The run is three stages. Clearing a stage's final boss opens a portal to the next; the hero keeps everything.
        public class StageDef
        {
            public string name, intro;
            public string[] waves;
            public string miniBoss, finalBoss, islandName;
            /// The final boss's health on top of every other multiplier (stage 2: x2, stage 3: x3), its model ("" = the
            /// golem king, "ogre", "dragon") and the signature attack it adds to the usual set ("" = none).
            public float finalBossHpMult = 1f;
            public string finalBossModel = "", finalBossAbility = "";
            /// Enemy health multiplier on top of the stage's time growth (damage grows by its square root).
            public float enemyMult;
            /// Terrain palette.
            public Color grass, grass2, dry, dirt, rock, ash, mud;
            public float scorch;          // how much of the ground is burned (0..1)
            public Color fog, skyTop, skyHorizon, sunColor, skyTint;
            public float fogDensity, sunPitch;
            public Color bossTint;
        }

        /// Health multiplier handed to the final boss of a stage: the boss stat multiplier, the stage's growth and its extra boss health.
        public static float FinalBossHpMult(StageDef s) => BOSS_STAT_MULT * s.enemyMult * s.finalBossHpMult;

        public const int STAGE_COUNT = 3;
        public static readonly StageDef[] STAGES =
        {
            new StageDef { name = "The Burning Fields", intro = "Survive. A champion comes at 4:00; at 0:00 the Mountain King awaits.",
                waves = new[] { "The Burning Fields", "Night of Clubs", "Thunder in the Hills", "The Ashen Dusk" },
                miniBoss = "Krag the Stonehide", finalBoss = "Gorrak, the Mountain King", islandName = "Mountain King's Island", enemyMult = 1f,
                grass = C(0.27f, 0.34f, 0.15f), grass2 = C(0.36f, 0.38f, 0.17f), dry = C(0.47f, 0.41f, 0.25f), dirt = C(0.33f, 0.26f, 0.18f),
                rock = C(0.40f, 0.39f, 0.37f), ash = C(0.10f, 0.09f, 0.08f), mud = C(0.22f, 0.19f, 0.14f), scorch = 1f,
                fog = C(0.62f, 0.58f, 0.52f), skyTop = C(0.28f, 0.38f, 0.52f), skyHorizon = C(0.72f, 0.64f, 0.55f), sunColor = C(1f, 0.9f, 0.78f),
                skyTint = Color.white, fogDensity = 0.0045f, sunPitch = -38f, bossTint = Color.white },
            new StageDef { name = "The Verdant Marches", intro = "The portal sets you down among forest villages. The clans followed you through.",
                waves = new[] { "Whispering Pines", "The Hunted Hamlet", "Moonlit Clearing", "Wolfsbane Dusk" },
                miniBoss = "Vorna the Thornhide", finalBoss = "Ulgrim, the Forest Tyrant", islandName = "Forest Tyrant's Island", enemyMult = 2.185f,
                finalBossHpMult = 2f, finalBossModel = "ogre", finalBossAbility = "thorns",
                grass = C(0.22f, 0.42f, 0.14f), grass2 = C(0.32f, 0.5f, 0.17f), dry = C(0.42f, 0.45f, 0.22f), dirt = C(0.36f, 0.27f, 0.17f),
                rock = C(0.45f, 0.45f, 0.42f), ash = C(0.2f, 0.22f, 0.12f), mud = C(0.24f, 0.22f, 0.14f), scorch = 0.15f,
                fog = C(0.55f, 0.66f, 0.6f), skyTop = C(0.3f, 0.48f, 0.66f), skyHorizon = C(0.72f, 0.78f, 0.7f), sunColor = C(1f, 0.96f, 0.85f),
                skyTint = C(0.75f, 0.95f, 0.85f), fogDensity = 0.004f, sunPitch = -48f, bossTint = C(0.55f, 0.85f, 0.5f) },
            new StageDef { name = "The Sunken Crypts", intro = "A land of buried halls and torchlight. The crypt warlord waits at the end.",
                waves = new[] { "Crypt Gate", "Halls of Bone", "The Drowned Chapel", "Last Torch" },
                miniBoss = "Mordak the Gravecaller", finalBoss = "Skarnath, the Crypt Warlord", islandName = "Crypt Warlord's Island", enemyMult = 4.2f,
                finalBossHpMult = 3f, finalBossModel = "dragon", finalBossAbility = "gravehands",
                grass = C(0.31f, 0.3f, 0.25f), grass2 = C(0.38f, 0.34f, 0.27f), dry = C(0.46f, 0.39f, 0.29f), dirt = C(0.33f, 0.27f, 0.21f),
                rock = C(0.42f, 0.4f, 0.42f), ash = C(0.13f, 0.11f, 0.12f), mud = C(0.2f, 0.17f, 0.15f), scorch = 0.35f,
                fog = C(0.4f, 0.37f, 0.48f), skyTop = C(0.22f, 0.2f, 0.34f), skyHorizon = C(0.55f, 0.45f, 0.58f), sunColor = C(0.95f, 0.85f, 1f),
                skyTint = C(0.55f, 0.48f, 0.7f), fogDensity = 0.006f, sunPitch = -24f, bossTint = C(0.65f, 0.55f, 0.85f) },
        };
    }
}
