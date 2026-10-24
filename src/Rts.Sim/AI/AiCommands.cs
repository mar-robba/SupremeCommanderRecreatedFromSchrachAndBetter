using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>Builds ordinary player commands for one AI army (the AI has no other way to act on the world).</summary>
public sealed class AiCommands
{
    private readonly SimWorld _w;
    private readonly int _army;
    private readonly AiStats _stats;
    private List<Command> _out = new();
    private readonly List<UnitHandle> _tmp = new();

    public AiCommands(SimWorld w, int army, AiStats stats)
    {
        _w = w;
        _army = army;
        _stats = stats;
    }

    public void Begin(List<Command> output) => _out = output;

    private UnitHandle H(int i) => _w.Units.HandleOf(i);

    public void Order(int unit, Order o, bool queue = false)
    {
        _out.Add(new IssueOrderCommand(_army, [H(unit)], o, queue));
        _stats.Commands++;
    }

    public void Order(List<int> units, Order o, bool queue = false)
    {
        if (units.Count == 0) return;
        _tmp.Clear();
        foreach (int i in units) _tmp.Add(H(i));
        _out.Add(new IssueOrderCommand(_army, _tmp.ToArray(), o, queue));
        _stats.Commands++;
    }

    public void Stop(int unit)
    {
        _out.Add(new StopCommand(_army, [H(unit)]));
        _stats.Commands++;
    }

    /// <summary>In-place structure upgrade (to its blueprint's UpgradesTo).</summary>
    public void Upgrade(int unit)
    {
        _out.Add(new UpgradeCommand(_army, [H(unit)]));
        _stats.Commands++;
        _stats.Upgrades++;
    }

    /// <summary>Starts building an enhancement (by id) on a commander/SCU.</summary>
    public void Enhance(int unit, string enhancement)
    {
        _out.Add(new EnhanceCommand(_army, [H(unit)], enhancement));
        _stats.Commands++;
        _stats.Enhancements++;
    }

    public void SetPaused(int unit, bool paused)
    {
        _out.Add(new SetPausedCommand(_army, [H(unit)], paused));
        _stats.Commands++;
    }

    public void Enqueue(int factory, int bp, int count = 1)
    {
        _out.Add(new FactoryEnqueueCommand(_army, [H(factory)], bp, count));
        _stats.Commands++;
    }
}
