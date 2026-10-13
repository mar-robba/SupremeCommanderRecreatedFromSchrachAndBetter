using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.Rendering;

namespace Rts.Client.UI;

/// <summary>
/// Cursor hint for context orders (<see cref="SelectionController.HoverAction"/>): a small badge with a glyph
/// and a label next to the mouse (Attack, Guard, Assist, Repair, Assist construction, Reclaim +N mass) and a
/// ring around the hovered target. Nothing is shown for plain moves. Call after the HUD (2D).
/// </summary>
public static class CursorHint
{
    public static void Draw(GameContext ctx, SelectionController sel, bool mouseOverUi)
    {
        var a = sel.HoverAction;
        if (mouseOverUi || a.Kind is ContextKind.None or ContextKind.Move) return;
        var col = ColorOf(a.Kind);

        // Ring around the target on the ground.
        float radius = a.Radius;
        Vector3 center;
        if (a.Kind == ContextKind.Reclaim) center = new Vector3(a.Pos.X, ctx.Terrain.GetHeight(a.Pos.X, a.Pos.Y), a.Pos.Y);
        else
        {
            var snap = ctx.Snapshots.Current;
            if (!snap.TryGetUnit(a.Target.Index, a.Target.Generation, out var v)) return;
            var bp = ctx.Db[v.Bp];
            radius = MathF.Max(bp.Radius, MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) * 0.6f);
            center = UnitRenderer.Interpolate(v, ctx.Snapshots.Prev, ctx.Snapshots.Alpha).pos;
        }
        GroundRing(ctx, center, MathF.Max(0.8f, radius) + 0.5f, col);

        var m = Raylib.GetMousePosition();
        float r = Ui.P(9);
        var c = new Vector2(m.X + Ui.P(22), m.Y + Ui.P(20));
        Raylib.DrawCircleV(c, r + 2, new Color(0, 0, 0, 170));
        Raylib.DrawCircleV(c, r, new Color(14, 20, 28, 235));
        Raylib.DrawCircleLinesV(c, r, col);
        Glyph(a.Kind, c, r * 0.62f, col);
        int fs = Ui.F(12.5f);
        Ui.TextShadow(a.Label, c.X + r + Ui.P(6), c.Y - fs * 0.5f - 1, fs, col, true);
    }

    public static readonly Color CaptureColor = new(110, 225, 235, 255);

    private static Color ColorOf(ContextKind k) => k switch
    {
        ContextKind.Attack => new Color(255, 105, 90, 255),
        ContextKind.Reclaim => new Color(130, 235, 120, 255),
        ContextKind.Guard => new Color(120, 190, 255, 255),
        ContextKind.Capture => CaptureColor,
        _ => new Color(245, 210, 90, 255),
    };

    private static void Glyph(ContextKind k, Vector2 c, float s, Color col)
    {
        switch (k)
        {
            case ContextKind.Attack: // crosshair
                Raylib.DrawCircleLinesV(c, s * 0.75f, col);
                Raylib.DrawLineEx(c - new Vector2(s * 1.2f, 0), c - new Vector2(s * 0.35f, 0), 2, col);
                Raylib.DrawLineEx(c + new Vector2(s * 0.35f, 0), c + new Vector2(s * 1.2f, 0), 2, col);
                Raylib.DrawLineEx(c - new Vector2(0, s * 1.2f), c - new Vector2(0, s * 0.35f), 2, col);
                Raylib.DrawLineEx(c + new Vector2(0, s * 0.35f), c + new Vector2(0, s * 1.2f), 2, col);
                break;
            case ContextKind.Reclaim: // arrow down into a tray
                Raylib.DrawLineEx(c + new Vector2(0, -s * 1.1f), c + new Vector2(0, s * 0.2f), 2.5f, col);
                Raylib.DrawTriangle(c + new Vector2(-s * 0.6f, 0), c + new Vector2(0, s * 0.75f), c + new Vector2(s * 0.6f, 0), col);
                Raylib.DrawLineEx(c + new Vector2(-s, s * 1.05f), c + new Vector2(s, s * 1.05f), 2, col);
                break;
            case ContextKind.Repair: // plus
                Raylib.DrawRectangleV(c - new Vector2(s * 0.28f, s), new Vector2(s * 0.56f, s * 2), col);
                Raylib.DrawRectangleV(c - new Vector2(s, s * 0.28f), new Vector2(s * 2, s * 0.56f), col);
                break;
            case ContextKind.Guard: // shield
                Raylib.DrawTriangle(c + new Vector2(-s * 0.85f, -s * 0.2f), c + new Vector2(0, s * 1.1f), c + new Vector2(s * 0.85f, -s * 0.2f), col);
                Raylib.DrawRectangleV(c - new Vector2(s * 0.85f, s * 0.95f), new Vector2(s * 1.7f, s * 0.8f), col);
                break;
            case ContextKind.Capture: DrawFlag(c, s, col); break;
            default: // assist / finish: up arrow (adds build power)
                Raylib.DrawTriangle(c + new Vector2(0, -s * 1.1f), c + new Vector2(-s * 0.85f, 0), c + new Vector2(s * 0.85f, 0), col);
                Raylib.DrawRectangleV(c - new Vector2(s * 0.3f, 0), new Vector2(s * 0.6f, s * 1.05f), col);
                break;
        }
    }

    /// <summary>Capture glyph: a flag on a pole.</summary>
    public static void DrawFlag(Vector2 c, float s, Color col)
    {
        float t = MathF.Max(2f, s * 0.22f);
        Raylib.DrawLineEx(c + new Vector2(-s * 0.6f, -s * 1.1f), c + new Vector2(-s * 0.6f, s * 1.1f), t, col);
        Raylib.DrawTriangle(c + new Vector2(-s * 0.5f, -s * 1.05f), c + new Vector2(-s * 0.5f, s * 0.05f), c + new Vector2(s * 0.95f, -s * 0.5f), col);
        Raylib.DrawLineEx(c + new Vector2(-s * 1.0f, s * 1.1f), c + new Vector2(-s * 0.2f, s * 1.1f), t, col);
    }

    /// <summary>Screen-space polyline of a circle lying on the ground.</summary>
    private static void GroundRing(GameContext ctx, Vector3 center, float radius, Color col)
    {
        var cam = ctx.Camera.Camera;
        const int n = 28;
        Vector2 prev = default;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n * MathF.Tau;
            var p = center + new Vector3(MathF.Cos(t) * radius, 0.25f, MathF.Sin(t) * radius);
            p.Y = MathF.Max(p.Y, ctx.Terrain.GetHeight(p.X, p.Z) + 0.25f);
            var s = Raylib.GetWorldToScreen(p, cam);
            if (i > 0) Raylib.DrawLineEx(prev, s, 2f, ColorUtil.WithAlpha(col, 210));
            prev = s;
        }
    }
}
