using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Sandbox;

/// <summary>
/// Drop-down text console of the sandbox (F10 / the key left of 1): output log, input line with history (Up/Down),
/// Tab completion and live suggestions from <see cref="SandboxConsoleParser"/>. Executing a line is up to the
/// owner (<see cref="SandboxController"/>), which gets it from <see cref="Update"/>.
/// </summary>
public sealed class SandboxConsole
{
    public enum Kind { Input, Info, Ok, Error }

    private readonly List<(string text, Kind kind)> _log = new();
    private readonly List<string> _history = new();
    private int _historyPos = -1;
    private readonly SandboxTextField _input = new();
    private List<string> _suggestions = new();
    private string _suggestFor = "\0";
    public bool Open;
    public Rectangle Rect;

    public bool Focused => Open && _input.Focused;

    public void Show(bool focus = true)
    {
        Open = true;
        if (focus) _input.Focus();
    }

    public void Hide()
    {
        Open = false;
        _input.Focused = false;
    }

    public void Unfocus() => _input.Focused = false;

    public void Print(string text, Kind kind = Kind.Info)
    {
        foreach (var line in text.Split('\n')) _log.Add((line, kind));
        if (_log.Count > 400) _log.RemoveRange(0, _log.Count - 400);
    }

    public void ClearLog() => _log.Clear();

    public void Layout()
    {
        float top = Ui.P(34);
        float w = MathF.Min(Ui.Sw - Ui.P(320), Ui.P(760));
        Rect = Ui.R(Ui.P(8), top, w, MathF.Min(Ui.Sh * 0.42f, Ui.P(300)));
    }

    /// <summary>Keyboard work for this frame. Returns the submitted line (Enter) or null.</summary>
    public string? Update(ConsoleContext ctx)
    {
        if (!Open) return null;
        Layout();
        // Click inside focuses, click outside gives the keyboard back to the game (the console stays visible).
        if (Raylib.IsMouseButtonPressed(MouseButton.Left)) { if (Ui.Hover(Rect)) _input.Focus(); else _input.Focused = false; }
        if (!_input.Focused) return null;

        if (Raylib.IsKeyPressed(KeyboardKey.Up) && _history.Count > 0)
        {
            _historyPos = _historyPos < 0 ? _history.Count - 1 : Math.Max(0, _historyPos - 1);
            _input.Text = _history[_historyPos];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Down) && _historyPos >= 0)
        {
            _historyPos++;
            if (_historyPos >= _history.Count) { _historyPos = -1; _input.Text = ""; }
            else _input.Text = _history[_historyPos];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            var done = SandboxConsoleParser.Complete(_input.Text, ctx);
            if (done != null) _input.Text = done;
        }
        if (_input.HandleInput())
        {
            string line = _input.Text.Trim();
            _input.Text = "";
            _historyPos = -1;
            if (line.Length == 0) return null;
            if (_history.Count == 0 || _history[^1] != line) _history.Add(line);
            if (_history.Count > 100) _history.RemoveAt(0);
            return line;
        }
        if (_suggestFor != _input.Text)
        {
            _suggestFor = _input.Text;
            _suggestions = _input.Text.Trim().Length == 0 ? new() : SandboxConsoleParser.Suggestions(_input.Text, ctx, 6);
        }
        return null;
    }

    public void Draw()
    {
        if (!Open) return;
        Layout();
        var r = Rect;
        byte alpha = (byte)(_input.Focused ? 235 : 170);
        Raylib.DrawRectangleRec(r, new Color(6, 10, 16, (int)alpha));
        Raylib.DrawRectangleLinesEx(r, 1, _input.Focused ? Ui.Accent : Ui.PanelBorder);
        int fs = Ui.F(12);
        float lh = fs + Ui.P(3);
        Ui.Text("SANDBOX CONSOLE", r.X + Ui.P(8), r.Y + Ui.P(5), Ui.F(10), Ui.TextFaint, true);
        Ui.TextRight("help · Tab complete · Up/Down history · F10 or ` close", r.X + r.Width - Ui.P(8), r.Y + Ui.P(5), Ui.F(10), Ui.TextFaint);

        float inputH = Ui.P(24);
        var inR = Ui.R(r.X + Ui.P(6), r.Y + r.Height - inputH - Ui.P(6), r.Width - Ui.P(12), inputH);
        // Log, newest at the bottom.
        float y = inR.Y - Ui.P(4) - lh;
        float minY = r.Y + Ui.P(20);
        for (int i = _log.Count - 1; i >= 0 && y >= minY; i--)
        {
            var (text, kind) = _log[i];
            var c = kind switch { Kind.Input => Ui.Accent, Kind.Ok => Ui.Good, Kind.Error => Ui.Bad, _ => Ui.TextColor };
            Ui.Text(Ui.Fit(kind == Kind.Input ? "> " + text : text, r.Width - Ui.P(16), fs), r.X + Ui.P(8), y, fs, c);
            y -= lh;
        }
        _input.Draw(inR, "type a command (help)… click here to type", ">");

        // Suggestions drop below the console.
        if (_input.Focused && _suggestions.Count > 0)
        {
            float sh = _suggestions.Count * lh + Ui.P(8);
            var sr = Ui.R(r.X, r.Y + r.Height + Ui.P(2), r.Width, sh);
            Raylib.DrawRectangleRec(sr, new Color(10, 16, 24, 230));
            Raylib.DrawRectangleLinesEx(sr, 1, Ui.PanelBorder);
            float sy = sr.Y + Ui.P(4);
            foreach (var s in _suggestions)
            {
                Ui.Text(Ui.Fit(s, sr.Width - Ui.P(16), fs), sr.X + Ui.P(8), sy, fs, Ui.TextDim);
                sy += lh;
            }
        }
    }

    /// <summary>True if the mouse is over the console (or its suggestion box).</summary>
    public bool MouseOver()
    {
        if (!Open) return false;
        Layout();
        var r = Rect;
        if (_input.Focused && _suggestions.Count > 0) r.Height += (_suggestions.Count + 1) * (Ui.F(12) + Ui.P(3));
        return Ui.Hover(r);
    }
}
