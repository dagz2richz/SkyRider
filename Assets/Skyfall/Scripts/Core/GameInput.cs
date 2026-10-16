using UnityEngine;
using UnityEngine.InputSystem;

namespace Skyfall
{
    /// Keyboard, mouse and gamepad bindings (Input System package, read directly from the devices).
    public static class GameInput
    {
        static Keyboard K => Keyboard.current;
        static Mouse Ms => Mouse.current;
        static Gamepad Pad => Gamepad.current;

        const float STICK_DEADZONE = 0.15f;

        static bool Held(params Key[] keys)
        {
            if (K == null) return false;
            foreach (var k in keys) if (K[k].isPressed) return true;
            return false;
        }

        static bool Pressed(params Key[] keys)
        {
            if (K == null) return false;
            foreach (var k in keys) if (K[k].wasPressedThisFrame) return true;
            return false;
        }

        /// Radial deadzone: nothing inside the zone, then rescaled so the output still reaches 1 at full tilt.
        public static Vector2 Deadzone(Vector2 v, float dz = STICK_DEADZONE)
        {
            float m = v.magnitude;
            if (m < dz) return Vector2.zero;
            return v / m * Mathf.Min(1f, (m - dz) / (1f - dz));
        }

        /// Set by the automated tests to steer the hero without a keyboard (x = right, y = back; null = real input).
        public static Vector2? TestMove;
        public static bool TestJumpHeld;

        /// x = right - left, y = back - forward (like Godot's Input.get_vector). Keyboard and left stick add up.
        public static Vector2 Move
        {
            get
            {
                if (TestMove.HasValue) return TestMove.Value;
                float x = (Held(Key.D, Key.RightArrow) ? 1f : 0f) - (Held(Key.A, Key.LeftArrow) ? 1f : 0f);
                float y = (Held(Key.S, Key.DownArrow) ? 1f : 0f) - (Held(Key.W, Key.UpArrow) ? 1f : 0f);
                var v = new Vector2(x, y);
                if (Pad != null)
                {
                    var s = Deadzone(Pad.leftStick.ReadValue());
                    v += new Vector2(s.x, -s.y);
                }
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        /// Frame in which a menu action consumed the gamepad's A button, so the hero does not also jump.
        public static int PadUiFrame = -1;

        public static bool JumpPressed => Pressed(Key.Space) || (Pad != null && Pad.buttonSouth.wasPressedThisFrame && PadUiFrame != Time.frameCount);
        public static bool JumpHeld => TestJumpHeld || Held(Key.Space) || (Pad != null && Pad.buttonSouth.isPressed);
        public static bool DescendHeld => Held(Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl)
            || (Pad != null && (Pad.buttonEast.isPressed || Pad.leftTrigger.ReadValue() > 0.4f));
        public static bool PausePressed => Pressed(Key.Escape, Key.P) || (Pad != null && Pad.startButton.wasPressedThisFrame);
        /// One-shot key presses injected by the automated tests (consumed when read).
        public static bool TestFlyPress, TestAbilityPress;

        public static bool FlyPressed
        {
            get
            {
                if (TestFlyPress) { TestFlyPress = false; return true; }
                return Pressed(Key.Q) || (Pad != null && Pad.buttonNorth.wasPressedThisFrame);
            }
        }

        public static bool AbilityPressed
        {
            get
            {
                if (TestAbilityPress) { TestAbilityPress = false; return true; }
                return Pressed(Key.E) || (Pad != null && (Pad.rightShoulder.wasPressedThisFrame || Pad.buttonWest.wasPressedThisFrame));
            }
        }
        /// F (gamepad LB): the hero's active talent (the Knight's Dragon Form).
        public static bool TestTalentPress;

        public static bool TalentPressed
        {
            get
            {
                if (TestTalentPress) { TestTalentPress = false; return true; }
                return Pressed(Key.F) || (Pad != null && Pad.leftShoulder.wasPressedThisFrame);
            }
        }

        public static bool StatsPressed =>Pressed(Key.Tab) || (Pad != null && Pad.selectButton.wasPressedThisFrame);

        /// R (gamepad Y) on the level-up screen: reroll the cards.
        public static bool RerollPressed => Pressed(Key.R) || (Pad != null && Pad.buttonNorth.wasPressedThisFrame);

        /// 0, 1 or 2 when a level-up card key (1/2/3) was pressed this frame, else -1.
        public static int CardPressed
        {
            get
            {
                if (Pressed(Key.Digit1, Key.Numpad1)) return 0;
                if (Pressed(Key.Digit2, Key.Numpad2)) return 1;
                if (Pressed(Key.Digit3, Key.Numpad3)) return 2;
                return -1;
            }
        }

        public static Vector2 MouseDelta => Ms != null ? Ms.delta.ReadValue() : Vector2.zero;

        /// Right stick for the camera (x = right, y = up), deadzoned, with a gentle response curve for fine aiming.
        public static Vector2 LookStick
        {
            get
            {
                if (Pad == null) return Vector2.zero;
                var s = Deadzone(Pad.rightStick.ReadValue());
                return s * Mathf.Lerp(0.5f, 1f, s.magnitude);
            }
        }

        static float zoomHold;

        /// +1 per wheel notch up, -1 down. The D-pad zooms too (up = in), repeating while held.
        public static float Scroll
        {
            get
            {
                if (Ms != null)
                {
                    float y = Ms.scroll.ReadValue().y;
                    if (Mathf.Abs(y) >= 0.01f) return Mathf.Sign(y);
                }
                if (Pad == null) return 0f;
                float dir = Pad.dpad.up.isPressed ? 1f : (Pad.dpad.down.isPressed ? -1f : 0f);
                if (dir == 0f) { zoomHold = 0f; return 0f; }
                if (Pad.dpad.up.wasPressedThisFrame || Pad.dpad.down.wasPressedThisFrame) { zoomHold = 0f; return dir; }
                zoomHold += Time.unscaledDeltaTime;
                if (zoomHold < 0.3f) return 0f;
                zoomHold -= 0.07f;
                return dir;
            }
        }

        /// Right stick click: back to the default camera distance.
        public static bool ZoomResetPressed => Pad != null && Pad.rightStickButton.wasPressedThisFrame;

        public static bool MouseCaptured
        {
            get => Cursor.lockState == CursorLockMode.Locked;
            set
            {
                Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !value;
            }
        }

        // ---------------------------------------------------------------- menu navigation (gamepad)

        public static bool PadConnected => Pad != null;

        /// D-pad or left stick as a menu direction (-1/0/1 per axis, y up); the stick needs a firm push.
        public static Vector2Int NavDirection
        {
            get
            {
                var p = Pad;
                if (p == null) return Vector2Int.zero;
                int x = (p.dpad.right.isPressed ? 1 : 0) - (p.dpad.left.isPressed ? 1 : 0);
                int y = (p.dpad.up.isPressed ? 1 : 0) - (p.dpad.down.isPressed ? 1 : 0);
                if (x == 0 && y == 0)
                {
                    var s = p.leftStick.ReadValue();
                    if (s.magnitude > 0.6f)
                    {
                        if (Mathf.Abs(s.x) >= Mathf.Abs(s.y)) x = s.x > 0 ? 1 : -1;
                        else y = s.y > 0 ? 1 : -1;
                    }
                }
                return new Vector2Int(x, y);
            }
        }

        public static bool UiConfirm => Pad != null && Pad.buttonSouth.wasPressedThisFrame;
        public static bool UiBack => Pad != null && Pad.buttonEast.wasPressedThisFrame;
        public static bool UiTabLeft => Pad != null && Pad.leftShoulder.wasPressedThisFrame;
        public static bool UiTabRight => Pad != null && Pad.rightShoulder.wasPressedThisFrame;

        /// True on a frame in which any gamepad button was pressed (shows the menu focus highlight again).
        public static bool PadAnyButton
        {
            get
            {
                var p = Pad;
                if (p == null) return false;
                return p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame || p.buttonNorth.wasPressedThisFrame
                    || p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame || p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame
                    || p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame || p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame
                    || NavDirection != Vector2Int.zero;
            }
        }

        /// True when the mouse moved or clicked this frame (hides the menu focus highlight).
        public static bool MouseActive => Ms != null && (Ms.delta.ReadValue().sqrMagnitude > 4f || Ms.leftButton.wasPressedThisFrame);

        /// Mouse position in the HUD's virtual canvas (origin top left).
        public static Vector2 MouseCanvasPos
        {
            get
            {
                if (Ms == null) return new Vector2(-1000f, -1000f);
                var p = Ms.position.ReadValue();
                float s = UiKit.Scale;
                return new Vector2(p.x / s, (Screen.height - p.y) / s);
            }
        }

        // ---------------------------------------------------------------- rumble

        static float rumbleEnd;

        /// A short rumble on the gamepad (strength 0..1). Ignored when no gamepad is connected.
        public static void Rumble(float strength, float seconds = 0.18f)
        {
            var p = Pad;
            if (p == null || Application.isBatchMode) return;
            strength = Mathf.Clamp01(strength);
            try { p.SetMotorSpeeds(strength * 0.6f, strength); } catch { return; }
            rumbleEnd = Time.unscaledTime + seconds;
        }

        /// Stops the rumble once its time is up; call every frame.
        public static void TickRumble()
        {
            if (rumbleEnd > 0f && Time.unscaledTime >= rumbleEnd) StopRumble();
        }

        public static void StopRumble()
        {
            rumbleEnd = 0f;
            var p = Pad;
            if (p == null) return;
            try { p.ResetHaptics(); } catch { }
        }
    }
}
