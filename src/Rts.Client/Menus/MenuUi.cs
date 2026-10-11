using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>
/// Menu widgets on top of the HUD toolkit (<see cref="Ui"/>): big buttons, cycle buttons (left click next,
/// right click previous), segmented choices, sliders, a numeric text field, toggles and headings. Immediate mode:
/// call <see cref="Ui.BeginDraw"/> at the start of the menu frame and <see cref="Ui.DrawTooltip"/> at the end.
/// </summary>
public static class MenuUi
{
    public static readonly Color PrimaryTint = new(38, 88, 146, 255);
    public static readonly Color DangerTint = new(110, 42, 38, 255);
    private static int _activeSlider = -1;
    private static int _focusedField = -1;

    /// <summary>Automated screenshot runs: menus ignore real keyboard/mouse input (stray desktop input).</summary>
    public static bool Automated;

    /// <summary>Start of a menu/overlay frame: resets the UI toolkit; swallows clicks in automated runs.</summary>
    public static void BeginFrame()
    {
        Ui.BeginDraw();
        if (Automated) Ui.ConsumeClicks();
    }

    /// <summary>Key shortcut for menus (always false in automated runs).</summary>
    public static bool Key(KeyboardKey k) => !Automated && Raylib.IsKeyPressed(k);

    public static bool BigButton(Rectangle r, string label, bool enabled = true, bool primary = false, float size = 15)
    {
        var st = Ui.Button(r, enabled, false, primary ? PrimaryTint : null);
        Ui.TextCentered(label, r, Ui.F(size), enabled ? Color.White : Ui.TextFaint, true);
        return st.Left;
    }

    /// <summary>Main-menu style entry: left-aligned label with an accent bar on hover.</summary>
    public static bool MenuEntry(Rectangle r, string label, string? hint = null, bool enabled = true)
    {
        bool hover = enabled && Ui.Hover(r);
        var bg = hover ? new Color(40, 62, 88, 220) : new Color(14, 20, 28, 170);
        Raylib.DrawRectangleRec(r, bg);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)Ui.P(hover ? 5 : 3), (int)r.Height, hover ? Ui.Accent : Ui.PanelBorder);
        Raylib.DrawRectangleLinesEx(r, 1, hover ? new Color(120, 170, 220, 200) : new Color(60, 78, 98, 200));
        int fs = Ui.F(19);
        Ui.Text(label.ToUpperInvariant(), r.X + Ui.P(18), r.Y + (r.Height - fs) * 0.5f - (hint != null ? Ui.P(6) : 1), fs, enabled ? Ui.TextColor : Ui.TextFaint, true);
        if (hint != null) Ui.Text(hint, r.X + Ui.P(18), r.Y + r.Height * 0.5f + Ui.P(5), Ui.F(11), Ui.TextDim);
        return enabled && Ui.LeftClickIn(r);
    }

    /// <summary>Cycle button: returns +1 on left click, -1 on right click, 0 otherwise.</summary>
    public static int Cycle(Rectangle r, string text, bool enabled = true, Color? textColor = null)
    {
        var st = Ui.Button(r, enabled);
        int fs = Ui.F(12.5f);
        var c = enabled ? textColor ?? Ui.TextColor : Ui.TextFaint;
        Ui.TextCentered(text, r, fs, c, true);
        if (enabled && st.Hover)
        {
            Ui.Text("<", r.X + Ui.P(5), r.Y + (r.Height - fs) * 0.5f - 1, fs, Ui.TextDim, true);
            Ui.TextRight(">", r.X + r.Width - Ui.P(5), r.Y + (r.Height - fs) * 0.5f - 1, fs, Ui.TextDim, true);
        }
        return st.Left ? 1 : st.Right ? -1 : 0;
    }

    /// <summary>Row of mutually exclusive buttons; returns the (possibly new) selected index.</summary>
    public static int Segmented(Rectangle r, string[] labels, int selected, bool enabled = true, float fontSize = 12.5f)
    {
        float gap = Ui.P(4);
        float w = (r.Width - gap * (labels.Length - 1)) / labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            var b = Ui.R(r.X + i * (w + gap), r.Y, w, r.Height);
            bool sel = i == selected;
            var st = Ui.Button(b, enabled, sel, sel ? PrimaryTint : null);
            Ui.TextCentered(labels[i], b, Ui.F(fontSize), sel ? Color.White : enabled ? Ui.TextColor : Ui.TextFaint, sel);
            if (st.Left) selected = i;
        }
        return selected;
    }

    /// <summary>Horizontal slider 0..1 with a value label; id must be unique per slider.</summary>
    public static float Slider(int id, Rectangle r, float value, string? label = null)
    {
        var m = Ui.Mouse;
        var hit = new Rectangle(r.X - Ui.P(6), r.Y - Ui.P(6), r.Width + Ui.P(12), r.Height + Ui.P(12));
        if (_activeSlider < 0 && Raylib.IsMouseButtonPressed(MouseButton.Left) && Ui.Hover(hit) && Ui.LeftClickIn(hit)) _activeSlider = id;
        if (_activeSlider == id)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left)) value = Math.Clamp((m.X - r.X) / r.Width, 0f, 1f);
            else _activeSlider = -1;
        }
        Ui.Bar(r, value, Ui.Accent);
        float kx = r.X + r.Width * value;
        var knob = new Rectangle(kx - Ui.P(5), r.Y - Ui.P(4), Ui.P(10), r.Height + Ui.P(8));
        bool hot = _activeSlider == id || Ui.Hover(hit);
        Raylib.DrawRectangleRec(knob, hot ? new Color(220, 235, 250, 255) : new Color(170, 190, 210, 255));
        Raylib.DrawRectangleLinesEx(knob, 1, new Color(20, 26, 34, 255));
        if (label != null) Ui.Text(label, r.X + r.Width + Ui.P(12), r.Y + (r.Height - Ui.F(12)) * 0.5f - 1, Ui.F(12), Ui.TextColor, true);
        return value;
    }

    /// <summary>Single-line numeric text field. Returns true when the text changed.</summary>
    public static bool NumberField(int id, Rectangle r, ref string text, int maxLen = 9)
    {
        bool changed = false;
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Ui.Hover(r)) { _focusedField = id; Ui.LeftClickIn(r); }
            else if (_focusedField == id) _focusedField = -1;
        }
        bool focused = _focusedField == id;
        if (focused)
        {
            for (int ch = Raylib.GetCharPressed(); ch != 0; ch = Raylib.GetCharPressed())
            {
                if (ch >= '0' && ch <= '9' && text.Length < maxLen) { text += (char)ch; changed = true; }
            }
            if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && text.Length > 0)
            {
                text = text[..^1];
                changed = true;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) _focusedField = -1;
        }
        Raylib.DrawRectangleRec(r, new Color(8, 11, 16, 255));
        Raylib.DrawRectangleLinesEx(r, focused ? 2 : 1, focused ? Ui.Accent : Ui.PanelBorder);
        int fs = Ui.F(13);
        float ty = r.Y + (r.Height - fs) * 0.5f - 1;
        Ui.Text(text, r.X + Ui.P(8), ty, fs, Ui.TextColor, true);
        if (focused && (int)(Raylib.GetTime() * 2) % 2 == 0)
        {
            float cx = r.X + Ui.P(9) + Ui.Measure(text, fs, true);
            Raylib.DrawRectangle((int)cx, (int)(ty + 1), 2, fs, Ui.Accent);
        }
        return changed;
    }

    public static bool FieldFocused => _focusedField >= 0;

    /// <summary>On/off toggle button.</summary>
    public static bool Toggle(Rectangle r, bool on)
    {
        var st = Ui.Button(r, true, on, on ? PrimaryTint : null);
        Ui.TextCentered(on ? "ON" : "OFF", r, Ui.F(12.5f), on ? Color.White : Ui.TextDim, true);
        return st.Left ? !on : on;
    }

    /// <summary>Small caps label (left of a control).</summary>
    public static void Label(string s, float x, float y, float h)
    {
        int fs = Ui.F(11.5f);
        Ui.Text(s.ToUpperInvariant(), x, y + (h - fs) * 0.5f - 1, fs, Ui.TextDim, true);
    }

    /// <summary>Big screen title with an accent underline.</summary>
    public static void ScreenTitle(string title, string? subtitle, float x, float y)
    {
        int fs = Ui.F(30);
        Ui.TextShadow(title.ToUpperInvariant(), x, y, fs, Ui.TextColor, true);
        float w = Ui.Measure(title.ToUpperInvariant(), fs, true);
        Raylib.DrawRectangle((int)x, (int)(y + fs + Ui.P(6)), (int)MathF.Min(w, Ui.P(120)), (int)Ui.P(3), Ui.Accent);
        if (subtitle != null) Ui.TextShadow(subtitle, x, y + fs + Ui.P(14), Ui.F(13), Ui.TextDim);
    }

    /// <summary>Darkens the screen (modal backdrop).</summary>
    public static void Dim(byte alpha = 150) => Raylib.DrawRectangle(0, 0, Ui.Sw, Ui.Sh, new Color(4, 7, 11, (int)alpha));

    public static void ColorSwatch(Rectangle r, Color c, bool hover)
    {
        Raylib.DrawRectangleRec(r, c);
        Raylib.DrawRectangleGradientV((int)r.X, (int)r.Y, (int)r.Width, (int)(r.Height * 0.5f), new Color(255, 255, 255, 60), new Color(255, 255, 255, 0));
        Raylib.DrawRectangleLinesEx(r, hover ? 2 : 1, hover ? Color.White : new Color(0, 0, 0, 200));
    }

    /// <summary>Spinner for loading screens.</summary>
    public static void Spinner(Vector2 c, float radius, Color col)
    {
        float t = (float)Raylib.GetTime();
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathF.Tau + t * 5f;
            float k = ((i + (int)(t * 12)) % 12) / 12f;
            Raylib.DrawCircleV(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, Ui.P(2.6f), ColorUtil.WithAlpha(col, (byte)(60 + 195 * k)));
        }
    }
}
