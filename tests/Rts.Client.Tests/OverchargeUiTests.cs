using System.Numerics;
using Rts.Client.Input;
using Rts.Client.Rendering;
using Rts.Client.Sandbox;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Client.Tests;

/// <summary>ACU Overcharge UI (toggle state fold, blueprint lookup), the commander death blast fx test and the
/// sandbox console commands <c>overcharge</c> / area <c>kill</c>.</summary>
public class OverchargeUiTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    private static ConsoleContext Ctx(int local = 0, int count = 2) => new() { Db = Db, LocalArmy = local, ArmyCount = count };

    private static T Ok<T>(string line, ConsoleContext? ctx = null) where T : ConsoleAction
    {
        var r = SandboxConsoleParser.Parse(line, ctx ?? Ctx());
        Assert.True(r.Error == null, $"'{line}' failed: {r.Error}");
        return Assert.IsType<T>(r.Action);
    }

    private static IEnumerable<int> Commanders() => Db.Factions.All.Select(f => Db.Factions.CommanderOf(f.Id));

    // ------------------------------------------------------------ toggle state

    [Fact]
    public void EvaluateNoOverchargeUnits()
    {
        var s = UnitToggles.EvaluateOvercharge([]);
        Assert.False(s.Any);
        Assert.False(s.AllOn);
        Assert.Null(s.Rule);
        // Units without an Overcharge weapon are ignored.
        s = UnitToggles.EvaluateOvercharge([(null, true, 5000f), (null, false, 5000f)]);
        Assert.Equal(0, s.Count);
        Assert.False(s.Ready);
    }

    [Fact]
    public void EvaluateArmedAndReady()
    {
        var oc = new OverchargeBlueprint { MinEnergy = 1000, MaxEnergy = 5000 };
        var s = UnitToggles.EvaluateOvercharge([(oc, true, 2500f), (null, false, 0f)]);
        Assert.Equal(1, s.Count);
        Assert.Equal(1, s.On);
        Assert.True(s.AllOn);
        Assert.True(s.Ready);
        Assert.Equal(2500f, s.Stored);
        Assert.Same(oc, s.Rule);
        Assert.Contains("Armed", UnitToggles.OverchargeTooltip(s));

        // Two ACUs, one armed: not AllOn (the toggle then arms all).
        s = UnitToggles.EvaluateOvercharge([(oc, true, 2500f), (oc, false, 2500f)]);
        Assert.Equal(2, s.Count);
        Assert.Equal(1, s.On);
        Assert.False(s.AllOn);
        Assert.Contains("1 of 2", UnitToggles.OverchargeTooltip(s));
    }

    [Fact]
    public void EvaluateNotReadyBelowMinEnergy()
    {
        var oc = new OverchargeBlueprint { MinEnergy = 1000, MaxEnergy = 5000 };
        var s = UnitToggles.EvaluateOvercharge([(oc, true, 999f)]);
        Assert.True(s.Any);
        Assert.True(s.AllOn);
        Assert.False(s.Ready);
        Assert.Equal(999f, s.Stored);
        Assert.Contains("Not ready", UnitToggles.OverchargeTooltip(s));
        // Ready when any of the armies stores enough.
        Assert.True(UnitToggles.EvaluateOvercharge([(oc, false, 10f), (oc, false, 1000f)]).Ready);
    }

    [Fact]
    public void EveryFactionCommanderHasOverchargeAndATankHasNone()
    {
        Assert.True(Db.Factions.All.Count >= 2);
        foreach (int cmd in Commanders())
        {
            Assert.True(cmd >= 0);
            var oc = UnitToggles.OverchargeOf(Db[cmd]);
            Assert.NotNull(oc);
            Assert.True(oc!.MinEnergy > 0 && oc.MaxEnergy >= oc.MinEnergy, Db[cmd].Id);
        }
        foreach (var f in Db.Factions.All)
        {
            int tank = Db.Factions.RoleBlueprint(f.Id, FactionRoles.TankT1);
            if (tank >= 0) Assert.Null(UnitToggles.OverchargeOf(Db[tank]));
        }
    }

    [Fact]
    public void CommanderBlastOnlyForCommanders()
    {
        foreach (int cmd in Commanders()) Assert.True(EffectsRenderer.IsCommanderBlast(Db, cmd), Db[cmd].Id);
        foreach (var f in Db.Factions.All)
        {
            int tank = Db.Factions.RoleBlueprint(f.Id, FactionRoles.TankT1);
            if (tank >= 0) Assert.False(EffectsRenderer.IsCommanderBlast(Db, tank));
        }
        Assert.False(EffectsRenderer.IsCommanderBlast(Db, -1));
        Assert.False(EffectsRenderer.IsCommanderBlast(Db, Db.Count));
    }

    // ------------------------------------------------------------ console

    [Fact]
    public void OverchargeCommandParsesArmyAndOnOff()
    {
        Assert.Equal(new OverchargeAction(0, true), Ok<OverchargeAction>("overcharge"));
        Assert.Equal(new OverchargeAction(1, false), Ok<OverchargeAction>("oc enemy off"));
        Assert.Equal(new OverchargeAction(-1, true), Ok<OverchargeAction>("overcharge all on"));
    }

    [Fact]
    public void KillCommandAreaAndSelectionForms()
    {
        Assert.Equal(new KillAreaAction(8f, new Vector2(100, 120)), Ok<KillAreaAction>("kill 8 at 100,120"));
        Assert.Equal(new KillAreaAction(SandboxConsoleParser.DefaultKillRadius, new Vector2(100, 120)), Ok<KillAreaAction>("kill at 100,120"));
        var r = SandboxConsoleParser.Parse("kill 8", Ctx());
        Assert.Null(r.Action);
        Assert.NotNull(r.Error);
        Assert.Equal(SandboxUnitAction.Kill, Ok<UnitActionAction>("kill").Action);
    }
}
