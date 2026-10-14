using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Static game data: heroes, weapons, spells, passives, dragons, stat buckets, ogres, bosses and waves.
    /// Balance lives almost entirely here.
    public static class Data
    {
        public const float RUN_LENGTH = 600f;
        public const float WAVE_LENGTH = 120f;
        public const int MAX_LEVEL = 5;

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
                desc = "Armored melee battlemage. Longsword sweeps around you; a shield blocks blows.", hp = 100f },
            ["huntress"] = new HeroDef { name = "Blood Huntress", color = C(0.9f, 0.2f, 0.25f), weapon = "blood_lance", defense = "evasion",
                desc = "Ranged blood mage. Hurls piercing Blood Javelins paid for in her own blood that make ogres bleed; dodges instead of blocking.", hp = 90f },
            ["pyromancer"] = new HeroDef { name = "Ember Mage", color = C(1.0f, 0.55f, 0.15f), weapon = "ember_bolt", defense = "ward",
                desc = "Fire caster. Rapidly flings small fireballs that burst and singe; a Flame Ward turns blows aside and burns the attacker.", hp = 85f },
        };

        // ------------------------------------------------------------ weapons (auto attacks)

        public class WeaponDef
        {
            public string name, desc, up;
            public Color color;
            public float damage, cooldown, range, arc, knock, hp_cost, orb_bonus, bleed, radius;
        }

        public static readonly Dictionary<string, WeaponDef> WEAPONS = new Dictionary<string, WeaponDef>
        {
            ["sword"] = new WeaponDef { name = "Knight's Longsword", color = C(0.75f, 0.88f, 1.0f),
                damage = 27.5f, cooldown = 0.6f, range = 5.0f, arc = 170f, knock = 5f,
                up = "+15% damage, faster swings, +6% reach" },
            ["blood_lance"] = new WeaponDef { name = "Blood Lance", color = C(0.95f, 0.15f, 0.2f),
                damage = 27.5f, cooldown = 0.75f, range = 28f, hp_cost = 0.02f, orb_bonus = 0.05f, bleed = 0.35f,
                desc = "Blood Javelins that pierce through everything and cause bleeding. Costs 2% of current health per throw, no mana. +5% blood orb chance on kills.",
                up = "+15% damage, faster throws; +1 javelin at Lv 3 and 5" },
            ["ember_bolt"] = new WeaponDef { name = "Ember Bolt", color = C(1.0f, 0.55f, 0.15f),
                damage = 20f, cooldown = 0.5f, range = 26f, radius = 1.6f,
                desc = "Small fireballs that burst on impact and singe ogres.",
                up = "+15% damage, faster casting; +1 fireball at Lv 3 and 5" },
        };

        // ------------------------------------------------------------ defenses

        public class DefenseDef
        {
            public string name, desc;
            public Color color;
            public float baseChance;
            public string[] ups;
        }

        public static readonly DefenseDef WARD = new DefenseDef { name = "Flame Ward", color = C(1.0f, 0.5f, 0.15f), baseChance = 0.10f,
            desc = "A chance to turn any blow aside; the attacker catches fire.",
            ups = new[] { "", "+5% ward chance", "+5% ward; warded blows explode in fire", "+5% ward chance", "+5% ward; each ward restores 5 mana" } };

        public static readonly DefenseDef EVASION = new DefenseDef { name = "Evasion", color = C(0.95f, 0.4f, 0.45f), baseChance = 0.15f,
            desc = "A chance to dodge any blow entirely.",
            ups = new[] { "", "+5% dodge chance", "+5% dodge; dodging heals 3% health", "+5% dodge chance", "+5% dodge; dodging hurls a counter-javelin" } };

        public static readonly DefenseDef SHIELD = new DefenseDef { name = "Dragonguard Shield", color = C(0.45f, 0.65f, 1.0f),
            desc = "A chance to block any blow entirely.",
            ups = new[] { "", "+5% block chance", "+5% block; blocks stagger the attacker", "+5% block chance", "+5% block; blocks reflect double damage" } };

        /// Health orb drop chance (bigger ogres drop them more often) and how much they heal.
        public const float ORB_CHANCE = 0.03f;
        public const float ORB_HEAL = 0.10f;

        // ------------------------------------------------------------ spells (auto-cast)

        public class SpellDef
        {
            public string name, desc;
            public Color color;
            public float cost, cd;
            public string[] ups;
        }

        public static readonly string[] SPELL_ORDER = { "gravity_well", "frost_bolt", "chain_lightning", "meteor" };
        public static readonly Dictionary<string, SpellDef> SPELLS = new Dictionary<string, SpellDef>
        {
            ["gravity_well"] = new SpellDef { name = "Gravity Well", color = C(0.6f, 0.3f, 1.0f), cost = 40f, cd = 4f,
                desc = "Opens a crushing vortex in the thickest crowd, dragging ogres in before it implodes.",
                ups = new[] { "", "+pull, +damage", "+radius, longer vortex", "+damage, bigger implosion", "opens a second well" } },
            ["frost_bolt"] = new SpellDef { name = "Frost Bolt", color = C(0.6f, 0.9f, 1.0f), cost = 16f, cd = 0.8f,
                desc = "A bolt of ice that bursts on contact, sending shards out from the center. Shards can freeze.",
                ups = new[] { "", "+2 shards, +damage", "+freeze chance", "+2 shards, +damage", "shards pierce through foes" } },
            ["chain_lightning"] = new SpellDef { name = "Chain Lightning", color = C(0.75f, 0.75f, 1.0f), cost = 24f, cd = 1.4f,
                desc = "Lightning leaps from foe to foe, with a chance to shock (1s stun on all but bosses).",
                ups = new[] { "", "+2 jumps, +damage", "+shock chance", "+2 jumps, +damage", "forks into two chains" } },
            ["meteor"] = new SpellDef { name = "Meteor Storm", color = C(1.0f, 0.65f, 0.25f), cost = 48f, cd = 5f,
                desc = "Burning stars rain on the thickest crowd, with a chance to ignite what they hit.",
                ups = new[] { "", "+2 meteors, +damage", "+ignite chance, bigger impacts", "+2 meteors, +damage", "a giant final meteor" } },
        };

        // ------------------------------------------------------------ passives

        public class PassiveDef
        {
            public string name, desc, up;
            public Color color;
        }

        public static readonly string[] PASSIVE_ORDER = { "blood_aura", "lightning_storm", "bloodthirst", "spellblade", "windwalker" };
        public static readonly Dictionary<string, PassiveDef> PASSIVES = new Dictionary<string, PassiveDef>
        {
            ["blood_aura"] = new PassiveDef { name = "Blood Aura", color = C(0.85f, 0.1f, 0.15f),
                desc = "A crimson ring drains every ogre inside it, healing you for part of the damage.",
                up = "+damage, +radius, +lifesteal" },
            ["lightning_storm"] = new PassiveDef { name = "Lightning Storm", color = C(0.65f, 0.75f, 1.0f),
                desc = "Lightning periodically strikes ogres around you, with a high chance to shock. Hits feed your mana.",
                up = "+1 bolt, +damage, strikes more often" },
            ["bloodthirst"] = new PassiveDef { name = "Bloodthirst", color = C(0.9f, 0.2f, 0.2f),
                desc = "Your weapons drink life: heal for part of the damage they deal.",
                up = "+lifesteal, +weapon damage" },
            ["spellblade"] = new PassiveDef { name = "Spellblade", color = C(0.4f, 0.8f, 1.0f),
                desc = "Weapon hits charge far more mana, and your spell hits harder.",
                up = "+mana per hit, +spell damage" },
            ["windwalker"] = new PassiveDef { name = "Windwalker", color = C(0.7f, 1.0f, 0.75f),
                desc = "Fleet of foot: move faster and sometimes dodge blows entirely.",
                up = "+speed, +dodge chance, longer dragon flights" },
        };

        // ------------------------------------------------------------ dragons

        public class DragonDef
        {
            public string name, focus, desc, sig, sig_desc;
            public float damage;
            public Color body, edge, belly, membrane, horn, eye, breath;
        }

        public static readonly string[] DRAGON_ORDER = { "blue", "red", "black" };
        public static readonly Dictionary<string, DragonDef> DRAGONS = new Dictionary<string, DragonDef>
        {
            ["blue"] = new DragonDef { name = "Azure Wyrm", focus = "Fire", damage = 1.0f,
                body = C(0.1f, 0.42f, 1.0f), edge = C(0.02f, 0.07f, 0.3f), belly = C(0.62f, 0.82f, 1.0f),
                membrane = C(0.1f, 0.3f, 0.75f), horn = C(0.92f, 0.88f, 0.78f), eye = C(1.0f, 0.85f, 0.25f),
                breath = C(0.25f, 0.6f, 1.0f),
                desc = "Fire focus. Its flames leave a heavy burning debuff.",
                sig = "Inferno Wave", sig_desc = "A torrent of fireballs sets the crowd ablaze." },
            ["red"] = new DragonDef { name = "Crimson Drake", focus = "Explosive", damage = 1.25f,
                body = C(0.75f, 0.08f, 0.05f), edge = C(0.2f, 0.02f, 0.01f), belly = C(0.95f, 0.7f, 0.35f),
                membrane = C(0.5f, 0.06f, 0.04f), horn = C(0.25f, 0.2f, 0.18f), eye = C(1.0f, 0.9f, 0.3f),
                breath = C(1.0f, 0.55f, 0.15f),
                desc = "Explosive and physical. Deals 1.25x the damage of other dragons; its breath bursts on impact.",
                sig = "Cataclysm", sig_desc = "A massive blast at the heart of the crowd, then a ring of aftershocks." },
            ["black"] = new DragonDef { name = "Night Wyrm", focus = "Blood", damage = 1.0f,
                body = C(0.08f, 0.07f, 0.08f), edge = C(0.45f, 0.02f, 0.04f), belly = C(0.3f, 0.05f, 0.07f),
                membrane = C(0.18f, 0.02f, 0.03f), horn = C(0.6f, 0.55f, 0.5f), eye = C(1.0f, 0.15f, 0.1f),
                breath = C(0.75f, 0.05f, 0.1f),
                desc = "Rains blood. Its breath and rain weaken ogres so they hit for less.",
                sig = "Blood Rain", sig_desc = "A storm of blood over the crowd: steady damage and -40% ogre attack." },
        };

        /// Dragon upgrades (shared by all dragons).
        public const string DRAGONFIRE_UP = "+damage, +range, breathes and dives more often";
        public const string SIGNATURE_UP = "+damage, bigger area, shorter cooldown";
        public const float SIGNATURE_CD = 10f;

        // ------------------------------------------------------------ stat buckets

        public class BucketDef
        {
            public string name;
            public Color color;
            public string[] stats;
        }

        public static readonly string[] BUCKET_ORDER = { "health", "mana", "offense", "defense", "dragon", "utility" };
        public static readonly Dictionary<string, BucketDef> BUCKETS = new Dictionary<string, BucketDef>
        {
            ["health"] = new BucketDef { name = "Health", color = C(0.9f, 0.25f, 0.25f),
                stats = new[] { "+12% max health", "+0.5 health regen / s", "+10% healing received" } },
            ["mana"] = new BucketDef { name = "Mana", color = C(0.5f, 0.45f, 1.0f),
                stats = new[] { "+15 max mana", "+0.8 mana regen / s", "+15% mana per hit" } },
            ["offense"] = new BucketDef { name = "Offense", color = C(1.0f, 0.6f, 0.25f),
                stats = new[] { "+8% damage", "+3% critical chance", "+6% attack speed" } },
            ["defense"] = new BucketDef { name = "Defense", color = C(0.5f, 0.75f, 1.0f),
                stats = new[] { "+5% block / dodge (defense level)", "+1.5 armor", "+2% parry" } },
            ["dragon"] = new BucketDef { name = "Dragon", color = C(0.35f, 0.65f, 1.0f),
                stats = new[] { "+12% dragon damage", "+20% flight time", "-6% dragon cooldowns" } },
            ["utility"] = new BucketDef { name = "Utility", color = C(0.6f, 1.0f, 0.6f),
                stats = new[] { "+10% jump height", "+6% movement speed", "+25% orb attract range" } },
        };

        /// Chance that a level-up swaps its usual type (ability <-> stats).
        public const float LEVEL_SWAP_CHANCE = 0.25f;

        // ------------------------------------------------------------ ogres

        public class OgreDef
        {
            public string name, role = "";
            public float size;
        }

        public static readonly Dictionary<string, OgreDef> OGRES = new Dictionary<string, OgreDef>
        {
            ["runt"] = new OgreDef { name = "Ogre Runt", size = 0.7f },
            ["ogre"] = new OgreDef { name = "Ogre", size = 1.0f },
            ["brute"] = new OgreDef { name = "Ogre Brute", size = 1.45f },
            ["hulk"] = new OgreDef { name = "Ogre Hulk", size = 2.0f },
            ["archer"] = new OgreDef { name = "Ogre Archer", size = 0.95f, role = "archer" },
            ["shaman"] = new OgreDef { name = "Ogre Shaman", size = 1.1f, role = "shaman" },
        };

        /// Global enemy tuning.
        public const float SPAWN_RATE_MULT = 0.7f;
        public const float ENEMY_STAT_MULT = 1.3f;
        public const float BOSS_STAT_MULT = 1.5f;

        /// Spawn weights per wave, as (kind, weight) pairs in a fixed order.
        public static readonly (string kind, int weight)[][] WAVE_WEIGHTS =
        {
            new[] { ("runt", 65), ("ogre", 30), ("archer", 5) },
            new[] { ("runt", 45), ("ogre", 35), ("brute", 8), ("archer", 10), ("shaman", 2) },
            new[] { ("runt", 35), ("ogre", 30), ("brute", 15), ("hulk", 4), ("archer", 12), ("shaman", 4) },
            new[] { ("runt", 25), ("ogre", 30), ("brute", 20), ("hulk", 8), ("archer", 12), ("shaman", 5) },
            new[] { ("runt", 20), ("ogre", 27), ("brute", 23), ("hulk", 12), ("archer", 12), ("shaman", 6) },
        };

        public static readonly string[] WAVE_NAMES =
        {
            "The Burning Fields",
            "Night of Clubs",
            "The Hungry March",
            "Thunder in the Hills",
            "The Ashen Dusk",
        };

        public class BossDef
        {
            public string name;
            public float size, hp, speed, damage;
            public string[] abilities;
            public Color skin;
        }

        public static readonly BossDef[] BOSSES =
        {
            new BossDef { name = "Grimgar, the Ogre Warlord", size = 3.6f, hp = 1800f, speed = 3.4f, damage = 22f,
                abilities = new[] { "slam", "charge" }, skin = C(0.4f, 0.48f, 0.3f) },
            new BossDef { name = "Mother Gruul", size = 4.0f, hp = 3200f, speed = 3.0f, damage = 20f,
                abilities = new[] { "boulders", "summon", "slam" }, skin = C(0.5f, 0.42f, 0.32f) },
            new BossDef { name = "Krag the Stonehide", size = 4.6f, hp = 5200f, speed = 3.2f, damage = 28f,
                abilities = new[] { "charge", "slam", "boulders" }, skin = C(0.42f, 0.42f, 0.45f) },
            new BossDef { name = "The Twin-Club Butcher", size = 5.0f, hp = 7000f, speed = 3.8f, damage = 26f,
                abilities = new[] { "charge", "boulders", "slam", "summon" }, skin = C(0.55f, 0.32f, 0.28f) },
            new BossDef { name = "Gorrak, the Mountain King", size = 6.5f, hp = 14000f, speed = 3.0f, damage = 34f,
                abilities = new[] { "slam", "boulders", "charge", "summon", "boulders" }, skin = C(0.32f, 0.38f, 0.28f) },
        };
    }
}
