using System.Numerics;
using Rts.Client.Input;
using Rts.Client.Sandbox;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;

namespace Rts.Client.Tests;

/// <summary>Personal Teleporter UI (TeleportInput selection fold, destination clamp, cost / estimate helpers, labels)
/// and the sandbox console command <c>teleport</c>.</summary>
public class TeleportUiTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    private static ConsoleContext Ctx(int local = 0, int count = 2) => new() { Db = Db, LocalArmy = local, ArmyCount = count };

    private static T Ok<T>(string line, ConsoleContext? ctx = null) where T : ConsoleAction
    {
        var r = SandboxConsoleParser.Parse(line, ctx ?? Ctx());
        Assert.True(r.Error == null, $"'{line}' failed: {r.Error}");
        return Assert.IsType<T>(r.Action);
    }

    private static void Fails(string line)
    {
        var r = SandboxConsoleParser.Parse(line, Ctx());
        Assert.Null(r.Action);
        Assert.False(string.IsNullOrEmpty(r.Error), $"'{line}' should fail");
    }

    private static TeleportBlueprint VariantTeleporter()
    {
        Assert.True(Db.TryGet("uef_commander+PersonalTeleporter", out var bp), "teleporter variant missing");
        Assert.NotNull(bp.Teleport);
        return bp.Teleport!;
    }

    // ------------------------------------------------------------ selection fold

    [Fact]
    public void PlainCommanderHasNoTeleporterVariantHas()
    {
        Assert.Null(Db["uef_commander"].Teleport);
        var tp = VariantTeleporter();
        var none = TeleportInput.Evaluate([(Db["uef_commander"].Teleport, -1f)]);
        Assert.False(none.Any);
        Assert.Null(none.Rule);
        Assert.Equal(0, TeleportInput.Evaluate([]).Count);

        var s = TeleportInput.Evaluate([(tp, -1f), (null, 0.5f), (tp, 0.3f)]);
        Assert.True(s.Any);
        Assert.Equal(2, s.Count);
        Assert.Equal(1, s.Charging); // the teleporter-less unit's fraction is ignored
        Assert.Same(tp, s.Rule);
    }

    // ------------------------------------------------------------ destination / cost

    [Fact]
    public void DestinationClampsToMaxRange()
    {
        var unlimited = new TeleportBlueprint { MaxRange = 0 };
        Assert.Equal(new Vector2(900, 100), TeleportInput.Destination(unlimited, new Vector2(100, 100), new Vector2(900, 100)));
        var tp = new TeleportBlueprint { MaxRange = 200 };
        var d = TeleportInput.Destination(tp, new Vector2(100, 100), new Vector2(500, 100));
        Assert.Equal(300f, d.X, 3);
        Assert.Equal(100f, d.Y, 3);
        // Within range: unchanged.
        Assert.Equal(new Vector2(150, 220), TeleportInput.Destination(tp, new Vector2(100, 100), new Vector2(150, 220)));
    }

    [Fact]
    public void CostMatchesBlueprintRule()
    {
        var tp = VariantTeleporter();
        var from = new Vector2(100, 128);
        Assert.Equal(tp.Cost(50), TeleportInput.Cost(tp, from, new Vector2(150, 128)), 2);
        Assert.Equal(tp.EnergyBase + tp.EnergyPerDistance * 50, TeleportInput.Cost(tp, from, new Vector2(150, 128)), 2);
        Assert.Equal(tp.EnergyBase, TeleportInput.Cost(tp, from, from), 2);
        // The cost uses the clamped distance.
        var ranged = new TeleportBlueprint { EnergyBase = 1000, EnergyPerDistance = 10, MaxRange = 100 };
        Assert.Equal(ranged.Cost(100), TeleportInput.Cost(ranged, Vector2.Zero, new Vector2(400, 0)), 2);
        Assert.True(TeleportInput.Cost(new TeleportBlueprint { EnergyBase = 0, EnergyPerDistance = 0 }, from, from) >= 1f);
    }

    [Fact]
    public void StartPointIsQueueEndWhenQueueing()
    {
        var snap = new SimSnapshot();
        snap.Orders[3] = new OrderView { Type = OrderType.Move, Pos = new Vector3(40, 0, 50) };
        snap.Orders[4] = new OrderView { Type = OrderType.Move, Pos = new Vector3(60, 0, 70) };
        var v = new UnitView { Pos = new Vector3(10, 5, 20), OrderStart = 3, OrderCount = 2 };
        Assert.Equal(new Vector2(10, 20), TeleportInput.StartPoint(snap, v, false));
        Assert.Equal(new Vector2(60, 70), TeleportInput.StartPoint(snap, v, true));
        var idle = new UnitView { Pos = new Vector3(10, 5, 20) };
        Assert.Equal(new Vector2(10, 20), TeleportInput.StartPoint(snap, idle, true));
    }

    [Fact]
    public void LabelsShowEnergyAndTime()
    {
        var tp = VariantTeleporter();
        Assert.Equal("Teleport: 7500 E · 10 s", TeleportInput.CostLabel(7500, 1, 10));
        Assert.Equal("Teleport ×2: 15000 E · 10 s", TeleportInput.CostLabel(15000, 2, 10));
        string rule = TeleportInput.RuleText(tp);
        Assert.Contains($"{tp.EnergyBase:0} energy", rule);
        Assert.Contains("cancels", rule);
        Assert.Contains(tp.MaxRange > 0 ? "Range" : "Any distance", rule);
    }

    [Fact]
    public void TeleportHotkeyIsJInOrders()
    {
        Assert.Equal(Raylib_cs.KeyboardKey.J, Hotkeys.Teleport.Key);
        Assert.Contains(Hotkeys.Teleport, Hotkeys.All);
    }

    // ------------------------------------------------------------ console

    [Fact]
    public void TeleportCommandParsesArmyAndPosition()
    {
        Assert.Equal(new TeleportAction(0, new Vector2(150, 128)), Ok<TeleportAction>("teleport 150,128"));
        Assert.Equal(new TeleportAction(1, new Vector2(10, 20)), Ok<TeleportAction>("teleport enemy at 10,20"));
        Assert.Equal(new TeleportAction(0, new Vector2(30, 40)), Ok<TeleportAction>("tp me at 30 40"));
        Assert.Equal(new TeleportAction(-1, new Vector2(5, 6)), Ok<TeleportAction>("teleport all 5,6"));
        Assert.Equal(new TeleportAction(0, new Vector2(7, 8)), Ok<TeleportAction>("teleport enemy 7,8", Ctx(local: 1)));
    }

    [Fact]
    public void TeleportCommandRejectsBadInput()
    {
        Fails("teleport");
        Fails("teleport enemy");
        Fails("teleport at");
        Fails("teleport at banana");
        Fails("teleport 10,x");
        Fails("teleport 10,20 30,40");
        Fails("teleport me enemy 10,20");
        Fails("teleport 10,20 me");
    }

    [Fact]
    public void InterpolationSnapsOnTeleportJumps()
    {
        var a = new Vector3(100, 5, 128);
        // Normal movement (fastest unit: 2.5 per tick) is interpolated, height too.
        var b = new Vector3(102, 7, 129);
        Assert.Equal(Vector3.Lerp(a, b, 0.5f), SnapshotInterpolator.LerpPos(a, b, 0.5f));
        Assert.Equal(a, SnapshotInterpolator.LerpPos(a, b, 0f));
        // A jump further than SnapDistance (horizontal) shows the new position at once, at any alpha.
        var far = new Vector3(150, 5, 128);
        Assert.Equal(far, SnapshotInterpolator.LerpPos(a, far, 0f));
        Assert.Equal(far, SnapshotInterpolator.LerpPos(a, far, 0.3f));
        Assert.Equal(new Vector3(100, 5, 123), SnapshotInterpolator.LerpPos(a, new Vector3(100, 5, 108), 0.25f)); // exactly 20: still a lerp
        // Vertical changes alone never snap (e.g. an aircraft climbing).
        var up = new Vector3(100, 60, 128);
        Assert.Equal(Vector3.Lerp(a, up, 0.5f), SnapshotInterpolator.LerpPos(a, up, 0.5f));
    }
}
