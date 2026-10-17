using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Skyfall
{
    /// Settings (shared) and three save slots, saved as JSON to Application.persistentDataPath/skyfall_save.json.
    /// Each slot holds its own high scores, owned dragons, last loadout, the run in progress and the permanent
    /// talent trees (hero XP per hero, bond XP per dragon). The active slot is loaded into the static fields below.
    public static class Save
    {
        public const int SLOT_COUNT = 3;

        [Serializable]
        public class RunRecord
        {
            public int score;
            public float time;
            public int kills;
            public int level;
            public int stage = 1;
            public bool won;
            public string date;
        }

        [Serializable]
        public class IntEntry
        {
            public string key;
            public int value;
        }

        /// One armor piece: slot, tier and the stat keys ("bucket:index") it carries.
        [Serializable]
        public class ArmorPiece
        {
            public string slot;
            public int tier;
            public List<string> stats = new List<string>();
        }

        /// The run in progress, written when a stage begins and when you leave mid-run. Continuing restarts the stage you
        /// were on with the hero exactly as saved.
        [Serializable]
        public class RunState
        {
            public bool active;
            public string hero, dragon, spell, passive;
            public int stage = 1;
            public int level = 1;
            public float xp, xpNext = 6f;
            public float hpFrac = 1f, mana = 40f;
            public int kills, bossesSlain;
            public float totalTime, damageDealt, biggestHit;
            /// Meta XP sources so far this run (hero kills, dragon kills, seconds ridden, signatures, rescues).
            public int heroKills, dragonKills, signatures, rescues;
            /// Level-up rerolls left in the current world.
            public int rerolls = Data.REROLLS_PER_WORLD;
            public float rideTime;
            public List<IntEntry> spells = new List<IntEntry>();
            public List<IntEntry> buckets = new List<IntEntry>();
            /// In-run talents only; permanent tree picks live in the slot.
            public List<string> talents = new List<string>();
            public List<ArmorPiece> armor = new List<ArmorPiece>();
            /// Dragons rescued this run (the field keeps its old name so saved runs still load).
            public List<string> hatched = new List<string>();
            public string date;
        }

        /// A permanent talent tree ("hero:knight", "dragon:blue"): unspent XP, rows bought and the pick in each row.
        [Serializable]
        public class TreeState
        {
            public string key;
            public int xp;
            public int earned;
            public int rows;
            public List<string> picks = new List<string>();
        }

        [Serializable]
        public class SlotData
        {
            public bool used;
            public string created;
            public int best_score;
            public float best_time;
            public int best_kills;
            public int best_level;
            public int best_stage;
            public int runs;
            public int wins;
            public int last_score;
            public List<RunRecord> history = new List<RunRecord>();
            public string spell = "frost_bolt";
            public string passive = "lightning_storm";
            public string hero = "knight";
            public string dragon = "blue";
            public List<string> unlocked_dragons = new List<string> { "blue" };
            /// Old saves counted dragon eggs here: read once into rescues_found, never written with a value again.
            public int eggs_found;
            public int rescues_found;
            public RunState run = new RunState();
            public List<TreeState> trees = new List<TreeState>();
        }

        [Serializable]
        class SaveFile
        {
            public int version = 3;
            public float volume = 0.8f;
            public float game_speed = 1f;
            public float mouse_sens = 1f;
            public int slot;
            public List<SlotData> slots = new List<SlotData>();
        }

        /// A version 2 save (one save, no slots), read once to move it into slot 1.
        [Serializable]
        class LegacyFile
        {
            public int best_score;
            public float best_time;
            public int best_kills;
            public int best_level;
            public int best_stage;
            public int runs;
            public int wins;
            public int last_score;
            public List<RunRecord> history;
            public string spell, passive, hero, dragon;
            public List<string> unlocked_dragons;
            public int eggs_found;
            public RunState run;
        }

        static string fileName;

        /// skyfall_save.json, or the name given with -skyfallSaveFile (so automated tests never touch the real save).
        static string PathName
        {
            get
            {
                if (fileName == null)
                {
                    fileName = "skyfall_save.json";
                    var args = Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length - 1; i++)
                        if (args[i] == "-skyfallSaveFile") fileName = args[i + 1];
                }
                return System.IO.Path.Combine(Application.persistentDataPath, fileName);
            }
        }

        public static string FilePath => PathName;

        static bool loaded;
        static SlotData[] slots = NewSlots();
        public static int slot;

        // Settings (shared by every slot).
        public static float volume = 0.8f;
        public static float game_speed = 1f;
        public static float mouse_sens = 1f;

        // The active slot.
        public static int best_score, best_kills, best_level, best_stage, runs, wins, last_score;
        public static float best_time;
        public static string last_spell = "frost_bolt";
        public static string last_passive = "lightning_storm";
        public static string last_hero = "knight";
        public static string last_dragon = "blue";
        public static List<RunRecord> history = new List<RunRecord>();   // recent runs, newest first
        /// Dragons the player owns. Blue is always unlocked; the others are rescued from the stage warlords' captivity.
        public static List<string> unlocked_dragons = new List<string> { "blue" };
        /// How many captive dragons have ever been rescued (the first one is guaranteed). Migrated from the old eggs_found.
        public static int rescues_found;

        /// Rescue count for a loaded slot: the new counter, an old egg count, or (for saves from before either was counted)
        /// every dragon owned beyond blue.
        public static int MigrateRescues(int rescues, int legacyEggs, int unlockedCount) =>
            Mathf.Max(rescues, legacyEggs, unlockedCount - 1, 0);
        /// The run in progress (active = false when there is none).
        public static RunState run = new RunState();
        /// The slot's permanent talent trees.
        public static List<TreeState> trees = new List<TreeState>();

        static SlotData[] NewSlots()
        {
            var s = new SlotData[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; i++) s[i] = new SlotData();
            return s;
        }

        public static bool HasRun => run != null && run.active && Data.HEROES.ContainsKey(run.hero ?? "");

        public static bool IsDragonUnlocked(string id) => id == "blue" || unlocked_dragons.Contains(id);

        /// Unlocks a dragon and saves. Returns true if it was new.
        public static bool UnlockDragon(string id)
        {
            if (IsDragonUnlocked(id)) return false;
            unlocked_dragons.Add(id);
            Write();
            return true;
        }

        public static void ClearRun()
        {
            run = new RunState();
            Write();
        }

        // ---------------------------------------------------------------- slots

        /// What the slot picker shows for a slot (null when the slot is empty).
        public static SlotData SlotInfo(int i)
        {
            if (i == slot) Store(slots[slot]);
            return i >= 0 && i < SLOT_COUNT && slots[i].used ? slots[i] : null;
        }

        /// Makes a slot active (starting a fresh one if it was empty) and saves.
        public static void UseSlot(int i)
        {
            i = Mathf.Clamp(i, 0, SLOT_COUNT - 1);
            if (i != slot) Store(slots[slot]);
            slot = i;
            if (!slots[i].used) slots[i] = new SlotData { used = true, created = DateTime.Now.ToString("yyyy-MM-dd") };
            Load(slots[i]);
            Write();
        }

        /// Wipes a slot. Deleting the active slot leaves it active but empty (a fresh start).
        public static void DeleteSlot(int i)
        {
            if (i < 0 || i >= SLOT_COUNT) return;
            slots[i] = new SlotData();
            if (i == slot)
            {
                slots[i].used = true;
                slots[i].created = DateTime.Now.ToString("yyyy-MM-dd");
                Load(slots[i]);
            }
            Write();
        }

        static void Load(SlotData s)
        {
            best_score = s.best_score;
            best_time = s.best_time;
            best_kills = s.best_kills;
            best_level = s.best_level;
            best_stage = s.best_stage;
            runs = s.runs;
            wins = s.wins;
            last_score = s.last_score;
            history = s.history ?? new List<RunRecord>();
            last_spell = Data.SPELLS.ContainsKey(s.spell ?? "") ? s.spell : "frost_bolt";
            last_passive = Data.PASSIVES.ContainsKey(s.passive ?? "") ? s.passive : "lightning_storm";
            last_hero = Data.HEROES.ContainsKey(s.hero ?? "") ? s.hero : "knight";
            unlocked_dragons = s.unlocked_dragons != null && s.unlocked_dragons.Count > 0 ? new List<string>(s.unlocked_dragons) : new List<string> { "blue" };
            unlocked_dragons.RemoveAll(d => !Data.DRAGONS.ContainsKey(d));
            if (!unlocked_dragons.Contains("blue")) unlocked_dragons.Insert(0, "blue");
            last_dragon = IsDragonUnlocked(s.dragon ?? "") && Data.DRAGONS.ContainsKey(s.dragon ?? "") ? s.dragon : "blue";
            // Old saves counted eggs (eggs_found); every dragon owned beyond blue was rescued or hatched.
            rescues_found = MigrateRescues(s.rescues_found, s.eggs_found, unlocked_dragons.Count);
            run = s.run ?? new RunState();
            trees = s.trees ?? new List<TreeState>();
            trees.RemoveAll(t => t == null || Data.Tree(t.key ?? "") == null);
            foreach (var t in trees) MigrateTree(t);
        }

        /// The hero trees were cut from six rows to four (new talents, no dragon picks): a tree saved with rows or picks the
        /// current tree no longer has is closed and every XP it ever earned is handed back to spend again.
        static void MigrateTree(TreeState t)
        {
            var tree = Data.Tree(t.key);
            bool stale = t.rows > tree.Length;
            for (int r = 0; r < t.rows && r < t.picks.Count && !stale; r++)
            {
                if (string.IsNullOrEmpty(t.picks[r])) continue;
                stale = System.Array.FindIndex(tree[r], x => x.id == t.picks[r]) < 0;
            }
            if (!stale) return;
            t.rows = 0;
            for (int r = 0; r < t.picks.Count; r++) t.picks[r] = "";
            t.xp = Mathf.Max(t.xp, t.earned);
        }

        static void Store(SlotData s)
        {
            s.best_score = best_score;
            s.best_time = best_time;
            s.best_kills = best_kills;
            s.best_level = best_level;
            s.best_stage = best_stage;
            s.runs = runs;
            s.wins = wins;
            s.last_score = last_score;
            s.history = history;
            s.spell = last_spell;
            s.passive = last_passive;
            s.hero = last_hero;
            s.dragon = last_dragon;
            s.unlocked_dragons = unlocked_dragons;
            s.rescues_found = rescues_found;
            s.eggs_found = 0;
            s.run = run ?? new RunState();
            s.trees = trees;
        }

        /// The product name the game shipped under before it was renamed to DragonRider Survivors. Unity keeps saves in
        /// a folder named after the product, so an old save is copied over once.
        const string OLD_PRODUCT_NAME = "DragonFall SkyRiders";

        static void CarryOverOldSave()
        {
            try
            {
                if (File.Exists(PathName)) return;
                var parent = Directory.GetParent(Application.persistentDataPath);
                if (parent == null) return;
                var old = System.IO.Path.Combine(parent.FullName, OLD_PRODUCT_NAME, System.IO.Path.GetFileName(PathName));
                if (!File.Exists(old)) return;
                Directory.CreateDirectory(Application.persistentDataPath);
                File.Copy(old, PathName);
                Debug.Log("Skyfall: carried the save over from " + old);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Skyfall: could not carry over the old save: " + e.Message);
            }
        }

        public static void LoadAll()
        {
            if (loaded) return;
            loaded = true;
            CarryOverOldSave();
            slots = NewSlots();
            slot = 0;
            try
            {
                if (File.Exists(PathName))
                {
                    string json = File.ReadAllText(PathName);
                    var f = JsonUtility.FromJson<SaveFile>(json);
                    if (f != null)
                    {
                        volume = f.volume;
                        game_speed = f.game_speed <= 0f ? 1f : f.game_speed;
                        mouse_sens = f.mouse_sens <= 0f ? 1f : f.mouse_sens;
                        if (f.version >= 3 && f.slots != null && f.slots.Count > 0)
                        {
                            for (int i = 0; i < SLOT_COUNT && i < f.slots.Count; i++) if (f.slots[i] != null) slots[i] = f.slots[i];
                            slot = Mathf.Clamp(f.slot, 0, SLOT_COUNT - 1);
                        }
                        else
                        {
                            // A single save from before slots: it becomes slot 1.
                            var old = JsonUtility.FromJson<LegacyFile>(json);
                            slots[0] = new SlotData
                            {
                                used = true, created = DateTime.Now.ToString("yyyy-MM-dd"),
                                best_score = old.best_score, best_time = old.best_time, best_kills = old.best_kills, best_level = old.best_level,
                                best_stage = old.best_stage, runs = old.runs, wins = old.wins, last_score = old.last_score,
                                history = old.history ?? new List<RunRecord>(), spell = old.spell, passive = old.passive, hero = old.hero, dragon = old.dragon,
                                unlocked_dragons = old.unlocked_dragons, rescues_found = old.eggs_found, run = old.run ?? new RunState(),
                            };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Skyfall: could not read save file: " + e.Message);
            }
            if (!slots[slot].used) slots[slot] = new SlotData { used = true, created = DateTime.Now.ToString("yyyy-MM-dd") };
            Load(slots[slot]);
        }

        public static void Write()
        {
            Store(slots[slot]);
            var f = new SaveFile { volume = volume, game_speed = game_speed, mouse_sens = mouse_sens, slot = slot, slots = new List<SlotData>(slots) };
            try
            {
                File.WriteAllText(PathName, JsonUtility.ToJson(f, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Skyfall: could not write save file: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- permanent talent trees

        public static string HeroTree(string hero) => "hero:" + hero;
        public static string DragonTree(string dragon) => "dragon:" + dragon;

        /// The slot's state for a tree (created on first use).
        public static TreeState Tree(string key)
        {
            var t = trees.Find(x => x.key == key);
            if (t == null)
            {
                t = new TreeState { key = key };
                trees.Add(t);
            }
            while (t.picks.Count < Data.TREE_ROWS) t.picks.Add("");
            return t;
        }

        /// Opens the next row of a tree if there is enough unspent XP. Returns true on success.
        public static bool BuyRow(string key)
        {
            var t = Tree(key);
            int rows = Data.RowCount(key);
            if (DevMenu.FreeTalents && t.rows < rows) { t.rows++; Write(); return true; }   // DEV
            if (t.rows >= rows || t.xp < Data.RowCost(key, t.rows)) return false;
            t.xp -= Data.RowCost(key, t.rows);
            t.rows++;
            Write();
            return true;
        }

        /// Picks a talent in an opened row (swapping is free).
        public static void Pick(string key, int row, string talentId)
        {
            var t = Tree(key);
            if (row < 0 || row >= t.rows) return;
            t.picks[row] = talentId;
            Write();
        }

        /// Free reset (for testing): every row closes and its XP comes back.
        public static void Respec(string key)
        {
            var t = Tree(key);
            for (int r = 0; r < t.rows; r++) t.xp += Data.RowCost(key, r);
            t.rows = 0;
            for (int r = 0; r < t.picks.Count; r++) t.picks[r] = "";
            Write();
        }

        /// The talents picked in a tree's open rows.
        public static List<string> Picks(string key)
        {
            var list = new List<string>();
            var t = Tree(key);
            for (int r = 0; r < t.rows && r < t.picks.Count; r++)
                if (Data.FindTalent(t.picks[r]) != null) list.Add(t.picks[r]);
            return list;
        }

        public static void GrantXp(string key, int amount)
        {
            if (amount <= 0) return;
            var t = Tree(key);
            t.xp += amount;
            t.earned += amount;
        }

        // ---------------------------------------------------------------- runs

        public static int ScoreFor(float timeS, int kills, int level, int stage, bool won)
        {
            return (int)(kills * 10 + timeS * 5f + level * 100 + (stage - 1) * 3000 + (won ? 5000 : 0));
        }

        /// Records a finished run (and clears the run in progress). Returns true if it set a new best score.
        public static bool RecordRun(float timeS, int kills, int level, int stage, bool won)
        {
            int score = ScoreFor(timeS, kills, level, stage, won);
            runs += 1;
            if (won) wins += 1;
            last_score = score;
            bool isBest = score > best_score;
            best_score = Mathf.Max(best_score, score);
            best_time = Mathf.Max(best_time, timeS);
            best_kills = Mathf.Max(best_kills, kills);
            best_level = Mathf.Max(best_level, level);
            best_stage = Mathf.Max(best_stage, stage);
            history.Insert(0, new RunRecord { score = score, time = timeS, kills = kills, level = level, stage = stage, won = won,
                date = DateTime.Now.ToString("yyyy-MM-dd") });
            if (history.Count > 5) history.RemoveRange(5, history.Count - 5);
            run = new RunState();
            Write();
            return isBest;
        }
    }
}
