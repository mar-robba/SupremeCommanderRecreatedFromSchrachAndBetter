using Rts.Sim.Core;
using Rts.Sim.Orders;

namespace Rts.Client.Input;

/// <summary>
/// Sends unit commands for a selection. The sim checks every unit against the command's Army, so in the sandbox
/// (<see cref="GameContext.ControlAnyArmy"/>: units of any army can be selected) the selection is split by army and
/// one command per army is sent. In normal games the selection only holds own units: one command for the local army,
/// exactly as before.
/// </summary>
public static class ArmyCommands
{
    /// <summary>Groups units by army: ascending army, selection order kept inside a group, duplicates and
    /// negative armies (unknown units) dropped. Pure.</summary>
    public static List<(int Army, UnitHandle[] Units)> GroupByArmy(IEnumerable<(UnitHandle Unit, int Army)> units)
    {
        var byArmy = new SortedDictionary<int, List<UnitHandle>>();
        foreach (var (h, army) in units)
        {
            if (army < 0) continue;
            if (!byArmy.TryGetValue(army, out var list)) byArmy[army] = list = new List<UnitHandle>();
            if (!list.Contains(h)) list.Add(h);
        }
        var result = new List<(int, UnitHandle[])>(byArmy.Count);
        foreach (var (army, list) in byArmy) result.Add((army, list.ToArray()));
        return result;
    }

    /// <summary>The units split per army (normal games: one group for the local army with every unit).</summary>
    public static List<(int Army, UnitHandle[] Units)> Groups(GameContext ctx, IReadOnlyList<UnitHandle> units)
    {
        if (units.Count == 0) return [];
        if (!ctx.ControlAnyArmy) return [(ctx.LocalArmy, units.ToArray())];
        var snap = ctx.Snapshots.Current;
        return GroupByArmy(units.Select(h => (h, snap.TryGetUnit(h.Index, h.Generation, out var v) ? v.Army : -1)));
    }

    /// <summary>Enqueues make(army, units) once per army of the selection.</summary>
    public static void Send(GameContext ctx, IReadOnlyList<UnitHandle> units, Func<int, UnitHandle[], Command> make)
    {
        foreach (var (army, group) in Groups(ctx, units)) ctx.Runner.Enqueue(make(army, group));
    }

    /// <summary>Army whose point of view applies to the selection as a whole (cursor hints): the local army, or in the
    /// sandbox the army of the first selected unit that still exists.</summary>
    public static int PrimaryArmy(GameContext ctx, IReadOnlyList<UnitHandle> units)
    {
        if (!ctx.ControlAnyArmy) return ctx.LocalArmy;
        var snap = ctx.Snapshots.Current;
        foreach (var h in units)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v)) return v.Army;
        return ctx.LocalArmy;
    }

    /// <summary>The units of one army (the primary army's share of a mixed sandbox selection).</summary>
    public static UnitHandle[] OfArmy(GameContext ctx, IReadOnlyList<UnitHandle> units, int army)
    {
        if (!ctx.ControlAnyArmy) return units.ToArray();
        var snap = ctx.Snapshots.Current;
        return units.Where(h => snap.TryGetUnit(h.Index, h.Generation, out var v) && v.Army == army).ToArray();
    }
}
