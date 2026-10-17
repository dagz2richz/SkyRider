using System.Collections.Generic;
using UnityEngine;
using static Skyfall.UiKit;

namespace Skyfall
{
    // DEV MENU — testing tools, opened from the main menu ("Dev Menu" button).
    // To remove it: delete this file, then delete every line marked "// DEV" (Hud.cs, Save.cs, Player.cs).

    /// Testing switches and shortcuts. The toggles are remembered on this computer (PlayerPrefs), not in the save slots.
    public static class DevMenu
    {
        /// Talent tree rows open without spending XP.
        public static bool FreeTalents
        {
            get => PlayerPrefs.GetInt("skyfall_dev_free_talents", 0) == 1;
            set => PlayerPrefs.SetInt("skyfall_dev_free_talents", value ? 1 : 0);
        }

        /// In-run XP gems are worth 10x (fast level-ups to test cards and talents).
        public static bool FastLevels
        {
            get => PlayerPrefs.GetInt("skyfall_dev_fast_levels", 0) == 1;
            set => PlayerPrefs.SetInt("skyfall_dev_fast_levels", value ? 1 : 0);
        }

        static IEnumerable<string> AllTrees()
        {
            foreach (var h in Data.HERO_ORDER) yield return Save.HeroTree(h);
            foreach (var d in Data.DRAGON_ORDER) yield return Save.DragonTree(d);
        }

        /// Opens every row of every hero and dragon tree (picks stay as they are).
        public static void OpenAllRows()
        {
            foreach (var key in AllTrees()) Save.Tree(key).rows = Data.RowCount(key);
            Save.Write();
        }

        public static void GrantXpToAll(int amount)
        {
            foreach (var key in AllTrees()) Save.GrantXp(key, amount);
            Save.Write();
        }

        public static void ResetAllTrees()
        {
            foreach (var key in AllTrees()) Save.Respec(key);
        }

        public static void UnlockAllDragons()
        {
            foreach (var d in Data.DRAGON_ORDER) Save.UnlockDragon(d);
        }
    }

    public partial class Hud
    {
        void DrawDevMenu()
        {
            Overlay(0.75f);
            const float w = 360f;
            Centered(() =>
            {
                Label("Dev Menu", 44, new Color(0.6f, 1f, 0.6f), 10, TextAnchor.UpperCenter, 0, false, true);
                Label($"Testing tools  ·  Save Slot {Save.slot + 1}", 15, M.Darken(PARCHMENT, 0.2f), 3, TextAnchor.UpperCenter);
                Space(18);
                CenterRow(() =>
                {
                    GUILayout.BeginVertical(GUILayout.Width(w));
                    Button(DevMenu.FreeTalents ? "Free talent rows: ON" : "Free talent rows: OFF", 18, w, true, () => DevMenu.FreeTalents = !DevMenu.FreeTalents);
                    Label("Open tree rows without spending XP", 13, M.Darken(PARCHMENT, 0.3f), 3, TextAnchor.UpperCenter, w);
                    Space(8);
                    Button(DevMenu.FastLevels ? "10x run XP: ON" : "10x run XP: OFF", 18, w, true, () => DevMenu.FastLevels = !DevMenu.FastLevels);
                    Label("Level up fast during runs", 13, M.Darken(PARCHMENT, 0.3f), 3, TextAnchor.UpperCenter, w);
                    Space(8);
                    Button("Open every tree row", 18, w, true, DevMenu.OpenAllRows);
                    Space(8);
                    Button("+1000 XP to every tree", 18, w, true, () => DevMenu.GrantXpToAll(1000));
                    Space(8);
                    Button("Unlock every dragon", 18, w, true, () => { DevMenu.UnlockAllDragons(); RefreshPreview(); });
                    Space(8);
                    Button("Reset every tree (refund)", 18, w, true, DevMenu.ResetAllTrees);
                    GUILayout.EndVertical();
                });
                Space(18);
                CenterRow(() =>
                {
                    Button("Talent Trees", 20, 0, true, () => ShowTalents(Menu.Dev));
                    Space(16);
                    Button("Back", 22, 0, true, ShowMainMenu);
                });
            });
        }
    }
}
