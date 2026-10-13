using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Client.UI;

/// <summary>
/// Command-line helpers for automated UI screenshots:
/// <c>--select commander|engineer|factory|army</c> selects own units once they appear in a snapshot,
/// <c>--ui-demo</c> spawns a small base for army 0 (land + air factory, engineers, eco) via DebugSpawnCommand,
/// queues units in the land factory and selects it, <c>--ui-place &lt;blueprintId&gt;</c> starts structure
/// placement (<c>id@x,z</c>: plus a Shift+drag line from world point x,z to the cursor), <c>--mouse x,y</c> pins the
/// cursor (to show hover tooltips / the placement ghost).
/// </summary>
public sealed class UiStartup
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly string? _select;
    private readonly bool _demo;
    private readonly string? _place;
    private readonly Vector2? _mouse;
    private readonly Vector2? _lineFrom;
    private int _frames;
    private bool _selectDone, _queued, _placeDone;

    public UiStartup(GameContext ctx, SelectionController sel, ClientOptions o)
    {
        _ctx = ctx;
        _sel = sel;
        _select = o.Select ?? (o.UiDemo ? "factory" : o.UiPlace != null ? "commander" : null);
        _demo = o.UiDemo;
        _place = o.UiPlace;
        if (_place != null && _place.IndexOf('@') is int at and >= 0)
        {
            var xz = _place[(at + 1)..].Split(',');
            if (xz.Length == 2 && float.TryParse(xz[0], System.Globalization.CultureInfo.InvariantCulture, out float lx)
                && float.TryParse(xz[1], System.Globalization.CultureInfo.InvariantCulture, out float lz)) _lineFrom = new Vector2(lx, lz);
            _place = _place[..at];
        }
        _mouse = o.MousePos;
        _selectDone = _select == null;
        _queued = !_demo;
        _placeDone = _place == null;
        if (_demo) SpawnDemoBase();
    }

    private void SpawnDemoBase()
    {
        var t = _ctx.Terrain;
        int army = _ctx.LocalArmy;
        var sp = t.StartPositions[army % t.StartPositions.Length];
        var center = new Vector2(t.Width * 0.5f, t.Height * 0.5f);
        var dir = Vector2.Normalize(center - sp);
        var right = new Vector2(-dir.Y, dir.X);
        void Spawn(string id, Vector2 p, int n = 1)
        {
            int bp = _ctx.Db.IndexOf(id);
            if (bp >= 0) _ctx.Runner.Enqueue(new DebugSpawnCommand(army, bp, t.Clamp(p, 4), n));
        }
        Spawn("uef_t1_land_factory", sp - dir * 9 + right * 10);
        Spawn("uef_t1_air_factory", sp - dir * 9 - right * 10);
        Spawn("uef_t1_power_generator", sp - dir * 3 + right * 6);
        Spawn("uef_t1_power_generator", sp - dir * 3 + right * 3);
        Spawn("uef_t1_engineer", sp + right * 5 + dir * 4, 2);
        // A mass extractor on the nearest mass deposit.
        Rts.Sim.Map.Deposit? best = null;
        foreach (var d in t.Deposits)
            if (d.Type == Rts.Sim.Map.DepositType.Mass && (best == null || Vector2.Distance(d.Pos, sp) < Vector2.Distance(best.Value.Pos, sp))) best = d;
        if (best is { } dep) Spawn("uef_t1_mass_extractor", dep.Pos);
    }

    /// <summary>Called once per frame before input handling.</summary>
    public void Update()
    {
        _frames++;
        if (_mouse is Vector2 m) Raylib.SetMousePosition((int)m.X, (int)m.Y);
        if (_selectDone && _queued && _placeDone) return;
        if (_frames > 600) { _selectDone = _queued = _placeDone = true; return; }
        var snap = _ctx.Snapshots.Current;

        if (!_queued)
        {
            int lf = _ctx.Db.IndexOf("uef_t1_land_factory");
            for (int k = 0; k < snap.UnitCount; k++)
            {
                ref var v = ref snap.Units[k];
                if (v.Army != _ctx.LocalArmy || v.Bp != lf) continue;
                var f = new[] { new UnitHandle(v.Index, v.Gen) };
                void Q(string id, int n) { int b = _ctx.Db.IndexOf(id); if (b >= 0) _ctx.Runner.Enqueue(new FactoryEnqueueCommand(_ctx.LocalArmy, f, b, n)); }
                Q("uef_t1_tank", 3);
                Q("uef_t1_engineer", 1);
                Q("uef_t1_artillery", 2);
                Q("uef_t1_tank", 2);
                _ctx.Runner.Enqueue(new IssueOrderCommand(_ctx.LocalArmy, f, Order.MoveTo(new Vector2(v.Pos.X, v.Pos.Z) + new Vector2(0, 12)), false));
                _queued = true;
                break;
            }
        }

        if (!_selectDone)
        {
            var picked = new List<UnitHandle>();
            // "any:<what>": units of every army the player controls (sandbox), e.g. --select any:uef_t1_tank.
            bool anyArmy = _select!.StartsWith("any:");
            string what = anyArmy ? _select[4..] : _select;
            for (int k = 0; k < snap.UnitCount; k++)
            {
                ref var v = ref snap.Units[k];
                if (anyArmy ? !_ctx.Controls(v.Army) : v.Army != _ctx.LocalArmy) continue;
                var bp = _ctx.Db[v.Bp];
                bool ok = what switch
                {
                    "commander" or "acu" => bp.Has(UnitCategory.Commander),
                    "engineer" => bp.Has(UnitCategory.Engineer) && !bp.Has(UnitCategory.Commander),
                    "factory" => bp.Has(UnitCategory.Factory) && bp.Has(UnitCategory.Land),
                    "airfactory" or "air_factory" => bp.Has(UnitCategory.Factory) && bp.Has(UnitCategory.Air),
                    "factories" => bp.Has(UnitCategory.Factory),
                    "army" or "all" => bp.IsMobile,
                    _ => _ctx.Db[v.Bp].Id == what,
                };
                if (ok) picked.Add(new UnitHandle(v.Index, v.Gen));
                if (ok && !anyArmy && what is "commander" or "acu" or "factory" or "airfactory" or "air_factory") break;
            }
            // Wait for the demo factory to exist before giving up on it.
            if (picked.Count > 0 && (!_demo || _queued || _select != "factory"))
            {
                _sel.SelectOnly(picked);
                _selectDone = true;
            }
        }

        if (_selectDone && !_placeDone)
        {
            int bp = _ctx.Db.IndexOf(_place!);
            if (bp >= 0) _sel.Placement.Begin(bp);
            if (bp >= 0 && _lineFrom is Vector2 lf) _sel.Placement.DebugHoldLine(lf);
            _placeDone = true;
        }
    }
}
