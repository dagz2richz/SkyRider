using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// A small immediate-mode UI toolkit on top of Unity's IMGUI: outlined text, rounded panels, bars and buttons,
    /// all laid out in a virtual 900-pixel-tall canvas that scales with the window.
    public static class UiKit
    {
        public static readonly Color GOLD = new Color(0.95f, 0.78f, 0.4f);
        public static readonly Color PARCHMENT = new Color(0.93f, 0.88f, 0.76f);
        public static readonly Color INK = new Color(0.08f, 0.06f, 0.05f, 0.92f);

        public const float VIRTUAL_H = 900f;
        public static float Scale => Screen.height / VIRTUAL_H;
        public static float W => Screen.width / Scale;
        public static float H => VIRTUAL_H;

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, GUIStyle> styleCache = new Dictionary<string, GUIStyle>();
        static Texture2D white;

        public static Texture2D White
        {
            get
            {
                if (white) return white;
                white = new Texture2D(1, 1) { name = "UiWhite" };
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                return white;
            }
        }

        public static void BeginScaled()
        {
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        public static Vector2 Mouse => Event.current.mousePosition;

        // ---------------------------------------------------------------- textures and styles

        /// Rounded rectangle with a border, used as a 9-sliced background.
        public static Texture2D PanelTex(Color bg, Color border, int radius, int borderW)
        {
            string key = $"{ColorUtility.ToHtmlStringRGBA(bg)}_{ColorUtility.ToHtmlStringRGBA(border)}_{radius}_{borderW}";
            if (texCache.TryGetValue(key, out var t) && t) return t;
            int n = radius * 2 + 4;
            t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Panel" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Distance to the rounded rectangle's edge (negative inside).
                    float cx = Mathf.Clamp(x + 0.5f, radius, n - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius;
                    if (radius == 0) d = Mathf.Max(Mathf.Max(-x - 0.5f, x + 0.5f - n), Mathf.Max(-y - 0.5f, y + 0.5f - n));
                    float outer = Mathf.Clamp01(0.5f - d);
                    float inner = Mathf.Clamp01(0.5f - (d + borderW));
                    var c = Color.Lerp(border, bg, inner);
                    c.a *= outer;
                    px[y * n + x] = c;
                }
            t.SetPixels(px);
            t.Apply();
            texCache[key] = t;
            return t;
        }

        public static GUIStyle PanelStyle(Color? bg = null, Color? border = null, int radius = 6, int borderW = 2, int padding = 14)
        {
            var b = bg ?? INK;
            var bd = border ?? GOLD;
            string key = $"panel{ColorUtility.ToHtmlStringRGBA(b)}_{ColorUtility.ToHtmlStringRGBA(bd)}_{radius}_{borderW}_{padding}";
            if (styleCache.TryGetValue(key, out var st) && st.normal.background) return st;
            st = new GUIStyle
            {
                border = new RectOffset(radius + 2, radius + 2, radius + 2, radius + 2),
                padding = new RectOffset(padding, padding, padding, padding),
                margin = new RectOffset(0, 0, 0, 0),
            };
            st.normal.background = PanelTex(b, bd, radius, borderW);
            styleCache[key] = st;
            return st;
        }

        public static GUIStyle Text(int size, TextAnchor align = TextAnchor.UpperLeft, bool wrap = false, bool bold = false)
        {
            string key = $"text{size}_{align}_{wrap}_{bold}";
            if (styleCache.TryGetValue(key, out var st)) return st;
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = align,
                wordWrap = wrap,
                richText = false,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow,
            };
            styleCache[key] = st;
            return st;
        }

        public static GUIStyle ButtonStyle(int size)
        {
            string key = $"button{size}";
            if (styleCache.TryGetValue(key, out var st) && st.normal.background) return st;
            st = new GUIStyle(GUI.skin.button)
            {
                fontSize = size,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(6, 6, 6, 6),
                padding = new RectOffset(16, 16, 10, 10),
                margin = new RectOffset(0, 0, 0, 0),
            };
            var edge = new Color(0.4f, 0.28f, 0.1f);
            st.normal.background = PanelTex(GOLD, edge, 4, 2);
            st.hover.background = PanelTex(new Color(1f, 0.88f, 0.55f), edge, 4, 2);
            st.active.background = PanelTex(new Color(0.8f, 0.6f, 0.3f), edge, 4, 2);
            st.focused.background = st.normal.background;
            st.normal.textColor = INK;
            st.hover.textColor = new Color(0.2f, 0.05f, 0f);
            st.active.textColor = INK;
            st.focused.textColor = INK;
            styleCache[key] = st;
            return st;
        }

        // ---------------------------------------------------------------- drawing

        /// Draws text with a dark outline (only during repaint).
        public static void DrawText(Rect r, string text, GUIStyle st, Color color, int outline = 6, float alpha = 1f)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;
            var prev = GUI.color;
            if (outline > 0)
            {
                float o = Mathf.Max(1f, outline * 0.3f);
                GUI.color = new Color(0, 0, 0, 0.85f * alpha * color.a);
                st.normal.textColor = Color.white;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 0.25f;
                    var rr = r;
                    rr.x += Mathf.Cos(a) * o;
                    rr.y += Mathf.Sin(a) * o;
                    st.Draw(rr, text, false, false, false, false);
                }
            }
            GUI.color = new Color(1, 1, 1, alpha);
            st.normal.textColor = color;
            st.Draw(r, text, false, false, false, false);
            GUI.color = prev;
        }

        public static void Rect(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        public static void Panel(Rect r, Color? bg = null, Color? border = null, int radius = 6, int borderW = 2, float alpha = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            PanelStyle(bg, border, radius, borderW).Draw(r, false, false, false, false);
            GUI.color = prev;
        }

        public static void Bar(Rect r, float value, Color fill)
        {
            Panel(r, new Color(0.05f, 0.04f, 0.04f, 0.85f), new Color(0.6f, 0.5f, 0.3f, 0.9f), 4, 2);
            value = Mathf.Clamp01(value);
            if (value <= 0f) return;
            var inner = new Rect(r.x + 2, r.y + 2, (r.width - 4) * value, r.height - 4);
            Panel(inner, fill, fill, 3, 0);
        }

        // ---------------------------------------------------------------- layout helpers

        /// A layout label with an outline. width &lt;= 0 means natural width.
        public static Rect Label(string text, int size, Color? color = null, int outline = 6, TextAnchor align = TextAnchor.UpperLeft,
            float width = 0f, bool wrap = false, bool bold = false, float alpha = 1f)
        {
            var st = Text(size, align, wrap, bold);
            var content = new GUIContent(text);
            Rect r = width > 0f
                ? GUILayoutUtility.GetRect(content, st, GUILayout.Width(width))
                : GUILayoutUtility.GetRect(content, st, GUILayout.ExpandWidth(align != TextAnchor.UpperLeft));
            DrawText(r, text, st, color ?? PARCHMENT, outline, alpha);
            return r;
        }

        /// A gold button. With enabled = false it is greyed out and never reports a click.
        public static bool Button(string text, int size = 22, float width = 0f, bool enabled = true)
        {
            var st = ButtonStyle(size);
            var prevEnabled = GUI.enabled;
            var prevColor = GUI.color;
            if (!enabled)
            {
                GUI.enabled = false;
                GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
            }
            bool clicked = width > 0f ? GUILayout.Button(text, st, GUILayout.Width(width)) : GUILayout.Button(text, st);
            GUI.enabled = prevEnabled;
            GUI.color = prevColor;
            clicked &= enabled;
            if (clicked) Sfx.Play("click", -4f, 0f);
            return clicked;
        }

        /// A row of small squares showing a level or tier (filled / total).
        public static void Pips(int filled, int total, Color c)
        {
            var r = GUILayoutUtility.GetRect(total * 20f, 16f, GUILayout.Width(total * 20f), GUILayout.Height(16f));
            for (int i = 0; i < total; i++)
            {
                var q = new Rect(r.x + i * 20f, r.y + 1f, 15f, 14f);
                if (i < filled) Panel(q, c, M.Lighten(c, 0.4f), 3, 1);
                else Panel(q, new Color(0.1f, 0.08f, 0.07f, 0.9f), M.Darken(GOLD, 0.5f), 3, 1);
            }
        }

        static Texture2D coinTex;

        /// A small gold coin icon (a round texture made once).
        public static void CoinIcon(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!coinTex)
            {
                const int N = 48;
                coinTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Coin" };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                        var c = d > 0.78f ? new Color(0.62f, 0.42f, 0.06f) : (d > 0.6f ? new Color(1f, 0.88f, 0.4f) : new Color(1f, 0.74f, 0.16f));
                        c.a = Mathf.Clamp01((1f - d) * N * 0.5f);
                        px[y * N + x] = c;
                    }
                coinTex.SetPixels(px);
                coinTex.Apply();
            }
            GUI.DrawTexture(r, coinTex);
        }

        public static void Space(float px) => GUILayout.Space(px);

        /// Centers the content of the given builder in a full-screen area.
        public static void Centered(System.Action content, float separation = 18f)
        {
            GUILayout.BeginArea(new Rect(0, 0, W, H));
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            content();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        public static void CenterRow(System.Action content)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            content();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// Hover tint and click detection for the last layout group (cards).
        public static bool Clickable(Rect r)
        {
            var e = Event.current;
            if (e.type == EventType.Repaint && r.Contains(e.mousePosition)) Rect(r, new Color(1f, 0.95f, 0.8f, 0.08f));
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                Sfx.Play("click", -4f, 0f);
                return true;
            }
            return false;
        }

        public static void Overlay(float alpha) => Rect(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.01f, alpha));

        public static string Repeat(string s, int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }
    }
}
