using System.Collections.Concurrent;
using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Persistence;

/// <summary>Short notifications ("Game saved") at the top of the screen. <see cref="Show"/> from any thread
/// (save writers run on workers); <see cref="Draw"/> on the render thread.</summary>
public static class Toasts
{
    private static readonly ConcurrentQueue<(string text, bool error)> _incoming = new();
    private static readonly List<(string text, bool error, double t)> _shown = new();
    private const double Duration = 3.0;

    public static void Show(string text, bool error = false)
    {
        _incoming.Enqueue((text, error));
        Console.WriteLine((error ? "ERROR: " : "") + text);
    }

    public static void Clear()
    {
        _incoming.Clear();
        _shown.Clear();
    }

    public static void Draw()
    {
        double now = Raylib.GetTime();
        while (_incoming.TryDequeue(out var m)) _shown.Add((m.text, m.error, now));
        _shown.RemoveAll(m => now - m.t > Duration);
        if (_shown.Count > 4) _shown.RemoveRange(0, _shown.Count - 4);
        float y = Ui.P(40);
        int fs = Ui.F(13);
        foreach (var (text, error, t) in _shown)
        {
            float a = (float)Math.Clamp((Duration - (now - t)) / 0.4, 0, 1);
            float w = Ui.Measure(text, fs, true) + Ui.P(28);
            var r = Ui.R((Ui.Sw - w) * 0.5f, y, w, fs + Ui.P(14));
            Raylib.DrawRectangleRec(r, new Color(12, 18, 26, (int)(225 * a)));
            Raylib.DrawRectangleLinesEx(r, 1, ColorUtil.WithAlpha(error ? Ui.Bad : Ui.Accent, (byte)(220 * a)));
            Ui.TextCentered(text, r, fs, ColorUtil.WithAlpha(error ? Ui.Bad : Ui.TextColor, (byte)(255 * a)), true);
            y += r.Height + Ui.P(6);
        }
    }
}
