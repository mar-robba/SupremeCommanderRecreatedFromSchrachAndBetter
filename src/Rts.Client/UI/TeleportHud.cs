using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.Rendering;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.UI;

/// <summary>
/// Personal Teleporter overlay (<see cref="TeleportInput"/>): rings at the queued Teleport orders of the selected own
/// units (the front one fills with the charge, <c>UnitView.TeleportFrac</c>); in teleport mode an arc from each selected
/// teleporter (or the end of its queue with Shift) to where it would land (<see cref="TeleportInput.Destination"/>,
/// MaxRange clamp; a range ring when limited) and, in 2D, the energy / charge-time label at the cursor.
/// </summary>
public static class TeleportHud
{
    private static readonly Color Ring = new(110, 225, 255, 210);
    private static readonly Color RingDim = new(110, 225, 255, 120);

    /// <summary>Inside BeginMode3D (after the selection lines).</summary>
    public static void Draw3D(GameContext ctx, SelectionController sel, SimSnapshot snap)
    {
        float t = (float)Raylib.GetTime();
        foreach (var h in sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !ctx.Controls(v.Army)) continue;
            for (int o = 0; o < v.OrderCount; o++)
            {
                var ov = snap.Orders[v.OrderStart + o];
                if (ov.Type != OrderType.Teleport) continue;
                var c = new Vector2(ov.Pos.X, ov.Pos.Z);
                float r = MathF.Max(2.5f, ctx.Db[v.Bp].Radius + 1.5f);
                GroundRing(ctx, c, r, o == 0 ? Ring : RingDim, 40);
                if (o == 0 && v.TeleportFrac >= 0)
                {
                    float pulse = 0.5f + 0.5f * MathF.Sin(t * 7f);
                    GroundRing(ctx, c, MathF.Max(0.3f, r * v.TeleportFrac), new Color(215, 250, 255, (int)(170 + 80 * pulse)), 32);
                }
            }
        }
        if (sel.Mode != Mode.Teleport || ctx.PickGround(Raylib.GetMousePosition()) is not Vector3 g) return;
        var p = ctx.Terrain.Clamp(new Vector2(g.X, g.Z), 1f);
        bool queue = Ui.Shift;
        foreach (var h in sel.Selected)
        {
            if (!TeleportInput.Teleporter(ctx, snap, h, out var v)) continue;
            var tp = ctx.Db[v.Bp].Teleport!;
            var from = queue && v.OrderCount > 0
                ? TeleportInput.StartPoint(snap, v, true)
                : Flat(UnitRenderer.Interpolate(v, ctx.Snapshots.Prev, ctx.Snapshots.Alpha).pos);
            var dest = TeleportInput.Destination(tp, from, p);
            if (tp.MaxRange > 0) GroundRing(ctx, from, tp.MaxRange, RingDim, 128);
            Arc(ctx, from, dest, Ring);
            float r = MathF.Max(2.5f, ctx.Db[v.Bp].Radius + 1.5f);
            GroundRing(ctx, dest, r, Ring, 40);
            GroundRing(ctx, dest, r * 0.45f, Ring, 24);
        }
    }

    /// <summary>2D, after the cursor hint: "Teleport: N E · T s" next to the mouse.</summary>
    public static void Draw2D(GameContext ctx, SelectionController sel, bool mouseOverUi)
    {
        if (mouseOverUi || sel.Mode != Mode.Teleport || ctx.PickGround(Raylib.GetMousePosition()) is not Vector3 g) return;
        var p = ctx.Terrain.Clamp(new Vector2(g.X, g.Z), 1f);
        var (energy, count, chargeTime) = TeleportInput.Estimate(ctx, sel.Selected, p, Ui.Shift);
        if (count == 0) return;
        var m = Raylib.GetMousePosition();
        int fs = Ui.F(12.5f);
        float x = m.X + Ui.P(20), y = m.Y + Ui.P(14);
        Ui.TextShadow(TeleportInput.CostLabel(energy, count, chargeTime), x, y, fs, TeleportButton.Cyan, true);
        Ui.TextShadow("Shift: queue", x, y + fs + Ui.P(3), Ui.F(10.5f), Ui.TextDim, false);
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    /// <summary>A parabolic arc above the terrain from a to b.</summary>
    private static void Arc(GameContext ctx, Vector2 a, Vector2 b, Color col)
    {
        var terrain = ctx.Terrain;
        float lift = Math.Clamp(Vector2.Distance(a, b) * 0.18f, 2f, 30f);
        const int seg = 28;
        Vector3 Pt(int k)
        {
            float u = k / (float)seg;
            var q = Vector2.Lerp(a, b, u);
            return new Vector3(q.X, terrain.GetSurfaceHeight(terrain.Clamp(q)) + 0.6f + lift * 4f * u * (1 - u), q.Y);
        }
        var prev = Pt(0);
        for (int k = 1; k <= seg; k++)
        {
            var cur = Pt(k);
            if ((k & 1) == 1) Raylib.DrawLine3D(prev, cur, col); // dashed
            prev = cur;
        }
    }

    /// <summary>Terrain-following circle (as StrategicHud.GroundRing).</summary>
    private static void GroundRing(GameContext ctx, Vector2 c, float r, Color col, int seg)
    {
        var terrain = ctx.Terrain;
        Vector3 Pt(int k)
        {
            float a = k * MathF.Tau / seg;
            var q = new Vector2(c.X + MathF.Cos(a) * r, c.Y + MathF.Sin(a) * r);
            return new Vector3(q.X, terrain.GetSurfaceHeight(terrain.Clamp(q)) + 0.35f, q.Y);
        }
        var prev = Pt(0);
        for (int k = 1; k <= seg; k++)
        {
            var cur = Pt(k);
            Raylib.DrawLine3D(prev, cur, col);
            prev = cur;
        }
    }
}
