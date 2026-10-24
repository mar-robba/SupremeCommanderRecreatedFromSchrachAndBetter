using System.Numerics;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Systems;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>ACU Personal Teleporter (Commander/TeleportSystem, wave 6a G7).</summary>
public class TeleportTests
{
    private static Simulation NewGame(string faction, int seed = 42, MapPreset preset = MapPreset.Plains) =>
        GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = seed, MapSize = 256, Players = 2, AiPlayers = new bool[2], Factions = [faction, faction], MapPreset = preset,
        });

    private static UnitHandle Acu(Simulation sim, int army = 0) => sim.World.Armies[army].Commander;

    /// <summary>Installs the teleporter at once (test shortcut for the enhancement build, as in EnhancementTests).</summary>
    private static void InstallTeleporter(SimWorld w, UnitHandle h)
    {
        var bp = w.BpOf(h.Index);
        int e = EnhancementCatalog.Find(bp, "PersonalTeleporter");
        Assert.True(EnhancementCatalog.CanInstall(bp, e), $"can't install the teleporter on {bp.Id}");
        w.Units.Upgrade[h.Index] = new UpgradeState { Active = true, TargetBp = EnhancementCatalog.WithSlot(w.Blueprints, bp, bp.Enhancements[e].Slot, e) };
        UpgradeSystem.Complete(w, h.Index);
        Assert.NotNull(w.BpOf(h.Index).Teleport);
    }

    private static Simulation Ready(string faction = "uef")
    {
        var sim = NewGame(faction);
        sim.Step();
        InstallTeleporter(sim.World, Acu(sim));
        return sim;
    }

    /// <summary>Point <paramref name="dist"/> from the ACU towards the map center (open plains).</summary>
    private static Vector2 Toward(SimWorld w, float dist)
    {
        var c = CommanderPos(w);
        var center = new Vector2(w.Terrain.Width, w.Terrain.Height) * 0.5f;
        return c + Vector2.Normalize(center - c) * dist;
    }

    private static Command Teleport(Simulation sim, Vector2 p, bool queue = false) =>
        new IssueOrderCommand(0, [Acu(sim)], Order.TeleportTo(p), queue);

    private static void StepTopped(Simulation sim, params Command[] cmds)
    {
        foreach (var a in sim.World.Armies) a.Eco.Energy = a.Eco.EnergyMax;
        sim.Step(cmds);
    }

    private static int Count(SimWorld w, EffectKind k) => w.Events.Effects.Count(e => e.Kind == k);

    private static TeleportBlueprint Tp(SimWorld w, UnitHandle h) => w.BpOf(h.Index).Teleport!;

    [Theory]
    [InlineData("uef")]
    [InlineData("cybran")]
    public void JumpsAfterTheChargeTimeAndNotBefore(string faction)
    {
        var sim = Ready(faction);
        var w = sim.World;
        var acu = Acu(sim);
        Assert.Equal(w.Blueprints.Factions.CommanderOf(faction), w.BpOf(acu.Index).EnhancementBase);
        var start = w.Units.Pos[acu.Index];
        var dest = Toward(w, 60);
        var tp = Tp(w, acu);
        int chargeTicks = (int)MathF.Round(tp.ChargeTime / SimWorld.Dt);
        w.Events.Effects.Clear();
        StepTopped(sim, Teleport(sim, dest));
        Assert.Equal(1, Count(w, EffectKind.TeleportCharge));
        Assert.Equal(OrderType.Teleport, w.Units.Orders[acu.Index].Current.Type);
        Assert.InRange(TeleportSystem.CostOf(w, acu.Index), tp.Cost(Vector2.Distance(start, dest)) - 1f, tp.Cost(Vector2.Distance(start, dest)) + 1f);
        float last = -1;
        for (int t = 1; t < chargeTicks; t++)
        {
            StepTopped(sim);
            Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], start) < 0.5f, $"moved before the charge ended (tick {t})");
            float f = TeleportSystem.Progress(w, acu.Index);
            Assert.True(f > last && f < 1f, $"charge not rising at tick {t}: {f}");
            last = f;
        }
        int jumped = -1;
        for (int t = 0; t < 5 && jumped < 0; t++)
        {
            w.Events.Effects.Clear();
            StepTopped(sim);
            if (Vector2.Distance(w.Units.Pos[acu.Index], dest) < 1.5f) jumped = t;
        }
        Assert.True(jumped >= 0, "never arrived");
        Assert.Equal(1, Count(w, EffectKind.TeleportOut));
        Assert.Equal(1, Count(w, EffectKind.TeleportIn));
        var fxIn = w.Events.Effects.First(e => e.Kind == EffectKind.TeleportIn);
        Assert.Equal(w.BpOf(acu.Index).Radius, fxIn.Size);
        Assert.True(Vector2.Distance(new Vector2(fxIn.Pos.X, fxIn.Pos.Z), dest) < 1.5f);
        Assert.True(w.Units.Orders[acu.Index].IsEmpty);
        Assert.Equal(-1f, TeleportSystem.Progress(w, acu.Index));
        Assert.False(w.Units.Move[acu.Index].HasGoal);
        // It stays there (no leftover goal back to the start).
        TestUtil.Run(sim, 30);
        Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], dest) < 1.5f);
    }

    [Fact]
    public void EnergySpentIsBasePlusDistance()
    {
        Simulation Make()
        {
            var s = Ready();
            var w = s.World;
            var back = CommanderPos(w) - (Toward(w, 1) - CommanderPos(w)) * 14f;
            for (int k = 0; k < 4; k++) TestUtil.Spawn(s, "uef_t1_energy_storage", 0, FindSite(w, "uef_t1_energy_storage", back, 0, 20));
            s.Step();
            w.Armies[0].Eco.Energy = 20000;
            Assert.Equal(20000f, w.Armies[0].Eco.Energy);
            return s;
        }
        var a = Make();
        var b = Make();
        var dest = Toward(a.World, 80);
        float dist = Vector2.Distance(CommanderPos(a.World), dest);
        float cost = Tp(a.World, Acu(a)).Cost(dist);
        Assert.InRange(cost, 8000f, 10000f); // 5000 + 50 × ~80
        a.Step([Teleport(a, dest)]);
        b.Step();
        for (int t = 0; t < 120; t++) { a.Step(); b.Step(); }
        Assert.True(Vector2.Distance(CommanderPos(a.World), dest) < 1.5f, "never arrived");
        float spent = b.World.Armies[0].Eco.Energy - a.World.Armies[0].Eco.Energy;
        Assert.InRange(spent, cost - 30f, cost + 30f);
    }

    [Fact]
    public void EnergyStallSlowsTheCharge()
    {
        var sim = Ready();
        var w = sim.World;
        var acu = Acu(sim);
        var start = w.Units.Pos[acu.Index];
        var dest = Toward(w, 60);
        w.Armies[0].Eco.Energy = 0;
        sim.Step([Teleport(sim, dest)]);
        for (int t = 0; t < 200; t++) { w.Armies[0].Eco.Energy = 0; sim.Step(); }
        Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], start) < 0.5f, "jumped during an energy stall");
        float f = TeleportSystem.Progress(w, acu.Index);
        Assert.InRange(f, 0.001f, 0.25f); // the ACU's own trickle income only
        // Power back: it finishes the rest.
        for (int t = 0; t < 110 && Vector2.Distance(w.Units.Pos[acu.Index], dest) > 1.5f; t++) StepTopped(sim);
        Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], dest) < 1.5f, "never arrived after the stall");
    }

    [Fact]
    public void PausedChargeWaits()
    {
        var sim = Ready();
        var w = sim.World;
        var acu = Acu(sim);
        StepTopped(sim, Teleport(sim, Toward(w, 40)));
        for (int t = 0; t < 30; t++) StepTopped(sim);
        w.Units.Flags[acu.Index] |= UnitFlags.Paused;
        StepTopped(sim); // the request made before the pause is still paid
        float f = TeleportSystem.Progress(w, acu.Index);
        for (int t = 0; t < 150; t++) StepTopped(sim);
        Assert.Equal(f, TeleportSystem.Progress(w, acu.Index));
        Assert.Equal(OrderType.Teleport, w.Units.Orders[acu.Index].Current.Type);
        w.Units.Flags[acu.Index] &= ~UnitFlags.Paused;
        for (int t = 0; t < 100 && !w.Units.Orders[acu.Index].IsEmpty; t++) StepTopped(sim);
        Assert.True(w.Units.Orders[acu.Index].IsEmpty, "never jumped after unpausing");
    }

    [Fact]
    public void NewOrderCancelsAndTheEnergyIsLost()
    {
        var sim = Ready();
        var w = sim.World;
        var acu = Acu(sim);
        var start = w.Units.Pos[acu.Index];
        var dest = Toward(w, 60);
        StepTopped(sim, Teleport(sim, dest));
        for (int t = 0; t < 60; t++) StepTopped(sim);
        Assert.True(TeleportSystem.Progress(w, acu.Index) > 0.5f);
        var side = start + new Vector2(0, 6);
        StepTopped(sim, new IssueOrderCommand(0, [acu], Order.MoveTo(side), false));
        Assert.Equal(-1f, TeleportSystem.Progress(w, acu.Index));
        for (int t = 0; t < 150; t++) StepTopped(sim);
        Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], dest) > 20f, "jumped after the cancel");
        // A new jump starts from zero.
        StepTopped(sim, Teleport(sim, dest));
        Assert.InRange(TeleportSystem.Progress(w, acu.Index), 0f, 0.05f);
    }

    [Fact]
    public void QueuedTeleportRunsAfterTheMove()
    {
        var sim = Ready();
        var w = sim.World;
        var acu = Acu(sim);
        var mid = CommanderPos(w) + new Vector2(0, 8);
        var dest = Toward(w, 50);
        StepTopped(sim, new IssueOrderCommand(0, [acu], Order.MoveTo(mid), false), Teleport(sim, dest, queue: true));
        Assert.Equal(2, w.Units.Orders[acu.Index].Count);
        for (int t = 0; t < 300 && !w.Units.Orders[acu.Index].IsEmpty; t++) StepTopped(sim);
        Assert.True(w.Units.Orders[acu.Index].IsEmpty);
        Assert.True(Vector2.Distance(CommanderPos(w), dest) < 1.5f);
    }

    [Fact]
    public void UnitsWithoutATeleporterDropTheOrder()
    {
        var sim = NewGame("uef");
        sim.Step();
        var w = sim.World;
        var acu = Acu(sim);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, CommanderPos(w) + new Vector2(4, 0));
        var dest = Toward(w, 60);
        var startAcu = w.Units.Pos[acu.Index];
        StepTopped(sim, new IssueOrderCommand(0, [acu, tank], Order.TeleportTo(dest), false));
        Assert.True(w.Units.Orders[acu.Index].IsEmpty);
        Assert.True(w.Units.Orders[tank.Index].IsEmpty);
        for (int t = 0; t < 130; t++) StepTopped(sim);
        Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], startAcu) < 1f);
        Assert.True(Vector2.Distance(w.Units.Pos[tank.Index], dest) > 20f);
        Assert.Equal(-1f, TeleportSystem.Progress(w, acu.Index));

        // A queued Teleport whose unit lost the teleporter is dropped by OrderSystem, the queue goes on.
        InstallTeleporter(w, acu);
        var mid = CommanderPos(w) + new Vector2(0, 6);
        StepTopped(sim, new IssueOrderCommand(0, [acu], Order.MoveTo(mid), false), Teleport(sim, dest, queue: true));
        Assert.True(EnhancementSystem.Remove(w, acu.Index, EnhancementSlot.Back));
        Assert.Null(w.BpOf(acu.Index).Teleport);
        for (int t = 0; t < 200; t++) StepTopped(sim);
        Assert.True(Vector2.Distance(CommanderPos(w), dest) > 20f);
        Assert.True(w.Units.Orders[acu.Index].IsEmpty || w.Units.Orders[acu.Index].Current.Type != OrderType.Teleport);
    }

    [Fact]
    public void ArrivalIsClampedToTheMapAndAStandableCell()
    {
        var sim = Ready();
        var w = sim.World;
        var acu = Acu(sim);
        var bp = w.BpOf(acu.Index);
        // Off the map.
        StepTopped(sim, Teleport(sim, new Vector2(-40, -40)));
        for (int t = 0; t < 110 && !w.Units.Orders[acu.Index].IsEmpty; t++) StepTopped(sim);
        var p = w.Units.Pos[acu.Index];
        Assert.True(w.Terrain.InBounds(p) && p.X <= 3f && p.Y <= 3f, $"off-map jump landed at {p}");
        Assert.True(w.Paths.CanStand(bp.Movement.Layer, p));

        // Onto a structure: lands next to it, outside its footprint.
        var site = FindSite(w, "uef_t1_land_factory", Toward(w, 0) + new Vector2(30, 30), 0, 40);
        var fac = TestUtil.Spawn(sim, "uef_t1_land_factory", 1, site);
        TestUtil.Run(sim, 2);
        float half = ConstructionSystem.HalfSize(w.BpOf(fac.Index));
        StepTopped(sim, Teleport(sim, site));
        for (int t = 0; t < 140 && !w.Units.Orders[acu.Index].IsEmpty; t++) StepTopped(sim);
        p = w.Units.Pos[acu.Index];
        Assert.True(MathF.Max(MathF.Abs(p.X - site.X), MathF.Abs(p.Y - site.Y)) > half, $"landed inside the factory at {p}");
        Assert.True(Vector2.Distance(p, site) < half + 6f, $"landed too far from the target: {p}");
        Assert.True(w.Paths.CanStand(bp.Movement.Layer, p));
    }

    [Fact]
    public void DeepWaterTargetPutsTheAmphibiousAcuOnTheSeabed()
    {
        var sim = NewGame("cybran", preset: MapPreset.Lakes);
        sim.Step();
        var w = sim.World;
        var acu = Acu(sim);
        InstallTeleporter(w, acu);
        var bp = w.BpOf(acu.Index);
        Assert.Equal(MotionLayer.Amphibious, bp.Movement.Layer);
        Vector2? water = null;
        var c = CommanderPos(w);
        float best = float.MaxValue;
        for (int z = 8; z < w.Terrain.Height - 8; z += 2)
        for (int x = 8; x < w.Terrain.Width - 8; x += 2)
        {
            var q = new Vector2(x + 0.5f, z + 0.5f);
            if (w.Terrain.GetHeight(q) > w.Terrain.WaterLevel - 2f) continue;
            float d = Vector2.DistanceSquared(q, c);
            if (d < best) { best = d; water = q; }
        }
        Assert.True(water.HasValue, "no deep water on the lakes map");
        StepTopped(sim, Teleport(sim, water!.Value));
        for (int t = 0; t < 400 && !w.Units.Orders[acu.Index].IsEmpty; t++) StepTopped(sim);
        Assert.True(w.Units.Orders[acu.Index].IsEmpty, "never jumped");
        var p = w.Units.Pos[acu.Index];
        Assert.True(w.Paths.CanStand(bp.Movement.Layer, p));
        Assert.True(Vector2.Distance(p, water.Value) < 8f, $"landed at {p}, target {water.Value}");
        Assert.Equal(w.Terrain.GetHeight(p), w.Units.Elevation[acu.Index]);
    }

    [Fact]
    public void ArrivalHelperFindsTheNearestStandableCell()
    {
        var sim = Ready();
        var w = sim.World;
        var bp = w.BpOf(Acu(sim).Index);
        var layer = bp.Movement.Layer;
        foreach (var target in new[] { new Vector2(-10, 50), new Vector2(500, 500), Toward(w, 30) })
        {
            var p = TeleportSystem.Arrival(w, bp, target);
            Assert.True(w.Terrain.InBounds(p));
            Assert.True(w.Paths.CanStand(layer, p), $"{target} -> {p}");
        }
    }

    [Fact]
    public void SaveLoadMidChargeContinuesIdentically()
    {
        var sim = Ready("cybran");
        var w = sim.World;
        var acu = Acu(sim);
        var dest = Toward(w, 70);
        StepTopped(sim, Teleport(sim, dest));
        for (int t = 0; t < 40; t++) StepTopped(sim);
        float f = TeleportSystem.Progress(w, acu.Index);
        Assert.InRange(f, 0.3f, 0.5f);

        var saved = WorldState.Capture(sim);
        var b = WorldState.Restore(saved, w.Blueprints, w.Terrain);
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(b)), "reload is not byte-identical");
        Assert.Equal(f, TeleportSystem.Progress(b.World, acu.Index));
        Assert.Equal(sim.StateHash(), b.StateHash());
        for (int t = 0; t < 100; t += 10)
        {
            for (int k = 0; k < 10; k++) { StepTopped(sim); StepTopped(b); }
            Assert.Equal(sim.StateHash(), b.StateHash());
        }
        Assert.True(Vector2.Distance(b.World.Units.Pos[acu.Index], dest) < 1.5f);
        Assert.True(Vector2.Distance(w.Units.Pos[acu.Index], dest) < 1.5f);
    }

    [Fact]
    public void BothCommandersOfferTheTeleporterInTheBackSlot()
    {
        var db = TestUtil.Db;
        foreach (var faction in new[] { "uef", "cybran" })
        {
            var acu = db[db.Factions.CommanderOf(faction)];
            int e = EnhancementCatalog.Find(acu, "PersonalTeleporter");
            Assert.True(e >= 0, faction);
            var x = acu.Enhancements[e];
            Assert.Equal(EnhancementSlot.Back, x.Slot);
            Assert.Equal("", x.Prerequisite);
            Assert.Equal("TP", x.Icon);
            Assert.Null(acu.Teleport);
            int withTp = 0;
            foreach (int v in acu.EnhancementVariants)
            {
                bool has = db[v].Installed[(int)EnhancementSlot.Back] == e;
                Assert.Equal(has, db[v].Teleport != null);
                if (has) withTp++;
            }
            Assert.True(withTp > 0);
        }
    }
    [Fact]
    public void ArrivalEffectIsSeenByOwnTeamOutsideVision()
    {
        var sim = Ready();
        var w = sim.World;
        var far = new Vector3(250, 0, 6); // map corner: nobody's vision
        Assert.False(w.Intel.IsPositionVisible(0, new Vector2(far.X, far.Z)));
        Assert.False(w.Intel.IsPositionVisible(1, new Vector2(far.X, far.Z)));
        w.Events.Effects.Clear();
        w.Events.Effects.Add(new EffectEvent { Kind = EffectKind.TeleportIn, Pos = far, Size = 2, Army = 0 });
        w.Events.Effects.Add(new EffectEvent { Kind = EffectKind.TeleportOut, Pos = far, Size = 2, Army = 0 });
        var own = new Rts.Sim.Snapshot.SimSnapshot();
        Rts.Sim.Snapshot.SnapshotWriter.Write(w, own, 0);
        Assert.Equal([EffectKind.TeleportIn], own.Effects.Select(e => e.Kind));
        var enemy = new Rts.Sim.Snapshot.SimSnapshot();
        Rts.Sim.Snapshot.SnapshotWriter.Write(w, enemy, 1);
        Assert.Empty(enemy.Effects);
    }
}
