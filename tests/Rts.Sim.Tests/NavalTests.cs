using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Small hand-made water maps for naval tests.</summary>
public static class NavalWorld
{
    public const float Water = 6f;

    /// <summary>
    /// 128x128 sea (floor at 2 = 4 deep) with a dry land strip x in [60, 68] (height <paramref name="stripHeight"/>)
    /// and a shallow shelf x in [40, 44] (floor 5.6 = 0.4 deep).
    /// </summary>
    public static Simulation Basin(int armies = 2, bool strip = true, ulong seed = 1, float stripHeight = 10f)
    {
        const int size = 128;
        var heights = new float[(size + 1) * (size + 1)];
        for (int z = 0; z <= size; z++)
        for (int x = 0; x <= size; x++)
        {
            float h = 2f;
            if (x >= 40 && x <= 44) h = 5.6f;
            if (strip && x >= 60 && x <= 68) h = stripHeight;
            heights[z * (size + 1) + x] = h;
        }
        var terrain = new Terrain(size, size, heights, Water, [], [new Vector2(20, 20), new Vector2(108, 108)], 1);
        var arr = new Army[armies];
        for (int a = 0; a < armies; a++) arr[a] = new Army { Index = a, Team = a, Name = $"A{a}" };
        return new Simulation(new SimWorld(TestUtil.Db, terrain, arr, seed, unitCapacity: 2048, projectileCapacity: 8192));
    }

    /// <summary>A power generator on the land strip so the army can pay sensor upkeep (storage comes from units).</summary>
    public static void Power(Simulation sim, int army) => Spawn(sim, "uef_t1_power_generator", army, 64, 100 + 4 * army);

    public static UnitHandle Spawn(Simulation sim, string bp, int army, float x, float z, float heading = 0) =>
        sim.World.SpawnUnit(TestUtil.Db.IndexOf(bp), army, new Vector2(x, z), heading, complete: true);

    public static Simulation Archipelago(int seed = 42, int size = 256, int players = 2) =>
        GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = seed, MapSize = size, Players = players, MapPreset = MapPreset.Archipelago, AiPlayers = new bool[players],
        });

    /// <summary>Nearest valid naval factory site to a start position.</summary>
    public static Vector2 NavalSite(SimWorld w, Vector2 near, string bp = "uef_t1_naval_factory")
    {
        Assert.True(NavalRules.FindSite(w.Occupancy, w.Terrain, TestUtil.Db[bp], near, 160f, out var site), $"no {bp} site near {near}");
        return site;
    }
}

public class NavalTests
{
    private static int Bp(string id) => TestUtil.Db.IndexOf(id);

    [Fact]
    public void NavalBlueprintsLoadWithLinks()
    {
        var db = TestUtil.Db;
        var f1 = db["uef_t1_naval_factory"];
        Assert.True(NavalRules.IsWaterStructure(f1));
        Assert.Equal(db.IndexOf("uef_t2_naval_factory"), f1.UpgradesToIndex);
        Assert.Equal(db.IndexOf("uef_t3_naval_factory"), db["uef_t2_naval_factory"].UpgradesToIndex);
        Assert.Contains(db.IndexOf("uef_t1_naval_factory"), db["uef_commander"].BuildIndices);
        Assert.Contains(db.IndexOf("uef_t1_sonar"), db["uef_t1_engineer"].BuildIndices);
        Assert.Contains(db.IndexOf("uef_t1_frigate"), f1.BuildIndices);
        Assert.Contains(db.IndexOf("uef_t3_battleship"), db["uef_t3_naval_factory"].BuildIndices);
        Assert.Equal(Combat.WeaponKind.Torpedo, Torpedoes.KindOf(db["uef_t1_sub"].Weapons[0]));
        Assert.Equal(Combat.WeaponKind.AntiTorpedo, Torpedoes.KindOf(db["uef_t2_destroyer"].Weapons[2]));
        Assert.Equal(MotionLayer.Sub, db["uef_t1_sub"].Movement.Layer);
    }

    [Fact]
    public void WaterStructuresOnlyOnWaterOfSufficientDepth()
    {
        var sim = NavalWorld.Basin();
        var w = sim.World;
        var factory = TestUtil.Db["uef_t1_naval_factory"];
        var sonar = TestUtil.Db["uef_t1_sonar"];
        var landFactory = TestUtil.Db["uef_t1_land_factory"];
        Assert.True(w.Occupancy.CanPlace(factory, OccupancyGrid.SnapStructure(factory, new Vector2(20, 64)), w.Terrain), "deep water");
        Assert.False(w.Occupancy.CanPlace(factory, OccupancyGrid.SnapStructure(factory, new Vector2(64, 64)), w.Terrain), "dry land");
        Assert.False(w.Occupancy.CanPlace(factory, OccupancyGrid.SnapStructure(factory, new Vector2(42, 64)), w.Terrain), "shallows");
        Assert.False(w.Occupancy.CanPlace(factory, OccupancyGrid.SnapStructure(factory, new Vector2(57, 64)), w.Terrain), "half on the shore");
        // The sonar only needs a little water; land structures never go on water.
        Assert.True(w.Occupancy.CanPlace(sonar, OccupancyGrid.SnapStructure(sonar, new Vector2(42, 64)), w.Terrain));
        Assert.False(w.Occupancy.CanPlace(sonar, OccupancyGrid.SnapStructure(sonar, new Vector2(64, 64)), w.Terrain));
        Assert.True(w.Occupancy.CanPlace(landFactory, OccupancyGrid.SnapStructure(landFactory, new Vector2(64, 64)), w.Terrain));
        Assert.False(w.Occupancy.CanPlace(landFactory, OccupancyGrid.SnapStructure(landFactory, new Vector2(20, 64)), w.Terrain));

        // A build order on land is dropped; on water the site appears and sits on the water surface.
        var cmd = NavalWorld.Spawn(sim, "uef_commander", 0, 58, 40);
        TestUtil.Run(sim, 5, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_naval_factory"), new Vector2(64, 40)), false));
        Assert.Empty(EconomyTestUtil.UnitsOf(w, 0, "uef_t1_naval_factory"));
        TestUtil.Run(sim, 40, new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_naval_factory"), new Vector2(50, 40)), false));
        var sites = EconomyTestUtil.UnitsOf(w, 0, "uef_t1_naval_factory");
        Assert.Single(sites);
        Assert.Equal(NavalWorld.Water, w.Units.Elevation[sites[0]], 3);
    }

    [Fact]
    public void NavalFactoryReleasesFrigateThatSailsAcrossTheArchipelago()
    {
        var sim = NavalWorld.Archipelago(seed: 42);
        var w = sim.World;
        var t = w.Terrain;
        var site = NavalWorld.NavalSite(w, t.StartPositions[0]);
        var fac = w.SpawnUnit(Bp("uef_t1_naval_factory"), 0, site, DMath.HeadingOf(t.StartPositions[1] - site));
        var far = NavalWorld.NavalSite(w, t.StartPositions[1]); // open water off the enemy island
        sim.Step([new FactoryEnqueueCommand(0, [fac], Bp("uef_t1_frigate"), 1)]);
        int frigate = -1;
        for (int k = 0; k < 900 && frigate < 0; k++)
        {
            sim.Step();
            var list = EconomyTestUtil.UnitsOf(w, 0, "uef_t1_frigate");
            if (list.Count == 1 && w.Units.IsComplete(list[0])) frigate = list[0];
        }
        Assert.True(frigate >= 0, "frigate not built");
        Assert.True(t.CanTraverse(MotionLayer.Water, (int)w.Units.Pos[frigate].X, (int)w.Units.Pos[frigate].Y), "released on water");
        var h = w.Units.HandleOf(frigate);
        sim.Step([new IssueOrderCommand(0, [h], Order.MoveTo(far), false)]);
        for (int k = 0; k < 2400 && Vector2.Distance(w.Units.Pos[frigate], far) > 4f; k++) sim.Step();
        Assert.True(Vector2.Distance(w.Units.Pos[frigate], far) <= 4f, $"frigate at {w.Units.Pos[frigate]}, wanted {far} (start {site})");
    }

    [Fact]
    public void T3NavalFactoryReleasesABattleshipThatReachesTheEnemyCoast()
    {
        var sim = NavalWorld.Archipelago(seed: 7);
        var w = sim.World;
        var t = w.Terrain;
        var site = NavalWorld.NavalSite(w, t.StartPositions[0], "uef_t3_naval_factory");
        var fac = w.SpawnUnit(Bp("uef_t3_naval_factory"), 0, site, DMath.HeadingOf(t.StartPositions[1] - site));
        var far = NavalWorld.NavalSite(w, t.StartPositions[1]);
        sim.Step([new FactoryEnqueueCommand(0, [fac], Bp("uef_t3_battleship"), 1)]);
        int ship = -1;
        for (int k = 0; k < 4000 && ship < 0; k++)
        {
            var e = w.Armies[0].Eco;
            e.Mass = e.MassMax;
            e.Energy = e.EnergyMax;
            sim.Step();
            var list = EconomyTestUtil.UnitsOf(w, 0, "uef_t3_battleship");
            if (list.Count == 1 && w.Units.IsComplete(list[0]) && w.Units.Factory[fac.Index].Current.IsNone) ship = list[0];
        }
        Assert.True(ship >= 0, "battleship not built");
        var h = w.Units.HandleOf(ship);
        sim.Step([new IssueOrderCommand(0, [h], Order.MoveTo(far), false)]);
        for (int k = 0; k < 3000 && Vector2.Distance(w.Units.Pos[ship], far) > 6f; k++) sim.Step();
        Assert.True(Vector2.Distance(w.Units.Pos[ship], far) <= 6f, $"battleship at {w.Units.Pos[ship]}, wanted {far} (factory {site})");
    }

    [Fact]
    public void TorpedoesHitShipsAndSubs()
    {
        // Sub vs frigate: the frigate can't shoot back at a submerged sub.
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        NavalWorld.Spawn(sim, "uef_t1_sub", 0, 20, 64);
        var frigate = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 20, 40);
        bool sawTorpedo = false;
        for (int k = 0; k < 300; k++)
        {
            sim.Step();
            for (int p = 0; p < w.Projectiles.AliveCount; p++)
            {
                int pi = w.Projectiles.AliveList[p];
                if ((w.Projectiles.Flags[pi] & ProjectileFlags.Torpedo) == 0) continue;
                sawTorpedo = true;
                Assert.True(w.Projectiles.Pos[pi].Y <= NavalWorld.Water, "sub torpedoes run under water");
            }
        }
        Assert.True(sawTorpedo);
        Assert.True(CombatWorld.Health(sim, frigate) < TestUtil.Db["uef_t1_frigate"].MaxHealth, "torpedoes should hit the frigate");

        // Sub vs sub: both take torpedo hits.
        var sim2 = NavalWorld.Basin(strip: false);
        var a = NavalWorld.Spawn(sim2, "uef_t1_sub", 0, 20, 64);
        var b = NavalWorld.Spawn(sim2, "uef_t1_sub", 1, 20, 88);
        TestUtil.Run(sim2, 200);
        float max = TestUtil.Db["uef_t1_sub"].MaxHealth;
        Assert.True(CombatWorld.Health(sim2, a) < max && CombatWorld.Health(sim2, b) < max, "subs should torpedo each other");
    }

    [Fact]
    public void TorpedoesDieOnTheShore()
    {
        var sim = NavalWorld.Basin(strip: true);
        var w = sim.World;
        // Sub west of the land strip, frigate just east of it: the torpedo's straight run crosses land.
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 50, 64);
        var frigate = NavalWorld.Spawn(sim, "uef_t1_frigate", 1, 73, 64);
        sim.Step([new IssueOrderCommand(0, [sub], Order.AttackUnit(frigate), false)]);
        // Keep the sub in place (it would otherwise try to path around).
        int fired = 0;
        for (int k = 0; k < 300; k++)
        {
            w.Units.Pos[sub.Index] = new Vector2(50, 64);
            sim.Step();
            for (int p = 0; p < w.Projectiles.AliveCount; p++)
            {
                int pi = w.Projectiles.AliveList[p];
                if ((w.Projectiles.Flags[pi] & ProjectileFlags.Torpedo) == 0) continue;
                fired++;
                Assert.True(w.Projectiles.Pos[pi].X < 61f, $"torpedo crossed the land strip: {w.Projectiles.Pos[pi]}");
            }
        }
        Assert.True(fired > 0, "sub never fired");
        Assert.Equal(TestUtil.Db["uef_t1_frigate"].MaxHealth, CombatWorld.Health(sim, frigate));
    }

    [Fact]
    public void SubsNeedSonarToBeSeenAndTargeted()
    {
        var sim = NavalWorld.Basin(strip: true);
        var w = sim.World;
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 56, 64);
        var tank = NavalWorld.Spawn(sim, "uef_t1_tank", 1, 63, 64); // on the land strip, 7 away
        NavalWorld.Power(sim, 1);
        TestUtil.Run(sim, 5);
        Assert.False(w.Intel.IsVisible(1, sub.Index), "submerged sub visible without sonar");
        Assert.False(w.Intel.IsDetected(1, sub.Index), "submerged sub detected without sonar");
        // The sub sees the tank (vision) but can't shoot it: torpedoes only hit water/sub layers.
        Assert.True(w.Intel.IsVisible(0, tank.Index));

        // A sonar reveals the sub; a torpedo launcher then kills it.
        NavalWorld.Spawn(sim, "uef_t1_sonar", 1, 50, 80);
        TestUtil.Run(sim, 3);
        Assert.True(w.Intel.IsDetected(1, sub.Index), "sonar should detect the sub");
        NavalWorld.Spawn(sim, "uef_t1_torpedo_launcher", 1, 56, 90);
        for (int k = 0; k < 900 && CombatWorld.Alive(sim, sub); k++) sim.Step();
        Assert.False(CombatWorld.Alive(sim, sub), "torpedo launcher should sink the sub");
    }

    [Fact]
    public void LandUnitsCannotTargetSubsEvenWithSonar()
    {
        var sim = NavalWorld.Basin(strip: true);
        var w = sim.World;
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 0, 56, 64);
        var tank = NavalWorld.Spawn(sim, "uef_t1_tank", 1, 63, 64);
        NavalWorld.Power(sim, 1);
        NavalWorld.Spawn(sim, "uef_t1_sonar", 1, 12, 118); // out of the sub's torpedo range
        sim.Step([new IssueOrderCommand(1, [tank], Order.AttackUnit(sub), false)]);
        for (int k = 0; k < 200; k++)
        {
            sim.Step();
            Assert.True(CombatWorld.Weapon(sim, tank).Target.IsNone, "tank acquired a submerged sub");
        }
        Assert.True(w.Intel.IsDetected(1, sub.Index), "sonar should detect the sub");
        Assert.Equal(TestUtil.Db["uef_t1_sub"].MaxHealth, CombatWorld.Health(sim, sub));
    }

    [Fact]
    public void ShipsCanBeHitByNormalWeapons()
    {
        // Low strip (1 m above the water) so straight shots aren't stopped by the cliff edge.
        var sim = NavalWorld.Basin(strip: true, stripHeight: 7f);
        var frigate = NavalWorld.Spawn(sim, "uef_t1_frigate", 0, 54, 64);
        var tank = NavalWorld.Spawn(sim, "uef_t1_tank", 1, 64, 64);
        TestUtil.Run(sim, 200);
        Assert.True(CombatWorld.Health(sim, frigate) < TestUtil.Db["uef_t1_frigate"].MaxHealth, "tank should shell the frigate");
        Assert.True(CombatWorld.Health(sim, tank) < TestUtil.Db["uef_t1_tank"].MaxHealth, "frigate should shell the tank");
    }

    [Fact]
    public void TorpedoDefenseInterceptsAtALimitedRate()
    {
        var sim = NavalWorld.Basin(strip: false);
        var w = sim.World;
        var destroyer = NavalWorld.Spawn(sim, "uef_t2_destroyer", 0, 30, 64);
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 1, 30, 100);
        sim.Step();
        // Two torpedoes in the water inside the flare range, heading for the destroyer.
        var P = w.Projectiles;
        int subBp = Bp("uef_t1_sub");
        int[] ids = new int[2];
        for (int k = 0; k < 2; k++)
        {
            var pos = new Vector3(30 + k, NavalWorld.Water - Torpedoes.RunDepth, 72);
            ids[k] = P.Spawn(pos, new Vector3(0, 0, -9), 1, sub, subBp, 0, destroyer, pos, 150, 0, 5, 0);
            P.Flags[ids[k]] = ProjectileFlags.Torpedo;
        }
        P.RebuildAliveList();
        sim.Step();
        int alive = (P.Alive[ids[0]] ? 1 : 0) + (P.Alive[ids[1]] ? 1 : 0);
        Assert.Equal(1, alive); // one flare per 2 s: the second torpedo gets through
        Assert.True(w.Units.Weapons[destroyer.Index * UnitStore.MaxWeapons + 2].Cooldown > 1f);
    }

    [Theory]
    [InlineData(256, 2, 42)]
    [InlineData(256, 2, 7)]
    [InlineData(256, 3, 5)]
    [InlineData(256, 4, 2)]
    [InlineData(256, 6, 1)]
    [InlineData(512, 4, 11)]
    public void ArchipelagoStartsHaveLandAndAreLinkedBySea(int size, int players, int seed)
    {
        var sim = NavalWorld.Archipelago(seed, size, players);
        var w = sim.World;
        var t = w.Terrain;
        var battleship = TestUtil.Db["uef_t3_battleship"];
        var sites = new List<Vector2>();
        foreach (var sp in t.StartPositions)
        {
            int island = MapGenerator.FloodLand(t, sp).Count(b => b);
            Assert.True(island >= 2500, $"start {sp}: island of only {island} land cells");
            // A naval factory fits on the coast within reach of a short walk.
            var site = NavalWorld.NavalSite(w, sp);
            Assert.True(Vector2.Distance(site, sp) < 80f * MathF.Sqrt(size / 256f) + 10f, $"start {sp}: nearest naval site {site} too far");
            sites.Add(site);
        }
        // The biggest ships can sail from every start's coast to every other start's coast.
        w.Paths.EnsureGrids();
        var grid = w.Paths.GridFor(MotionLayer.Water)!;
        int req = PathService.ClearanceOf(battleship);
        Vector2 Harbor(Vector2 near)
        {
            Assert.True(grid.FindNearestPassable((int)near.X, (int)near.Y, req, 40, out int x, out int z), $"no open water near {near}");
            return new Vector2(x + 0.5f, z + 0.5f);
        }
        var h0 = Harbor(sites[0]);
        for (int k = 1; k < sites.Count; k++)
            Assert.True(w.Paths.CanReach(MotionLayer.Water, battleship.Radius, h0, Harbor(sites[k])), $"no sea lane from start 0 to start {k}");
    }

    [Fact]
    public void CommanderBuildsANavalFactoryOnTheArchipelagoCoast()
    {
        var sim = NavalWorld.Archipelago(seed: 7);
        var w = sim.World;
        var cmd = w.Armies[0].Commander;
        var site = NavalWorld.NavalSite(w, w.Terrain.StartPositions[0]);
        sim.Step([new IssueOrderCommand(0, [cmd], Order.BuildAt(Bp("uef_t1_naval_factory"), site, 0), false)]);
        bool done = false;
        for (int k = 0; k < 1500 && !done; k++)
        {
            sim.Step();
            var list = EconomyTestUtil.UnitsOf(w, 0, "uef_t1_naval_factory");
            done = list.Count == 1 && w.Units.IsComplete(list[0]);
        }

        Assert.True(done, $"commander at {w.Units.Pos[cmd.Index]} never finished the naval factory at {site}");
    }

    [Fact]
    public void NavalFactoryUpgradesToT3InPlaceAndTechBuildersHaveNavalEntries()
    {
        var db = TestUtil.Db;
        Assert.Contains(Bp("uef_t2_torpedo_bomber"), db["uef_t2_air_factory"].BuildIndices);
        Assert.Contains(Bp("uef_t2_torpedo_bomber"), db["uef_t3_air_factory"].BuildIndices);
        foreach (var eng in new[] { "uef_t2_engineer", "uef_t3_engineer" })
        foreach (var s in new[] { "uef_t1_naval_factory", "uef_t1_sonar", "uef_t1_torpedo_launcher" })
            Assert.Contains(Bp(s), db[eng].BuildIndices);

        var sim = NavalWorld.Archipelago(seed: 7);
        var w = sim.World;
        var u = w.Units;
        var site = NavalWorld.NavalSite(w, w.Terrain.StartPositions[0]);
        var f = w.SpawnUnit(Bp("uef_t1_naval_factory"), 0, site, 0);
        foreach (var next in new[] { "uef_t2_naval_factory", "uef_t3_naval_factory" })
        {
            sim.Step([new UpgradeCommand(0, [f])]);
            Assert.True(u.Upgrade[f.Index].Active, $"upgrade to {next} not started");
            for (int k = 0; k < 2000 && u.Bp[f.Index] != Bp(next); k++)
            {
                var e = w.Armies[0].Eco;
                e.Mass = e.MassMax;
                e.Energy = e.EnergyMax;
                sim.Step();
            }
            Assert.True(u.IsActive(f), "same handle after the upgrade");
            Assert.Equal(Bp(next), u.Bp[f.Index]);
        }
        Assert.True(NavalRules.IsWaterStructure(db["uef_t3_naval_factory"]));
        Assert.Contains(Bp("uef_t3_battleship"), db["uef_t3_naval_factory"].BuildIndices);
        Assert.Contains(Bp("uef_t1_frigate"), db["uef_t3_naval_factory"].BuildIndices);
    }

    [Fact]
    public void NavalCombatIsDeterministicAcrossThreadCounts()
    {
        ulong RunWith(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = NavalWorld.Archipelago(seed: 7);
                var w = sim.World;
                var t = w.Terrain;
                var units = new[] { new List<UnitHandle>(), new List<UnitHandle>() };
                string[] fleet = ["uef_t1_frigate", "uef_t1_sub", "uef_t2_destroyer", "uef_t1_sub", "uef_t2_cruiser", "uef_t1_frigate"];
                var targets = new Vector2[2];
                for (int a = 0; a < 2; a++)
                {
                    var site = NavalWorld.NavalSite(w, t.StartPositions[a]);
                    targets[1 - a] = site;
                    for (int k = 0; k < 24; k++)
                    {
                        var p = site + new Vector2((k % 6 - 2.5f) * 3.5f, (k / 6 - 1.5f) * 6f);
                        if (!t.CanTraverse(MotionLayer.Water, (int)p.X, (int)p.Y)) continue;
                        units[a].Add(w.SpawnUnit(Bp(fleet[k % fleet.Length]), a, p, 0));
                    }
                    var bomber = w.SpawnUnit(Bp("uef_t2_torpedo_bomber"), a, site + new Vector2(0, 10), 0);
                    units[a].Add(bomber);
                }
                sim.Step([
                    new IssueOrderCommand(0, units[0].ToArray(), Order.AttackMoveTo(targets[0]), false),
                    new IssueOrderCommand(1, units[1].ToArray(), Order.AttackMoveTo(targets[1]), false),
                ]);
                for (int k = 0; k < 900; k++) sim.Step();
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }

        Assert.Equal(RunWith(1), RunWith(8));
    }
}
