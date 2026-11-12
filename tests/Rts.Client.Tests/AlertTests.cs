using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.UI;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>(7b) Notification log (merge, cooldown, jump cycle), the alert monitor's rules on hand-made snapshots, ping kinds.</summary>
public class AlertTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
    private static int Mobile => Enumerable.Range(0, Db.Count).First(i => Db[i].IsMobile && !Db[i].IsStructure && !Db[i].Has(UnitCategory.Commander));
    private static int Commander => Enumerable.Range(0, Db.Count).First(i => Db[i].Has(UnitCategory.Commander));
    private static int[] Structures => Enumerable.Range(0, Db.Count).Where(i => Db[i].IsStructure).Take(2).ToArray();

    /// <summary>Armies: 0 = me, 1 = ally, 2 = enemy.</summary>
    private static SimSnapshot Snap(int tick, params UnitView[] units)
    {
        var s = new SimSnapshot { Tick = tick, UnitCount = units.Length };
        s.Units = units.Length > 0 ? units.ToArray() : new UnitView[4];
        s.SlotToView = Enumerable.Repeat(-1, 64).ToArray();
        for (int k = 0; k < units.Length; k++) s.SlotToView[units[k].Index] = k;
        s.Armies = [new ArmyView { Name = "Me" }, new ArmyView { Name = "Ally" }, new ArmyView { Name = "Foe" }];
        return s;
    }

    private static UnitView U(int index, int army, int bp, float hp, float x = 100, UnitFlags flags = 0) =>
        new() { Index = index, Gen = 1, Army = army, Bp = bp, HealthFrac = hp, Pos = new Vector3(x, 0, 100), Flags = flags };

    private static List<(AlertKind kind, string text)> Run(AlertMonitor m, SimSnapshot? prev, SimSnapshot cur,
        List<EffectEvent>? fx = null, bool onScreen = false, double now = 10)
    {
        var got = new List<(AlertKind, string)>();
        m.Poll(prev, cur, fx ?? [], 0, a => a == 2, _ => onScreen, now, (k, t, _) => got.Add((k, t)));
        return got;
    }

    [Fact]
    public void LogMergesNearbyAlertsOfOneKindAndCyclesJumps()
    {
        var log = new AlertLog();
        Assert.Null(log.NextJump(0));
        Assert.True(log.Add(AlertKind.UnderAttack, new Vector3(10, 0, 10), 1, "a"));
        Assert.False(log.Add(AlertKind.UnderAttack, new Vector3(20, 0, 10), 2, "a")); // same area, recent
        Assert.True(log.Add(AlertKind.StructureLost, new Vector3(20, 0, 10), 2, "b")); // other kind
        Assert.True(log.Add(AlertKind.UnderAttack, new Vector3(200, 0, 10), 3, "c")); // far away
        Assert.True(log.Add(AlertKind.UnderAttack, new Vector3(10, 0, 10), 1 + AlertLog.AreaCooldown + 0.1, "d")); // later
        Assert.Equal(4, log.Entries.Count);
        double t = 20;
        Assert.Equal("d", log.NextJump(t)!.Value.Text);
        Assert.Equal("c", log.NextJump(t + 1)!.Value.Text);
        Assert.Equal("b", log.NextJump(t + 2)!.Value.Text);
        Assert.Equal("a", log.NextJump(t + 3)!.Value.Text);
        Assert.Equal("d", log.NextJump(t + 4)!.Value.Text); // wraps
        Assert.Equal("d", log.NextJump(t + 4 + AlertLog.CycleWindow + 1)!.Value.Text); // after a pause: newest again
        Assert.Single(log.Flashes(1 + AlertLog.AreaCooldown + 1)); // only "d" still flashing
    }

    [Fact]
    public void NotifyCooldownIsPerKind()
    {
        var log = new AlertLog();
        Assert.True(log.ShouldNotify(AlertKind.UnderAttack, 0, 25));
        Assert.False(log.ShouldNotify(AlertKind.UnderAttack, 10, 25));
        Assert.True(log.ShouldNotify(AlertKind.CommanderUnderAttack, 10, 20));
        Assert.True(log.ShouldNotify(AlertKind.UnderAttack, 26, 25));
    }

    [Fact]
    public void DamageRaisesOwnAndAllyAlertsButNotEnemy()
    {
        var log = new AlertLog();
        var m = new AlertMonitor(Db, log);
        var prev = Snap(1, U(1, 0, Mobile, 1f), U(2, 1, Mobile, 1f, 400), U(3, 2, Mobile, 1f, 700));
        var cur = Snap(2, U(1, 0, Mobile, 0.9f), U(2, 1, Mobile, 0.9f, 400), U(3, 2, Mobile, 0.5f, 700));
        var got = Run(m, prev, cur);
        Assert.Contains((AlertKind.UnderAttack, "Units under attack"), got);
        Assert.Contains((AlertKind.AllyUnderAttack, "Ally under attack: Ally"), got);
        Assert.Equal(2, got.Count);
        Assert.Equal(2, log.Entries.Count); // both flash on the minimap
    }

    [Fact]
    public void OnScreenAttackFlashesWithoutToastAndCommanderAlwaysAlerts()
    {
        var log = new AlertLog();
        var m = new AlertMonitor(Db, log);
        var got = Run(m, Snap(1, U(1, 0, Mobile, 1f), U(2, 0, Commander, 1f, 300)),
            Snap(2, U(1, 0, Mobile, 0.8f), U(2, 0, Commander, 0.8f, 300)), onScreen: true);
        Assert.Equal([(AlertKind.CommanderUnderAttack, "Commander under attack!")], got);
        Assert.Equal(2, log.Entries.Count);
    }

    [Fact]
    public void BuildCompleteTellsUpgradeFromConstructionAndCountsRepeats()
    {
        var m = new AlertMonitor(Db, new AlertLog());
        int a = Structures[0], b = Structures[1];
        var fx = new EffectEvent { Kind = EffectKind.BuildComplete, Army = 0, BlueprintIndex = b, Pos = new Vector3(100, 0, 100) };
        // Same unit handle had blueprint a before: an upgrade.
        var got = Run(m, Snap(1, U(1, 0, a, 1f)), Snap(2, U(1, 0, b, 1f)), [fx]);
        Assert.Equal([(AlertKind.UpgradeDone, "Upgrade complete: " + Db[b].Name)], got);
        // A new unit: construction; twice within 4 s: "×2".
        got = Run(m, Snap(2), Snap(3, U(5, 0, b, 1f)), [fx], now: 11);
        Assert.Equal([(AlertKind.ConstructionDone, "Construction complete: " + Db[b].Name)], got);
        got = Run(m, Snap(3), Snap(4, U(6, 0, b, 1f)), [fx], now: 12);
        Assert.Equal([(AlertKind.ConstructionDone, "Construction complete: " + Db[b].Name + " ×2")], got);
        // Someone else's structure: nothing.
        Assert.Empty(Run(m, Snap(4), Snap(5), [fx with { Army = 1 }], now: 13));
    }

    [Fact]
    public void StructureLostSkipsCancelledConstruction()
    {
        var m = new AlertMonitor(Db, new AlertLog());
        int a = Structures[0];
        var fx = new EffectEvent { Kind = EffectKind.UnitDeath, Army = 0, BlueprintIndex = a, Pos = new Vector3(100, 0, 100) };
        Assert.Empty(Run(m, Snap(1, U(1, 0, a, 0.3f, flags: UnitFlags.UnderConstruction)), Snap(2), [fx]));
        Assert.Equal([(AlertKind.StructureLost, "Structure lost: " + Db[a].Name)], Run(m, Snap(2, U(1, 0, a, 0.3f)), Snap(3), [fx]));
        Assert.Empty(Run(m, Snap(3), Snap(4), [fx with { BlueprintIndex = Mobile }])); // mobile units: no toast
    }

    [Fact]
    public void StructureLostSkipsSelfDestruct() // (10b-2)
    {
        var log = new AlertLog();
        var m = new AlertMonitor(Db, log);
        int a = Structures[0];
        var fx = new EffectEvent { Kind = EffectKind.UnitDeath, Army = 0, BlueprintIndex = a, Pos = new Vector3(100, 0, 100) };
        log.NoteSelfDestruct(new Rts.Sim.Core.UnitHandle(1, 1));
        Assert.Empty(Run(m, Snap(1, U(1, 0, a, 1f)), Snap(2), [fx]));
        Assert.Single(Run(m, Snap(2, U(1, 0, a, 1f)), Snap(3), [fx])); // forgotten once used: a later death of that slot alerts
    }

    [Fact]
    public void NewPingsAlertOnceAndOwnPingsOnlySound()
    {
        var log = new AlertLog();
        var m = new AlertMonitor(Db, log);
        Assert.Empty(Run(m, null, Snap(1))); // baseline
        var s = Snap(2);
        s.Pings[0] = new MapPingView { Id = 1, Army = 1, Kind = PingKind.Attack, Pos = new Vector3(50, 0, 60), Tick = 2 };
        s.Pings[1] = new MapPingView { Id = 2, Army = 0, Kind = PingKind.Move, Pos = new Vector3(300, 0, 60), Tick = 2 };
        s.PingCount = 2;
        s.PingSerial = 2;
        Assert.Equal([(AlertKind.Ping, "Ally: attack here"), (AlertKind.Ping, "")], Run(m, Snap(1), s));
        var s2 = Snap(3);
        Array.Copy(s.Pings, s2.Pings, 2);
        s2.PingCount = 2;
        s2.PingSerial = 2;
        Assert.Empty(Run(m, s, s2)); // already seen
        Assert.Equal(new Vector3(300, 0, 60), log.NextJump(20)!.Value.Pos); // Backspace goes to the newest ping
    }

    [Fact]
    public void UnitCapAlertsOncePerFill()
    {
        var m = new AlertMonitor(Db, new AlertLog());
        SimSnapshot Cap(int tick, int count) { var s = Snap(tick); s.Armies[0].UnitCap = 10; s.Armies[0].UnitCount = count; return s; }
        Assert.Single(Run(m, null, Cap(1, 10)), g => g.kind == AlertKind.UnitCapReached);
        Assert.Empty(Run(m, null, Cap(2, 10)));
        Assert.Empty(Run(m, null, Cap(3, 5)));
        Assert.Single(Run(m, null, Cap(4, 10)));
    }

    [Fact]
    public void PingKindForMouseButtons()
    {
        Assert.Equal(PingKind.Alert, PingInput.KindFor(right: false, shift: false));
        Assert.Equal(PingKind.Attack, PingInput.KindFor(right: false, shift: true));
        Assert.Equal(PingKind.Move, PingInput.KindFor(right: true, shift: false));
        Assert.Equal(Audio.SoundId.AlertPing, Audio.GameAudio.AlertSound(AlertKind.Ping));
        Assert.NotEqual(PingMarkers.KindColor(PingKind.Alert).R + PingMarkers.KindColor(PingKind.Alert).B, PingMarkers.KindColor(PingKind.Move).R + PingMarkers.KindColor(PingKind.Move).B);
    }
}
