using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    public enum NavDir { Left, Right, Up, Down }

    /// Gamepad focus for the IMGUI screens. Every button, card and slider row registers its rect while the screen is repainted
    /// (<see cref="Register"/>); the D-pad / left stick then moves the focus to the nearest element in that direction, A activates it
    /// and left/right adjusts a focused slider. Activations run from <c>Hud.Update</c>, never inside an OnGUI pass, so the layout
    /// never changes between the Layout and Repaint events of one frame.
    public static class UiNav
    {
        public class Entry
        {
            public string key;
            public Rect rect;
            public bool enabled;
            public Action act;
            public Action<int> adjust;
        }

        /// The screen currently being drawn (set by the Hud before each Draw call); only the topmost screen registers.
        public static string Layer;
        /// The key of the focused entry (text + occurrence index, e.g. "Back#0").
        public static string FocusKey;
        /// Whether the gold focus outline is drawn (hidden while the mouse is in use).
        public static bool Visible;
        /// Picks the key to focus when a screen opens (set by the Hud).
        public static Func<string, string> DefaultKeyFor;

        public const float REPEAT_FIRST = 0.38f, REPEAT_NEXT = 0.12f;

        static string top, prevLayer, listLayer;
        static readonly List<Entry> building = new List<Entry>();
        static readonly List<Entry> current = new List<Entry>();
        static readonly Dictionary<string, int> occ = new Dictionary<string, int>();
        static readonly Dictionary<string, string> memory = new Dictionary<string, string>();
        static Vector2 focusCenter;
        static Vector2Int lastDir;
        static float repeatT;
        static bool inited;
        static float layerSince;

        public static IList<Entry> Entries => current;
        public static string ListLayer => listLayer;

        public static void Reset()
        {
            building.Clear();
            current.Clear();
            memory.Clear();
            occ.Clear();
            FocusKey = null;
            top = prevLayer = listLayer = Layer = null;
            lastDir = Vector2Int.zero;
            repeatT = 0f;
            inited = false;
        }

        // ---------------------------------------------------------------- registration (once per repaint)

        /// Start of a repaint pass; topScreen = the topmost screen (null when no screen takes gamepad input).
        public static void BeginFrame(string topScreen)
        {
            building.Clear();
            occ.Clear();
            top = topScreen;
        }

        public static void Register(string text, Rect rect, bool enabled, Action act, Action<int> adjust = null)
        {
            if (top == null || Layer != top) return;
            occ.TryGetValue(text, out int n);
            occ[text] = n + 1;
            building.Add(new Entry { key = text + "#" + n, rect = rect, enabled = enabled, act = act, adjust = adjust });
        }

        /// End of a repaint pass: the registered elements become the ones the gamepad navigates, and the focus is kept or reset.
        public static void EndFrame()
        {
            current.Clear();
            current.AddRange(building);
            listLayer = top;
            if (top != prevLayer && prevLayer != null && FocusKey != null) memory[prevLayer] = FocusKey;   // coming back to a screen (e.g. closing Controls) restores its focus
            if (top == null)
            {
                prevLayer = null;
                FocusKey = null;
                return;
            }
            bool fresh = top != prevLayer || FocusKey == null;
            if (top != prevLayer) layerSince = Time.unscaledTime;
            prevLayer = top;
            int i = -1;
            if (fresh)
            {
                if (top != "level" && top != "end" && memory.TryGetValue(top, out var mk)) i = IndexOf(mk);
                if (i < 0) i = IndexOf(DefaultKeyFor != null ? DefaultKeyFor(top) : null);
            }
            else i = IndexOf(FocusKey);
            if (i < 0 && !fresh) i = Nearest(focusCenter);
            if (i < 0) i = current.Count > 0 ? 0 : -1;
            if (i >= 0) SetFocus(i);
        }

        /// The focused entry of the entries registered during the current repaint (for drawing the outline).
        public static bool FocusedRect(out Rect r)
        {
            foreach (var e in building)
                if (e.key == FocusKey) { r = e.rect; return true; }
            r = default;
            return false;
        }

        // ---------------------------------------------------------------- navigation

        static int IndexOf(string key)
        {
            if (key == null) return -1;
            for (int i = 0; i < current.Count; i++) if (current[i].key == key) return i;
            return -1;
        }

        static int Nearest(Vector2 p)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < current.Count; i++)
            {
                float d = (current[i].rect.center - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static void SetFocus(int i)
        {
            FocusKey = current[i].key;
            focusCenter = current[i].rect.center;
        }

        static float IntervalGap(float a0, float a1, float b0, float b1) => Mathf.Max(0f, Mathf.Max(b0 - a1, a0 - b1));

        /// The entry nearest to list[from] in direction d, or -1. Cost = distance along the axis + penalties for sideways offset,
        /// so aligned neighbours win. With wrap and nothing ahead, jumps to the far side of the screen (vertical, or horizontal in the same row).
        public static int Pick(IList<Entry> list, int from, NavDir d, bool wrap)
        {
            if (from < 0 || from >= list.Count) return -1;
            var R = list[from].rect;
            var c = R.center;
            bool horiz = d == NavDir.Left || d == NavDir.Right;
            float sign = (d == NavDir.Right || d == NavDir.Down) ? 1f : -1f;
            int best = -1, bestWrap = -1;
            float bs = float.MaxValue, bw = float.MaxValue;
            for (int j = 0; j < list.Count; j++)
            {
                if (j == from) continue;
                var r = list[j].rect;
                var e = r.center;
                float along = horiz ? (e.x - c.x) * sign : (e.y - c.y) * sign;
                float perpGap = horiz ? IntervalGap(R.yMin, R.yMax, r.yMin, r.yMax) : IntervalGap(R.xMin, R.xMax, r.xMin, r.xMax);
                float perpCenter = Mathf.Abs(horiz ? e.y - c.y : e.x - c.x);
                float cost = Mathf.Abs(along) + 3f * perpGap + 0.5f * perpCenter;
                if (along > 1f)
                {
                    if (cost < bs) { bs = cost; best = j; }
                }
                else if (along < -1f && wrap && (!horiz || perpGap <= 0f))
                {
                    // Far side: the farther away along the axis, the better.
                    float wc = -Mathf.Abs(along) + 3f * perpGap + 0.5f * perpCenter;
                    if (wc < bw) { bw = wc; bestWrap = j; }
                }
            }
            return best >= 0 ? best : bestWrap;
        }

        /// Moves the focus (or adjusts a focused slider when pressing left/right). Returns true if something changed.
        public static bool Move(NavDir d)
        {
            int f = IndexOf(FocusKey);
            if (f < 0) return false;
            var cur = current[f];
            if (cur.adjust != null && (d == NavDir.Left || d == NavDir.Right))
            {
                cur.adjust(d == NavDir.Right ? 1 : -1);
                return true;
            }
            int n = Pick(current, f, d, true);
            if (n < 0) return false;
            SetFocus(n);
            Sfx.Play("click", -14f, 0.1f);
            return true;
        }

        /// Runs the focused entry's action (if it is enabled). The registered list is dropped until the next repaint, because the action may change the screen.
        public static bool Activate()
        {
            int f = IndexOf(FocusKey);
            if (f < 0) return false;
            var e = current[f];
            if (!e.enabled || e.act == null) return false;
            current.Clear();
            Sfx.Play("click", -4f, 0f);
            e.act();
            return true;
        }

        // ---------------------------------------------------------------- per-frame input

        /// Repeat timing for held directions: fires on the first press, after REPEAT_FIRST, then every REPEAT_NEXT.
        public static bool Repeater(Vector2Int dir, float dt)
        {
            if (dir == Vector2Int.zero) { lastDir = dir; return false; }
            if (dir != lastDir) { lastDir = dir; repeatT = REPEAT_FIRST; return true; }
            repeatT -= dt;
            if (repeatT > 0f) return false;
            repeatT = REPEAT_NEXT;
            return true;
        }

        /// Called from Hud.Update with unscaled time. back = the screen's Back action; tab = LB/RB (-1 / +1), only used by the shop.
        public static void Tick(string topScreen, float dt, Action back, Action<int> tab)
        {
            if (!inited) { inited = true; Visible = GameInput.PadConnected; }
            if (topScreen == null) { lastDir = Vector2Int.zero; return; }
            if (!GameInput.PadConnected) return;
            if (GameInput.MouseActive) Visible = false;
            else if (GameInput.PadAnyButton) Visible = true;
            if (listLayer != topScreen) return;   // the screen changed since the last repaint: wait for fresh rects

            if (!Visible && GameInput.MouseActive)
            {
                // Hovering with the mouse moves the focus too, so switching back to the pad continues from there.
                var m = GameInput.MouseCanvasPos;
                for (int i = 0; i < current.Count; i++)
                    if (current[i].rect.Contains(m)) { SetFocus(i); break; }
            }

            var dir = GameInput.NavDirection;
            if (Repeater(dir, dt))
            {
                if (dir.x != 0) Move(dir.x > 0 ? NavDir.Right : NavDir.Left);
                else Move(dir.y > 0 ? NavDir.Up : NavDir.Down);
            }
            if (GameInput.UiTabLeft) { tab?.Invoke(-1); }
            if (GameInput.UiTabRight) { tab?.Invoke(1); }
            // Level-up and end screens ignore A for a moment, so a jump or attack button mash does not pick a card by accident.
            bool grace = (topScreen == "level" || topScreen == "end") && Time.unscaledTime - layerSince < 0.3f;
            if (GameInput.UiConfirm && !grace)
            {
                GameInput.PadUiFrame = Time.frameCount;
                Activate();
            }
            else if (GameInput.UiBack)
            {
                GameInput.PadUiFrame = Time.frameCount;
                if (back != null)
                {
                    current.Clear();
                    back();
                }
            }
        }
    }
}
