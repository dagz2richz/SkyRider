using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Skyfall.EditorTools
{
    /// -skyfallFeatureTest gamepad (use with -skyfallSaveFile): a virtual gamepad drives the hero in play mode, and the menu focus
    /// logic (UiNav) is checked with fake rects, since OnGUI does not run in batch mode.
    public static partial class SkyfallTests
    {
        static Gamepad pad;
        static Vector3 padPos0;
        static float padYaw0, padPitch0, padDist0;
        static readonly List<string> navLog = new List<string>();

        static void PadState(GamepadState s) => InputSystem.QueueStateEvent(pad, s);

        static GamepadState PadBtn(GamepadButton b) => new GamepadState().WithButton(b);

        static void P(string msg) => Debug.Log("SKYFALL_TEST pad " + msg);

        static void GamepadTest(Game game)
        {
            var pl = game.player;
            int f = frame;
            if (f == 1)
            {
                var mf = typeof(Hud).GetField("menu", NP);   // the test harness starts the run without the loadout screen, which would normally clear the menu
                mf.SetValue(game.hud, System.Enum.ToObject(mf.FieldType, 0));
            }
            if (f == 2)
            {
                pad = InputSystem.AddDevice<Gamepad>();
                P($"virtual gamepad added: current={(Gamepad.current != null)} hero={pl.heroId}");
                Tele(pl, OnGround(game, Vector3.zero));
                pl.camYaw = 0f;
                pl.camPitch = 0.32f;
            }
            if (pad == null) return;

            // ---- A: jump
            if (f == 10) { P($"jump: onGround={(bool)typeof(Player).GetField("onGround", NP).GetValue(pl)} y0={pl.Pos.y:0.00}"); PadState(PadBtn(GamepadButton.South)); }
            if (f == 11) PadState(new GamepadState());
            if (f == 20) P($"jump: vy={pl.velocity.y:0.00} (after A) airborne={!(bool)typeof(Player).GetField("onGround", NP).GetValue(pl)}");

            // ---- left stick: move. Full stick forward for 60 frames.
            if (f == 120) { padPos0 = pl.Pos; PadState(new GamepadState { leftStick = new Vector2(0f, 1f) }); }
            if (f == 180)
            {
                var d = pl.Pos - padPos0; d.y = 0;
                P($"left stick forward 1s: moved {d.magnitude:0.00} m (expected ~7-9)");
                PadState(new GamepadState { leftStick = new Vector2(0.1f, 0.1f) });   // inside the deadzone
            }
            if (f == 200) padPos0 = pl.Pos;
            if (f == 230)
            {
                var d = pl.Pos - padPos0; d.y = 0;
                P($"left stick inside deadzone (0.14): moved {d.magnitude:0.00} m (expected ~0)");
                PadState(new GamepadState());
            }

            // ---- right stick: look
            if (f == 240) { padYaw0 = pl.camYaw; padPitch0 = pl.camPitch; PadState(new GamepadState { rightStick = new Vector2(1f, 0f) }); }
            if (f == 300) { P($"right stick right 1s: yaw change {pl.camYaw - padYaw0:0.000} rad (expected ~2.8*sens {Save.mouse_sens:0.00})"); PadState(new GamepadState { rightStick = new Vector2(0f, 1f) }); padPitch0 = pl.camPitch; }
            if (f == 330) { P($"right stick up 0.5s: pitch change {pl.camPitch - padPitch0:0.000} rad (expected negative, ~-0.9)"); PadState(new GamepadState()); }

            // ---- D-pad zoom + R3 reset
            if (f == 340) { padDist0 = pl.camDistTarget; PadState(PadBtn(GamepadButton.DpadUp)); }
            if (f == 342) PadState(new GamepadState());
            if (f == 345) { P($"dpad up: camDist {padDist0:0.0} -> {pl.camDistTarget:0.0} (expected smaller)"); PadState(PadBtn(GamepadButton.DpadDown)); }
            if (f == 347) PadState(new GamepadState());
            if (f == 350) { P($"dpad down: camDist -> {pl.camDistTarget:0.0} (expected back up)"); PadState(PadBtn(GamepadButton.RightStick)); }
            if (f == 352) PadState(new GamepadState());
            if (f == 355) P($"right stick click: camDist -> {pl.camDistTarget:0.0} (expected 9.0)");

            // ---- Y: take flight (and again to drop)
            if (f == 360) { P($"before Y: flying={pl.flying} stamina={pl.flightStamina:0.0}"); PadState(PadBtn(GamepadButton.North)); }
            if (f == 362) PadState(new GamepadState());
            if (f == 380) P($"after Y: flying={pl.flying} y={pl.Pos.y:0.0}");

            // ---- A held while flying climbs; LT / B dives
            if (f == 381) PadState(PadBtn(GamepadButton.South));
            if (f == 400) { P($"A held while flying: vy={pl.velocity.y:0.0} (expected ~+10)"); PadState(new GamepadState { leftTrigger = 1f }); }
            if (f == 420) { P($"LT held 1/3 s while flying: vy={pl.velocity.y:0.0} (expected about -5: diving at 45 m/s^2 from +10)"); PadState(PadBtn(GamepadButton.East)); }
            if (f == 430) { P($"B held 1/6 s while flying: vy={pl.velocity.y:0.0} (expected below -5: still diving)"); PadState(new GamepadState()); }

            // ---- Y again: drop off
            if (f == 440) PadState(PadBtn(GamepadButton.North));
            if (f == 442) PadState(new GamepadState());
            if (f == 450) P($"after second Y: flying={pl.flying} (expected False)");

            // ---- RB / X: dragon signature (on the ground)
            if (f == 500) { P($"before RB: rideCd frac={game.pet.RideFrac:0.00}"); PadState(PadBtn(GamepadButton.RightShoulder)); }
            if (f == 502) PadState(new GamepadState());
            if (f == 510) P($"after RB: rideCd frac={game.pet.RideFrac:0.00} (expected > 0)");
            if (f == 520) typeof(DragonPet).GetField("rideCd", NP).SetValue(game.pet, 0f);
            if (f == 530) PadState(PadBtn(GamepadButton.West));
            if (f == 532) PadState(new GamepadState());
            if (f == 540) P($"after X: rideCd frac={game.pet.RideFrac:0.00} (expected > 0)");

            // ---- Start pauses, Start resumes; Select opens stats, Select closes
            if (f == 560) PadState(PadBtn(GamepadButton.Start));
            if (f == 562) PadState(new GamepadState());
            if (f == 570) { P($"after Start: paused={game.Paused} timeScale={Time.timeScale}"); PadState(PadBtn(GamepadButton.Start)); }
            if (f == 572) PadState(new GamepadState());
            if (f == 580) { P($"after Start again: paused={game.Paused}"); PadState(PadBtn(GamepadButton.Select)); }
            if (f == 582) PadState(new GamepadState());
            if (f == 590) { P($"after Select: paused={game.Paused} statsOpen={typeof(Hud).GetField("statsOpen", NP).GetValue(game.hud)}"); PadState(PadBtn(GamepadButton.Select)); }
            if (f == 592) PadState(new GamepadState());
            if (f == 600) P($"after Select again: paused={game.Paused}");

            // ---- menu navigation driven by the real gamepad through UiNav.Tick (fake rects, fake screen)
            if (f >= 620) NavScript(game, f);
        }

        static void Reg(string text, float x, float y, float w, float h, System.Action act = null, bool enabled = true, System.Action<int> adjust = null)
        {
            UiNav.Layer = "t";
            UiNav.Register(text, new Rect(x, y, w, h), enabled, act, adjust);
        }

        static int navPlays;

        /// Fake main-menu column: Play, Shop, Settings, Controls, Quit.
        static void FrameColumn(System.Action[] acts)
        {
            UiNav.BeginFrame("t");
            string[] names = { "Play", "Shop", "Settings", "Controls", "Quit" };
            for (int i = 0; i < 5; i++) Reg(names[i], 100, 100 + i * 70, 300, 50, acts != null ? acts[i] : null);
            UiNav.EndFrame();
        }

        static void NavScript(Game game, int f)
        {
            int k = f - 620;
            if (k == 0)
            {
                // Pure picking tests.
                UiNav.Reset();
                Hud.TestTop = "t";
                UiNav.DefaultKeyFor = s => "Play#0";
                FrameColumn(null);
                P($"nav default focus={UiNav.FocusKey} (expected Play#0)");
                var list = UiNav.Entries;
                P($"pick Down from Play={list[UiNav.Pick(list, 0, NavDir.Down, true)].key} (expected Shop#0)");
                P($"pick Up from Play (wrap)={list[UiNav.Pick(list, 0, NavDir.Up, true)].key} (expected Quit#0)");
                P($"pick Up from Play (no wrap)={UiNav.Pick(list, 0, NavDir.Up, false)} (expected -1)");
                P($"pick Down from Quit (wrap)={list[UiNav.Pick(list, 4, NavDir.Down, true)].key} (expected Play#0)");
                P($"pick Right from Play={UiNav.Pick(list, 0, NavDir.Right, true)} (expected -1)");

                // Card row with a Back button underneath, and a tab row above.
                UiNav.BeginFrame("t");
                Reg("Weapons", 500, 20, 180, 50);
                Reg("Armor", 700, 20, 180, 50);
                Reg("c0", 20, 100, 400, 300);
                Reg("c1", 450, 100, 400, 300);
                Reg("c2", 880, 100, 400, 300);
                Reg("Back", 560, 450, 200, 50);
                UiNav.EndFrame();
                list = UiNav.Entries;
                int c0 = 2, c1 = 3, c2 = 4, back = 5, tabW = 0;
                P($"cards: Down c0={list[UiNav.Pick(list, c0, NavDir.Down, true)].key} (Back); Up Back={list[UiNav.Pick(list, back, NavDir.Up, true)].key} (c1); " +
                  $"Right c0={list[UiNav.Pick(list, c0, NavDir.Right, true)].key} (c1); Right c2 wrap={list[UiNav.Pick(list, c2, NavDir.Right, true)].key} (c0); " +
                  $"Left c0 wrap={list[UiNav.Pick(list, c0, NavDir.Left, true)].key} (c2); Up c1={list[UiNav.Pick(list, c1, NavDir.Up, true)].key} (Weapons); " +
                  $"Right Weapons={list[UiNav.Pick(list, tabW, NavDir.Right, true)].key} (Armor)");

                // Slider rows: left/right adjusts, up/down moves; focus survives a re-registration.
                int val = 5;
                UiNav.BeginFrame("t");
                Reg("Volume", 100, 100, 600, 40, null, true, d => val += d);
                Reg("Back", 100, 200, 200, 50);
                UiNav.EndFrame();
                UiNav.FocusKey = "Volume#0";
                UiNav.Move(NavDir.Right); UiNav.Move(NavDir.Right); UiNav.Move(NavDir.Left);
                P($"slider adjust: val={val} (expected 6)");
                UiNav.Move(NavDir.Down);
                P($"slider Down -> focus={UiNav.FocusKey} (expected Back#0)");
                UiNav.BeginFrame("t");
                Reg("Volume", 100, 100, 600, 40, null, true, d => val += d);
                Reg("Back", 100, 200, 200, 50);
                UiNav.EndFrame();
                P($"focus kept across repaint: {UiNav.FocusKey} (expected Back#0)");

                // Activation: runs the action once, drops the stale list, ignores disabled entries.
                int fired = 0, firedDisabled = 0;
                UiNav.BeginFrame("t");
                Reg("Go", 0, 0, 100, 40, () => fired++);
                Reg("Nope", 0, 60, 100, 40, () => firedDisabled++, false);
                UiNav.EndFrame();
                UiNav.FocusKey = "Go#0";
                bool a = UiNav.Activate();
                P($"activate Go: ran={a} fired={fired} staleListCleared={UiNav.Entries.Count == 0} (expected True 1 True)");
                UiNav.BeginFrame("t");
                Reg("Go", 0, 0, 100, 40, () => fired++);
                Reg("Nope", 0, 60, 100, 40, () => firedDisabled++, false);
                UiNav.EndFrame();
                UiNav.FocusKey = "Nope#0";
                P($"activate disabled: ran={UiNav.Activate()} firedDisabled={firedDisabled} (expected False 0)");

                // Focus fallback: the focused key vanishes (a price changed): nearest entry takes over; duplicate labels get index suffixes.
                UiNav.BeginFrame("t");
                Reg("Upgrade 200", 100, 300, 200, 50);
                Reg("Upgrade 450", 400, 300, 200, 50);
                Reg("Choose", 100, 500, 100, 40); Reg("Choose", 300, 500, 100, 40); Reg("Choose", 500, 500, 100, 40);
                UiNav.EndFrame();
                UiNav.FocusKey = "Upgrade 200#0";
                UiNav.BeginFrame("t");
                Reg("Upgrade 400", 105, 300, 200, 50);
                Reg("Upgrade 450", 400, 300, 200, 50);
                UiNav.EndFrame();
                P($"focus fallback to nearest: {UiNav.FocusKey} (expected Upgrade 400#0)");
                UiNav.BeginFrame("t");
                Reg("Choose", 100, 500, 100, 40); Reg("Choose", 300, 500, 100, 40); Reg("Choose", 500, 500, 100, 40);
                UiNav.EndFrame();
                P($"keys of equal labels: {UiNav.Entries[0].key},{UiNav.Entries[1].key},{UiNav.Entries[2].key} (Choose#0,#1,#2)");

                // Screen changes: default focus on the new screen, and the old screen's focus is restored when it comes back.
                UiNav.Reset();
                UiNav.DefaultKeyFor = s => s == "A" ? "Play#0" : "Back#0";
                UiNav.BeginFrame("A");
                UiNav.Layer = "A";
                UiNav.Register("Play", new Rect(0, 0, 100, 40), true, null); UiNav.Register("Controls", new Rect(0, 60, 100, 40), true, null);
                UiNav.EndFrame();
                UiNav.FocusKey = "Controls#0";
                UiNav.BeginFrame("B");
                UiNav.Layer = "B";
                UiNav.Register("Back", new Rect(0, 0, 100, 40), true, null);
                UiNav.EndFrame();
                string onB = UiNav.FocusKey;
                UiNav.BeginFrame("A");
                UiNav.Layer = "A";
                UiNav.Register("Play", new Rect(0, 0, 100, 40), true, null); UiNav.Register("Controls", new Rect(0, 60, 100, 40), true, null);
                UiNav.EndFrame();
                P($"screen change: new screen focus={onB} (Back#0); back on old screen focus={UiNav.FocusKey} (Controls#0)");
                // Only the topmost layer registers.
                UiNav.BeginFrame("B");
                UiNav.Layer = "A";
                UiNav.Register("Behind", new Rect(0, 0, 100, 40), true, null);
                UiNav.Layer = "B";
                UiNav.Register("Front", new Rect(0, 0, 100, 40), true, null);
                UiNav.EndFrame();
                P($"modal layering: entries={UiNav.Entries.Count} first={UiNav.Entries[0].key} (expected 1 Front#0)");

                // Repeat timing for a held direction: 1 + (1.0-0.38)/0.12 presses.
                var held = new Vector2Int(0, -1);
                int n = 0;
                for (int i = 0; i < 60; i++) if (UiNav.Repeater(held, 1f / 60f)) n++;
                P($"repeater: {n} presses in 1.0 s held (expected 6)");
                UiNav.Repeater(Vector2Int.zero, 0.016f);
            }

            // Real gamepad through UiNav.Tick (called by Hud.Update with the fake screen "t").
            if (k == 20)
            {
                UiNav.Reset();
                UiNav.DefaultKeyFor = s => "Play#0";
                navLog.Clear();
                navPlays = 0;
                var acts = new System.Action[5];
                for (int i = 0; i < 5; i++) { string nm = new[] { "Play", "Shop", "Settings", "Controls", "Quit" }[i]; acts[i] = () => navLog.Add(nm); }
                FrameColumn(acts);
                PadState(PadBtn(GamepadButton.DpadDown));
            }
            if (k == 22) PadState(new GamepadState());
            if (k == 24) P($"pad dpad down: focus={UiNav.FocusKey} (expected Shop#0)");
            if (k == 25) PadState(new GamepadState { leftStick = new Vector2(0f, -1f) });   // left stick down, held 1 s
            if (k == 26) { P($"stick down first press: focus={UiNav.FocusKey} (expected Settings#0)"); }
            if (k == 25 + 62) { P($"stick held ~1 s: focus={UiNav.FocusKey} (about 7 steps from Shop at the repeat rate)"); PadState(new GamepadState()); }
            if (k == 95)
            {
                FrameColumn(new System.Action[] { () => navLog.Add("Play"), () => navLog.Add("Shop"), () => navLog.Add("Settings"), () => navLog.Add("Controls"), () => navLog.Add("Quit") });
                UiNav.FocusKey = "Controls#0";
                PadState(PadBtn(GamepadButton.South));
            }
            if (k == 97) PadState(new GamepadState());
            if (k == 100)
                P($"pad A on Controls: actions run=[{string.Join(",", navLog)}] (expected Controls) hero jumped? vy={game.player.velocity.y:0.0} (A was consumed by the menu: expected not a jump)");
            if (k == 110)
            {
                bool back = false;
                Hud.TestBack = () => back = true;
                navLog.Clear();
                PadState(PadBtn(GamepadButton.East));
                backFlag = () => back;
            }
            if (k == 112) PadState(new GamepadState());
            if (k == 116) { P($"pad B: back action ran={backFlag()} (expected True)"); }
            if (k == 130)
            {
                Hud.TestTop = null;
                Hud.TestBack = null;
                UiNav.Reset();
                UiNav.DefaultKeyFor = null;
                // Remove the device again.
                InputSystem.RemoveDevice(pad);
                P($"virtual gamepad removed: Gamepad.current={(Gamepad.current == null ? "null" : "still there")}");
                pad = null;
                Finish(0);
            }
        }

        static System.Func<bool> backFlag = () => false;
    }
}

namespace Skyfall.EditorTools
{
    public static partial class SkyfallTests
    {
        static void PadProbe(Game game)
        {
            int f = frame;
            if (f == 2) { pad = InputSystem.AddDevice<Gamepad>(); }
            if (pad == null) return;
            var names = System.Enum.GetValues(typeof(GamepadButton));
            int i = (f - 10) / 10, sub = (f - 10) % 10;
            if (f >= 10 && i < names.Length)
            {
                var b = (GamepadButton)names.GetValue(i);
                if (sub == 0) PadState(PadBtn(b));
                if (sub == 4) P($"probe {b}: bit={(int)b} pressed={pad[b].isPressed} wasPressed={pad[b].wasPressedThisFrame} frame={Time.frameCount} editorFrame={f}");
                if (sub == 6) PadState(new GamepadState());
                return;
            }
            if (f == 400) PadState(new GamepadState { leftTrigger = 1f });
            if (f == 405) P($"probe LT: {pad.leftTrigger.ReadValue()} ");
            if (f == 410) { InputSystem.RemoveDevice(pad); Finish(0); }
        }
    }
}
