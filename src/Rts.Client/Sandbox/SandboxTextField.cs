using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Sandbox;

/// <summary>
/// Minimal single-line text input for the sandbox console and the panel's search box: typing at the end, Backspace
/// (with key repeat), Ctrl+Backspace / Ctrl+W deletes a word, Ctrl+U clears, Ctrl+V pastes. Only reads the keyboard
/// while <see cref="Focused"/>; the owner blocks game hotkeys meanwhile (see <see cref="SandboxController"/>).
/// </summary>
public sealed class SandboxTextField
{
    public string Text = "";
    public bool Focused;
    public int MaxLength = 160;
    /// <summary>Frame the field got focus: the key that opened it may still be in the char queue, ignore it.</summary>
    private bool _skipChars;

    public void Focus()
    {
        if (!Focused) _skipChars = true;
        Focused = true;
    }

    /// <summary>Reads this frame's typing. Returns true when Enter was pressed.</summary>
    public bool HandleInput()
    {
        if (!Focused) return false;
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
                if (ch < 32 || ch == 127 || ch == '`') continue; // ` toggles the console
                if (Text.Length < MaxLength) Text += char.ConvertFromUtf32(ch);
            }
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
        {
            if (ctrl) DeleteWord();
            else if (Text.Length > 0) Text = Text[..^1];
        }
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.W)) DeleteWord();
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.U)) Text = "";
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.V))
        {
            string clip = (Raylib.GetClipboardText_() ?? "").Replace('\n', ';').Replace("\r", "");
            Text = (Text + clip).Length > MaxLength ? (Text + clip)[..MaxLength] : Text + clip;
        }
        return Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter);
    }

    private void DeleteWord()
    {
        string t = Text.TrimEnd();
        int i = t.LastIndexOf(' ');
        Text = i < 0 ? "" : t[..(i + 1)];
    }

    /// <summary>Draws the field (frame, text, blinking caret, placeholder when empty).</summary>
    public void Draw(Rectangle r, string placeholder, string prefix = "")
    {
        Raylib.DrawRectangleRec(r, Focused ? new Color(8, 12, 18, 235) : new Color(14, 19, 26, 220));
        Raylib.DrawRectangleLinesEx(r, 1, Focused ? Ui.Accent : Ui.PanelBorder);
        int fs = Ui.F(12);
        float x = r.X + Ui.P(6), y = r.Y + (r.Height - fs) * 0.5f - 1;
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
        // Show the end of long text.
        string shown = Text;
        while (shown.Length > 0 && Ui.Measure(shown, fs) > avail - Ui.P(6)) shown = shown[1..];
        Ui.Text(shown, x, y, fs, Ui.TextColor);
        if (Focused && (Raylib.GetTime() % 1.0) < 0.55)
        {
            float cx = x + Ui.Measure(shown, fs) + 1;
            Raylib.DrawRectangle((int)cx, (int)(y + 1), Math.Max(1, (int)Ui.P(1.5f)), fs, Ui.Accent);
        }
    }
}
