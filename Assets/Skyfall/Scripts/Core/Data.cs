using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// Static game data: heroes, weapons, spells, passives, dragons, stat buckets, armor, talents, ogres, bosses, stages.
    /// Balance lives almost entirely here.
    public static class Data
    {
        /// Length of one stage; the mini boss arrives at 4:00 and the stage's final boss at 0:00 on the floating island.
        public const float RUN_LENGTH = 480f;
        public const float WAVE_LENGTH = 120f;
        public const float MINI_BOSS_TIME = 240f;
        public const int MAX_LEVEL = 5;
        /// Spells reach their evolved form (a unique, visually distinct upgrade) on their third upgrade.
        public const int EVOLVE_LEVEL = 4;

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
            ["pyromancer"] = new HeroDef { name = "Ember Mage", color = C(1.0f, 0.55f, 0.15f), weapon = "staff", defense = "ward",
                desc = "Fire caster. Her Ember Staff hurls bursting fire bolts that set ogres alight; a Flame Ward turns blows aside and burns the attacker.", hp = 85f },
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
                desc = "Wide sweeping cuts that knock ogres back.",
                up = "+15% damage, faster swings, +6% reach, +1 defense level" },
            ["blood_lance"] = new WeaponDef { name = "Blood Javelin", color = C(0.95f, 0.15f, 0.2f),
                damage = 27.5f, cooldown = 0.75f, range = 28f, hp_cost = 0.02f, orb_bonus = 0.05f, bleed = 0.35f,
                desc = "Blood Javelins that pierce through everything and cause bleeding. Costs 2% of current health per throw, no mana. +5% blood orb chance on kills.",
                up = "+15% damage, faster throws, +1 defense level; +1 javelin at Lv 3 and 5" },
            ["staff"] = new WeaponDef { name = "Ember Staff", color = C(1.0f, 0.55f, 0.15f),
                damage = 24f, cooldown = 0.5f, range = 30f, radius = 1.6f,
                desc = "Fire bolts from the staff's crystal that burst on impact and set ogres alight.",
                up = "+15% damage, faster casts, +1 defense level; +1 bolt at Lv 3 and 5; bigger bursts at Lv 5" },
        };

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
                    return new[] { $"Damage {dmg:0}   ·   {perSec:0.0} swings/s", $"Reach {w.range * (1f + 0.06f * (lvl - 1)):0.0} m" };
                case "blood_lance":
                    return new[] { $"Damage {dmg:0}   ·   {perSec:0.0} throws/s", $"{shots} javelin{(shots > 1 ? "s" : "")} per throw  ·  bleeds, 2% health cost" };
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
            desc = "A chance to turn any blow aside; the attacker catches fire.",
            ups = new[] { "", "+5% ward chance", "+5% ward; warded blows explode in fire", "+5% ward chance", "+5% ward; each ward restores 5 mana" } };

        public static readonly DefenseDef EVASION = new DefenseDef { name = "Evasion", color = C(0.95f, 0.4f, 0.45f), baseChance = 0.15f,
            desc = "A chance to dodge any blow entirely.",
            ups = new[] { "", "+5% dodge chance", "+5% dodge; dodging heals 3% health", "+5% dodge chance", "+5% dodge; dodging hurls a counter-javelin" } };

        public static readonly DefenseDef SHIELD = new DefenseDef { name = "Dragonguard Shield", color = C(0.45f, 0.65f, 1.0f), baseChance = 0.12f,
            desc = "A chance to block any blow entirely.",
            ups = new[] { "", "+5% block chance", "+5% block; blocks stagger the attacker", "+5% block chance", "+5% block; blocks reflect double damage" } };

        /// Health orb drop chance (bigger ogres drop them more often) and how much they heal.
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
        public static readonly Dictionary<string, SpellDef> SPELLS = new Dictionary<string, SpellDef>
        {
            ["gravity_well"] = new SpellDef { name = "Gravity Well", color = C(0.6f, 0.3f, 1.0f), cost = 40f, cd = 4f,
                evolved = "Event Horizon", evolvedColor = C(1f, 0.78f, 0.3f),
                desc = "Opens a crushing vortex in the thickest crowd, dragging ogres in before it implodes.",
                ups = new[] { "", "+pull, +damage", "+radius, longer vortex",
                    "EVOLVES into Event Horizon: a golden-ringed black hole that drags ogres from far away, implodes twice and stuns everything it crushed",
                    "opens a second well" } },
            ["frost_bolt"] = new SpellDef { name = "Frost Bolt", color = C(0.6f, 0.9f, 1.0f), cost = 16f, cd = 0.8f,
                evolved = "Glacial Comet", evolvedColor = C(0.85f, 1f, 1f),
                desc = "A bolt of ice that bursts on contact, sending shards out from the center. Shards can freeze.",
                ups = new[] { "", "+2 shards, +damage", "+freeze chance",
                    "EVOLVES into Glacial Comet: a huge comet that always freezes what it hits and leaves a field of ice that freezes ogres walking in",
                    "shards pierce through foes" } },
            ["chain_lightning"] = new SpellDef { name = "Chain Lightning", color = C(0.75f, 0.75f, 1.0f), cost = 24f, cd = 1.4f,
                evolved = "Storm Lord", evolvedColor = C(0.85f, 0.55f, 1f),
                desc = "Lightning leaps from foe to foe, with a chance to shock (1s stun on all but bosses).",
                ups = new[] { "", "+2 jumps, +damage", "+shock chance",
                    "EVOLVES into Storm Lord: violet lightning that never weakens as it jumps and leaves a crackling storm orb that keeps zapping",
                    "forks into two chains" } },
            ["meteor"] = new SpellDef { name = "Meteor Storm", color = C(1.0f, 0.65f, 0.25f), cost = 48f, cd = 5f,
                evolved = "Starfall", evolvedColor = C(0.6f, 0.8f, 1f),
                desc = "Burning stars rain on the thickest crowd, with a chance to ignite what they hit.",
                ups = new[] { "", "+2 meteors, +damage", "+ignite chance, bigger impacts",
                    "EVOLVES into Starfall: blue-white stars that hit twice as hard and leave pools of starfire burning on the ground",
                    "a giant final meteor" } },
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
                desc = "Fire focus. Its blue flames leave a heavy burning debuff.",
                sig = "Inferno Wave", sig_desc = "A torrent of fireballs sets the crowd ablaze." },
            ["red"] = new DragonDef { name = "Crimson Drake", focus = "Explosive", damage = 1.25f, skin = "red", skinTint = C(0.82f, 0.3f, 0.3f),
                body = C(0.5f, 0.03f, 0.05f), edge = C(0.12f, 0.01f, 0.02f), belly = C(0.72f, 0.38f, 0.22f),
                membrane = C(0.32f, 0.02f, 0.04f), horn = C(0.25f, 0.2f, 0.18f), eye = C(1.0f, 0.9f, 0.3f),
                breath = C(0.85f, 0.08f, 0.06f),
                desc = "Explosive and physical. Deals 1.25x the damage of other dragons; its breath bursts on impact.",
                sig = "Cataclysm", sig_desc = "A massive blast at the heart of the crowd, then a ring of aftershocks." },
            ["black"] = new DragonDef { name = "Night Wyrm", focus = "Blood", damage = 1.0f, skin = "black", glowBreath = false,
                body = C(0.08f, 0.07f, 0.08f), edge = C(0.45f, 0.02f, 0.04f), belly = C(0.3f, 0.05f, 0.07f),
                membrane = C(0.18f, 0.02f, 0.03f), horn = C(0.6f, 0.55f, 0.5f), eye = C(1.0f, 0.15f, 0.1f),
                breath = C(0.75f, 0.05f, 0.1f),
                desc = "Rains blood. Its breath and rain weaken ogres so they hit for less.",
                sig = "Blood Rain", sig_desc = "A storm of blood over the crowd: steady damage and -40% ogre attack." },
        };

        /// The dragon's signature (E) cooldown before upgrades.
        public const float SIGNATURE_CD = 10f;

        /// Eggs: the first final boss you ever slay always drops one (for a dragon you don't own); after that it's a coin flip.
        public const float EGG_CHANCE = 0.5f;

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
                stats = new[] { St(10f, "+{0}% jump height"), St(6f, "+{0}% movement speed"), St(25f, "+{0}% orb attract range"), St(0.1f, "+{0}% power-up drop chance") } },
        };

        /// Stat key used by armor and the player's stat table: "bucket:index".
        public static string StatKey(string bucket, int idx) => bucket + ":" + idx;

        // ------------------------------------------------------------ armor (chosen every 5 levels)

        /// Armor pieces are offered every ARMOR_EVERY levels: pick one of three. Each piece fills a slot (replacing what was
        /// there) and carries stats drawn from the stat buckets. Tiers: Common (3 stats), Superior (4), Mythic (6, rare);
        /// each tier is 15% stronger per stat than the one below.
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
                new[] { T("k_wyrmsteel", "Wyrmsteel Edge", "Sword damage +30%; every swing sets ogres ablaze."),
                        T("k_bulwark", "Bulwark", "+12% block chance; each block heals 2% max health."),
                        T("k_rally", "Rallying Cry", "+20% max health. Every 10 s gain a barrier that absorbs 15% of your max health.") },
                new[] { T("k_whirlwind", "Whirlwind", "Every 5th swing becomes a full spin dealing double damage around you."),
                        T("k_bash", "Shield Bash", "Blocks send out a shockwave that stuns and hurls back nearby ogres."),
                        T("k_reach", "Dragon's Reach", "+35% sword reach and +50% knockback.") },
            },
            ["huntress"] = new[]
            {
                new[] { T("h_hemorrhage", "Hemorrhage", "Bleeds deal 60% more damage and last longer."),
                        T("h_pact", "Blood Pact", "Javelins no longer cost health and deal 10% more damage."),
                        T("h_fleet", "Fleetfoot", "+15% dodge chance and +15% movement speed.") },
                new[] { T("h_volley", "Crimson Volley", "+2 javelins per throw."),
                        T("h_sanguine", "Sanguine Shield", "Dodging grants a blood barrier absorbing 10% of your max health (6 s cooldown)."),
                        T("h_orbs", "Blood Harvest", "Health orbs drop twice as often and also restore 20 mana.") },
            },
            ["pyromancer"] = new[]
            {
                new[] { T("p_kindling", "Kindling", "Burns deal 60% more damage."),
                        T("p_emberward", "Ember Ward", "+12% ward chance; each ward heals 3% max health."),
                        T("p_font", "Mana Font", "+40 max mana and +2 mana regen per second.") },
                new[] { T("p_twin", "Twin Embers", "+1 fire bolt per cast."),
                        T("p_firewalk", "Fire Walk", "You leave a trail of flames that burns ogres who follow you."),
                        T("p_combustion", "Combustion", "Burning ogres explode when they die, igniting their neighbors.") },
            },
        };

        // ---- permanent trees (main menu): six rows of three, one pick per row; rows are bought with hero or bond XP

        /// XP needed to open each row of a permanent tree (hero XP for hero trees, bond XP for dragon trees).
        public static readonly int[] TREE_ROW_COST = { 100, 200, 350, 500, 700, 900 };
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
                new[] { T("k_aegis", "Aegis of Scales", "Dropping below 30% health makes you invulnerable for 3 s and heals 20% (60 s cooldown)."),
                        T("k_charge", "Ember Charge", "Every 8 s you charge into the thickest nearby crowd, burning and scattering it."),
                        T("k_oath", "Oathbound", "Your dragon deals +40% damage and breathes 25% more often.") },
                new[] { T("k_guard", "Shield Wall", "+6% block chance and +1 health regen per second.", "block%:6,regen:1"),
                        T("k_cleave", "Cleave", "+20% weapon damage.", "wdmg%:20"),
                        T("k_rider", "Lancer", "+40% damage while riding your dragon.", "mountdmg%:40") },
                new[] { T("k_thunderclap", "Thunderclap", "Landing after a double jump slams the ground for heavy damage."),
                        T("k_retribution", "Retribution", "30% of the damage you take lashes out at every ogre within 6 m (tripled)."),
                        T("k_sky", "Sky Sovereign", "While riding you take 30% less damage and your dragon's breath deals 25% more.") },
                new[] { T("k_bastion", "Bastion", "You take 12% less damage.", "dmgtaken%:12"),
                        T("k_fervor", "Battle Fervor", "+25% healing received and +2 health regen per second.", "heal%:25,regen:2"),
                        T("k_wyrmcall", "Wyrmcaller", "Your dragon's signature deals 35% more damage over a 20% larger area.", "sigdmg%:35,sigarea%:20") },
                new[] { T("k_dragonform", "Dragon Form", "Press F (gamepad LB) to become a dragon for 12 s: fly freely, breathe fire on everything, take 50% less damage. 45 s cooldown."),
                        T("k_firewave", "Dragonfire Blade", "Every swing also launches a wave of dragonfire that rolls forward through the crowd."),
                        T("k_vow", "Undying Vow", "Once per stage, a killing blow instead leaves you at 50% health in a nova of holy fire.") },
            },
            ["huntress"] = new[]
            {
                new[] { T("h_keen", "Keen Eye", "+10% damage and +6% attack speed.", "dmg%:10,atkspd%:6"),
                        T("h_lithe", "Lithe", "+8% dodge chance and +8% movement speed.", "block%:8,move%:8"),
                        T("h_bondblood", "Blood Bond", "Your dragon deals +20% damage and its cooldowns are 10% shorter.", "dragondmg%:20,dragoncd%:10") },
                new[] { T("h_execute", "Exsanguinate", "Javelins execute bleeding ogres below 25% health (not bosses)."),
                        T("h_shadowstep", "Shadowstep", "When an ogre gets close, blink 8 m away and leave a blood decoy that explodes (7 s cooldown)."),
                        T("h_wyrmbond", "Wyrm Bond", "Your dragon's breath makes ogres bleed; dragon damage +30%.") },
                new[] { T("h_wings", "Wings of Blood", "Crimson wings: +2 air jumps, and holding jump in the air lets you glide."),
                        T("h_quiver", "Deep Quiver", "+15% attack speed.", "atkspd%:15"),
                        T("h_hunter", "Wyrm Hunter", "+40% damage while riding your dragon.", "mountdmg%:40") },
                new[] { T("h_lance", "Bloodstorm Lance", "Every 8th throw hurls a giant lance that explodes along its whole path."),
                        T("h_thirst", "Thirst", "Every kill heals 1.5% of your max health."),
                        T("h_phantom", "Phantom Hunt", "While flying you throw twice as fast and take 25% less damage.") },
                new[] { T("h_vital", "Vital Essence", "+20% max health and +1.5 health regen per second.", "hp%:20,regen:1.5"),
                        T("h_predator", "Apex Predator", "+18% damage.", "dmg%:18"),
                        T("h_skyblood", "Skyblood", "Your dragon's signature deals 35% more damage over a 20% larger area.", "sigdmg%:35,sigarea%:20") },
                new[] { T("h_bloodmoon", "Blood Moon", "Every 30 s a blood moon rises for 8 s: triple javelins, 10% lifesteal and every ogre near you bleeds."),
                        T("h_tempest", "Crimson Tempest", "Javelins split into three smaller javelins on their first hit."),
                        T("h_specter", "Crimson Specter", "Press F (gamepad LB) to become a blood wraith for 10 s: pass through ogres, move 40% faster, triple javelins, heal from every hit and take 60% less damage. 45 s cooldown.") },
            },
            ["pyromancer"] = new[]
            {
                new[] { T("p_focus", "Arcane Focus", "+12% spell damage and +1 mana regen per second.", "spelldmg%:12,manaregen:1"),
                        T("p_ember", "Emberskin", "+3 armor and +10% max health.", "armor:3,hp%:10"),
                        T("p_kinship", "Ember Kinship", "Your dragon deals +20% damage and its cooldowns are 10% shorter.", "dragondmg%:20,dragoncd%:10") },
                new[] { T("p_phoenix", "Ember Rebirth", "Once per stage, death instead revives you at full health in a burst of fire."),
                        T("p_blink", "Blink", "When 5+ ogres surround you, teleport 10 m away leaving an explosion (6 s cooldown)."),
                        T("p_dragonkin", "Dragon Kin", "Dragon damage +35% and its breath reaches 30% farther.") },
                new[] { T("p_pillars", "Pillars of Flame", "Every 12 s a ring of fire pillars erupts around you."),
                        T("p_haste", "Quickcast", "+12% attack speed and spells cool down 10% faster.", "atkspd%:12,spellcd%:10"),
                        T("p_skyfire", "Skyfire", "+40% damage while riding your dragon.", "mountdmg%:40") },
                new[] { T("p_inferno", "Inferno Bolts", "Fire bolts burst 80% wider and pass through the first ogre they hit."),
                        T("p_molten", "Molten Armor", "+6 armor; ogres that hit you in melee catch fire."),
                        T("p_weaver", "Spell Weaver", "Spells cool down 25% faster and cost 20% less mana.") },
                new[] { T("p_ascend", "Ascendance", "+15% damage.", "dmg%:15"),
                        T("p_warding", "Warding Flames", "You take 12% less damage and gain +5% ward chance.", "dmgtaken%:12,block%:5"),
                        T("p_flamecaller", "Flamecaller", "Your dragon's signature deals 35% more damage over a 20% larger area.", "sigdmg%:35,sigarea%:20") },
                new[] { T("p_supernova", "Supernova", "Every 35 s you erupt in a supernova that scorches every ogre within 18 m."),
                        T("p_sunorbs", "Sunfire Orbs", "Three blazing orbs circle you, burning any ogre they touch."),
                        T("p_phoenixform", "Phoenix Form", "Press F (gamepad LB) to become a phoenix for 12 s: fly freely, rain fire on everything below and take 50% less damage. 45 s cooldown.") },
            },
        };

        /// Dragon trees: rows 1, 3 and 5 are shared by every dragon; rows 2, 4 and 6 are the dragon's own.
        static readonly TalentDef[][] DRAGON_SHARED_ROWS =
        {
            new[] { T("dt_fury", "Fury", "Your dragon deals 15% more damage.", "dragondmg%:15"),
                    T("dt_swift", "Swiftwing", "Your dragon's cooldowns are 12% shorter.", "dragoncd%:12"),
                    T("dt_vast", "Vast", "Your dragon grows 15% larger: its breath reaches farther and its dives hit wider.", "size%:15") },
            new[] { T("dt_shelter", "Sheltering Wings", "+1 rank of Sheltering Wing: it shields you from arrows, boulders and harpoons.", "wing:1"),
                    T("dt_talons", "Watchful Talons", "+1 rank of Guardian's Talons: it swoops in and carries you out when you're nearly dead.", "rescue:1"),
                    T("dt_wrath", "Wrathful", "+1 rank of Wrathful Guardian: it scorches anything that hurts you.", "wrath:1") },
            new[] { T("dt_skyrider", "Skyrider", "While riding you take 25% less damage.", "mountdr%:25"),
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
                        T("db_ash", "Ashfall", "Its dives leave the ground burning.") },
                new[] { T("db_inferno", "Greater Inferno", "Inferno Wave hurls 4 more fireballs.", "fireballs:4"),
                        T("db_kiln", "Kiln Heart", "Each breath lasts 40% longer.", "breathtime%:40"),
                        T("db_cinder", "Cinder Guard", "+1 rank of Wrathful Guardian.", "wrath:1") },
                new[] { T("db_azure", "Azure Inferno", "Its breath sets the ground ablaze along its path."),
                        T("db_rekindle", "Rekindle", "Once per stage, a killing blow instead has your dragon pull you from the fire at 40% health."),
                        T("db_sunburst", "Sunburst", "Its dives hit 30% wider and 40% harder.", "diverad%:30,divedmg%:40") }),
            ["red"] = DragonTree(
                new[] { T("dr_volatile", "Volatile", "Breath hits have a 20% chance to burst in a small explosion."),
                        T("dr_crater", "Crater", "Its dives hit 25% wider.", "diverad%:25"),
                        T("dr_hide", "Molten Hide", "While riding you take 20% less damage.", "mountdr%:20") },
                new[] { T("dr_aftershock", "Aftershocks", "Cataclysm sets off 3 more aftershocks.", "aftershocks:3"),
                        T("dr_quake", "Quake", "Its dives stun 1.5 s longer.", "divestun:1.5"),
                        T("dr_fuse", "Short Fuse", "Your dragon's cooldowns are 15% shorter.", "dragoncd%:15") },
                new[] { T("dr_meteor", "Meteor Dive", "Its dives deal 60% more damage.", "divedmg%:60"),
                        T("dr_bulwark", "Living Bulwark", "When you drop below 30% health your dragon erupts around you, blasting everything away (45 s cooldown)."),
                        T("dr_chain", "Chain Reaction", "Ogres your dragon kills explode for 30% of their max health.") }),
            ["black"] = DragonTree(
                new[] { T("dk_leech", "Sanguine Breath", "You heal for 3% of the damage your dragon deals.", "leech%:3"),
                        T("dk_dread", "Dread", "Its weakening is 20% stronger (ogres hit for 60% less).", "weaken:0.2"),
                        T("dk_veil", "Shadow Veil", "While riding you take 15% less damage.", "mountdr%:15") },
                new[] { T("dk_monsoon", "Monsoon", "Blood Rain covers 40% more ground and lasts 2 s longer.", "rainarea%:40,raintime:2"),
                        T("dk_hunger", "Hunger", "Every ogre your dragon kills heals you for 1% of your max health.", "killheal%:1"),
                        T("dk_night", "Nightfall", "Your dragon's cooldowns are 15% shorter.", "dragoncd%:15") },
                new[] { T("dk_tide", "Crimson Tide", "Standing in Blood Rain heals you 2% of your max health per second."),
                        T("dk_pact", "Pact of Wings", "Once per stage, a killing blow instead has your dragon shield you, leaving you at 40% health."),
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
            public int start, max = 5;
            public bool guardian;
        }

        public static readonly DragonCardDef[] DRAGON_CARDS =
        {
            new DragonCardDef { id = "dragonfire", name = "Breath", start = 1, desc = "+damage, +range, breathes and dives more often" },
            new DragonCardDef { id = "ride", name = "Signature", start = 1, desc = "+damage, bigger area, shorter cooldown" },
            new DragonCardDef { id = "d_size", name = "Ancient Growth", desc = "Your dragon grows 12% larger: breath reaches 8% farther and dives hit 8% wider" },
            new DragonCardDef { id = "d_cone", name = "Wide Maw", desc = "Breath cone 6° wider" },
            new DragonCardDef { id = "d_haste", name = "Restless Wings", desc = "Breath, dives and signature cool down 8% faster" },
            new DragonCardDef { id = "d_rescue", name = "Guardian's Talons", guardian = true,
                desc = "When you fall below 35% health on foot, your dragon swoops in and carries you off (90 s cooldown, -10 s per rank)" },
            new DragonCardDef { id = "d_wing", name = "Sheltering Wing", guardian = true,
                desc = "12% chance per rank that your dragon's wing stops an arrow, boulder or harpoon meant for you" },
            new DragonCardDef { id = "d_wrath", name = "Wrathful Guardian", guardian = true,
                desc = "20% chance per rank that your dragon scorches whatever hurts you" },
            new DragonCardDef { id = "d_blue", name = "Everburning", only = "blue", desc = "Its burns deal 30% more damage per rank" },
            new DragonCardDef { id = "d_red", name = "Shatterscale", only = "red", desc = "Its blasts and dives hit 10% wider per rank" },
            new DragonCardDef { id = "d_black", name = "Blood Thirst", only = "black", desc = "You heal for 2% per rank of the damage your dragon deals" },
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
            ["magnet"] = new PowerUpDef { name = "Lodestone", glyph = "U", color = C(0.78f, 0.45f, 1f), desc = "Every gem and orb on the field flies to you; huge pickup range" },
        };

        /// XP: half as many ogres drop a gem, but each gem is worth twice as much.
        public const float GEM_DROP_CHANCE = 0.5f;
        public const float GEM_VALUE_MULT = 2f;

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
            ["harpooner"] = new OgreDef { name = "Ogre Harpooner", size = 1.15f, role = "harpooner" },
            ["shaman"] = new OgreDef { name = "Ogre Shaman", size = 1.1f, role = "shaman" },
        };

        /// Global enemy tuning (health and damage raised 25% twice: 1.3 -> 1.625 -> 2.03 and 1.5 -> 1.875 -> 2.34).
        public const float SPAWN_RATE_MULT = 0.7f;
        public const float ENEMY_STAT_MULT = 2.03125f;
        public const float BOSS_STAT_MULT = 2.34375f;

        /// Spawn weights per wave (four 2-minute waves, the hardest mix last), as (kind, weight) pairs in a fixed order.
        /// Ranged ogres were halved, and one in five of those left is a harpooner that only hunts riders in the air.
        public static readonly (string kind, int weight)[][] WAVE_WEIGHTS =
        {
            new[] { ("runt", 650), ("ogre", 300), ("archer", 20), ("harpooner", 5) },
            new[] { ("runt", 450), ("ogre", 350), ("brute", 80), ("archer", 40), ("harpooner", 10), ("shaman", 20) },
            new[] { ("runt", 350), ("ogre", 300), ("brute", 150), ("hulk", 40), ("archer", 48), ("harpooner", 12), ("shaman", 40) },
            new[] { ("runt", 200), ("ogre", 270), ("brute", 230), ("hulk", 120), ("archer", 48), ("harpooner", 12), ("shaman", 60) },
        };

        /// Ogre Harpooner: wind-up (telegraphed) before each throw, seconds between throws, and what a hit does.
        /// One hit drains flight and slows the dragon; only a second hit while still hooked drags the rider down.
        public const float HARPOON_WINDUP = 1.1f;
        public const float HARPOON_COOLDOWN = 6f;
        public const float HARPOON_RANGE = 42f;
        public const float HARPOON_FLIGHT_DRAIN = 3f;
        public const float HARPOON_HOOK_TIME = 3f;
        public const float HARPOON_SLOW = 0.55f;
        /// After a hook, harpoons in this window only deal damage (so one volley can never drag you down).
        public const float HARPOON_GRACE = 1.5f;
        /// Harpooners take turns: no wind-up starts within this many seconds of another one's.
        public const float HARPOON_STAGGER = 2f;

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

        // ------------------------------------------------------------ stages

        /// The run is three stages. Clearing a stage's final boss opens a portal to the next; the hero keeps everything.
        public class StageDef
        {
            public string name, intro;
            public string[] waves;
            public string miniBoss, finalBoss, islandName;
            /// Enemy health multiplier on top of the stage's time growth (damage grows by its square root).
            public float enemyMult;
            /// Terrain palette.
            public Color grass, grass2, dry, dirt, rock, ash, mud;
            public float scorch;          // how much of the ground is burned (0..1)
            public Color fog, skyTop, skyHorizon, sunColor, skyTint;
            public float fogDensity, sunPitch;
            public Color bossTint;
        }

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
                miniBoss = "Vorna the Thornhide", finalBoss = "Ulgrim, the Forest Tyrant", islandName = "Forest Tyrant's Island", enemyMult = 1.9f,
                grass = C(0.22f, 0.42f, 0.14f), grass2 = C(0.32f, 0.5f, 0.17f), dry = C(0.42f, 0.45f, 0.22f), dirt = C(0.36f, 0.27f, 0.17f),
                rock = C(0.45f, 0.45f, 0.42f), ash = C(0.2f, 0.22f, 0.12f), mud = C(0.24f, 0.22f, 0.14f), scorch = 0.15f,
                fog = C(0.55f, 0.66f, 0.6f), skyTop = C(0.3f, 0.48f, 0.66f), skyHorizon = C(0.72f, 0.78f, 0.7f), sunColor = C(1f, 0.96f, 0.85f),
                skyTint = C(0.75f, 0.95f, 0.85f), fogDensity = 0.004f, sunPitch = -48f, bossTint = C(0.55f, 0.85f, 0.5f) },
            new StageDef { name = "The Sunken Crypts", intro = "A land of buried halls and torchlight. Their warlord waits at the end.",
                waves = new[] { "Crypt Gate", "Halls of Bone", "The Drowned Chapel", "Last Torch" },
                miniBoss = "Mordak the Gravecaller", finalBoss = "Skarnath, the Crypt Warlord", islandName = "Crypt Warlord's Island", enemyMult = 3.0f,
                grass = C(0.31f, 0.3f, 0.25f), grass2 = C(0.38f, 0.34f, 0.27f), dry = C(0.46f, 0.39f, 0.29f), dirt = C(0.33f, 0.27f, 0.21f),
                rock = C(0.42f, 0.4f, 0.42f), ash = C(0.13f, 0.11f, 0.12f), mud = C(0.2f, 0.17f, 0.15f), scorch = 0.35f,
                fog = C(0.4f, 0.37f, 0.48f), skyTop = C(0.22f, 0.2f, 0.34f), skyHorizon = C(0.55f, 0.45f, 0.58f), sunColor = C(0.95f, 0.85f, 1f),
                skyTint = C(0.55f, 0.48f, 0.7f), fogDensity = 0.006f, sunPitch = -24f, bossTint = C(0.65f, 0.55f, 0.85f) },
        };
    }
}
