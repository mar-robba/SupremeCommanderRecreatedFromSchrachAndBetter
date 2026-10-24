using System.Globalization;
using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Input;

/// <summary>
/// (7b) Screenshot hooks for the order-editing UI (cheats on, e.g. with --demo):
/// <c>--ui-formation x1,z1:x2,z2</c> spawns 8 own tanks in front of the line, selects them and shows the right-drag
/// line formation preview frozen (facing away from the camera); <c>--ui-waypoints x,z</c> spawns 6 own tanks at x,z,
/// queues four waypoints for them and shows the second one being dragged. Positions are printed; aim with --camera.
/// </summary>
public sealed class OrdersUiDemo
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly (Vector2 From, Vector2 To)? _line;
    private readonly Vector2? _wp;
    private int _frame, _step;
    private Vector2 _spawn;
    private Vector2[] _route = [];

    public static OrdersUiDemo? Create(GameContext ctx, SelectionController sel, ClientOptions o)
    {
        if (o.UiFormation == null && o.UiWaypoints == null) return null;
        return new OrdersUiDemo(ctx, sel, o);
    }

    private OrdersUiDemo(GameContext ctx, SelectionController sel, ClientOptions o)
    {
        _ctx = ctx;
        _sel = sel;
        if (o.UiFormation?.Split(':') is [var a, var b] && Xz(a) is Vector2 fa && Xz(b) is Vector2 fb) _line = (fa, fb);
        if (o.UiWaypoints != null) _wp = Xz(o.UiWaypoints);
    }

    private static Vector2? Xz(string s) =>
        s.Split(',') is [var x, var z] && float.TryParse(x, CultureInfo.InvariantCulture, out float fx)
        && float.TryParse(z, CultureInfo.InvariantCulture, out float fz) ? new Vector2(fx, fz) : null;

    /// <summary>Per frame, before the selection's input handling.</summary>
    public void Update()
    {
        _frame++;
        if (_step >= 3 || _frame > 900) return;
        int army = _ctx.LocalArmy;
        int tank = _ctx.Db.IndexOf("uef_t1_tank");
        if (tank < 0) { _step = 3; return; }
        if (_step == 0 && _frame >= 5)
        {
            if (_line is var (from, to))
            {
                var mid = (from + to) * 0.5f;
                _spawn = mid - Facing(from, to) * 14f;
                _ctx.Runner.Enqueue(new DebugSpawnCommand(army, tank, _spawn, 8));
            }
            else if (_wp is Vector2 p)
            {
                _spawn = p;
                _ctx.Runner.Enqueue(new DebugSpawnCommand(army, tank, _spawn, 6));
                _route = [p + new Vector2(14, 2), p + new Vector2(24, 14), p + new Vector2(16, 26), p + new Vector2(2, 22)];
            }
            Console.WriteLine($"[ui-orders] {(_line != null ? "formation" : "waypoints")}: tanks at {_spawn.X:F0},{_spawn.Y:F0}");
            _step = 1;
            return;
        }
        if (_step == 1)
        {
            var snap = _ctx.Snapshots.Current;
            var picked = new List<UnitHandle>();
            for (int k = 0; k < snap.UnitCount; k++)
            {
                ref var v = ref snap.Units[k];
                if (v.Army == army && v.Bp == tank && Vector2.Distance(new Vector2(v.Pos.X, v.Pos.Z), _spawn) < 14f)
                    picked.Add(new UnitHandle(v.Index, v.Gen));
            }
            if (picked.Count == 0) return;
            _sel.Selected.Clear();
            _sel.Selected.AddRange(picked);
            if (_line is var (from, to)) { _sel.Formation.Freeze(from, to, Facing(from, to), OrderType.Move); _step = 3; return; }
            var units = picked.ToArray();
            for (int k = 0; k < _route.Length; k++)
            {
                var order = k == 2 ? Order.AttackMoveTo(_route[k]) : Order.MoveTo(_route[k]);
                _ctx.Runner.Enqueue(new IssueOrderCommand(army, units, order, k > 0));
            }
            _step = 2;
            return;
        }
        if (_step == 2)
        {
            _sel.Waypoints.Freeze(_route[1], _route[1] + new Vector2(9, -5));
            if (_sel.Waypoints.Dragging != null) _step = 3;
        }
    }

    /// <summary>Ground-line normal facing away from the camera (as a left→right screen drag).</summary>
    private Vector2 Facing(Vector2 from, Vector2 to)
    {
        var cam = _ctx.Camera.Camera;
        var fwd3 = cam.Target - cam.Position;
        var right3 = Vector3.Cross(fwd3, cam.Up);
        var fwd = Vector2.Normalize(new Vector2(fwd3.X, fwd3.Z));
        var right = Vector2.Normalize(new Vector2(right3.X, right3.Z));
        return OrderEditMath.FacingFromDrag(Vector2.Zero, new Vector2(100, 0), from, to, right, fwd);
    }
}
