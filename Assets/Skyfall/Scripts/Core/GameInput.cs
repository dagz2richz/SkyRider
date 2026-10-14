using UnityEngine;
using UnityEngine.InputSystem;

namespace Skyfall
{
    /// Keyboard and mouse bindings (Input System package, read directly from the devices).
    public static class GameInput
    {
        static Keyboard K => Keyboard.current;
        static Mouse Ms => Mouse.current;

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

        /// Set by the automated tests to steer the hero without a keyboard (x = right, y = back; null = real input).
        public static Vector2? TestMove;
        public static bool TestJumpHeld;

        /// x = right - left, y = back - forward (like Godot's Input.get_vector).
        public static Vector2 Move
        {
            get
            {
                if (TestMove.HasValue) return TestMove.Value;
                float x = (Held(Key.D, Key.RightArrow) ? 1f : 0f) - (Held(Key.A, Key.LeftArrow) ? 1f : 0f);
                float y = (Held(Key.S, Key.DownArrow) ? 1f : 0f) - (Held(Key.W, Key.UpArrow) ? 1f : 0f);
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        public static bool JumpPressed => Pressed(Key.Space);
        public static bool JumpHeld => TestJumpHeld || Held(Key.Space);
        public static bool DescendHeld => Held(Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl);
        public static bool PausePressed => Pressed(Key.Escape, Key.P);
        /// One-shot key presses injected by the automated tests (consumed when read).
        public static bool TestFlyPress, TestAbilityPress;

        public static bool FlyPressed
        {
            get
            {
                if (TestFlyPress) { TestFlyPress = false; return true; }
                return Pressed(Key.Q);
            }
        }

        public static bool AbilityPressed
        {
            get
            {
                if (TestAbilityPress) { TestAbilityPress = false; return true; }
                return Pressed(Key.E);
            }
        }
        public static bool StatsPressed => Pressed(Key.Tab);

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

        /// +1 per wheel notch up, -1 down.
        public static float Scroll
        {
            get
            {
                if (Ms == null) return 0f;
                float y = Ms.scroll.ReadValue().y;
                return Mathf.Abs(y) < 0.01f ? 0f : Mathf.Sign(y);
            }
        }

        public static bool MouseCaptured
        {
            get => Cursor.lockState == CursorLockMode.Locked;
            set
            {
                Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !value;
            }
        }
    }
}
