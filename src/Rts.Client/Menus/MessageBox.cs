using Raylib_cs;
using Rts.Client.UI;

namespace Rts.Client.Menus;

/// <summary>Modal message box (errors, confirmations) drawn over the current screen.</summary>
public static class MessageBox
{
    /// <summary>
    /// Draws the box; returns the index of the clicked button (-1 = none this frame). Enter = button 0, Esc = the last
    /// button. Clicks elsewhere are swallowed. <paramref name="danger"/>: button 0 gets the danger tint.
    /// </summary>
    public static int Draw(string title, string message, string[] buttons, bool error = false, bool danger = false)
    {
        MenuUi.Dim(160);
        float w = MathF.Min(Ui.P(520), Ui.Sw - Ui.P(40));
        int fs = Ui.F(13);
        var lines = Ui.Wrap(message, w - Ui.P(44), fs);
        float h = Ui.P(58) + lines.Count * (fs + Ui.P(5)) + Ui.P(70);
        var p = Ui.R((Ui.Sw - w) * 0.5f, (Ui.Sh - h) * 0.42f, w, h);
        Ui.Panel(p);
        Raylib.DrawRectangle((int)p.X, (int)p.Y, (int)p.Width, (int)Ui.P(3), error ? Ui.Bad : Ui.Accent);
        Ui.Text(title, p.X + Ui.P(22), p.Y + Ui.P(18), Ui.F(17), error ? Ui.Bad : Ui.TextColor, true);
        float y = p.Y + Ui.P(52);
        foreach (var l in lines)
        {
            Ui.Text(l, p.X + Ui.P(22), y, fs, Ui.TextDim);
            y += fs + Ui.P(5);
        }
        int result = -1;
        float bw = Ui.P(130), bh = Ui.P(38), gap = Ui.P(10);
        float bx = p.X + p.Width - Ui.P(20) - buttons.Length * bw - (buttons.Length - 1) * gap;
        for (int i = 0; i < buttons.Length; i++)
        {
            var r = Ui.R(bx + i * (bw + gap), p.Y + p.Height - bh - Ui.P(18), bw, bh);
            var st = Ui.Button(r, true, false, i == 0 ? (danger ? MenuUi.DangerTint : MenuUi.PrimaryTint) : null);
            Ui.TextCentered(buttons[i], r, Ui.F(14), Color.White, true);
            if (st.Left) result = i;
        }
        if (result < 0 && (MenuUi.Key(KeyboardKey.Enter) || MenuUi.Key(KeyboardKey.KpEnter))) result = 0;
        if (result < 0 && MenuUi.Key(KeyboardKey.Escape)) result = buttons.Length - 1;
        Ui.ConsumeClicks();
        return result;
    }
}
