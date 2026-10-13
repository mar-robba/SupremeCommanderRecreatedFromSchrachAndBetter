using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.Rendering;

/// <summary>
/// 3D overlay for the cargo mechanic (inside BeginMode3D): ferry routes of the local army
/// (<see cref="SimSnapshot.Ferries"/>: beacon at the pickup, animated chevrons to the drop point, drop marker; brighter
/// when a selected unit has that ferry order) and the preview of the Ferry / Unload / Transport-to order modes.
/// Lines of selected units with Load / Dock orders are the regular order lines. Owned by the transport UI.
/// </summary>
public static class TransportOverlay
{
    private static readonly Color Dim = new(90, 190, 220, 150);

    public static void Draw3D(GameContext ctx, HashSet<long> selected, SimSnapshot cur, SimSnapshot? prev, float alpha)
    {
        float time = (float)Raylib.GetTime();
        for (int k = 0; k < cur.FerryCount; k++)
        {
            ref var f = ref cur.Ferries[k];
            if (f.Army != ctx.LocalArmy) continue;
            bool hi = RouteSelected(cur, selected, f);
            DrawRoute(ctx, new Vector2(f.Pickup.X, f.Pickup.Z), new Vector2(f.Drop.X, f.Drop.Z), hi ? TransportInput.Cyan : Dim, time);
        }

        // Order mode previews.
        var mode = TransportInput.CurrentMode;
        if (!TransportInput.IsTransportMode(mode) || ctx.PickGround(Raylib.GetMousePosition()) is not Vector3 g) return;
        var p = new Vector2(g.X, g.Z);
        var col = TransportInput.Cyan;
        if (mode == Mode.Ferry && TransportInput.PendingBeacon is Vector2 b) DrawRoute(ctx, b, p, col, time);
        else if (mode == Mode.Ferry) Beacon(ctx, p, col, time);
        else GroundRing(ctx, p, 2.5f + 0.4f * MathF.Sin(time * 5f), col, 32);
    }

    /// <summary>Is a selected unit following this ferry route (front order or queued)?</summary>
    private static bool RouteSelected(SimSnapshot snap, HashSet<long> selected, in FerryView f)
    {
        foreach (long key in selected)
        {
            if (!snap.TryGetUnit((int)(key >> 32), (int)(uint)key, out var v)) continue;
            for (int i = 0; i < v.OrderCount; i++)
            {
                ref var o = ref snap.Orders[v.OrderStart + i];
                if (o.Type != OrderType.Ferry) continue;
                float dx = o.Pos.X - f.Pickup.X, dz = o.Pos.Z - f.Pickup.Z;
                if (dx * dx + dz * dz < 4f) return true;
            }
        }
        return false;
    }

    private static void DrawRoute(GameContext ctx, Vector2 from, Vector2 to, Color col, float time)
    {
        Beacon(ctx, from, col, time);
        // Drop marker: double ring.
        GroundRing(ctx, to, 2.6f, col, 28);
        GroundRing(ctx, to, 1.4f, col, 20);
        var d = to - from;
        float len = d.Length();
        if (len < 1f) return;
        var dir = d / len;
        var side = new Vector2(-dir.Y, dir.X);
        // Route line slightly above the ground, following the terrain.
        int n = Math.Clamp((int)(len / 4f), 2, 96);
        Vector3 prevP = At(ctx, from + dir * 3f, 1.2f);
        for (int i = 1; i <= n; i++)
        {
            var q = Vector2.Lerp(from + dir * 3f, to - dir * 2.6f, i / (float)n);
            var cp = At(ctx, q, 1.2f);
            Raylib.DrawLine3D(prevP, cp, ColorUtil.WithAlpha(col, (byte)(col.A * 0.7f)));
            prevP = cp;
        }
        // Chevrons moving from the beacon to the drop point.
        const float spacing = 7f;
        float phase = time * 6f % spacing;
        for (float s = 4f + phase; s < len - 3f; s += spacing)
        {
            var q = from + dir * s;
            var tip = At(ctx, q + dir * 0.9f, 1.25f);
            Raylib.DrawLine3D(At(ctx, q - dir * 0.6f + side * 1.1f, 1.25f), tip, col);
            Raylib.DrawLine3D(At(ctx, q - dir * 0.6f - side * 1.1f, 1.25f), tip, col);
        }
    }

    /// <summary>Ferry beacon: a pole with a pulsing ring at its foot.</summary>
    private static void Beacon(GameContext ctx, Vector2 p, Color col, float time)
    {
        var foot = At(ctx, p, 0.2f);
        var top = foot + new Vector3(0, 6f, 0);
        Raylib.DrawLine3D(foot, top, col);
        Raylib.DrawSphereEx(top, 0.45f, 6, 8, col);
        float pulse = time * 1.2f % 1f;
        GroundRing(ctx, p, 2f, col, 28);
        GroundRing(ctx, p, 2f + pulse * 3f, ColorUtil.WithAlpha(col, (byte)(col.A * (1f - pulse))), 28);
    }

    private static Vector3 At(GameContext ctx, Vector2 p, float lift)
    {
        var t = ctx.Terrain;
        return new Vector3(p.X, t.GetSurfaceHeight(t.Clamp(p)) + lift, p.Y);
    }

    private static void GroundRing(GameContext ctx, Vector2 c, float r, Color col, int seg)
    {
        var prevP = At(ctx, c + new Vector2(r, 0), 0.35f);
        for (int k = 1; k <= seg; k++)
        {
            float a = k * MathF.Tau / seg;
            var cp = At(ctx, c + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r), 0.35f);
            Raylib.DrawLine3D(prevP, cp, col);
            prevP = cp;
        }
    }

    /// <summary>2D: thin fuel sliver under a selected aircraft's health bar (<paramref name="x"/>, <paramref name="y"/> =
    /// top-left of the health bar, <paramref name="w"/> its width).</summary>
    public static void DrawFuelSliver(float x, float y, float w, float fuel)
    {
        if (fuel < 0) return;
        var col = fuel > 0.25f ? new Color(235, 165, 60, 255) : new Color(235, 70, 50, 255);
        Raylib.DrawRectangle((int)x - 1, (int)y + 4, (int)w + 2, 4, Color.Black);
        Raylib.DrawRectangle((int)x, (int)y + 5, (int)(w * Math.Clamp(fuel, 0f, 1f)), 2, col);
    }
}
