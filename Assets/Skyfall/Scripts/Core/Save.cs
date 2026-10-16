using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Skyfall
{
    /// High scores, settings, owned dragons, the last loadout and the run in progress, saved as JSON to
    /// Application.persistentDataPath/skyfall_save.json.
    public static class Save
    {
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
            public List<IntEntry> spells = new List<IntEntry>();
            public List<IntEntry> buckets = new List<IntEntry>();
            public List<string> talents = new List<string>();
            public List<ArmorPiece> armor = new List<ArmorPiece>();
            public List<string> hatched = new List<string>();
            public string date;
        }

        [Serializable]
        class SaveFile
        {
            public int version = 2;
            public float volume = 0.8f;
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
            public float game_speed = 1f;
            public float mouse_sens = 1f;
            public List<string> unlocked_dragons = new List<string> { "blue" };
            public int eggs_found;
            public RunState run = new RunState();
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
        public static float volume = 0.8f;
        public static int best_score, best_kills, best_level, best_stage, runs, wins, last_score;
        public static float best_time;
        public static string last_spell = "frost_bolt";
        public static string last_passive = "lightning_storm";
        public static string last_hero = "knight";
        public static string last_dragon = "blue";
        public static float game_speed = 1f;
        public static float mouse_sens = 1f;
        public static List<RunRecord> history = new List<RunRecord>();   // recent runs, newest first
        /// Dragons the player owns. Blue is always unlocked; the others hatch from eggs dropped by stage bosses.
        public static List<string> unlocked_dragons = new List<string> { "blue" };
        /// How many eggs have ever dropped (the first one is guaranteed).
        public static int eggs_found;
        /// The run in progress (active = false when there is none).
        public static RunState run = new RunState();

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

        public static void LoadAll()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(PathName)) return;
                var f = JsonUtility.FromJson<SaveFile>(File.ReadAllText(PathName));
                if (f == null) return;
                volume = f.volume;
                best_score = f.best_score;
                best_time = f.best_time;
                best_kills = f.best_kills;
                best_level = f.best_level;
                best_stage = f.best_stage;
                runs = f.runs;
                wins = f.wins;
                last_score = f.last_score;
                history = f.history ?? new List<RunRecord>();
                last_spell = f.spell;
                last_passive = f.passive;
                last_hero = f.hero;
                last_dragon = f.dragon;
                unlocked_dragons = f.unlocked_dragons != null && f.unlocked_dragons.Count > 0 ? new List<string>(f.unlocked_dragons) : new List<string> { "blue" };
                unlocked_dragons.RemoveAll(d => !Data.DRAGONS.ContainsKey(d));
                if (!unlocked_dragons.Contains("blue")) unlocked_dragons.Insert(0, "blue");
                if (!IsDragonUnlocked(last_dragon)) last_dragon = "blue";
                game_speed = f.game_speed <= 0f ? 1f : f.game_speed;
                mouse_sens = f.mouse_sens <= 0f ? 1f : f.mouse_sens;
                // Saves from before eggs were counted: every dragon owned beyond blue came from an egg.
                eggs_found = Mathf.Max(f.eggs_found, unlocked_dragons.Count - 1);
                run = f.run ?? new RunState();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Skyfall: could not read save file: " + e.Message);
            }
        }

        public static void Write()
        {
            var f = new SaveFile
            {
                volume = volume, best_score = best_score, best_time = best_time, best_kills = best_kills,
                best_level = best_level, best_stage = best_stage, runs = runs, wins = wins, last_score = last_score, history = history,
                spell = last_spell, passive = last_passive, hero = last_hero, dragon = last_dragon,
                game_speed = game_speed, mouse_sens = mouse_sens, unlocked_dragons = unlocked_dragons,
                eggs_found = eggs_found, run = run ?? new RunState(),
            };
            try
            {
                File.WriteAllText(PathName, JsonUtility.ToJson(f, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Skyfall: could not write save file: " + e.Message);
            }
        }

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
