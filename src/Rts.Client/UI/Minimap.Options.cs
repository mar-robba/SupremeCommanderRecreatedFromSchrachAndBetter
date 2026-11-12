using System.Numerics;
using Raylib_cs;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// (9b-2) Minimap options: a row of small buttons along the top edge while the mouse is over the minimap (size -/+,
/// zoom -/+, layers Resources / Units / Trees), the mouse wheel zooms around the camera, and the optional tree layer.
/// Every change goes to <see cref="Menus.AppSettings"/> (saved at once; the settings tab "Minimap" edits the same
/// values). Size and zoom maths: <see cref="MinimapView"/>.
/// </summary>
public sealed partial class Minimap
{
    /// <summary>Debug / screenshots (<c>--minimap buttons=on</c>): draw the buttons without hovering.</summary>
    public static bool ButtonsAlways;

    private static readonly string[] ButtonLabels = ["-", "+", "Z-", "Z+", "R", "U", "T"];
    private static readonly string[] ButtonTips =
    [
        "Smaller minimap", "Larger minimap", "Zoom out (mouse wheel)", "Zoom in around the camera (mouse wheel)",
        "Show / hide resource deposits", "Show / hide units", "Show / hide trees",
    ];

    private Rectangle ButtonRect(Rectangle vis, int i)
    {
        float b = Ui.P(15), gap = Ui.P(2);
        float x0 = vis.X + vis.Width - ButtonLabels.Length * (b + gap) - Ui.P(2);
        return new Rectangle(MathF.Round(x0 + i * (b + gap)), vis.Y + Ui.P(3), b, b);
    }

    private static bool ButtonOn(int i)
    {
        var s = Menus.AppSettings.Current;
        return i switch { 4 => s.MinimapResources, 5 => s.MinimapUnits, 6 => s.MinimapTrees, _ => false };
    }

    /// <summary>Wheel zoom and button clicks (before the map input, so a button click never moves the camera).
    /// Returns true when the mouse is over a button.</summary>
    private bool HandleButtons(Rectangle panel, Rectangle vis)
    {
        var s = Menus.AppSettings.Current;
        bool over = Ui.Hover(panel);
        if (over && !_dragging)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
            {
                float z = MinimapView.StepZoom(s.MinimapZoom, Math.Sign(wheel));
                if (z != s.MinimapZoom) { s.MinimapZoom = z; s.Save(); }
            }
        }
        if (!over || _dragging) return false;
        bool hover = false;
        for (int i = 0; i < ButtonLabels.Length; i++)
        {
            var r = ButtonRect(vis, i);
            if (!Ui.Hover(r)) continue;
            hover = true;
            Ui.Tooltip(r, "Minimap", ButtonTips[i]);
            if (!Ui.LeftClickIn(r)) continue;
            switch (i)
            {
                case 0: s.MinimapSize = MinimapView.StepSize(s.MinimapSize, -1); break;
                case 1: s.MinimapSize = MinimapView.StepSize(s.MinimapSize, +1); break;
                case 2: s.MinimapZoom = MinimapView.StepZoom(s.MinimapZoom, -1); break;
                case 3: s.MinimapZoom = MinimapView.StepZoom(s.MinimapZoom, +1); break;
                case 4: s.MinimapResources = !s.MinimapResources; break;
                case 5: s.MinimapUnits = !s.MinimapUnits; break;
                case 6: s.MinimapTrees = !s.MinimapTrees; break;
            }
            s.Save();
        }
        return hover;
    }

    private void DrawButtons(Rectangle panel, Rectangle vis)
    {
        float zoom = Menus.AppSettings.Current.MinimapZoom;
        if (zoom > 1.0001f)
            Ui.TextShadow($"{zoom:0.#}×", vis.X + Ui.P(4), vis.Y + vis.Height - Ui.F(11) - Ui.P(3), Ui.F(11), Ui.TextColor, true);
        if (!ButtonsAlways && (!Ui.Hover(panel) || _dragging)) return;
        for (int i = 0; i < ButtonLabels.Length; i++)
        {
            var r = ButtonRect(vis, i);
            bool on = ButtonOn(i), hot = Ui.Hover(r);
            Raylib.DrawRectangleRec(r, hot ? Ui.PanelBgLight : Ui.PanelBg);
            Raylib.DrawRectangleLinesEx(r, 1, on ? Ui.Accent : Ui.PanelBorder);
            Ui.TextCentered(ButtonLabels[i], r, Ui.F(10), i >= 4 && !on ? Ui.TextFaint : Ui.TextColor, true);
        }
    }

    /// <summary>Tree layer: one dark dot per minimap pixel (the dedupe grid of the unit dots, its own stamp).</summary>
    private void DrawTrees(Rectangle m, Rectangle vis, int gw, int gh)
    {
        var snap = _ctx.Snapshots.Current;
        int stamp = ++_dotFrame;
        float k = m.Width / _ctx.Terrain.Width, d = MathF.Max(1f, MathF.Min(3f, k * 0.8f));
        var alive = new Color(24, 58, 24, 255);
        var burnt = new Color(28, 26, 24, 255);
        for (int i = 0; i < snap.PropCount; i++)
        {
            ref var pv = ref snap.Props[i];
            if (pv.Kind != PropKind.Tree) continue;
            var p = ToScreen(m, pv.Pos.X, pv.Pos.Z);
            if (p.X < vis.X - 2 || p.Y < vis.Y - 2 || p.X > vis.X + vis.Width + 2 || p.Y > vis.Y + vis.Height + 2) continue;
            int cell = Math.Clamp((int)(p.Y - vis.Y) + 1, 0, gh - 1) * gw + Math.Clamp((int)(p.X - vis.X) + 1, 0, gw - 1);
            if (_dotStamp[cell] == stamp) continue;
            _dotStamp[cell] = stamp;
            _dotKey[cell] = -2;
            Raylib.DrawRectangleV(p - new Vector2(d * 0.5f, d * 0.5f), new Vector2(d, d), (pv.Flags & PropFlags.Burnt) != 0 ? burnt : alive);
        }
    }
}
