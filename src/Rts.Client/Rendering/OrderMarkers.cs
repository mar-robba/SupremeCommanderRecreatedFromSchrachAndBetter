using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.UI;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Rendering;

/// <summary>
/// (7b) Draws the order-editing overlays: the selected units' waypoint markers (hover / drag ghost with the route
/// lines to the neighbors, Input/WaypointEditor.cs) and the right-drag line formation preview (slots, line, facing
/// arrows, Input/FormationDrag.cs), plus their cursor hints. Called from GameSession (3D pass and 2D pass).
/// </summary>
public static class OrderMarkers
{
    private static readonly Color Ghost = new(255, 255, 255, 230);

    public static Color ColorOf(OrderType t, byte a = 220) => t switch
    {
        OrderType.Move => new Color((byte)60, (byte)220, (byte)60, a),
        OrderType.AttackMove => new Color((byte)230, (byte)60, (byte)50, a),
        OrderType.Patrol => new Color((byte)80, (byte)170, (byte)255, a),
        OrderType.Ferry or OrderType.Unload => new Color(TransportInput.Cyan.R, TransportInput.Cyan.G, TransportInput.Cyan.B, a),
        _ => new Color((byte)200, (byte)200, (byte)200, a),
    };

    public static void Draw3D(GameContext ctx, SelectionController sel)
    {
        var wp = sel.Waypoints;
        for (int k = 0; k < wp.Markers.Count; k++)
        {
            var m = wp.Markers[k];
            bool hot = k == wp.Hover || ReferenceEquals(m, wp.Dragging);
            var p = wp.World(m.Point);
            var c = ColorOf(m.Type, (byte)(ReferenceEquals(m, wp.Dragging) ? 110 : 230));
            float r = m.IsDrop ? 0.9f : 0.7f;
            Ring(p, r, c);
            Raylib.DrawSphere(p, 0.22f, c);
            if (hot) { Ring(p, r + 0.45f, Ghost); Ring(p, r + 0.5f, Ghost); }
        }
        if (wp.Dragging is { } d)
        {
            var g = wp.World(wp.Ghost);
            var c = ColorOf(d.Type);
            Ground(ctx, d.Prev, wp.Ghost, c);
            if (d.Next is Vector2 n) Ground(ctx, wp.Ghost, n, c);
            Ring(g, 0.8f, Ghost);
            Ring(g, 1.25f, c);
            Raylib.DrawSphere(g, 0.26f, Ghost);
        }

        var f = sel.Formation;
        if (!f.Active) return;
        var fc = ColorOf(f.Type);
        Ground(ctx, f.From, f.To, fc);
        foreach (var s in f.Slots)
        {
            var p = wp.World(s);
            Ring(p, 0.65f, fc);
            if (f.Facing == Vector2.Zero) continue;
            var tip = s + f.Facing * 1.7f;
            var side = new Vector2(-f.Facing.Y, f.Facing.X);
            var t3 = wp.World(tip);
            Raylib.DrawLine3D(p, t3, Ghost);
            Raylib.DrawLine3D(t3, wp.World(tip - f.Facing * 0.6f + side * 0.45f), Ghost);
            Raylib.DrawLine3D(t3, wp.World(tip - f.Facing * 0.6f - side * 0.45f), Ghost);
        }
    }

    public static void Draw2D(GameContext ctx, SelectionController sel, bool mouseOverUi)
    {
        var cam = ctx.Camera.Camera;
        var f = sel.Formation;
        var wp = sel.Waypoints;
        if (f.Active)
        {
            string what = f.Type == OrderType.AttackMove ? "Attack-move line" : "Line formation";
            Hint(Raylib.GetWorldToScreen(wp.World(f.To), cam), $"{what} · {f.Slots.Count} units", ColorOf(f.Type, 255));
            return;
        }
        if (wp.Dragging != null)
        {
            Hint(Raylib.GetWorldToScreen(wp.World(wp.Ghost), cam), "Release: move waypoint", Ghost);
            return;
        }
        if (!mouseOverUi && wp.Hover >= 0 && wp.Hover < wp.Markers.Count)
        {
            var m = wp.Markers[wp.Hover];
            Hint(Raylib.GetWorldToScreen(wp.World(m.Point), cam), "Drag: move waypoint · Del: delete", ColorOf(m.Type, 255));
        }
    }

    private static void Hint(Vector2 at, string text, Color col)
    {
        int fs = Ui.F(12.5f);
        Ui.TextShadow(text, at.X + Ui.P(16), at.Y + Ui.P(10), fs, col, true);
    }

    private static void Ring(Vector3 p, float r, Color c) => Raylib.DrawCircle3D(p, r, Vector3.UnitX, 90, c);

    /// <summary>A line on the ground from a to b, following the terrain (one segment per ~2 units).</summary>
    private static void Ground(GameContext ctx, Vector2 a, Vector2 b, Color c)
    {
        int n = Math.Clamp((int)(Vector2.Distance(a, b) / 2f) + 1, 1, 64);
        var last = new Vector3(a.X, ctx.Terrain.GetSurfaceHeight(a) + 0.35f, a.Y);
        for (int k = 1; k <= n; k++)
        {
            var q = Vector2.Lerp(a, b, k / (float)n);
            var p = new Vector3(q.X, ctx.Terrain.GetSurfaceHeight(q) + 0.35f, q.Y);
            Raylib.DrawLine3D(last, p, c);
            last = p;
        }
    }
}
