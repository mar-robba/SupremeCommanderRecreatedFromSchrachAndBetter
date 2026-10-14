using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Persistence;
using Rts.Sim.Strategic;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Faction infrastructure: data/factions loading and validation, roles, commanders, AI roles, saves.</summary>
public class FactionTests
{
    private static BlueprintDb Db => TestUtil.Db;

    /// <summary>A test-only faction that reuses UEF blueprints under other roles (commander = the SCU).</summary>
    private static FactionBlueprint TestFaction() => new()
    {
        Id = "testfac", Name = "Test Faction", Commander = "uef_scu", Color = "#C83030",
        Roles = new()
        {
            [FactionRoles.EngineerT1] = "uef_t2_engineer",
            [FactionRoles.TankT1] = "uef_t1_artillery",
            [FactionRoles.LandFactoryT1] = "uef_t1_land_factory",
            [FactionRoles.MexT1] = "uef_t1_mass_extractor",
            [FactionRoles.PgenT1] = "uef_t1_power_generator",
        },
    };

    /// <summary>A private blueprint db (the shared TestUtil.Db is never modified) with UEF + the test faction.</summary>
    private static BlueprintDb TwoFactionDb()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var uef = FactionDb.LoadDirectory(DataPaths.FactionDir, db).Get("uef")!;
        db.Factions = FactionDb.Create(db, [uef, TestFaction()]);
        return db;
    }

    [Fact]
    public void UefFactionLoadsWithRolesAndCommander()
    {
        var f = Db.Factions;
        Assert.Contains(f.All, x => x.Id == "uef");
        var uef = f.Get("UEF"); // ids are case-insensitive
        Assert.NotNull(uef);
        Assert.Equal("UEF", uef!.Name);
        Assert.NotEqual(0u, uef.ColorRgba);
        Assert.Equal(Db.IndexOf("uef_commander"), f.CommanderOf("uef"));
        Assert.Equal(Db.IndexOf("uef_t1_engineer"), f.RoleBlueprint("uef", FactionRoles.EngineerT1));
        Assert.Equal(Db.IndexOf("uef_t3_strategic_missile_launcher"), f.RoleBlueprint("uef", FactionRoles.NukeSilo));
        Assert.Equal(-1, f.RoleBlueprint("uef", FactionRoles.SniperT3)); // a known role UEF doesn't have
        Assert.Equal(-1, f.RoleBlueprint("uef", "no_such_role"));
        Assert.Equal(-1, f.RoleBlueprint("nope", FactionRoles.EngineerT1));
        Assert.Equal(-1, f.CommanderOf("nope"));
        Assert.Null(f.Get("nope"));
        for (int i = 0; i < f.Count; i++) Assert.Equal(i, f.All[i].Index);
    }

    [Fact]
    public void UefJsonMapsEveryUefBlueprint()
    {
        var uef = Db.Factions.Get("uef")!;
        var mapped = new HashSet<int>(uef.RoleIndices.Values) { uef.CommanderIndex };
        var missing = Db.All.Where(b => !b.IsVariant && b.Faction == "uef" && !mapped.Contains(b.Index)).Select(b => b.Id).ToList();
        Assert.True(missing.Count == 0, "UEF blueprints without a role in data/factions/uef.json: " + string.Join(", ", missing));
        foreach (var (role, id) in uef.Roles)
        {
            Assert.True(FactionRoles.IsKnown(role), role);
            Assert.Equal("uef", Db[id].Faction);
        }
        Assert.Equal(FactionRoles.All.Length, FactionRoles.All.Distinct().Count());
    }

    [Fact]
    public void BlueprintFactionDefaultsToTheIdPrefix()
    {
        Assert.All(Db.All, b => Assert.Equal("uef", b.Faction));
        Assert.Equal("cybran", BlueprintDb.FactionOfId("cybran_t1_tank"));
        Assert.Equal("uef", BlueprintDb.FactionOfId("UEF_x"));
        Assert.Equal("", BlueprintDb.FactionOfId("tank"));
    }

    [Fact]
    public void ValidationRejectsBadFactions()
    {
        FactionBlueprint F(Action<FactionBlueprint> edit) { var f = TestFaction(); edit(f); return f; }
        Assert.Throws<InvalidDataException>(() => FactionDb.Create(Db, [F(f => f.Roles[FactionRoles.TankT1] = "cybran_t1_tank")]));
        Assert.Throws<InvalidDataException>(() => FactionDb.Create(Db, [F(f => f.Roles["tank_t9"] = "uef_t1_tank")]));
        Assert.Throws<InvalidDataException>(() => FactionDb.Create(Db, [F(f => f.Commander = "nope")]));
        Assert.Throws<InvalidDataException>(() => FactionDb.Create(Db, [F(f => f.Color = "blue")]));
        Assert.Throws<InvalidDataException>(() => FactionDb.Create(Db, [F(f => f.Id = "random")]));
        Assert.Throws<InvalidDataException>(() => FactionDb.Create(Db, [TestFaction(), TestFaction()]));
        var ok = FactionDb.Create(Db, [F(f => f.Color = "")]);
        Assert.Equal(0u, ok.Get("testfac")!.ColorRgba);
    }

    [Fact]
    public void LoadDirectoryReadsJsonWithIdFromFileName()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rts_faction_test_" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "zeta.json"),
                "{ // comment\n \"Name\": \"Zeta\", \"Commander\": \"uef_commander\", \"Roles\": { \"tank_t1\": \"uef_t1_tank\" }, }");
            File.WriteAllText(Path.Combine(dir, "alpha.json"), "{ \"Commander\": \"uef_scu\" }");
            var f = FactionDb.LoadDirectory(dir, Db);
            Assert.Equal(["alpha", "zeta"], f.All.Select(x => x.Id));
            Assert.Equal("ALPHA", f.Get("alpha")!.Name);
            Assert.Equal(Db.IndexOf("uef_t1_tank"), f.RoleBlueprint("zeta", FactionRoles.TankT1));
            Assert.Equal(-1, f.RoleBlueprint("alpha", FactionRoles.TankT1));
            Assert.Equal(0, FactionDb.LoadDirectory(Path.Combine(dir, "missing"), Db).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RandomFactionIsResolvedDeterministicallyFromTheSeed()
    {
        var db = TwoFactionDb();
        var f = db.Factions;
        Assert.Equal("uef", f.Resolve(null, 1, 0));
        Assert.Equal("uef", f.Resolve("", 1, 0));
        Assert.Equal("testfac", f.Resolve("TESTFAC", 1, 0));
        Assert.Equal("cybran", f.Resolve("cybran", 1, 0)); // unknown ids pass through (GameSetup reports them)
        var picks = new HashSet<string>();
        for (int seed = 0; seed < 40; seed++)
            for (int a = 0; a < 4; a++)
            {
                string p = f.Resolve("random", seed, a);
                Assert.Equal(p, f.Resolve("Random", seed, a));
                picks.Add(p);
            }
        Assert.Equal(["testfac", "uef"], picks.Order());
        var s = new SkirmishSettings { Seed = 5, Players = 2, AiPlayers = [false, false], Factions = ["random", "random"] };
        Assert.Equal(GameSetup.ResolveFactions(db, s), GameSetup.ResolveFactions(db, s));
        Assert.Throws<InvalidOperationException>(() => GameSetup.ResolveFactions(db, new SkirmishSettings { Factions = ["cybran"] }));
    }

    [Fact]
    public void DefaultSkirmishArmiesAreUefWithTheUefCommander()
    {
        var sim = TestUtil.NewSkirmish(players: 3);
        foreach (var army in sim.World.Armies)
        {
            Assert.Equal("uef", army.Faction);
            Assert.Equal("uef_commander", sim.World.BpOf(army.Commander.Index).Id);
        }
    }

    [Fact]
    public void TwoFactionSetupSpawnsEachFactionsCommanderAndAiUsesItsRoles()
    {
        var db = TwoFactionDb();
        var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings { Seed = 3, Players = 2, AiPlayers = [true, true], Factions = ["uef", "testfac"] });
        var w = sim.World;
        Assert.Equal("uef", w.Armies[0].Faction);
        Assert.Equal("testfac", w.Armies[1].Faction);
        Assert.Equal("uef_commander", w.BpOf(w.Armies[0].Commander.Index).Id);
        Assert.Equal("uef_scu", w.BpOf(w.Armies[1].Commander.Index).Id);
        Assert.Equal(db["uef_scu"].Economy.StorageMass, w.Armies[1].Eco.MassMax);
        Assert.Equal(StrategicApi.NukeSiloOf(w, 0), StrategicApi.NukeSiloOf(w, 1)); // no nuke role → the first faction that has one

        TestUtil.Run(sim, 5);
        var uefAi = AiSystem.StateOf(w)!.BrainOrNull(0)!;
        var testAi = AiSystem.StateOf(w)!.BrainOrNull(1)!;
        Assert.Equal(db.IndexOf("uef_t1_engineer"), uefAi.BpEngineer);
        Assert.Equal(db.IndexOf("uef_t2_engineer"), testAi.BpEngineer);
        Assert.Equal(db.IndexOf("uef_t1_artillery"), testAi.BpTank);
        Assert.Equal(-1, testAi.BpAirFactory); // missing role: the AI copes without it
        Assert.Equal(-1, testAi.BpBomber);
    }

    [Fact]
    public void UefAiRolesAreTheBlueprintsTheAiAlwaysUsed()
    {
        var sim = GameSetup.CreateSkirmish(Db, new SkirmishSettings { Players = 2, AiPlayers = [true, true] });
        TestUtil.Run(sim, 3);
        var b = AiSystem.StateOf(sim.World)!.BrainOrNull(0)!;
        (int Got, string Id)[] expected =
        [
            (b.BpEngineer, "uef_t1_engineer"), (b.BpTank, "uef_t1_tank"), (b.BpArtillery, "uef_t1_artillery"),
            (b.BpMobileAA, "uef_t1_mobile_aa"), (b.BpScout, "uef_t1_scout"), (b.BpLandFactory, "uef_t1_land_factory"),
            (b.BpAirFactory, "uef_t1_air_factory"), (b.BpMex, "uef_t1_mass_extractor"), (b.BpPgen, "uef_t1_power_generator"),
            (b.BpHydro, "uef_t1_hydrocarbon"), (b.BpMassStorage, "uef_t1_mass_storage"), (b.BpEnergyStorage, "uef_t1_energy_storage"),
            (b.BpPD, "uef_t1_point_defense"), (b.BpAA, "uef_t1_aa_turret"), (b.BpRadar, "uef_t1_radar"),
            (b.BpInterceptor, "uef_t1_interceptor"), (b.BpBomber, "uef_t1_bomber"), (b.BpAirScout, "uef_t1_air_scout"),
        ];
        foreach (var (got, id) in expected) Assert.Equal(Db.IndexOf(id), got);
    }

    [Fact]
    public void TwoFactionAiGameIsDeterministicAcrossThreadCounts()
    {
        var db = TwoFactionDb();
        ulong Run(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = GameSetup.CreateSkirmish(db, new SkirmishSettings { Seed = 11, Players = 2, AiPlayers = [true, true], Factions = ["testfac", "uef"] });
                TestUtil.Run(sim, 900);
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }
        Assert.Equal(Run(1), Run(4));
    }

    [Fact]
    public void SaveRoundTripKeepsTheFactions()
    {
        var db = TwoFactionDb();
        var settings = new SkirmishSettings { Seed = 3, Players = 2, AiPlayers = [false, true], Factions = ["testfac", "uef"] };
        var sim = GameSetup.CreateSkirmish(db, settings);
        TestUtil.Run(sim, 20);
        var info = GameInfo.ForSkirmish(settings, sim, 0, sandbox: false);
        Assert.Equal(["testfac", "uef"], info.Players.Select(p => p.Faction));
        Assert.Equal(["testfac", "uef"], GameInfo.FromJson(info.ToJson()).Players.Select(p => p.Faction));

        var restored = WorldState.Restore(WorldState.Capture(sim), db, sim.World.Terrain);
        Assert.Equal(["testfac", "uef"], restored.World.Armies.Select(a => a.Faction));
        Assert.Equal(sim.StateHash(), restored.StateHash());
    }
}
