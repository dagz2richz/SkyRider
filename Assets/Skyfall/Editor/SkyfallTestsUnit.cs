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

            // Flight time: 10 s in worlds 1-2, 60 s in world 3.
            Check(Mathf.Approximately(Data.FlightTimeFor(1), 10f), "world 1 flight time is 10 s");
            Check(Mathf.Approximately(Data.FlightTimeFor(2), 10f), "world 2 flight time is 10 s");
            Check(Mathf.Approximately(Data.FlightTimeFor(3), 60f), "world 3 flight time is 60 s");

            // Talent cost curve: faster early, steeper late.
            var c = Data.TREE_ROW_COST;
            bool rising = true, convex = true;
            for (int i = 1; i < c.Length; i++) if (c[i] <= c[i - 1]) rising = false;
            for (int i = 2; i < c.Length; i++) if (c[i] - c[i - 1] <= c[i - 1] - c[i - 2]) convex = false;
            Check(c.Length == Data.TREE_ROWS, "one cost per tree row");
            Check(rising, "row costs rise every row");
            Check(convex, "each row's step up is bigger than the last (steepening)");
            Check(c[0] < 100 && c[1] < 200 && c[2] < 350, "first three rows are cheaper than the old curve (100, 200, 350)");
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
