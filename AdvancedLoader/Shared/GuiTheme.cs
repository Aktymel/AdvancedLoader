using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using KKAPI.Utilities;
using UnityEngine;

namespace AdvancedLoader
{
    internal enum WindowTheme
    {
        Classic,
        Dark,
        Light,
        Blue
    }

    internal enum DotState
    {
        None,
        Some,
        All
    }

    // Same file in Advanced Loader and Advanced Accessories Menu, only the namespace differs
    internal static class GuiTheme
    {
        private sealed class Palette
        {
            public Color Window, Box, Button, Hover, Pressed, On, OnHover, Border, Text, OnText, Field, Accent, Warning, Check;
        }

        private static readonly Dictionary<WindowTheme, Palette> Palettes = new Dictionary<WindowTheme, Palette>
        {
            {
                WindowTheme.Dark, new Palette
                {
                    Window = Rgb(0x1d1f24, 0.97f), Box = Rgb(0x272a31), Button = Rgb(0x353943), Hover = Rgb(0x424754), Pressed = Rgb(0x2b2e36),
                    On = Rgb(0x2f6da6), OnHover = Rgb(0x3a7fbf), Border = Rgb(0x121317), Text = Rgb(0xe4e6eb), OnText = Color.white,
                    Field = Rgb(0x16181c), Accent = Rgb(0x5cb3ff), Warning = new Color(1f, 0.85f, 0.2f), Check = Rgb(0xc8ccd4)
                }
            },
            {
                WindowTheme.Light, new Palette
                {
                    Window = Rgb(0xeceef2, 0.98f), Box = Rgb(0xfafbfc), Button = Rgb(0xdadee5), Hover = Rgb(0xe6e9ee), Pressed = Rgb(0xc8cdd6),
                    On = Rgb(0x3d7fd1), OnHover = Rgb(0x4c8cdc), Border = Rgb(0xa9afb9), Text = Rgb(0x1e2026), OnText = Color.white,
                    Field = Color.white, Accent = Rgb(0x2a6fc4), Warning = Rgb(0xa86200), Check = Rgb(0x5f6673)
                }
            },
            {
                WindowTheme.Blue, new Palette
                {
                    Window = Rgb(0x0f3557, 0.97f), Box = Rgb(0x16466f), Button = Rgb(0x1f5f93), Hover = Rgb(0x2a72ad), Pressed = Rgb(0x194f7c),
                    On = Rgb(0x4cc4ef), OnHover = Rgb(0x6dd2f5), Border = Rgb(0x8fd8ff), Text = Color.white, OnText = Rgb(0x082640),
                    Field = Rgb(0x0b2a46), Accent = Rgb(0x8fe3ff), Warning = new Color(1f, 0.85f, 0.2f), Check = Color.white
                }
            }
        };

        private static readonly Color DotNone = new Color(0.55f, 0.55f, 0.58f);
        private static readonly Color DotSome = new Color(1f, 0.62f, 0.15f);
        private static readonly Color DotAll = new Color(0.3f, 0.82f, 0.35f);
        private const float DotSize = 11;

        public static ConfigEntry<WindowTheme> Theme;

        private static WindowTheme? _built;
        private static GUISkin _skin;
        private static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        private static readonly Stack<GUISkin> Previous = new Stack<GUISkin>();
        private static Texture2D _dot;

        public static GUIStyle Header;
        public static GUIStyle Warning;
        public static GUIStyle Tab;
        public static GUIStyle SubTab;
        public static GUIStyle Cell;
        public static GUIStyle Line;
        public static GUIStyle SelectedButton;
        public static GUIStyle PanelWindow;
        public static Color Accent;

        public static void Bind(ConfigFile config)
        {
            Theme = config.Bind("General", "Window theme", WindowTheme.Dark, "Look of the plugin's windows.");
        }

        public static GUISkin Skin
        {
            get
            {
                var theme = Theme == null ? WindowTheme.Dark : Theme.Value;
                if (_skin == null || _built != theme) Build(theme);
                return _skin;
            }
        }

        // Only inside OnGUI
        public static void Begin()
        {
            Previous.Push(GUI.skin);
            GUI.skin = Skin;
        }

        public static void End()
        {
            GUI.skin = Previous.Count > 0 ? Previous.Pop() : null;
        }

        public static void DrawWarning(string text)
        {
            if (!string.IsNullOrEmpty(text)) GUILayout.Label(text, Warning);
        }

        // "s" and "X" at the right end of a row; true when X was clicked
        public static bool TitleButtons(string configSearch, bool close)
        {
            if (ConfigManagerBridge.Available && GUILayout.Button("s", GUILayout.Width(26))) ConfigManagerBridge.Open(configSearch);
            return close && GUILayout.Button("X", GUILayout.Width(26));
        }

        public static DotState StateOf(int on, int count) => on <= 0 ? DotState.None : on >= count ? DotState.All : DotState.Some;

        // A tab button with an optional status dot; the dot is a button of its own
        public static bool TabButton(string text, bool selected, DotState? dot, bool dotEnabled, out bool dotClicked, GUIStyle style, params GUILayoutOption[] options)
        {
            dotClicked = false;
            var content = new GUIContent(dot == null ? text : "     " + text);
            var rect = GUILayoutUtility.GetRect(content, style, options);
            Rect dotRect = default;
            if (dot != null)
            {
                dotRect = new Rect(rect.x + 7, rect.y + Mathf.Round((rect.height - DotSize) / 2), DotSize, DotSize);
                var enabled = GUI.enabled;
                GUI.enabled = enabled && dotEnabled;
                dotClicked = GUI.Button(new Rect(dotRect.x - 4, rect.y, dotRect.width + 8, rect.height), GUIContent.none, GUIStyle.none);
                GUI.enabled = enabled;
            }
            var clicked = GUI.Toggle(rect, selected, content, style) != selected;
            if (dot != null && Event.current.type == EventType.Repaint) DrawDot(dotRect, dot.Value, dotEnabled && GUI.enabled);
            return clicked;
        }

        // Screen pixels (top left origin) to IMGUI coordinates, which GUI.matrix may scale
        public static Rect ScreenToGui(Rect rect)
        {
            var m = GUI.matrix;
            return new Rect(rect.x / m.m00, rect.y / m.m11, rect.width / m.m00, rect.height / m.m11);
        }

        private static void DrawDot(Rect rect, DotState state, bool enabled)
        {
            if (_dot == null) _dot = Own(CircleTexture(32));
            var color = GUI.color;
            var tint = state == DotState.All ? DotAll : state == DotState.Some ? DotSome : DotNone;
            if (!enabled) tint.a = 0.45f;
            else if (rect.Contains(Event.current.mousePosition)) tint = Color.Lerp(tint, Color.white, 0.3f);
            GUI.color = tint;
            GUI.DrawTexture(rect, _dot);
            GUI.color = color;
        }

        private static void Build(WindowTheme theme)
        {
            foreach (var obj in Owned)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            Owned.Clear();
            _dot = null;
            _built = theme;

            var source = IMGUIUtils.SolidBackgroundGuiSkin;
            _skin = Own(UnityEngine.Object.Instantiate(source));
            Palettes.TryGetValue(theme, out var p);
            if (p != null) Paint(_skin, p);
            Accent = p?.Accent ?? new Color(0.4f, 0.8f, 1f);

            Header = new GUIStyle(_skin.label) { fontStyle = FontStyle.Bold };
            if (p != null) SetText(Header, p.Accent);
            Warning = new GUIStyle(_skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
            SetText(Warning, p?.Warning ?? new Color(1f, 0.85f, 0.2f));
            Tab = new GUIStyle(_skin.button) { wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(8, 8, 3, 3) };
            SubTab = new GUIStyle(Tab);
            SubTab.normal.background = null;
            if (p != null) SetText(SubTab, p.Text);
            SubTab.onNormal.textColor = SubTab.onHover.textColor = SubTab.onActive.textColor = p?.OnText ?? Color.white;
            Cell = new GUIStyle(_skin.toggle) { wordWrap = false, clipping = TextClipping.Clip };
            Line = new GUIStyle(_skin.label) { wordWrap = false, clipping = TextClipping.Clip };
            SelectedButton = new GUIStyle(_skin.button);
            SelectedButton.normal = _skin.button.onNormal;
            SelectedButton.hover = _skin.button.onHover;
            SelectedButton.active = _skin.button.onActive;
            PanelWindow = new GUIStyle(_skin.window) { padding = new RectOffset(6, 6, 6, 6) };
        }

        private static void Paint(GUISkin skin, Palette p)
        {
            const int r = 3;
            var window = Rounded(p.Window, p.Border, r);
            var box = Rounded(p.Box, Color.Lerp(p.Box, p.Border, 0.6f), r);
            var button = Rounded(p.Button, p.Border, r);
            var hover = Rounded(p.Hover, p.Border, r);
            var pressed = Rounded(p.Pressed, p.Border, r);
            var on = Rounded(p.On, p.Border, r);
            var onHover = Rounded(p.OnHover, p.Border, r);
            var field = Rounded(p.Field, p.Border, r);
            var fieldFocus = Rounded(p.Field, p.Accent, r);
            var slices = new RectOffset(r + 1, r + 1, r + 1, r + 1);

            SetBackgrounds(skin.window, window, window, window, window, window, window, slices);
            SetText(skin.window, p.Text);
            SetBackgrounds(skin.box, box, box, box, box, box, box, slices);
            SetText(skin.box, p.Text);
            PaintButton(skin.button, button, hover, pressed, on, onHover, slices, p);
            foreach (var style in skin.customStyles)
                if (style != null && style.name.StartsWith("button", StringComparison.OrdinalIgnoreCase))
                    PaintButton(style, button, hover, pressed, on, onHover, slices, p);
            SetBackgrounds(skin.textField, field, field, fieldFocus, field, field, fieldFocus, slices);
            skin.textField.focused.background = fieldFocus;
            skin.textField.onFocused.background = fieldFocus;
            SetText(skin.textField, p.Text);
            SetBackgrounds(skin.textArea, field, field, fieldFocus, field, field, fieldFocus, slices);
            SetText(skin.textArea, p.Text);
            SetText(skin.label, p.Text);

            var size = skin.toggle.border.left >= 10 ? skin.toggle.border.left : 14;
            // Only the transparent last column and row stretch
            skin.toggle.border = new RectOffset(size, 1, size, 1);
            var off = Own(CheckTexture(size, Color.Lerp(p.Field, p.Check, 0.12f), p.Check, null));
            var offHover = Own(CheckTexture(size, Color.Lerp(p.Field, p.Check, 0.2f), p.Accent, null));
            var check = Own(CheckTexture(size, p.On, p.Check, p.OnText));
            var checkHover = Own(CheckTexture(size, p.OnHover, p.Accent, p.OnText));
            skin.toggle.normal.background = off;
            skin.toggle.hover.background = offHover;
            skin.toggle.active.background = offHover;
            skin.toggle.onNormal.background = check;
            skin.toggle.onHover.background = checkHover;
            skin.toggle.onActive.background = checkHover;
            SetText(skin.toggle, p.Text);

            var track = Rounded(Color.Lerp(p.Field, p.Box, 0.5f), Color.clear, r);
            var thumb = Rounded(p.Button, p.Border, r);
            foreach (var style in new[] { skin.verticalScrollbar, skin.horizontalScrollbar })
                SetBackgrounds(style, track, track, track, track, track, track, slices);
            foreach (var style in new[] { skin.verticalScrollbarThumb, skin.horizontalScrollbarThumb })
                SetBackgrounds(style, thumb, thumb, thumb, thumb, thumb, thumb, slices);
        }

        private static void PaintButton(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D pressed, Texture2D on, Texture2D onHover, RectOffset slices, Palette p)
        {
            SetBackgrounds(style, normal, hover, pressed, on, onHover, on, slices);
            style.normal.textColor = style.hover.textColor = style.active.textColor = p.Text;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = p.OnText;
        }

        private static void SetBackgrounds(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, Texture2D onNormal, Texture2D onHover, Texture2D onActive, RectOffset slices)
        {
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = normal;
            style.onNormal.background = onNormal;
            style.onHover.background = onHover;
            style.onActive.background = onActive;
            style.onFocused.background = onNormal;
            style.border = new RectOffset(slices.left, slices.right, slices.top, slices.bottom);
        }

        private static void SetText(GUIStyle style, Color color)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = color;
        }

        private static T Own<T>(T obj) where T : UnityEngine.Object
        {
            obj.hideFlags = HideFlags.HideAndDontSave;
            Owned.Add(obj);
            return obj;
        }

        private static Color Rgb(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);

        private static Texture2D NewTexture(int size) =>
            new Texture2D(size, size, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        private static Texture2D Rounded(Color fill, Color border, int radius)
        {
            var size = radius * 2 + 2;
            var tex = Own(NewTexture(size));
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                tex.SetPixel(x, y, RoundedPixel(x, y, size, radius, fill, border));
            tex.Apply();
            return tex;
        }

        private static Color RoundedPixel(int x, int y, int size, int radius, Color fill, Color border)
        {
            float px = x + 0.5f, py = y + 0.5f;
            var cx = Mathf.Clamp(px, radius, size - radius);
            var cy = Mathf.Clamp(py, radius, size - radius);
            var d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            var alpha = Mathf.Clamp01(radius + 0.5f - d);
            var edge = Mathf.Clamp01(d - (radius - 1) + 0.5f);
            var color = Color.Lerp(fill, border, border.a > 0 ? edge : 0);
            color.a = (border.a > 0 ? Mathf.Lerp(fill.a, border.a, edge) : fill.a) * alpha;
            return color;
        }

        private static Texture2D CheckTexture(int size, Color fill, Color border, Color? mark)
        {
            var tex = NewTexture(size + 2);
            // Bilinear would blend the box edge into the stretched transparent column behind the text
            tex.filterMode = FilterMode.Point;
            var radius = Mathf.Max(2, size / 5);
            for (var y = 0; y < size + 2; y++)
            for (var x = 0; x < size + 2; x++)
                tex.SetPixel(x, y, Color.clear);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var color = RoundedPixel(x, y, size, radius, fill, border);
                if (mark != null)
                {
                    var s = size / 14f;
                    var p = new Vector2(x + 0.5f, size - y - 0.5f);
                    var d = Mathf.Min(SegmentDistance(p, new Vector2(3.5f, 7.2f) * s, new Vector2(6f, 9.8f) * s),
                        SegmentDistance(p, new Vector2(6f, 9.8f) * s, new Vector2(10.6f, 4.2f) * s));
                    var a = Mathf.Clamp01(0.95f * s + 0.5f - d);
                    color = Color.Lerp(color, new Color(mark.Value.r, mark.Value.g, mark.Value.b, color.a), a);
                }
                tex.SetPixel(x, y + 2, color);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D CircleTexture(int size)
        {
            var tex = NewTexture(size);
            var r = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                var alpha = Mathf.Clamp01(r - d);
                var shade = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(d - (r - 3.5f)));
                tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
            }
            tex.Apply();
            return tex;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
