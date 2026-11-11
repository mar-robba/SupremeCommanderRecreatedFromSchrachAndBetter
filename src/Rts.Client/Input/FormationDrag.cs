using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Input;

/// <summary>
/// (7b) Right-drag line formation (SupCom): with own mobile units selected in Normal (or AttackMove) mode, a right press
/// starts tracking; moving more than <see cref="OrderEditMath.DragThresholdPx"/> makes it a drag along the ground and
/// the release sends a <see cref="FormationMoveCommand"/> (Shift: queue). A short right click is a normal context
/// order, issued on release by <see cref="SelectionController"/>. The preview slots come from the sim's pure
/// <see cref="QueueEditSystem.LineOffsets(IReadOnlyList{Vector2}, IReadOnlyList{float}, IReadOnlyList{bool}, IReadOnlyList{int}, Vector2, Vector2, Vector2, Vector2)"/>,
/// so preview = result. Drawn by Rendering/OrderMarkers.cs.
/// </summary>
public sealed class FormationDrag
{
    public enum Result { None, Click, Issued }

    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private Vector2? _pressScreen;
    private Vector2 _pressWorld;
    private bool _frozen;

    /// <summary>True while dragging (preview visible).</summary>
    public bool Active { get; private set; }
    public Vector2 From, To, Facing;
    public OrderType Type = OrderType.Move;
    /// <summary>Preview slot positions on the ground (world x, z), one per own mobile selected unit.</summary>
    public readonly List<Vector2> Slots = new();

    public FormationDrag(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    /// <summary>Right press: starts tracking when the selection has own mobile units and the cursor is on the ground.
    /// False = not a formation candidate (the caller acts on the press at once, as before).</summary>
    public bool Begin(Vector2 mouse)
    {
        if (_frozen || !HasOwnMobile()) return false;
        if (_ctx.PickGround(mouse) is not Vector3 g) return false;
        _pressScreen = mouse;
        _pressWorld = new Vector2(g.X, g.Z);
        Active = false;
        return true;
    }

    /// <summary>Per frame (after Begin): follows the drag; on release returns Click (short) or Issued (formation sent).</summary>
    public Result Update(bool queue, SelectionController.CommandMode mode)
    {
        if (_frozen) { RebuildSlots(); return Result.None; }
        if (_pressScreen is not Vector2 press) return Result.None;
        var mouse = Raylib.GetMousePosition();
        if (!Active && OrderEditMath.IsDrag(press, mouse)) Active = true;
        Type = mode == SelectionController.CommandMode.AttackMove ? OrderType.AttackMove : OrderType.Move;
        if (Active)
        {
            if (_ctx.PickGround(mouse) is Vector3 g) To = new Vector2(g.X, g.Z);
            From = _pressWorld;
            Facing = FacingNow(press, mouse);
            RebuildSlots();
        }
        if (Raylib.IsMouseButtonDown(MouseButton.Right)) return Result.None;
        // Released.
        _pressScreen = null;
        if (!Active) return Result.Click;
        Active = false;
        Slots.Clear();
        if (!HasOwnMobile()) return Result.None;
        var (from, to, facing, type) = (From, To, Facing, Type);
        ArmyCommands.Send(_ctx, _sel.Selected, (army, units) => new FormationMoveCommand(army, units, type, from, to, facing, queue));
        var mid = (from + to) * 0.5f;
        _ctx.Feedback.AddClickMarker(new Vector3(mid.X, _ctx.Terrain.GetSurfaceHeight(mid), mid.Y), type);
        UiSounds.Confirm();
        return Result.Issued;
    }

    /// <summary>Drops a pending press (mode change, selection cleared…).</summary>
    public void Cancel()
    {
        if (_frozen) return;
        _pressScreen = null;
        Active = false;
        Slots.Clear();
    }

    /// <summary>Screenshot hook (--ui-formation): show the preview of a drag from→to, frozen (no mouse needed).</summary>
    public void Freeze(Vector2 from, Vector2 to, Vector2 facing, OrderType type)
    {
        _frozen = true;
        Active = true;
        (From, To, Facing, Type) = (from, to, facing, type);
        RebuildSlots();
    }

    private Vector2 FacingNow(Vector2 press, Vector2 mouse)
    {
        var cam = _ctx.Camera.Camera;
        var fwd3 = cam.Target - cam.Position;
        var right3 = Vector3.Cross(fwd3, cam.Up);
        static Vector2 Xz(Vector3 v) { var p = new Vector2(v.X, v.Z); return p.LengthSquared() > 1e-8f ? Vector2.Normalize(p) : Vector2.UnitX; }
        return OrderEditMath.FacingFromDrag(press, mouse, From, To, Xz(right3), Xz(fwd3));
    }

    private bool HasOwnMobile()
    {
        var snap = _ctx.Snapshots.Current;
        foreach (var h in _sel.Selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && _ctx.Controls(v.Army) && !_ctx.Db[v.Bp].IsStructure) return true;
        return false;
    }

    /// <summary>Same slots as the sim: per army, the group's units sorted by index (as ApplyIssueOrder collects them).</summary>
    private void RebuildSlots()
    {
        Slots.Clear();
        var snap = _ctx.Snapshots.Current;
        var from = _ctx.Terrain.Clamp(From);
        var to = _ctx.Terrain.Clamp(To);
        var center = (from + to) * 0.5f;
        var facing = Facing.LengthSquared() > 1e-6f ? Vector2.Normalize(Facing) : Vector2.Zero;
        var pos = new List<Vector2>(); var rad = new List<float>(); var st = new List<bool>(); var ids = new List<int>();
        foreach (var (army, units) in ArmyCommands.Groups(_ctx, _sel.Selected))
        {
            pos.Clear(); rad.Clear(); st.Clear(); ids.Clear();
            var sorted = units.Where(h => snap.TryGetUnit(h.Index, h.Generation, out var v) && v.Army == army)
                .Select(h => h.Index).Distinct().Order().ToList();
            foreach (int idx in sorted)
            {
                if (!snap.TryGetUnit(idx, FindGen(units, idx), out var v)) continue;
                var bp = _ctx.Db[v.Bp];
                pos.Add(new Vector2(v.Pos.X, v.Pos.Z)); rad.Add(bp.Radius); st.Add(bp.IsStructure); ids.Add(idx);
            }
            var off = QueueEditSystem.LineOffsets(pos, rad, st, ids, from, to, facing, center);
            for (int k = 0; k < off.Length; k++) if (!st[k]) Slots.Add(center + off[k]);
        }
    }

    private static int FindGen(UnitHandle[] units, int index)
    {
        foreach (var h in units) if (h.Index == index) return h.Generation;
        return 0;
    }
}
