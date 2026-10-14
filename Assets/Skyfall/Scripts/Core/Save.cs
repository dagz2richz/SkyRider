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
            public List<string> unlocked_dragons = new List<string> { "blue" };
            public int gold;
            public int sword_level = 1, javelin_level = 1, bow_level = 1;
            public int armor_helm, armor_chest, armor_gloves, armor_legs, armor_boots;
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
        /// Dragons the player owns. Blue is always unlocked; the others come from the Mountain King's eggs.
        public static List<string> unlocked_dragons = new List<string> { "blue" };

        /// Gold banked from finished runs, spent in the shop.
        public static int gold;
        public static int sword_level = 1, javelin_level = 1, bow_level = 1;
        public static int armor_helm, armor_chest, armor_gloves, armor_legs, armor_boots;

        /// Shop level (1..5) of a weapon id from Data.WEAPONS.
        public static int WeaponLevel(string id)
        {
            switch (id)
            {
                case "sword": return sword_level;
                case "blood_lance": return javelin_level;
                default: return bow_level;
            }
        }

        public static void SetWeaponLevel(string id, int lvl)
        {
            lvl = Mathf.Clamp(lvl, 1, Data.MAX_LEVEL);
            switch (id)
            {
                case "sword": sword_level = lvl; break;
                case "blood_lance": javelin_level = lvl; break;
                default: bow_level = lvl; break;
            }
        }

        /// Armor tier (0..5) of a slot from Data.ARMOR_ORDER.
        public static int ArmorTier(string slot)
        {
            switch (slot)
            {
                case "helm": return armor_helm;
                case "chest": return armor_chest;
                case "gloves": return armor_gloves;
                case "legs": return armor_legs;
                default: return armor_boots;
            }
        }

        public static void SetArmorTier(string slot, int tier)
        {
            tier = Mathf.Clamp(tier, 0, Data.MAX_ARMOR_TIER);
            switch (slot)
            {
                case "helm": armor_helm = tier; break;
                case "chest": armor_chest = tier; break;
                case "gloves": armor_gloves = tier; break;
                case "legs": armor_legs = tier; break;
                default: armor_boots = tier; break;
            }
        }

        /// Gold needed for the next level of a weapon, or -1 at the maximum.
        public static int WeaponUpgradeCost(string id)
        {
            int lvl = WeaponLevel(id);
            return lvl >= Data.MAX_LEVEL ? -1 : Data.WEAPON_COST[lvl - 1];
        }

        /// Gold needed for the next armor tier of a slot, or -1 at the maximum.
        public static int ArmorUpgradeCost(string slot)
        {
            int t = ArmorTier(slot);
            return t >= Data.MAX_ARMOR_TIER ? -1 : Data.ARMOR_COST[t];
        }

        /// Buys the next weapon level if affordable; saves immediately.
        public static bool BuyWeapon(string id)
        {
            int cost = WeaponUpgradeCost(id);
            if (cost < 0 || gold < cost) return false;
            gold -= cost;
            SetWeaponLevel(id, WeaponLevel(id) + 1);
            Write();
            return true;
        }

        /// Buys the next armor tier if affordable; saves immediately.
        public static bool BuyArmor(string slot)
        {
            int cost = ArmorUpgradeCost(slot);
            if (cost < 0 || gold < cost) return false;
            gold -= cost;
            SetArmorTier(slot, ArmorTier(slot) + 1);
            Write();
            return true;
        }

        public static bool IsDragonUnlocked(string id) => id == "blue" || unlocked_dragons.Contains(id);

        /// Unlocks a dragon and saves. Returns true if it was new.
        public static bool UnlockDragon(string id)
        {
            if (IsDragonUnlocked(id)) return false;
            unlocked_dragons.Add(id);
            Write();
            return true;
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
                runs = f.runs;
                wins = f.wins;
                last_score = f.last_score;
                history = f.history ?? new List<RunRecord>();
                last_spell = f.spell;
                last_passive = f.passive;
                last_hero = f.hero;
                last_dragon = f.dragon;
                unlocked_dragons = f.unlocked_dragons != null && f.unlocked_dragons.Count > 0 ? new List<string>(f.unlocked_dragons) : new List<string> { "blue" };
                if (!unlocked_dragons.Contains("blue")) unlocked_dragons.Insert(0, "blue");
                if (!IsDragonUnlocked(last_dragon)) last_dragon = "blue";
                game_speed = f.game_speed <= 0f ? 1f : f.game_speed;
                mouse_sens = f.mouse_sens <= 0f ? 1f : f.mouse_sens;
                gold = Mathf.Max(0, f.gold);
                sword_level = Mathf.Clamp(f.sword_level, 1, Data.MAX_LEVEL);
                javelin_level = Mathf.Clamp(f.javelin_level, 1, Data.MAX_LEVEL);
                bow_level = Mathf.Clamp(f.bow_level, 1, Data.MAX_LEVEL);
                armor_helm = Mathf.Clamp(f.armor_helm, 0, Data.MAX_ARMOR_TIER);
                armor_chest = Mathf.Clamp(f.armor_chest, 0, Data.MAX_ARMOR_TIER);
                armor_gloves = Mathf.Clamp(f.armor_gloves, 0, Data.MAX_ARMOR_TIER);
                armor_legs = Mathf.Clamp(f.armor_legs, 0, Data.MAX_ARMOR_TIER);
                armor_boots = Mathf.Clamp(f.armor_boots, 0, Data.MAX_ARMOR_TIER);
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
                game_speed = game_speed, mouse_sens = mouse_sens, unlocked_dragons = unlocked_dragons,
                gold = gold, sword_level = sword_level, javelin_level = javelin_level, bow_level = bow_level,
                armor_helm = armor_helm, armor_chest = armor_chest, armor_gloves = armor_gloves, armor_legs = armor_legs, armor_boots = armor_boots,
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
