using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.UI;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Input;

/// <summary>
/// (7b) Waypoint editing (SupCom): the selected own units' queued position orders (Move / AttackMove / Patrol / Ferry /
/// Unload; Ferry also its drop point) show one marker per group point (<see cref="OrderEditMath.Cluster"/>). Hover a
/// marker: left drag moves it (<see cref="MoveWaypointCommand"/> on release), the Delete key removes it
/// (<see cref="DeleteWaypointCommand"/>). Not in read-only (replay) contexts. Drawn by Rendering/OrderMarkers.cs.
/// </summary>
public sealed class WaypointEditor
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly List<OrderEditMath.WaypointRef> _refs = new();
    private readonly List<Vector2?> _screen = new();
    private bool _frozen;

    public List<OrderEditMath.Marker> Markers { get; private set; } = new();
    /// <summary>Hovered marker index (-1 = none).</summary>
    public int Hover { get; private set; } = -1;
    /// <summary>Marker being dragged (null = none) and its ghost position on the ground.</summary>
    public OrderEditMath.Marker? Dragging { get; private set; }
    public Vector2 Ghost;

    public WaypointEditor(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    public bool Enabled => !_ctx.ReadOnly && _sel.Selected.Count > 0;

    /// <summary>Per frame, before the selection's mouse handling: rebuilds the markers and the hover.</summary>
    public void Refresh(bool mouseOverUi)
    {
        if (_frozen) { Rebuild(); return; }
        if (!Enabled) { Markers.Clear(); Hover = -1; Dragging = null; return; }
        Rebuild();
        Hover = -1;
        if (mouseOverUi || Dragging != null) return;
        var cam = _ctx.Camera.Camera;
        _screen.Clear();
        foreach (var m in Markers)
        {
            var w = World(m.Point);
            // Behind the camera: no hover.
            _screen.Add(Vector3.Dot(w - cam.Position, cam.Target - cam.Position) > 0 ? Raylib.GetWorldToScreen(w, cam) : null);
        }
        Hover = OrderEditMath.Nearest(_screen, Raylib.GetMousePosition(), Ui.P(OrderEditMath.HoverRadiusPx));
    }

    /// <summary>Left press: starts dragging the hovered marker (true = consumed, no box select).</summary>
    public bool TryBeginDrag()
    {
        if (_frozen || Hover < 0 || Hover >= Markers.Count) return false;
        Dragging = Markers[Hover];
        Ghost = Dragging.Point;
        return true;
    }

    /// <summary>Per frame: follows a drag, sends it on release; Delete over a hovered marker removes it.</summary>
    public void Update()
    {
        if (_frozen) return;
        if (Dragging is { } d)
        {
            if (_ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g) Ghost = new Vector2(g.X, g.Z);
            if (Raylib.IsMouseButtonDown(MouseButton.Left)) return;
            Dragging = null;
            if (Vector2.Distance(Ghost, d.Point) <= QueueEditSystem.MatchEpsilon) return;
            var (from, to) = (d.Point, Ghost);
            ArmyCommands.Send(_ctx, d.Units, (army, units) => new MoveWaypointCommand(army, units, from, to));
            _ctx.Feedback.AddClickMarker(World(to), d.Type);
            UiSounds.Confirm();
            return;
        }
        if (Hover >= 0 && Hover < Markers.Count && Hotkeys.DeleteWaypoint.Pressed())
        {
            var m = Markers[Hover];
            var at = m.Point;
            ArmyCommands.Send(_ctx, m.Units, (army, units) => new DeleteWaypointCommand(army, units, at));
            UiSounds.Confirm();
            Hover = -1;
        }
    }

    /// <summary>Screenshot hook (--ui-waypoints): show the marker at <paramref name="point"/> dragged to
    /// <paramref name="ghost"/> (or only hovered when ghost is null), frozen.</summary>
    public void Freeze(Vector2 point, Vector2? ghost)
    {
        _frozen = true;
        Rebuild();
        int k = Markers.FindIndex(m => Vector2.Distance(m.Point, point) <= 0.5f);
        Hover = ghost == null ? k : -1;
        Dragging = ghost != null && k >= 0 ? Markers[k] : null;
        Ghost = ghost ?? point;
    }

    public Vector3 World(Vector2 p) => new(p.X, _ctx.Terrain.GetSurfaceHeight(p) + 0.4f, p.Y);

    private void Rebuild()
    {
        var snap = _ctx.Snapshots.Current;
        _refs.Clear();
        foreach (var h in _sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !_ctx.Controls(v.Army)) continue;
            var prev = new Vector2(v.Pos.X, v.Pos.Z);
            for (int o = 0; o < v.OrderCount; o++)
            {
                var ov = snap.Orders[v.OrderStart + o];
                if (!OrderEditMath.IsWaypoint(ov.Type)) { prev = new Vector2(ov.Pos.X, ov.Pos.Z); continue; }
                Vector2? next = o + 1 < v.OrderCount ? snap.Orders[v.OrderStart + o + 1].Point : null;
                if (ov.Type == OrderType.Ferry && ov.Aux != Vector2.Zero)
                {
                    _refs.Add(new(h, ov.Type, ov.Point, false, prev, ov.Aux));
                    _refs.Add(new(h, ov.Type, ov.Aux, true, ov.Point, next));
                }
                else _refs.Add(new(h, ov.Type, ov.Point, false, prev, next));
                prev = ov.Point;
            }
        }
        Markers = OrderEditMath.Cluster(_refs);
        if (Dragging != null)
        {
            // Keep the dragged marker's identity while the snapshot changes.
            int k = Markers.FindIndex(m => Vector2.DistanceSquared(m.Point, Dragging.Point) <= QueueEditSystem.MatchEpsilon * QueueEditSystem.MatchEpsilon);
            if (k >= 0) Dragging = Markers[k];
            else if (!_frozen) Dragging = null; // the waypoint is gone (reached / cancelled)
        }
    }
}
