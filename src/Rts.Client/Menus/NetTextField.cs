using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>
/// Single-line text input for the multiplayer screens (player name, address, port, lobby/in-game chat): click to
/// focus (click elsewhere unfocuses), typing at the end, Backspace with repeat, Ctrl+Backspace deletes a word,
/// Ctrl+V pastes, Enter = submit, Esc = unfocus. Ignores the keyboard in automated runs (<see cref="MenuUi.Automated"/>).
/// </summary>
public sealed class NetTextField
{
    public string Text;
    public bool Focused;
    public int MaxLength;
    /// <summary>Optional character filter (null = any printable character).</summary>
    public Func<char, bool>? Allow;
    /// <summary>The key that focused the field may still be in the char queue: drop it once.</summary>
    private bool _skipChars;

    public NetTextField(string text = "", int maxLength = 64, Func<char, bool>? allow = null)
    {
        Text = text;
        MaxLength = maxLength;
        Allow = allow;
    }

    public void Focus()
    {
        if (!Focused) _skipChars = true;
        Focused = true;
    }

    /// <summary>Mouse focus handling + this frame's typing. Returns true when Enter was pressed while focused.</summary>
    public bool Update(Rectangle r, bool enabled = true)
    {
        if (!enabled) { Focused = false; return false; }
        if (!MenuUi.Automated && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Ui.Hover(r)) { Focus(); Ui.LeftClickIn(r); }
            else Focused = false;
        }
        return HandleKeys();
    }

    /// <summary>Keyboard only (the owner decides when the field is focused, e.g. the in-game chat line).</summary>
    public bool HandleKeys()
    {
        if (!Focused || MenuUi.Automated) return false;
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (_skipChars)
        {
            _skipChars = false;
            while (Raylib.GetCharPressed() != 0) { }
        }
        else
        {
            for (int ch = Raylib.GetCharPressed(); ch != 0; ch = Raylib.GetCharPressed())
            {
                if (ch < 32 || ch == 127 || ch > 0xFFFF) continue;
                char c = (char)ch;
                if (Allow != null && !Allow(c)) continue;
                if (Text.Length < MaxLength) Text += c;
            }
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
        {
            if (ctrl)
            {
                string t = Text.TrimEnd();
                int i = t.LastIndexOf(' ');
                Text = i < 0 ? "" : t[..(i + 1)];
            }
            else if (Text.Length > 0) Text = Text[..^1];
        }
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.V))
        {
            string clip = (Raylib.GetClipboardText_() ?? "").Replace("\r", "").Replace('\n', ' ');
            foreach (char c in clip)
                if (Text.Length < MaxLength && !char.IsControl(c) && (Allow == null || Allow(c))) Text += c;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Focused = false;
        return Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
    }

    /// <summary>Frame, text (the end of long text), blinking caret, placeholder when empty and unfocused.</summary>
    public void Draw(Rectangle r, string placeholder = "", string prefix = "", float fontSize = 13, bool enabled = true)
    {
        Raylib.DrawRectangleRec(r, Focused ? new Color(8, 12, 18, 240) : new Color(10, 14, 20, 225));
        Raylib.DrawRectangleLinesEx(r, Focused ? 2 : 1, Focused ? Ui.Accent : Ui.PanelBorder);
        int fs = Ui.F(fontSize);
        float x = r.X + Ui.P(8), y = r.Y + (r.Height - fs) * 0.5f - 1;
        if (prefix.Length > 0)
        {
            Ui.Text(prefix, x, y, fs, Ui.Accent, true);
            x += Ui.Measure(prefix, fs, true) + Ui.P(4);
        }
        float avail = r.X + r.Width - Ui.P(8) - x;
        if (Text.Length == 0 && !Focused)
        {
            Ui.Text(Ui.Fit(placeholder, avail, fs), x, y, fs, Ui.TextFaint);
            return;
        }
        string shown = Text;
        while (shown.Length > 0 && Ui.Measure(shown, fs) > avail - Ui.P(6)) shown = shown[1..];
        Ui.Text(shown, x, y, fs, enabled ? Ui.TextColor : Ui.TextFaint);
        if (Focused && Raylib.GetTime() % 1.0 < 0.55)
        {
            float cx = x + Ui.Measure(shown, fs) + 1;
            Raylib.DrawRectangle((int)cx, (int)(y + 1), Math.Max(1, (int)Ui.P(1.5f)), fs, Ui.Accent);
        }
    }

    public static bool IsDigit(char c) => c is >= '0' and <= '9';
    public static bool IsAddressChar(char c) => char.IsLetterOrDigit(c) || c is '.' or ':' or '-' or '[' or ']' or '_';
    public static bool IsNameChar(char c) => !char.IsControl(c) && c != '`';
}
