using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Skyfall
{
    /// High scores, settings and the last loadout, saved as JSON to Application.persistentDataPath/skyfall_save.json.
    public static class Save
    {
        [Serializable]
        public class RunRecord
        {
            public int score;
            public float time;
            public int kills;
            public int level;
            public bool won;
            public string date;
        }

        [Serializable]
        class SaveFile
        {
            public float volume = 0.8f;
            public int best_score;
            public float best_time;
            public int best_kills;
            public int best_level;
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
        }

        static string PathName => System.IO.Path.Combine(Application.persistentDataPath, "skyfall_save.json");

        static bool loaded;
        public static float volume = 0.8f;
        public static int best_score, best_kills, best_level, runs, wins, last_score;
        public static float best_time;
        public static string last_spell = "frost_bolt";
        public static string last_passive = "lightning_storm";
        public static string last_hero = "knight";
        public static string last_dragon = "blue";
        public static float game_speed = 1f;
        public static float mouse_sens = 1f;
        public static List<RunRecord> history = new List<RunRecord>();   // recent runs, newest first

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
                runs = f.runs;
                wins = f.wins;
                last_score = f.last_score;
                history = f.history ?? new List<RunRecord>();
                last_spell = f.spell;
                last_passive = f.passive;
                last_hero = f.hero;
                last_dragon = f.dragon;
                game_speed = f.game_speed <= 0f ? 1f : f.game_speed;
                mouse_sens = f.mouse_sens <= 0f ? 1f : f.mouse_sens;
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
                best_level = best_level, runs = runs, wins = wins, last_score = last_score, history = history,
                spell = last_spell, passive = last_passive, hero = last_hero, dragon = last_dragon,
                game_speed = game_speed, mouse_sens = mouse_sens,
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

        public static int ScoreFor(float timeS, int kills, int level, bool won)
        {
            return (int)(kills * 10 + timeS * 5f + level * 100 + (won ? 5000 : 0));
        }

        /// Records a finished run. Returns true if it set a new best score.
        public static bool RecordRun(float timeS, int kills, int level, bool won)
        {
            int score = ScoreFor(timeS, kills, level, won);
            runs += 1;
            if (won) wins += 1;
            last_score = score;
            bool isBest = score > best_score;
            best_score = Mathf.Max(best_score, score);
            best_time = Mathf.Max(best_time, timeS);
            best_kills = Mathf.Max(best_kills, kills);
            best_level = Mathf.Max(best_level, level);
            history.Insert(0, new RunRecord { score = score, time = timeS, kills = kills, level = level, won = won,
                date = DateTime.Now.ToString("yyyy-MM-dd") });
            if (history.Count > 5) history.RemoveRange(5, history.Count - 5);
            Write();
            return isBest;
        }
    }
}
