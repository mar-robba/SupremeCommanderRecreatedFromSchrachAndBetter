using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>(6b) Game options: victory conditions, unit cap, start resources, fog, cheats, prebuilt base, spec, header.</summary>
public class GameOptionsTests
{
    public static Simulation Make(GameOptions o, int players = 2, int[]? teams = null, string[]? factions = null, int seed = 42) =>
        GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = seed, MapSize = 256, Players = players, AiPlayers = new bool[players], Teams = teams, Factions = factions, Options = o,
        });

    public static int Role(SimWorld w, int army, string role) => w.Blueprints.Factions.RoleBlueprint(w.Armies[army].Faction, role);

    private static UnitHandle SpawnRole(SimWorld w, int army, string role, Vector2 near, float minDist = 12)
    {
        int bp = Role(w, army, role);
        var p = w.Blueprints[bp].IsStructure ? EconomyTestUtil.FindSite(w, w.Blueprints[bp].Id, near, minDist, 40) : near + new Vector2(minDist, 0);
        return w.SpawnUnit(bp, army, p, 0, complete: true);
    }

    private static int AliveOf(SimWorld w, int army)
    {
        int n = 0;
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.IsActive(i) && w.Units.Army[i] == army) n++;
        return n;
    }

    // ------------------------------------------------------------------ victory

    [Fact]
    public void AssassinationCommanderDeathDefeatsTheArmy()
    {
        var sim = Make(new GameOptions());
        var w = sim.World;
        var pgen = SpawnRole(w, 1, FactionRoles.PgenT1, EconomyTestUtil.CommanderPos(w, 1));
        sim.Step();
        w.KillUnit(w.Armies[1].Commander.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.True(w.Armies[1].Defeated);
        Assert.False(w.Units.IsActive(pgen));
        Assert.Equal(0, w.WinningTeam);
    }

    [Fact]
    public void SupremacyIgnoresCommanderDeathAndDefeatsWithoutStructuresOrEngineers()
    {
        var sim = Make(new GameOptions { Victory = VictoryCondition.Supremacy });
        var w = sim.World;
        var cp = EconomyTestUtil.CommanderPos(w, 1);
        var pgen = SpawnRole(w, 1, FactionRoles.PgenT1, cp);
        var eng = SpawnRole(w, 1, FactionRoles.EngineerT1, cp, 8);
        var tank = SpawnRole(w, 1, FactionRoles.TankT1, cp, -8);
        sim.Step();
        w.KillUnit(w.Armies[1].Commander.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.False(w.Armies[1].Defeated); // commander death alone does not defeat
        w.KillUnit(pgen.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.False(w.Armies[1].Defeated); // the engineer still counts
        Assert.True(w.Units.IsActive(tank));
        w.KillUnit(eng.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.True(w.Armies[1].Defeated); // only a tank left
        Assert.False(w.Units.IsActive(tank));
        Assert.Equal(0, w.WinningTeam);
        Assert.False(w.Armies[0].Defeated);
    }

    [Fact]
    public void AnnihilationNeedsTheLastFinishedUnitDead()
    {
        var sim = Make(new GameOptions { Victory = VictoryCondition.Annihilation });
        var w = sim.World;
        var cp = EconomyTestUtil.CommanderPos(w, 1);
        var tank = SpawnRole(w, 1, FactionRoles.TankT1, cp, 8);
        // An unfinished structure does not keep the army alive.
        int pgenBp = Role(w, 1, FactionRoles.PgenT1);
        var shell = w.SpawnUnit(pgenBp, 1, EconomyTestUtil.FindSite(w, w.Blueprints[pgenBp].Id, cp, 12, 40), 0, complete: false);
        sim.Step();
        w.KillUnit(w.Armies[1].Commander.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.False(w.Armies[1].Defeated);
        Assert.True(w.Units.IsActive(tank));
        w.KillUnit(tank.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.True(w.Armies[1].Defeated);
        Assert.False(w.Units.IsActive(shell));
        Assert.Equal(0, w.WinningTeam);
    }

    [Fact]
    public void SandboxVictoryNeverDefeatsAnybody()
    {
        var sim = Make(new GameOptions { Victory = VictoryCondition.Sandbox });
        var w = sim.World;
        sim.Step();
        w.KillUnit(w.Armies[1].Commander.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 5);
        Assert.Equal(0, AliveOf(w, 1));
        Assert.False(w.Armies[1].Defeated);
        Assert.Equal(-1, w.WinningTeam);
    }

    [Fact]
    public void MultiplayerDropDefeatWorksInSupremacy()
    {
        var sim = Make(new GameOptions { Victory = VictoryCondition.Supremacy });
        var w = sim.World;
        var pgen = SpawnRole(w, 1, FactionRoles.PgenT1, EconomyTestUtil.CommanderPos(w, 1));
        sim.Step();
        sim.Step([new PlayerLeftCommand(1, DropAction.Defeat)]);
        EconomyTestUtil.Ticks(sim, 2);
        Assert.True(w.Armies[1].Defeated);
        Assert.False(w.Units.IsActive(pgen));
        Assert.Equal(0, AliveOf(w, 1));
        Assert.Equal(0, w.WinningTeam);
    }

    // ------------------------------------------------------------------ unit cap, resources, fog, cheats

    [Fact]
    public void FactoryWaitsAtTheOptionsUnitCapAndResumesAfterADeath()
    {
        var sim = Make(new GameOptions { UnitCap = 5 });
        var w = sim.World;
        Assert.All(w.Armies, a => Assert.Equal(5, a.UnitCap));
        var fac = SpawnRole(w, 0, FactionRoles.LandFactoryT1, EconomyTestUtil.CommanderPos(w, 0));
        int tank = Role(w, 0, FactionRoles.TankT1);
        sim.Step([new FactoryEnqueueCommand(0, [fac], tank, 10)]);
        EconomyTestUtil.Ticks(sim, 2500);
        var tanks = EconomyTestUtil.UnitsOf(w, 0, w.Blueprints[tank].Id);
        Assert.Equal(5, w.Armies[0].UnitCount); // commander + factory + 3 tanks
        Assert.Equal(3, tanks.Count);
        Assert.All(tanks, i => Assert.True(w.Units.IsComplete(i)));
        int built = w.Armies[0].Stats.UnitsBuilt;
        w.KillUnit(tanks[0], UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 1500);
        Assert.Equal(5, w.Armies[0].UnitCount);
        Assert.Equal(built + 1, w.Armies[0].Stats.UnitsBuilt);
    }

    [Theory]
    [InlineData(StartResources.Low, 0.5f)]
    [InlineData(StartResources.Normal, 1f)]
    [InlineData(StartResources.High, 1f)]
    public void StartResourcesFillTheStorage(StartResources r, float share)
    {
        var w = Make(new GameOptions { Resources = r }).World;
        var cmd = w.Blueprints[w.Blueprints.Factions.CommanderOf(w.Armies[0].Faction)].Economy;
        var a = w.Armies[0];
        Assert.Equal(cmd.StorageMass * share, a.Eco.Mass, 3);
        Assert.Equal(cmd.StorageEnergy * share, a.Eco.Energy, 3);
        float bank = r == StartResources.High ? 2 : 0;
        Assert.Equal(cmd.StorageMass * bank, a.StartBankMass, 3);
        Assert.Equal(cmd.StorageEnergy * bank, a.StartBankEnergy, 3);
    }

    [Fact]
    public void HighStartBankRefillsTheStorageAsItIsSpent()
    {
        var sim = Make(new GameOptions { Resources = StartResources.High });
        var w = sim.World;
        var a = w.Armies[0];
        sim.Step();
        float max = a.Eco.MassMax;
        float bank0 = a.StartBankMass;
        Assert.Equal(2 * max, bank0, 2);
        for (int k = 0; k < 2; k++)
        {
            a.Eco.Mass = 0;
            a.Eco.Energy = 0;
            sim.Step();
            Assert.Equal(max, a.Eco.Mass, 2);
            Assert.Equal(a.Eco.EnergyMax, a.Eco.Energy, 2);
        }
        Assert.True(a.StartBankMass < 1f, $"bank {a.StartBankMass} should be used up (2x storage, refilled twice)");
        a.Eco.Mass = 0;
        sim.Step();
        Assert.True(a.Eco.Mass < max * 0.5f, "an empty bank refills nothing");
        // Normal: no bank at all.
        var n = Make(new GameOptions()).World;
        Assert.Equal(0, n.Armies[0].StartBankMass);
        // An explicit StartMass wins over the option (and has no bank).
        var s = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        { AiPlayers = [false, false], StartMass = 100, Options = new GameOptions { Resources = StartResources.High } }).World;
        Assert.Equal(100, s.Armies[0].Eco.Mass);
        Assert.Equal(0, s.Armies[0].StartBankMass);
        Assert.True(s.Armies[0].StartBankEnergy > 0);
    }

    [Fact]
    public void FogNoneDisablesIntelAndExploredRevealsTheMap()
    {
        var none = Make(new GameOptions { Fog = FogMode.None });
        none.Step();
        Assert.False(none.World.Intel.Enabled);

        var normal = Make(new GameOptions());
        EconomyTestUtil.Ticks(normal, 3);
        Assert.True(normal.World.Intel.Enabled);
        Assert.Contains(IntelService.FogUnexplored, normal.World.Intel.FogOf(0).ToArray());

        var explored = Make(new GameOptions { Fog = FogMode.Explored });
        var w = explored.World;
        EconomyTestUtil.Ticks(explored, 3);
        Assert.True(w.Intel.Enabled);
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var fog = w.Intel.FogOf(a).ToArray();
            Assert.DoesNotContain(IntelService.FogUnexplored, fog);
            Assert.Contains(IntelService.FogVisible, fog); // vision still works
        }
        // Enemy units outside vision stay hidden.
        Assert.False(w.Intel.IsVisible(0, w.Armies[1].Commander.Index));
    }

    [Fact]
    public void CheatsOffIgnoresDebugSpawnsAndCheatTools()
    {
        var sim = Make(new GameOptions { Cheats = false, UnitCap = 500 });
        var w = sim.World;
        int tank = Role(w, 0, FactionRoles.TankT1);
        int before = w.Armies[0].UnitCount;
        sim.Step([new DebugSpawnCommand(0, tank, EconomyTestUtil.CommanderPos(w, 0) + new Vector2(10, 0), 3), new EnableCheatToolsCommand(0)]);
        Assert.False(w.CheatsEnabled);
        Assert.Equal(before, w.Armies[0].UnitCount);
        Assert.False(SandboxSystem.IsEnabled(w));
    }

    [Fact]
    public void CheatsOnHonorsDebugSpawnsAndEnablesCheatToolsKeepingCapAndVictory()
    {
        var sim = Make(new GameOptions { Cheats = true, UnitCap = 500 });
        var w = sim.World;
        int tank = Role(w, 0, FactionRoles.TankT1);
        int before = w.Armies[0].UnitCount;
        sim.Step([new DebugSpawnCommand(0, tank, EconomyTestUtil.CommanderPos(w, 0) + new Vector2(10, 0), 3)]);
        Assert.Equal(before + 3, w.Armies[0].UnitCount);
        Assert.False(SandboxSystem.IsEnabled(w));
        sim.Step([new EnableCheatToolsCommand(1)]);
        Assert.True(SandboxSystem.IsEnabled(w));
        Assert.True(SandboxSystem.StateOf(w)!.Victory);
        Assert.True(SandboxSystem.StateOf(w)!.Status.Victory);
        Assert.All(w.Armies, a => Assert.Equal(500, a.UnitCap));
        // The victory rule still works with the tools on.
        w.KillUnit(w.Armies[1].Commander.Index, UnitHandle.None);
        EconomyTestUtil.Ticks(sim, 3);
        Assert.True(w.Armies[1].Defeated);
        Assert.Equal(0, w.WinningTeam);
    }

    // ------------------------------------------------------------------ prebuilt base

    [Theory]
    [InlineData("uef", "cybran", 42)]
    [InlineData("cybran", "uef", 7)]
    public void PrebuiltBaseIsPlacedWithoutOverlapsAndItsFactoryWorks(string f0, string f1, int seed)
    {
        var sim = Make(new GameOptions { Prebuilt = true }, factions: [f0, f1], seed: seed);
        var w = sim.World;
        var u = w.Units;
        for (int a = 0; a < 2; a++)
        {
            int mex = UnitsOfBp(w, a, Role(w, a, FactionRoles.MexT1)).Count;
            Assert.InRange(mex, 1, PrebuiltBase.Mexes);
            Assert.Equal(PrebuiltBase.Pgens, UnitsOfBp(w, a, Role(w, a, FactionRoles.PgenT1)).Count);
            Assert.Single(UnitsOfBp(w, a, Role(w, a, FactionRoles.LandFactoryT1)));
            Assert.Equal(2 + mex + PrebuiltBase.Pgens, w.Armies[a].UnitCount);
        }
        // No overlaps: every structure still owns all of its footprint cells; commanders stand clear.
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.IsActive(i) || !w.BpOf(i).IsStructure) continue;
            var bp = w.BpOf(i);
            Assert.True(u.IsComplete(i));
            var (x0, z0, x1, z1) = OccupancyGrid.Footprint(bp, u.Pos[i]);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                Assert.Equal(i, w.Occupancy.At(x, z));
            foreach (var ar in w.Armies)
            {
                var c = u.Pos[ar.Commander.Index];
                Assert.False(c.X >= x0 - 1 && c.X <= x1 + 2 && c.Y >= z0 - 1 && c.Y <= z1 + 2, $"{bp.Id} on top of a commander");
            }
        }
        // The factory builds a tank.
        for (int a = 0; a < 2; a++)
        {
            var fac = UnitsOfBp(w, a, Role(w, a, FactionRoles.LandFactoryT1))[0];
            sim.Step([new FactoryEnqueueCommand(a, [u.HandleOf(fac)], Role(w, a, FactionRoles.TankT1), 1)]);
        }
        EconomyTestUtil.Ticks(sim, 900);
        for (int a = 0; a < 2; a++)
        {
            var tanks = UnitsOfBp(w, a, Role(w, a, FactionRoles.TankT1));
            Assert.Single(tanks);
            Assert.True(u.IsComplete(tanks[0]));
        }
    }

    [Fact]
    public void PrebuiltIsDeterministic()
    {
        ulong H() => Make(new GameOptions { Prebuilt = true }, players: 4, factions: ["uef", "cybran", "cybran", "uef"]).StateHash();
        Assert.Equal(H(), H());
    }

    private static List<int> UnitsOfBp(SimWorld w, int army, int bp)
    {
        var list = new List<int>();
        for (int i = 0; i < w.Units.Capacity; i++)
            if (w.Units.IsActive(i) && w.Units.Army[i] == army && w.Units.Bp[i] == bp) list.Add(i);
        return list;
    }

    // ------------------------------------------------------------------ spec + header

    [Fact]
    public void SpecRoundTripsAndParsesCaseInsensitively()
    {
        Assert.True(GameOptions.Parse("").SameAs(new GameOptions()));
        Assert.True(GameOptions.Parse(null).SameAs(new GameOptions()));
        Assert.True(GameOptions.Parse(new GameOptions().ToSpec()).SameAs(new GameOptions()));
        foreach (var v in Enum.GetValues<VictoryCondition>())
        foreach (var r in Enum.GetValues<StartResources>())
        foreach (var f in Enum.GetValues<FogMode>())
        foreach (var s in Enum.GetValues<GameSpeedMode>())
        {
            var o = new GameOptions { Victory = v, Resources = r, Fog = f, Speed = s, UnitCap = 250 + (int)v, Cheats = (int)f % 2 == 0, Prebuilt = (int)r % 2 == 1 };
            Assert.True(GameOptions.Parse(o.ToSpec()).SameAs(o), o.ToSpec());
            Assert.True(GameOptions.Parse(o.ToSpec().ToUpperInvariant()).SameAs(o));
        }
        var p = GameOptions.Parse(" Victory=SUPREMACY , cap=500,resources=high,fog=explored,speed=fast,cheats=off,prebuilt=1");
        Assert.Equal(VictoryCondition.Supremacy, p.Victory);
        Assert.Equal(500, p.UnitCap);
        Assert.Equal(StartResources.High, p.Resources);
        Assert.Equal(FogMode.Explored, p.Fog);
        Assert.Equal(GameSpeedMode.Fast, p.Speed);
        Assert.False(p.Cheats);
        Assert.True(p.Prebuilt);
        Assert.Equal(2f, p.LockedSpeed);
        Assert.Equal(16384, GameOptions.Parse("cap=16384").UnitCap);
        Assert.True(GameOptions.Parse("cheats=true").Cheats);
        Assert.False(GameOptions.Parse("cheats=0").Cheats);
    }

    [Theory]
    [InlineData("foo=1", "keys")]
    [InlineData("victory", "key=value")]
    [InlineData("victory=bogus", "supremacy")]
    [InlineData("cap=0", "1..")]
    [InlineData("cap=16385", "1..")]
    [InlineData("cap=lots", "1..")]
    [InlineData("fog=dark", "explored")]
    [InlineData("cheats=maybe", "on")]
    public void SpecErrorsNameTheChoices(string spec, string mentions)
    {
        var e = Assert.Throws<FormatException>(() => GameOptions.Parse(spec));
        Assert.Contains(mentions, e.Message);
    }

    [Fact]
    public void GameInfoHeaderRoundTripsTheOptions()
    {
        var s = new SkirmishSettings
        {
            AiPlayers = [false, true], Options = new GameOptions
            {
                Victory = VictoryCondition.Annihilation, UnitCap = 750, Resources = StartResources.Low, Fog = FogMode.None,
                Speed = GameSpeedMode.Normal, Cheats = false, Prebuilt = true,
            },
        };
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, s);
        Assert.True(sim.World.Options.SameAs(s.Options));
        Assert.NotSame(s.Options, sim.World.Options);
        var info = GameInfo.ForSkirmish(s, sim, 0, false);
        string json = info.ToJson();
        Assert.DoesNotContain("UnitCaps", json);
        Assert.DoesNotContain("LockedSpeed", json);
        Assert.Contains("Annihilation", json);
        var back = GameInfo.FromJson(json);
        Assert.NotNull(back.Options);
        Assert.True(back.Options!.SameAs(s.Options));
        // Headers from before 6b have no options (= defaults).
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(node.Remove("Options"));
        var old = GameInfo.FromJson(node.ToJsonString());
        Assert.Null(old.Options);
        // The snapshot hands the client the world's options and caps.
        var snap = new Snapshot.SimSnapshot();
        Snapshot.SnapshotWriter.Write(sim.World, snap);
        Assert.Same(sim.World.Options, snap.Options);
        Assert.Equal(750, snap.Armies[0].UnitCap);
    }

    [Fact]
    public void SaveLoadContinueKeepsOptionsAndBank()
    {
        var o = new GameOptions { Victory = VictoryCondition.Supremacy, Resources = StartResources.High, Prebuilt = true, UnitCap = 300 };
        var a = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = 5, Players = 4, Teams = [0, 0, 1, 1], AiPlayers = [true, true, true, true], Options = o,
        });
        for (int t = 0; t < 300; t++) a.Step();
        var saved = WorldState.Capture(a);
        var b = WorldState.Restore(saved, a.World.Blueprints, a.World.Terrain);
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(b)));
        Assert.True(b.World.Options.SameAs(o));
        for (int k = 0; k < 4; k++)
        {
            Assert.Equal(a.World.Armies[k].StartBankMass, b.World.Armies[k].StartBankMass);
            Assert.Equal(a.World.Armies[k].StartBankEnergy, b.World.Armies[k].StartBankEnergy);
            Assert.Equal(300, b.World.Armies[k].UnitCap);
        }
        for (int t = 0; t < 400; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(WorldState.Capture(a).AsSpan().SequenceEqual(WorldState.Capture(b)));
    }
}
