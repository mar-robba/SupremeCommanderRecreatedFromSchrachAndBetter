using System.Numerics;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>
/// ACU Personal Teleporter order (<see cref="SelectionController.CommandMode.Teleport"/>, hotkey <see cref="Hotkeys.Teleport"/>,
/// orders panel button): a click on the ground sends <see cref="Order.TeleportTo"/> to every selected unit that has a
/// teleporter (<c>bp.Teleport != null</c>, the Personal Teleporter enhancement); Shift queues. The sim charges for
/// ChargeTime seconds paying EnergyBase + EnergyPerDistance × distance (Commander/TeleportSystem). The targeting
/// overlay and cursor label are drawn by <see cref="UI.TeleportHud"/>.
/// </summary>
public static class TeleportInput
{
    /// <summary>Teleport state of a selection: Count = controllable, complete units with a teleporter, Charging = how
    /// many are charging a jump now (TeleportFrac ≥ 0), Rule = the teleporter of the first one (null when Count is 0).</summary>
    public readonly record struct TeleportSelection(int Count, int Charging, TeleportBlueprint? Rule)
    {
        public bool Any => Count > 0;
    }

    /// <summary>Pure fold: one entry per controllable, complete unit (its teleporter or null, its charge fraction).</summary>
    public static TeleportSelection Evaluate(IEnumerable<(TeleportBlueprint? Tp, float Frac)> units)
    {
        int n = 0, charging = 0;
        TeleportBlueprint? rule = null;
        foreach (var (tp, frac) in units)
        {
            if (tp == null) continue;
            rule ??= tp;
            n++;
            if (frac >= 0) charging++;
        }
        return new TeleportSelection(n, charging, rule);
    }

    public static TeleportSelection State(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var snap = ctx.Snapshots.Current;
        var list = new List<(TeleportBlueprint?, float)>(selected.Count);
        foreach (var h in selected)
            if (Teleporter(ctx, snap, h, out var v)) list.Add((ctx.Db[v.Bp].Teleport, v.TeleportFrac));
        return Evaluate(list);
    }

    /// <summary>A selected unit the local player may order that has a (finished) teleporter.</summary>
    public static bool Teleporter(GameContext ctx, SimSnapshot snap, UnitHandle h, out UnitView v) =>
        snap.TryGetUnit(h.Index, h.Generation, out v) && ctx.Controls(v.Army) && (v.Flags & UnitFlags.UnderConstruction) == 0
        && ctx.Db[v.Bp].Teleport != null;

    /// <summary>Where a jump from <paramref name="from"/> to the clicked point lands before the sim's passable-cell
    /// nudge: pulled back to MaxRange (when &gt; 0). Mirrors <c>TeleportSystem.Target</c> without the map clamp.</summary>
    public static Vector2 Destination(TeleportBlueprint tp, Vector2 from, Vector2 to)
    {
        if (tp.MaxRange <= 0) return to;
        var d = to - from;
        float len = d.Length();
        return len > tp.MaxRange ? from + d * (tp.MaxRange / len) : to;
    }

    /// <summary>Energy a jump from <paramref name="from"/> to <paramref name="to"/> costs (fixed when the charge starts).</summary>
    public static float Cost(TeleportBlueprint tp, Vector2 from, Vector2 to) =>
        MathF.Max(1f, tp.Cost(Vector2.Distance(from, Destination(tp, from, to))));

    /// <summary>Where the unit's next order would start: its position, or (queueing) the end of its order queue.</summary>
    public static Vector2 StartPoint(SimSnapshot snap, in UnitView v, bool queue)
    {
        if (queue && v.OrderCount > 0)
        {
            var o = snap.Orders[v.OrderStart + v.OrderCount - 1].Pos;
            return new Vector2(o.X, o.Z);
        }
        return new Vector2(v.Pos.X, v.Pos.Z);
    }

    /// <summary>Total energy of sending every selected teleporter to <paramref name="p"/> (Count = how many).</summary>
    public static (float Energy, int Count, float ChargeTime) Estimate(GameContext ctx, IReadOnlyList<UnitHandle> selected, Vector2 p, bool queue)
    {
        var snap = ctx.Snapshots.Current;
        float e = 0, t = 0;
        int n = 0;
        foreach (var h in selected)
        {
            if (!Teleporter(ctx, snap, h, out var v)) continue;
            var tp = ctx.Db[v.Bp].Teleport!;
            e += Cost(tp, StartPoint(snap, v, queue), p);
            t = MathF.Max(t, tp.ChargeTime);
            n++;
        }
        return (e, n, t);
    }

    /// <summary>The energy rule as one line (tooltips).</summary>
    public static string RuleText(TeleportBlueprint tp)
    {
        string range = tp.MaxRange > 0 ? $" Range {tp.MaxRange:0}." : " Any distance.";
        return $"Charges {tp.ChargeTime:0.#} s standing still, using {tp.EnergyBase:0} energy + {tp.EnergyPerDistance:0.#} per unit of "
               + $"distance (an energy stall slows it), then jumps.{range} Any new order cancels the charge (energy spent is lost).";
    }

    /// <summary>Cursor label while targeting: total energy and charge time.</summary>
    public static string CostLabel(float energy, int count, float chargeTime) =>
        $"Teleport{(count > 1 ? $" ×{count}" : "")}: {energy:0} E · {chargeTime:0.#} s";

    /// <summary>Teleport mode click: the ground point under the mouse.</summary>
    public static bool TryTeleport(GameContext ctx, SelectionController sel, Vector2 mouse, bool queue)
    {
        if (ctx.PickGround(mouse) is not Vector3 g) { UiSounds.Error(); return false; }
        return TryTeleportAt(ctx, sel, new Vector2(g.X, g.Z), queue);
    }

    /// <summary>Sends the selected teleporters to world point p (map or minimap click). False = none selected.</summary>
    public static bool TryTeleportAt(GameContext ctx, SelectionController sel, Vector2 p, bool queue)
    {
        var snap = ctx.Snapshots.Current;
        p = ctx.Terrain.Clamp(p, 1f);
        var units = new List<(UnitHandle, int)>();
        foreach (var h in sel.Selected)
            if (Teleporter(ctx, snap, h, out var v)) units.Add((h, v.Army));
        if (units.Count == 0) { UiSounds.Error(); return false; }
        foreach (var (army, group) in ArmyCommands.GroupByArmy(units))
            ctx.Runner.Enqueue(new IssueOrderCommand(army, group, Order.TeleportTo(p), queue));
        ctx.Feedback.AddClickMarker(new Vector3(p.X, ctx.Terrain.GetSurfaceHeight(p), p.Y), OrderType.Teleport);
        UiSounds.Confirm();
        return true;
    }

    /// <summary>J: enters teleport mode when a teleporter is selected.</summary>
    public static void HandleHotkey(GameContext ctx, SelectionController sel, bool ctrl)
    {
        if (!ctrl && Hotkeys.Teleport.Pressed() && State(ctx, sel.Selected).Any) sel.SetMode(SelectionController.CommandMode.Teleport);
    }
}
