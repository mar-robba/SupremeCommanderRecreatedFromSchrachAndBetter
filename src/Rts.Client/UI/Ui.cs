using System.Numerics;
using Raylib_cs;

namespace Rts.Client.UI;

/// <summary>One line of a tooltip.</summary>
/// <summary>One line of a tooltip. Optional Segments draw extra colored text after Text on the same line (no wrapping).</summary>
public readonly record struct TipLine(string Text, Color Color, float Size = 13, bool Bold = false, (string text, Color color)[]? Segments = null);

/// <summary>Result of an immediate-mode button.</summary>
public readonly record struct ButtonState(bool Hover, bool Left, bool Right)
{
    public bool Clicked => Left || Right;
}

/// <summary>
/// Tiny immediate-mode UI toolkit drawn with raylib 2D: UI scale, fonts (a system TrueType font if available,
/// raylib's default font otherwise), panels, buttons, bars, text wrapping and tooltips.
/// All sizes passed to helpers are in "base pixels" designed for 1280x720 and multiplied by <see cref="S"/>.
/// </summary>
public static class Ui
{
    /// <summary>UI scale: 1 at 1280x720, grows with the window.</summary>
    public static float S { get; private set; } = 1f;
    public static int Sw { get; private set; } = 1280;
    public static int Sh { get; private set; } = 720;
    public static Vector2 Mouse { get; private set; }
    public static bool Shift { get; private set; }
    public static bool Ctrl { get; private set; }
    public static bool Alt { get; private set; }

    // Palette (SupCom-like dark steel).
    public static readonly Color PanelBg = new(16, 22, 30, 232);
    public static readonly Color PanelBgLight = new(28, 37, 49, 240);
    public static readonly Color PanelBorder = new(78, 98, 120, 255);
    public static readonly Color PanelEdge = new(140, 170, 200, 255);
    public static readonly Color Accent = new(110, 180, 245, 255);
    public static readonly Color TextColor = new(225, 232, 240, 255);
    public static readonly Color TextDim = new(150, 165, 182, 255);
    public static readonly Color TextFaint = new(110, 122, 138, 255);
    public static readonly Color MassColor = new(110, 220, 90, 255);
    public static readonly Color EnergyColor = new(245, 205, 60, 255);
    public static readonly Color Good = new(110, 225, 110, 255);
    public static readonly Color Bad = new(240, 95, 85, 255);
    public static readonly Color Warn = new(245, 175, 60, 255);
    public static readonly Color BuildBlue = new(90, 170, 255, 255);

    private static bool _leftUsed, _rightUsed;
    private static readonly List<TipLine> _tip = new();
    private static Rectangle _tipAnchor;
    private static bool _tipAbove;
    private static float _tipMaxW = 340;

    /// <summary>Call at the start of UI work in a frame (cheap, idempotent).</summary>
    public static void BeginFrame()
    {
        Sw = Raylib.GetScreenWidth();
        Sh = Raylib.GetScreenHeight();
        S = Math.Clamp(MathF.Min(Sw / 1280f, Sh / 720f), 0.75f, 2.5f);
        Mouse = Raylib.GetMousePosition();
        Shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        Ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        Alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
    }

    /// <summary>Resets per-draw state (click consumption, tooltip). Call at the start of Hud.Draw.</summary>
    public static void BeginDraw()
    {
        BeginFrame();
        _leftUsed = _rightUsed = false;
        _tip.Clear();
    }

    /// <summary>Scaled pixels.</summary>
    public static float P(float basePx) => MathF.Round(basePx * S);

    /// <summary>Scaled font size.</summary>
    public static int F(float basePx) => Math.Max(9, (int)MathF.Round(basePx * S));

    public static bool Hover(Rectangle r) => Raylib.CheckCollisionPointRec(Mouse, r);

    public static bool LeftClickIn(Rectangle r)
    {
        if (_leftUsed || !Raylib.IsMouseButtonPressed(MouseButton.Left) || !Hover(r)) return false;
        _leftUsed = true;
        return true;
    }

    /// <summary>Marks this frame's clicks as handled (e.g. by a modal dialog) so no other widget reacts.</summary>
    public static void ConsumeClicks() => _leftUsed = _rightUsed = true;

    public static bool RightClickIn(Rectangle r)
    {
        if (_rightUsed || !Raylib.IsMouseButtonPressed(MouseButton.Right) || !Hover(r)) return false;
        _rightUsed = true;
        return true;
    }

    // ------------------------------------------------------------------ fonts / text

    private static readonly Dictionary<(int, bool), Font> _fonts = new();
    private static string? _regularPath, _boldPath;
    private static bool _searched;

    private static readonly string[] RegularCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu-sans-fonts/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/liberation-sans/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
        "/usr/share/fonts/noto/NotoSans-Regular.ttf",
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
    ];

    private static readonly string[] BoldCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/dejavu-sans-fonts/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/liberation-sans/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf",
        "/usr/share/fonts/noto/NotoSans-Bold.ttf",
        @"C:\Windows\Fonts\segoeuib.ttf",
        @"C:\Windows\Fonts\arialbd.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/Library/Fonts/Arial Bold.ttf",
    ];

    /// <summary>ASCII + Latin-1 + a few typographic symbols used by the HUD (… — – ≈ → ×).</summary>
    private static readonly int[] Codepoints = Enumerable.Range(32, 95).Concat(Enumerable.Range(160, 96))
        .Concat([0x2026, 0x2014, 0x2013, 0x2248, 0x2192, 0x2022]).ToArray();

    private static bool HasTtf
    {
        get
        {
            if (!_searched)
            {
                _searched = true;
                _regularPath = RegularCandidates.FirstOrDefault(File.Exists);
                _boldPath = BoldCandidates.FirstOrDefault(File.Exists) ?? _regularPath;
            }
            return _regularPath != null;
        }
    }

    private static Font GetFont(int px, bool bold)
    {
        if (!HasTtf) return Raylib.GetFontDefault();
        if (_fonts.TryGetValue((px, bold), out var f)) return f;
        if (_fonts.Count > 48)
        {
            foreach (var old in _fonts.Values) Raylib.UnloadFont(old);
            _fonts.Clear();
        }
        f = Raylib.LoadFontEx(bold ? _boldPath! : _regularPath!, px, Codepoints, Codepoints.Length);
        if (!Raylib.IsFontValid(f)) { _regularPath = null; return Raylib.GetFontDefault(); }
        Raylib.SetTextureFilter(f.Texture, TextureFilter.Bilinear);
        _fonts[(px, bold)] = f;
        return f;
    }

    /// <summary>Draws text with its top-left at (x, y). size is in real pixels (use F()).</summary>
    public static void Text(string s, float x, float y, int size, Color c, bool bold = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (HasTtf) Raylib.DrawTextEx(GetFont(size, bold), s, new Vector2(MathF.Round(x), MathF.Round(y)), size, 0, c);
        else Raylib.DrawText(s, (int)x, (int)y, size, c);
    }

    /// <summary>Text with a 1px dark shadow (for text over the 3D world).</summary>
    public static void TextShadow(string s, float x, float y, int size, Color c, bool bold = false)
    {
        Text(s, x + 1, y + 1, size, new Color(0, 0, 0, (int)(c.A * 0.8f)), bold);
        Text(s, x, y, size, c, bold);
    }

    public static float Measure(string s, int size, bool bold = false)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (HasTtf) return Raylib.MeasureTextEx(GetFont(size, bold), s, size, 0).X;
        return Raylib.MeasureText(s, size);
    }

    public static void TextCentered(string s, Rectangle r, int size, Color c, bool bold = false)
    {
        float w = Measure(s, size, bold);
        Text(s, r.X + (r.Width - w) * 0.5f, r.Y + (r.Height - size) * 0.5f - 1, size, c, bold);
    }

    public static void TextRight(string s, float right, float y, int size, Color c, bool bold = false) =>
        Text(s, right - Measure(s, size, bold), y, size, c, bold);

    /// <summary>Shortens s with an ellipsis so it fits maxWidth.</summary>
    public static string Fit(string s, float maxWidth, int size, bool bold = false)
    {
        if (Measure(s, size, bold) <= maxWidth) return s;
        for (int n = s.Length - 1; n > 0; n--)
        {
            string t = s[..n].TrimEnd() + "…";
            if (Measure(t, size, bold) <= maxWidth) return t;
        }
        return "";
    }

    /// <summary>Greedy word wrap.</summary>
    public static List<string> Wrap(string s, float maxWidth, int size, bool bold = false)
    {
        var lines = new List<string>();
        foreach (var para in s.Split('\n'))
        {
            string cur = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string t = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && Measure(t, size, bold) > maxWidth) { lines.Add(cur); cur = word; }
                else cur = t;
            }
            lines.Add(cur);
        }
        return lines;
    }

    // ------------------------------------------------------------------ shapes / widgets

    public static Rectangle R(float x, float y, float w, float h) => new(MathF.Round(x), MathF.Round(y), MathF.Round(w), MathF.Round(h));

    public static Rectangle Inset(Rectangle r, float d) => new(r.X + d, r.Y + d, r.Width - 2 * d, r.Height - 2 * d);

    /// <summary>Framed panel with a subtle top highlight (SupCom-like).</summary>
    public static void Panel(Rectangle r)
    {
        Raylib.DrawRectangleRec(r, PanelBg);
        Raylib.DrawRectangleGradientV((int)r.X, (int)r.Y, (int)r.Width, (int)MathF.Min(r.Height, P(24)), new Color(50, 66, 86, 140), new Color(50, 66, 86, 0));
        Raylib.DrawRectangleLinesEx(r, 1, PanelBorder);
        Raylib.DrawLineEx(new Vector2(r.X + 1, r.Y + 1), new Vector2(r.X + r.Width - 1, r.Y + 1), 1, new Color(150, 180, 210, 90));
        // Corner brackets.
        float c = P(7);
        var e = PanelEdge;
        Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)c, 2, e);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 2, (int)c, e);
        Raylib.DrawRectangle((int)(r.X + r.Width - c), (int)r.Y, (int)c, 2, e);
        Raylib.DrawRectangle((int)(r.X + r.Width - 2), (int)r.Y, 2, (int)c, e);
    }

    /// <summary>Panel title in the top-left; returns the y below it.</summary>
    public static float PanelTitle(Rectangle r, string title, Color? color = null)
    {
        Text(title.ToUpperInvariant(), r.X + P(8), r.Y + P(5), F(11), color ?? TextDim, true);
        return r.Y + P(5) + F(11) + P(4);
    }

    /// <summary>Button frame + hit test. Content is drawn by the caller on top.</summary>
    public static ButtonState Button(Rectangle r, bool enabled = true, bool active = false, Color? tint = null)
    {
        bool hover = enabled && Hover(r);
        bool down = hover && Raylib.IsMouseButtonDown(MouseButton.Left);
        var baseCol = tint ?? new Color(44, 58, 76, 255);
        var top = enabled ? ColorUtil.Scale(baseCol, hover ? 1.35f : 1.1f) : new Color(32, 36, 42, 255);
        var bottom = enabled ? ColorUtil.Scale(baseCol, hover ? 0.85f : 0.65f) : new Color(24, 27, 32, 255);
        if (down) (top, bottom) = (bottom, top);
        Raylib.DrawRectangleGradientV((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, top, bottom);
        var border = active ? Accent : hover ? new Color(170, 200, 230, 255) : new Color(90, 110, 132, 255);
        Raylib.DrawRectangleLinesEx(r, active ? 2 : 1, border);
        bool left = enabled && LeftClickIn(r);
        bool right = enabled && RightClickIn(r);
        if (left || right) Audio.UiSounds.Click();
        return new ButtonState(hover, left, right);
    }

    /// <summary>Horizontal progress/health bar with optional centered label.</summary>
    public static void Bar(Rectangle r, float frac, Color fill, string? label = null, Color? back = null)
    {
        frac = Math.Clamp(float.IsNaN(frac) ? 0 : frac, 0, 1);
        Raylib.DrawRectangleRec(r, back ?? new Color(10, 12, 16, 255));
        if (frac > 0)
        {
            var top = ColorUtil.Scale(fill, 1.15f);
            var bot = ColorUtil.Scale(fill, 0.7f);
            Raylib.DrawRectangleGradientV((int)r.X, (int)r.Y, (int)MathF.Max(1, r.Width * frac), (int)r.Height, top, bot);
        }
        Raylib.DrawRectangleLinesEx(r, 1, new Color(0, 0, 0, 200));
        if (label != null)
        {
            int fs = Math.Max(9, (int)(r.Height - 3));
            fs = Math.Min(fs, F(12));
            float w = Measure(label, fs, true);
            float tx = r.X + (r.Width - w) * 0.5f, ty = r.Y + (r.Height - fs) * 0.5f - 1;
            Text(label, tx + 1, ty + 1, fs, new Color(0, 0, 0, 150), true);
            Text(label, tx, ty, fs, Color.White, true);
        }
    }

    /// <summary>Small hotkey badge in the top-right corner of a rectangle.</summary>
    public static void HotkeyBadge(Rectangle r, string key)
    {
        int fs = F(10);
        float w = Measure(key, fs, true) + P(6);
        var b = new Rectangle(r.X + r.Width - w - 2, r.Y + 2, w, fs + P(3));
        Raylib.DrawRectangleRec(b, new Color(0, 0, 0, 170));
        Text(key, b.X + P(3), b.Y + P(1), fs, new Color(255, 230, 150, 255), true);
    }

    // ------------------------------------------------------------------ tooltips

    /// <summary>Requests a tooltip for this frame (the last request wins). Drawn by <see cref="DrawTooltip"/>.</summary>
    public static void Tooltip(Rectangle anchor, IEnumerable<TipLine> lines, bool above = true, float maxWidth = 340)
    {
        _tip.Clear();
        _tip.AddRange(lines);
        _tipAnchor = anchor;
        _tipAbove = above;
        _tipMaxW = maxWidth;
    }

    public static void Tooltip(Rectangle anchor, string title, string body, bool above = true)
    {
        var lines = new List<TipLine> { new(title, TextColor, 14, true) };
        foreach (var l in body.Split('\n')) lines.Add(new TipLine(l, TextDim, 12));
        Tooltip(anchor, lines, above);
    }

    public static void DrawTooltip()
    {
        if (_tip.Count == 0) return;
        float maxW = MathF.Min(P(_tipMaxW), Sw - 40);
        float pad = P(8);
        // Wrap long lines.
        var rows = new List<(string text, TipLine line)>();
        foreach (var l in _tip)
        {
            int fs = F(l.Size);
            if (l.Text.Length == 0 && l.Segments == null) { rows.Add(("", l)); continue; }
            if (l.Segments != null) { rows.Add((l.Text, l)); continue; }
            foreach (var w in Wrap(l.Text, maxW, fs, l.Bold)) rows.Add((w, l));
        }
        float width = 0, height = 0;
        foreach (var (t, l) in rows)
        {
            int fs = F(l.Size);
            float lw = Measure(t, fs, l.Bold);
            if (l.Segments != null) foreach (var seg in l.Segments) lw += Measure(seg.text, fs, l.Bold);
            width = MathF.Max(width, lw);
            height += t.Length == 0 && l.Segments == null ? P(5) : fs + P(3);
        }
        width += pad * 2;
        height += pad * 2 - P(3);
        float x = _tipAnchor.X + _tipAnchor.Width * 0.5f - width * 0.5f;
        float y = _tipAbove ? _tipAnchor.Y - height - P(6) : _tipAnchor.Y + _tipAnchor.Height + P(6);
        if (_tipAbove && y < P(36)) y = _tipAnchor.Y + _tipAnchor.Height + P(6);
        if (!_tipAbove && y + height > Sh) y = _tipAnchor.Y - height - P(6);
        x = Math.Clamp(x, 4, Sw - width - 4);
        y = Math.Clamp(y, 4, Sh - height - 4);
        var r = new Rectangle(MathF.Round(x), MathF.Round(y), MathF.Round(width), MathF.Round(height));
        Raylib.DrawRectangleRec(new Rectangle(r.X + 3, r.Y + 3, r.Width, r.Height), new Color(0, 0, 0, 90));
        Raylib.DrawRectangleRec(r, new Color(12, 17, 24, 245));
        Raylib.DrawRectangleLinesEx(r, 1, Accent);
        float cy = r.Y + pad;
        foreach (var (t, l) in rows)
        {
            int fs = F(l.Size);
            if (t.Length == 0 && l.Segments == null) { cy += P(5); continue; }
            Text(t, r.X + pad, cy, fs, l.Color, l.Bold);
            if (l.Segments != null)
            {
                float sx = r.X + pad + Measure(t, fs, l.Bold);
                foreach (var seg in l.Segments)
                {
                    Text(seg.text, sx, cy, fs, seg.color, l.Bold);
                    sx += Measure(seg.text, fs, l.Bold);
                }
            }
            cy += fs + P(3);
        }
    }

    // ------------------------------------------------------------------ formatting

    public static string Num(float v)
    {
        float a = MathF.Abs(v);
        if (a >= 100000) return (v / 1000f).ToString("0") + "k";
        if (a >= 10000) return (v / 1000f).ToString("0.#") + "k";
        if (a >= 100) return v.ToString("0");
        return v.ToString("0.#");
    }

    public static string Seconds(float s)
    {
        if (s >= 60) return $"{(int)(s / 60)}m {(int)(s % 60):00}s";
        return s >= 10 ? $"{s:0}s" : $"{s:0.#}s";
    }
}
