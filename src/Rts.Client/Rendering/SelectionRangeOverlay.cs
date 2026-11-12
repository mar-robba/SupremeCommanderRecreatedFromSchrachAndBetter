using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// (10b-2, G20) Range and intel rings of the selected units and structures: weapon ranges (anti-air in blue), min
/// ranges (dashed), radar / sonar / omni, shield radius, stealth field, missile defence. Holding Shift adds every own
/// and allied complete structure in view (dimmer). Rings follow the terrain; equal rings merge and overlapping rings
/// of one kind and radius draw only their union outline (<see cref="RangeRingSet"/>), capped at
/// <see cref="RangeRingSet.DefaultCap"/> merged rings. Toggle: <see cref="Hotkeys.RangeRings"/> (I) and the
/// Gameplay setting "Range rings" (<see cref="Enabled"/>). Drawn inside BeginMode3D; no allocation per frame.
/// </summary>
public sealed class SelectionRangeOverlay
{
    /// <summary>On / off (setting "Range rings", key I flips it for the session).</summary>
    public static bool Enabled = true;

    private readonly RangeRingSet _set = new();
    private readonly Func<Vector2, float, bool> _visible;

    public RangeRingSet Set => _set;

    public SelectionRangeOverlay()
    {
        _visible = static (c, r) => FrameView.Current.SphereVisible(new Vector3(c.X, 0, c.Y), r + 8f);
    }

    public static Color ColorOf(RingKind k) => k switch
    {
        RingKind.Weapon => new Color(235, 80, 60, 210),
        RingKind.AntiAir => new Color(110, 175, 255, 210),
        RingKind.MinRange => new Color(170, 40, 40, 200),
        RingKind.Radar => new Color(90, 210, 230, 190),
        RingKind.Sonar => new Color(70, 120, 255, 200),
        RingKind.Omni => new Color(205, 140, 255, 200),
        RingKind.Shield => new Color(140, 255, 200, 200),
        RingKind.StealthField => new Color(175, 175, 195, 190),
        RingKind.MissileDefense => new Color(245, 215, 80, 200),
        _ => Color.White,
    };

    public void Draw(GameContext ctx, SelectionController sel, SimSnapshot cur, SimSnapshot? prev, float alpha, bool shift)
    {
        if (!Enabled || ctx.Camera.Distance > IconRenderer.HideMeshDistance * 2.5f) return;
        if (sel.Selected.Count == 0 && !shift) return;
        Rebuild(ctx, sel, cur, prev, alpha, shift); // every frame: interpolated positions, culling (~0.1 ms at 500 units)
        DrawSegments(ctx);
    }

    private void Rebuild(GameContext ctx, SelectionController sel, SimSnapshot cur, SimSnapshot? prev, float alpha, bool shift)
    {
        var db = ctx.Db;
        _set.Clear();
        foreach (var h in sel.Selected)
        {
            if (!cur.TryGetUnit(h.Index, h.Generation, out var v) || (uint)v.Bp >= (uint)db.Count) continue;
            var (pos, _) = UnitRenderer.Interpolate(v, prev, alpha);
            _set.AddUnit(db[v.Bp], new Vector2(pos.X, pos.Z), 0);
        }
        if (shift)
        {
            var fv = FrameView.Current;
            for (int k = 0; k < cur.UnitCount; k++)
            {
                ref var v = ref cur.Units[k];
                if ((uint)v.Bp >= (uint)db.Count || (v.Flags & Rts.Sim.World.UnitFlags.UnderConstruction) != 0) continue;
                var bp = db[v.Bp];
                if (!bp.IsStructure || ctx.IsEnemy(v.Army)) continue;
                if (!fv.SphereVisible(v.Pos, 60f)) continue;
                _set.AddUnit(bp, new Vector2(v.Pos.X, v.Pos.Z), 1);
            }
        }
        _set.Finish();
        _set.BuildOutline(_visible);
    }

    private void DrawSegments(GameContext ctx)
    {
        int n = _set.SegmentCount;
        if (n == 0) return;
        var pts = _set.SegmentPoints;
        var rings = _set.Rings;
        var terrain = ctx.Terrain;
        Rlgl.CheckRenderBatchLimit(n * 2 + 2);
        Rlgl.Begin(DrawMode.Lines);
        int lastRing = -1;
        Color col = Color.White;
        for (int s = 0; s < n; s++)
        {
            int r = _set.SegmentRing(s);
            ref readonly var ring = ref rings[r];
            if (r != lastRing)
            {
                col = ColorOf(ring.Kind);
                if (ring.Source != 0) col.A = (byte)(col.A * 0.55f);
                lastRing = r;
            }
            // Min range: dashed (every other segment).
            if (ring.Kind == RingKind.MinRange && (s & 1) == 1) continue;
            var a = pts[s * 2];
            var b = pts[s * 2 + 1];
            Rlgl.Color4ub(col.R, col.G, col.B, col.A);
            Rlgl.Vertex3f(a.X, terrain.GetSurfaceHeight(a) + 0.35f, a.Y);
            Rlgl.Vertex3f(b.X, terrain.GetSurfaceHeight(b) + 0.35f, b.Y);
        }
        Rlgl.End();
    }
}
