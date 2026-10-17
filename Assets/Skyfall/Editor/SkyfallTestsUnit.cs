using UnityEditor;
using UnityEngine;

namespace Skyfall.EditorTools
{
    /// Edit-mode checks of the rules (no play mode needed).
    /// Usage: Unity.exe -batchmode -quit -projectPath ... -executeMethod Skyfall.EditorTools.SkyfallTests.RunUnit
    public static partial class SkyfallTests
    {
        static int unitFailures;

        static void Check(bool ok, string what)
        {
            if (!ok) unitFailures++;
            Debug.Log($"SKYFALL_TEST unit {(ok ? "PASS" : "FAIL")}: {what}");
        }

        public static void RunUnit()
        {
            unitFailures = 0;

            // Flight: the young dragon (world 2) and the adult (world 3) both fly; the hatchling does not.
            Check(!Data.CanFlyAtStage(1), "world 1 hatchling cannot fly");
            Check(Data.CanFlyAtStage(2), "world 2 young dragon can fly");
            Check(Data.CanFlyAtStage(3), "world 3 adult dragon can fly");
            Check(Data.DRAGON_AGE.Length == Data.STAGE_COUNT, "one dragon age per world");

            // Flight never runs out in worlds 2 and 3 (world 1 cannot fly at all); Q locks for 5 s after E.
            Check(!Data.UnlimitedFlightAtStage(1), "world 1 has no flight");
            Check(Data.UnlimitedFlightAtStage(2), "world 2 flight is unlimited");
            Check(Data.UnlimitedFlightAtStage(3), "world 3 flight is unlimited");
            Check(Mathf.Approximately(Data.Q_LOCKOUT_AFTER_E, 5f), "Q locks for 5 s after E");

            // Talent cost curve: faster early, steeper late.
            var c = Data.TREE_ROW_COST;
            bool rising = true, convex = true;
            for (int i = 1; i < c.Length; i++) if (c[i] <= c[i - 1]) rising = false;
            for (int i = 2; i < c.Length; i++) if (c[i] - c[i - 1] <= c[i - 1] - c[i - 2]) convex = false;
            Check(c.Length == Data.TREE_ROWS, "one cost per tree row");
            Check(rising, "row costs rise every row");
            Check(convex, "each row's step up is bigger than the last (steepening)");
            Check(c[0] < 100 && c[1] < 200 && c[2] < 350, "first three rows are cheaper than the old curve (100, 200, 350)");

            // Hero trees: four rows (the last the capstone), no dragon talents, and a rising cost curve of their own.
            var hc = Data.HERO_ROW_COST;
            bool heroRising = true;
            for (int i = 1; i < hc.Length; i++) if (hc[i] <= hc[i - 1]) heroRising = false;
            Check(heroRising, "hero row costs rise every row");
            foreach (var hero in Data.HERO_ORDER)
            {
                var tree = Data.Tree("hero:" + hero);
                Check(tree.Length == 4 && hc.Length == 4, $"{hero} tree has four rows");
                bool noDragon = true;
                foreach (var row in tree)
                {
                    Check(row.Length == 3, $"{hero} rows offer three talents");
                    foreach (var t in row)
                        if (t.desc.ToLower().Contains("your dragon") || t.mods.ContainsKey("dragondmg%") || t.mods.ContainsKey("mountdmg%")) noDragon = false;
                }
                Check(noDragon, $"{hero} tree has no dragon talents");
                Check(Data.RowCost("hero:" + hero, 3) == hc[3], $"{hero} capstone uses the hero cost curve");
            }
            Check(Data.RowCount("dragon:blue") == 6 && Data.RowCost("dragon:blue", 5) == c[5], "dragon trees keep six rows and their own curve");
            // Every spell level after the evolution and every passive level has its own unique text.
            foreach (var sp in Data.SPELLS.Values)
                for (int lv = Data.EVOLVE_LEVEL; lv < Data.SPELL_FINAL_LEVEL - 1; lv++)
                    Check(sp.ups[lv].StartsWith("UNIQUE"), $"{sp.name} level {lv + 1} is unique");
            foreach (var ps in Data.PASSIVES.Values)
                for (int lv = 1; lv < Data.MAX_LEVEL; lv++)
                    Check(ps.ups[lv].StartsWith("UNIQUE"), $"{ps.name} level {lv + 1} is unique");
            Check(c[5] > 900 && c[5] >= 20 * c[0], "the last row is far dearer than the first");

            // Save migration: eggs_found becomes the rescue counter; old saves infer it from the dragons owned.
            Check(Save.MigrateRescues(0, 3, 1) == 3, "an old eggs_found of 3 becomes 3 rescues");
            Check(Save.MigrateRescues(0, 0, 3) == 2, "no counter but 3 dragons owned means 2 rescues");
            Check(Save.MigrateRescues(2, 0, 1) == 2, "the new counter is kept");
            Check(Save.MigrateRescues(0, 0, 1) == 0, "a fresh save has no rescues (the first one is guaranteed)");

            // Gameplay keys (Q, E, F, jump, descend) do nothing on any menu, pause or other screen.
            Check(GameInput.IsBlocked(false, false, false), "keys blocked before a run starts (title screen)");
            Check(GameInput.IsBlocked(true, true, false), "keys blocked while paused (level-up, pause, stats)");
            Check(GameInput.IsBlocked(true, false, true), "keys blocked while a menu or still is open");
            Check(!GameInput.IsBlocked(true, false, false), "keys work in active play");

            // The feast captions name the warlord and the new age.
            Check(string.Format(Data.FEAST_CAPTION[0], "Gorrak", Data.DRAGON_AGE[1]).Contains("Young Dragon"), "world 1 caption names the Young Dragon");
            Check(string.Format(Data.FEAST_CAPTION[1], "Ulgrim", Data.DRAGON_AGE[2]).Contains("an Adult Dragon"), "world 2 caption names the Adult Dragon");

            Debug.Log($"SKYFALL_TEST unit done: {unitFailures} failures");
            if (Application.isBatchMode) EditorApplication.Exit(unitFailures == 0 ? 0 : 1);
        }
    }
}
