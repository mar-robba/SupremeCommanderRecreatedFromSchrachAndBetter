using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

public class SandboxTests
{
    private static Simulation NewSandbox(int seed = 42, int players = 2)
    {
        var sim = TestUtil.NewSkirmish(seed, 256, players);
        SandboxSystem.Enable(sim.World);
        return sim;
    }

    private static Vector2 Center(SimWorld w) => new(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);

    private static int Count(SimWorld w, int army, string bp) => UnitsOf(w, army, bp).Count;

    [Fact]
    public void CheatsAreIgnoredOutsideSandbox()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        sim.Step();
        var enemyCmd = w.Armies[1].Commander;
        float mass = w.Armies[0].Eco.Mass;
        TestUtil.Run(sim, 5,
            new SandboxSpawnCommand(0, 0, Bp("uef_t1_tank"), Center(w), 50),
            new SandboxToggleCommand(0, -1, SandboxToggle.InfiniteResources, true),
            new SandboxSetAiCommand(0, 1, true, AiDifficulty.Hard),
            new SandboxUnitActionCommand(0, [enemyCmd], SandboxUnitAction.Kill),
            new SandboxNukeCommand(0, w.Units.Pos[enemyCmd.Index]));
        Assert.False(SandboxSystem.IsEnabled(w));
        Assert.Equal(0, Count(w, 0, "uef_t1_tank"));
        Assert.True(w.Units.IsActive(enemyCmd));
        Assert.Equal(w.BpOf(enemyCmd.Index).MaxHealth, w.Units.Health[enemyCmd.Index]);
        Assert.False(w.Armies[1].IsAI);
        Assert.True(w.Armies[0].Eco.Mass <= mass + 1f);
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Null(snap.Sandbox);
    }

    [Fact]
    public void SpawnPlacesNValidUnitsInAGrid()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var c = Center(w);
        TestUtil.Run(sim, 2, new SandboxSpawnCommand(0, 1, Bp("uef_t1_tank"), c, 100));
        var tanks = UnitsOf(w, 1, "uef_t1_tank");
        Assert.Equal(100, tanks.Count);
        foreach (int i in tanks)
        {
            var p = w.Units.Pos[i];
            Assert.True(w.Terrain.CanTraverse(MotionLayer.Land, (int)MathF.Floor(p.X), (int)MathF.Floor(p.Y)), $"tank on bad cell {p}");
            Assert.True(w.Units.IsComplete(i));
            Assert.True(Vector2.Distance(p, c) < 20f, $"tank far from the point: {p}");
        }
        // No two units on the same spot.
        for (int a = 0; a < tanks.Count; a++)
        for (int b = a + 1; b < tanks.Count; b++)
            Assert.True(Vector2.Distance(w.Units.Pos[tanks[a]], w.Units.Pos[tanks[b]]) > 1f);
    }

    [Fact]
    public void LargeSpawnsAreSpreadOverTicks()
    {
        var sim = NewSandbox();
        var w = sim.World;
        sim.Step([new SandboxSpawnCommand(0, 0, Bp("uef_t1_scout"), Center(w), 1000)]);
        Assert.Equal(SandboxSystem.SpawnBudgetPerTick, Count(w, 0, "uef_t1_scout"));
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Equal(1000 - SandboxSystem.SpawnBudgetPerTick, snap.Sandbox!.PendingSpawns);
        Ticks(sim, 4);
        Assert.Equal(1000, Count(w, 0, "uef_t1_scout"));
        Assert.Equal(1001, w.Armies[0].UnitCount); // + commander
        sim.WriteSnapshot(snap);
        Assert.Equal(0, snap.Sandbox!.PendingSpawns);
    }

    [Fact]
    public void SpawnedStructuresUseFreeFootprintsAndDeposits()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var c = Center(w);
        TestUtil.Run(sim, 2,
            new SandboxSpawnCommand(0, 0, Bp("uef_t1_land_factory"), c, 6),
            new SandboxSpawnCommand(0, 0, Bp("uef_t1_mass_extractor"), CommanderPos(w), 3),
            new SandboxSpawnCommand(0, 1, Bp("uef_t1_power_generator"), c + new Vector2(0, 30), 4, Complete: false));
        var factories = UnitsOf(w, 0, "uef_t1_land_factory");
        Assert.Equal(6, factories.Count);
        var fbp = TestUtil.Db["uef_t1_land_factory"];
        // Footprints never overlap: every factory owns all its occupancy cells.
        foreach (int f in factories)
        {
            var (x0, z0, x1, z1) = OccupancyGrid.Footprint(fbp, w.Units.Pos[f]);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                Assert.Equal(f, w.Occupancy.At(x, z));
        }
        var mexes = UnitsOf(w, 0, "uef_t1_mass_extractor");
        Assert.Equal(3, mexes.Count);
        foreach (int m in mexes)
            Assert.Contains(w.Terrain.Deposits, d => d.Type == DepositType.Mass && Vector2.Distance(d.Pos, w.Units.Pos[m]) < 1f);
        var sites = UnitsOf(w, 1, "uef_t1_power_generator");
        Assert.Equal(4, sites.Count);
        Assert.All(sites, s => Assert.False(w.Units.IsComplete(s)));
    }

    [Fact]
    public void InfiniteResourcesKeepStorageFullAndNeverStall()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var eco = w.Armies[0].Eco;
        eco.Mass = 0;
        eco.Energy = 0;
        var cmd = w.Armies[0].Commander;
        var site = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 6);
        TestUtil.Run(sim, 30,
            new SandboxToggleCommand(0, 0, SandboxToggle.InfiniteResources, true),
            new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_land_factory"), site), false));
        Assert.True(eco.MassDemand > 0, "the commander should be building");
        Assert.Equal(1f, eco.MassEfficiency);
        Assert.Equal(1f, eco.EnergyEfficiency);
        Assert.Equal(eco.MassMax, eco.Mass);
        Assert.Equal(eco.EnergyMax, eco.Energy);
        // The enemy (toggle not set) is unaffected.
        Assert.True(w.Armies[1].Eco.Mass <= w.Armies[1].Eco.MassMax);
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.True(snap.Sandbox!.InfiniteResources[0]);
        Assert.False(snap.Sandbox.InfiniteResources[1]);
    }

    [Fact]
    public void InstantBuildFinishesSitesAndFactoryItems()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var cmd = w.Armies[0].Commander;
        var site = FindSite(w, "uef_t1_land_factory", CommanderPos(w), 6, 12);
        TestUtil.Run(sim, 40,
            new SandboxToggleCommand(0, 0, SandboxToggle.InstantBuild, true),
            new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_land_factory"), site), false));
        var fac = UnitsOf(w, 0, "uef_t1_land_factory");
        Assert.Single(fac);
        Assert.True(w.Units.IsComplete(fac[0]), "factory should be finished almost immediately");
        Assert.Equal(TestUtil.Db["uef_t1_land_factory"].MaxHealth, w.Units.Health[fac[0]]);

        TestUtil.Run(sim, 12, new FactoryEnqueueCommand(0, [w.Units.HandleOf(fac[0])], Bp("uef_t1_tank"), 5));
        Assert.Equal(5, Count(w, 0, "uef_t1_tank"));
        Assert.All(UnitsOf(w, 0, "uef_t1_tank"), t => Assert.True(w.Units.IsComplete(t)));
    }

    [Fact]
    public void BuildAnywhereWaivesTheDepositRule()
    {
        long Built(bool anywhere)
        {
            var sim = NewSandbox();
            var w = sim.World;
            var cmd = w.Armies[0].Commander;
            var site = FindSite(w, "uef_t1_power_generator", CommanderPos(w), 6, 14); // same 2x2 footprint, away from deposits
            var cmds = new List<Command> { new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_mass_extractor"), site), false) };
            if (anywhere) cmds.Insert(0, new SandboxToggleCommand(0, 0, SandboxToggle.BuildAnywhere, true));
            TestUtil.Run(sim, 60, cmds.ToArray());
            return Count(w, 0, "uef_t1_mass_extractor");
        }
        Assert.Equal(0, Built(false));
        Assert.Equal(1, Built(true));
    }

    [Fact]
    public void NukeVaporizesWrecksInsideTheInnerRing()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var c = Center(w);
        var tank = Bp("uef_t1_tank");
        for (int i = 0; i < 6; i++) w.SpawnUnit(tank, 1, c + new Vector2(i * 3 - 8, 4), 0);
        int farWreck = w.Props.Spawn(c + new Vector2(0, 70), 0, tank, 1f, 10f, 0f);
        sim.Step();
        sim.Step([new SandboxNukeCommand(0, c, Instant: true)]);
        int wrecks = 0;
        foreach (int i in w.Props.AliveList) if (Vector2.Distance(w.Props.Pos[i], c) <= SandboxSystem.NukeInnerRadius) wrecks++;
        Assert.True(wrecks > 0, "the blast should first leave wrecks");
        for (int t = 0; t < 3; t++) sim.Step();
        foreach (int i in w.Props.AliveList)
            Assert.True(Vector2.Distance(w.Props.Pos[i], c) > SandboxSystem.NukeInnerRadius, $"prop {i} left inside the crater");
        Assert.True(w.Props.Alive[farWreck], "props outside the inner ring stay");
    }

    [Fact]
    public void NukeKillsInsideTheInnerRingAndDamagesTheOuterRing()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var c = Center(w);
        var tank = Bp("uef_t1_tank");
        var dir = new Vector2(1, 0);
        var inner1 = w.SpawnUnit(tank, 1, c, 0);
        var inner2 = w.SpawnUnit(tank, 1, c + dir * 25, 0);
        var storageInner = w.SpawnUnit(Bp("uef_t1_mass_storage"), 1, c - dir * 20, 0);
        var outer = w.SpawnUnit(Bp("uef_t1_point_defense"), 1, c + new Vector2(0, 36), 0);
        var far = w.SpawnUnit(tank, 1, c - new Vector2(0, 60), 0);
        sim.Step();
        sim.Step([new SandboxNukeCommand(0, c, Instant: true)]);
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        var u = w.Units;
        Assert.False(u.IsActive(inner1));
        Assert.False(u.IsActive(inner2));
        Assert.False(u.IsActive(storageInner));
        Assert.True(u.IsActive(outer), "outer ring unit should survive");
        float hp = u.Health[outer.Index], pdMax = w.Blueprints[Bp("uef_t1_point_defense")].MaxHealth;
        Assert.Equal(pdMax - SandboxSystem.NukeOuterDamage, hp, 1f); // outer ring only
        Assert.Equal(w.Blueprints[tank].MaxHealth, u.Health[far.Index]);
        Assert.Contains(snap.Effects, e => e.Kind == EffectKind.Nuke && e.Size == SandboxSystem.NukeOuterRadius);
        Assert.Contains(snap.Effects, e => e.Kind == EffectKind.UnitDeath);
    }

    [Fact]
    public void NukeIsVisibleThroughTheFog()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var enemyBase = w.Units.Pos[w.Armies[1].Commander.Index];
        sim.Step();
        sim.Step([new SandboxNukeCommand(0, enemyBase, Instant: true)]);
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap, 0);
        Assert.True(snap.ViewArmy == 0);
        Assert.Single(snap.Effects, e => e.Kind == EffectKind.Nuke);
    }

    [Fact]
    public void UnitActionsKillDeleteHealDamage()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var c = Center(w);
        var tank = Bp("uef_t1_tank");
        var a = w.SpawnUnit(tank, 1, c, 0);
        var b = w.SpawnUnit(tank, 1, c + new Vector2(4, 0), 0);
        var d = w.SpawnUnit(tank, 1, c + new Vector2(8, 0), 0);
        sim.Step();
        int props = w.Props.Count;
        sim.Step([
            new SandboxUnitActionCommand(0, [a], SandboxUnitAction.Kill),
            new SandboxUnitActionCommand(0, [b], SandboxUnitAction.Delete),
            new SandboxUnitActionCommand(0, [d], SandboxUnitAction.Damage, 0.5f),
        ]);
        Assert.False(w.Units.IsActive(a));
        Assert.False(w.Units.IsActive(b));
        Assert.Equal(props + 1, w.Props.Count); // kill leaves a wreck, delete doesn't
        Assert.Equal(w.Blueprints[tank].MaxHealth * 0.5f, w.Units.Health[d.Index], 1);
        sim.Step([new SandboxUnitActionCommand(0, [d], SandboxUnitAction.Heal)]);
        Assert.Equal(w.Blueprints[tank].MaxHealth, w.Units.Health[d.Index]);
    }

    [Fact]
    public void ChangeOwnerTransfersUnitsButNotCommanders()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var t = w.SpawnUnit(Bp("uef_t1_tank"), 1, Center(w), 0);
        var enemyCmd = w.Armies[1].Commander;
        sim.Step();
        int mine = w.Armies[0].UnitCount, theirs = w.Armies[1].UnitCount;
        sim.Step([new SandboxUnitActionCommand(0, [t, enemyCmd], SandboxUnitAction.ChangeOwner, TargetArmy: 0)]);
        Assert.Equal(0, w.Units.Army[t.Index]);
        Assert.Equal(1, w.Units.Army[enemyCmd.Index]);
        Assert.Equal(mine + 1, w.Armies[0].UnitCount);
        Assert.Equal(theirs - 1, w.Armies[1].UnitCount);
        // The new owner can command it.
        var dest = Center(w) + new Vector2(10, 0);
        TestUtil.Run(sim, 80, new IssueOrderCommand(0, [t], Order.MoveTo(dest), false));
        Assert.True(Vector2.Distance(w.Units.Pos[t.Index], dest) < 2f);
    }

    [Fact]
    public void ClearAreaRemovesUnitsAndWrecksButSparesCommanders()
    {
        var sim = NewSandbox();
        var w = sim.World;
        var cpos = CommanderPos(w, 1);
        TestUtil.Run(sim, 2, new SandboxSpawnCommand(0, 1, Bp("uef_t1_tank"), cpos + new Vector2(6, 0), 30));
        Assert.Equal(30, Count(w, 1, "uef_t1_tank"));
        sim.Step([new SandboxClearAreaCommand(0, cpos, 40)]);
        Assert.Equal(0, Count(w, 1, "uef_t1_tank"));
        Assert.True(w.Units.IsActive(w.Armies[1].Commander));
        Assert.Equal(0, w.Props.Count);
    }

    [Fact]
    public void AiCanBeSwitchedOnAndOffAtRuntime()
    {
        var sim = NewSandbox();
        var w = sim.World;
        Assert.False(w.Armies[1].IsAI);
        TestUtil.Run(sim, 600, new SandboxSetAiCommand(0, 1, true, AiDifficulty.Hard));
        Assert.True(w.Armies[1].IsAI);
        Assert.Equal(AiDifficulty.Hard, w.Armies[1].AiDifficulty);
        Assert.True(w.Armies[1].UnitCount > 1, "the AI should have built something");
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.True(snap.Sandbox!.AiOn[1]);
        Assert.Equal(AiDifficulty.Hard, snap.Sandbox.AiDifficulty[1]);

        sim.Step([new SandboxSetAiCommand(0, 1, false, AiDifficulty.Easy)]);
        Assert.False(w.Armies[1].IsAI);
        Assert.Equal(AiConfig(w, 1), AiDifficulty.Easy);
    }

    private static AiDifficulty? AiConfig(SimWorld w, int army) => AI.AiSystem.StateOf(w)?.BrainOrNull(army)?.Cfg.Difficulty;

    [Fact]
    public void NoVictoryInSandboxUnlessEnabled_AndDefeatedPlayerKeepsCheating()
    {
        var sim = NewSandbox();
        var w = sim.World;
        sim.Step();
        // Nuke our own commander: we are defeated, but sandbox commands still work.
        sim.Step([new SandboxNukeCommand(0, CommanderPos(w, 0), Instant: true)]);
        Assert.True(w.Armies[0].Defeated);
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.Equal(-1, snap.WinningTeam);
        Assert.True(w.WinningTeam >= 0);

        TestUtil.Run(sim, 2, new SandboxSpawnCommand(0, 0, Bp("uef_t1_tank"), Center(w), 5));
        Assert.Equal(5, Count(w, 0, "uef_t1_tank"));
        Assert.False(w.Armies[0].Defeated); // revived
        Assert.Equal(-1, w.WinningTeam);

        sim.Step([new SandboxSetVictoryCommand(0, true), new SandboxNukeCommand(0, Center(w))]);
        sim.Step();
        sim.WriteSnapshot(snap);
        Assert.True(snap.Sandbox!.Victory);
    }

    [Fact]
    public void SandboxIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = NewSandbox(seed: 7);
                var w = sim.World;
                var c = Center(w);
                int tank = Bp("uef_t1_tank"), arty = Bp("uef_t1_artillery");
                sim.Step([
                    new SandboxToggleCommand(0, -1, SandboxToggle.InfiniteResources, true),
                    new SandboxToggleCommand(0, 0, SandboxToggle.InstantBuild, true),
                    new SandboxSpawnCommand(0, 0, tank, c - new Vector2(30, 30), 400),
                    new SandboxSpawnCommand(0, 1, arty, c + new Vector2(30, 30), 300),
                    new SandboxSpawnCommand(0, 1, Bp("uef_t1_land_factory"), c + new Vector2(50, -20), 3),
                    new SandboxSetAiCommand(0, 1, true, AiDifficulty.Normal),
                ]);
                for (int t = 0; t < 5; t++) sim.Step();
                var mine = UnitsOf(w, 0, "uef_t1_tank").Select(i => w.Units.HandleOf(i)).ToArray();
                sim.Step([new IssueOrderCommand(0, mine, Order.AttackMoveTo(c + new Vector2(30, 30)), false)]);
                for (int t = 0; t < 150; t++) sim.Step();
                sim.Step([new SandboxNukeCommand(0, c), new SandboxClearAreaCommand(0, c + new Vector2(50, -20), 10)]);
                for (int t = 0; t < 100; t++) sim.Step();
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}
