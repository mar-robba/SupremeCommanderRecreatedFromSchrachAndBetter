using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Experimental;
using Rts.Sim.Orders;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>Experimentals (Fatboy, Mavor), generic experimental mechanics and the Quantum Gateway / SCU.</summary>
public class ExperimentalTests
{
    private static void Rich(SimWorld w)
    {
        foreach (var a in w.Armies) { a.Eco.Mass = a.Eco.MassMax; a.Eco.Energy = a.Eco.EnergyMax; }
    }

    private static void StepRich(Simulation sim, params Command[] cmds)
    {
        Rich(sim.World);
        sim.Step(cmds);
    }

    private static Vector2 TowardCenter(SimWorld w, Vector2 from)
    {
        var c = new Vector2(w.Terrain.Width * 0.5f, w.Terrain.Height * 0.5f);
        return Vector2.Normalize(c - from);
    }

    [Fact]
    public void ExperimentalBlueprintsAreWiredUp()
    {
        var db = TestUtil.Db;
        foreach (var id in new[] { "uef_x_fatboy", "uef_x_mavor" })
        {
            var bp = db[id];
            Assert.True(bp.Has(UnitCategory.Experimental));
            Assert.Equal(4, bp.Tech);
            Assert.NotNull(bp.Experimental);
            Assert.Contains(bp.Index, db["uef_t3_engineer"].BuildIndices);
            Assert.Contains(bp.Index, db["uef_scu"].BuildIndices);
        }
        var fatboy = db["uef_x_fatboy"];
        Assert.True(fatboy.Radius <= 3.0f, "keep mobile radius ≤ 3 (separation query size for every unit)");
        Assert.True(fatboy.Has(UnitCategory.Factory) && fatboy.IsMobile);
        Assert.Contains(db.IndexOf("uef_t3_heavy_bot"), fatboy.BuildIndices);
        Assert.Contains(db.IndexOf("uef_t1_tank"), fatboy.BuildIndices);
        Assert.NotNull(fatboy.Shield);
        var mavor = db["uef_x_mavor"].Weapons[0];
        Assert.True(mavor.Ballistic && mavor.EnergyPerShot > 0);
        Assert.True(mavor.Range <= mavor.MuzzleVelocity * mavor.MuzzleVelocity / Ballistics.Gravity, "Mavor range must be reachable by a ballistic arc");
        Assert.True(mavor.Range >= 1000);
        Assert.Contains(db.IndexOf("uef_t3_quantum_gateway"), db["uef_t3_engineer"].BuildIndices);
        Assert.Contains(db.IndexOf("uef_scu"), db["uef_t3_quantum_gateway"].BuildIndices);
    }

    [Fact]
    public void FatboyCrushesSmallEnemyLandUnitsOnlyWhileMoving()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FindSite(w, "uef_x_fatboy", CommanderPos(w), 20, 60);
        var fb = TestUtil.Spawn(sim, "uef_x_fatboy", 0, site);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 1, site + new Vector2(2.5f, 0));
        var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, site + new Vector2(-2.5f, 0)); // T3: too big
        var ally = TestUtil.Spawn(sim, "uef_t1_tank", 0, site + new Vector2(0, 2.5f));   // own: not crushed
        var far = TestUtil.Spawn(sim, "uef_t1_tank", 1, site + new Vector2(0, -7f));     // out of reach
        w.Units.RebuildAliveList();
        w.Grid.Rebuild(w.Units);

        w.Events.Damage.Clear();
        w.Units.Move[fb.Index].Speed = 0;
        ExperimentalSystem.Run(w);
        Assert.DoesNotContain(w.Events.Damage, d => d.Source.Index == fb.Index);

        w.Units.Move[fb.Index].Speed = 1.5f;
        ExperimentalSystem.Run(w);
        var hits = w.Events.Damage.Where(d => d.Source.Index == fb.Index).ToList();
        Assert.Single(hits);
        Assert.Equal(tank.Index, hits[0].Target.Index);
        Assert.Equal(w.BpOf(fb.Index).Experimental!.CrushDamage * SimWorld.Dt, hits[0].Amount, 3);
        Assert.True(hits[0].Amount >= w.BpOf(tank.Index).MaxHealth, "a T1 tank is flattened in one tick");
        _ = bot; _ = ally; _ = far;
    }

    [Fact]
    public void ExperimentalDeathExplodesAfterDelayAndLeavesBigWreck()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var site = FindSite(w, "uef_x_fatboy", CommanderPos(w), 20, 60);
        var fb = TestUtil.Spawn(sim, "uef_x_fatboy", 0, site);
        var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 0, site + new Vector2(6f, 0)); // allied: nobody shoots it
        sim.Step();
        float hp0 = w.Units.Health[bot.Index];
        var x = w.BpOf(fb.Index).Experimental!;
        w.KillUnit(fb.Index, UnitHandle.None);
        int hitStep = -1;
        for (int t = 1; t <= 30 && hitStep < 0; t++)
        {
            sim.Step();
            if (w.Units.Health[bot.Index] < hp0 - 1) hitStep = t;
        }
        int delay = (int)MathF.Round(x.DeathDelay / SimWorld.Dt);
        Assert.InRange(hitStep, delay - 1, delay + 2);
        float dmg = hp0 - w.Units.Health[bot.Index];
        Assert.InRange(dmg, x.DeathDamage * 0.5f, x.DeathDamage); // linear falloff with distance

        float wreck = 0;
        foreach (int p in w.Props.AliveList) wreck = MathF.Max(wreck, w.Props.Mass[p]);
        var fbp = TestUtil.Db["uef_x_fatboy"];
        Assert.True(wreck >= fbp.Economy.BuildCostMass * fbp.WreckMassFraction * 0.99f, $"wreck {wreck}");
    }

    [Fact]
    public void FatboyBuildsUnitsWhileMovingAndReleasesThemBehind()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var u = w.Units;
        var site = FindSite(w, "uef_x_fatboy", CommanderPos(w), 20, 60);
        var fb = TestUtil.Spawn(sim, "uef_x_fatboy", 0, site);
        var dest = w.Terrain.Clamp(site + TowardCenter(w, site) * 40f);
        StepRich(sim,
            new IssueOrderCommand(0, [fb], Order.MoveTo(dest), false),
            new FactoryEnqueueCommand(0, [fb], Bp("uef_t1_tank"), 2));

        bool carried = false;
        int released = 0;
        for (int t = 0; t < 400 && released < 2; t++)
        {
            StepRich(sim);
            var cur = u.Factory[fb.Index].Current;
            if (!cur.IsNone && u.IsActive(cur) && !u.IsComplete(cur.Index) && u.Move[fb.Index].Speed > 0.3f)
            {
                Assert.Equal(u.Pos[fb.Index], u.Pos[cur.Index]); // rides inside
                carried = true;
            }
            var tanks = UnitsOf(w, 0, "uef_t1_tank").Where(u.IsComplete).ToList();
            if (tanks.Count > released)
            {
                released = tanks.Count;
                int tk = tanks[^1];
                Assert.True(u.Move[fb.Index].Speed > 0.3f, "the Fatboy keeps moving while it builds");
                var rel = u.Pos[tk] - u.Pos[fb.Index];
                Assert.True(Vector2.Dot(rel, DMath.Dir(u.Heading[fb.Index])) < 0, "units exit at the back");
                // The Fatboy's move order is not handed over as rally.
                Assert.NotEqual(dest, u.Orders[tk].Count > 0 ? u.Orders[tk].Current.Pos : dest);
            }
        }
        Assert.True(carried);
        Assert.Equal(2, released);
        Assert.True(Vector2.Distance(u.Pos[fb.Index], site) > 2f);
    }

    [Fact]
    public void MavorNeedsEnergyAndHitsFarTargetsWithBallisticShells()
    {
        var sim = TestUtil.NewSkirmish(mapSize: 512);
        var w = sim.World;
        var u = w.Units;
        int mavorBp = Bp("uef_x_mavor");
        var site = FindSite(w, "uef_x_mavor", CommanderPos(w), 15, 60);
        var mavor = TestUtil.Spawn(sim, "uef_x_mavor", 0, site);
        var dir = TowardCenter(w, site);
        var tsite = FindSite(w, "uef_t1_power_generator", site + dir * 260f, 0, 30);
        var target = TestUtil.Spawn(sim, "uef_t1_power_generator", 1, tsite);
        TestUtil.Spawn(sim, "uef_t3_omni_sensor", 0, FindSite(w, "uef_t3_omni_sensor", tsite, 8, 40)); // intel on the target
        u.Heading[mavor.Index] = MathF.Atan2(dir.Y, dir.X);

        int Shells() { int n = 0; foreach (int p in w.Projectiles.AliveList) if (w.Projectiles.SourceBp[p] == mavorBp) n++; return n; }

        // Starved of energy: no shot.
        for (int t = 0; t < 80; t++)
        {
            foreach (var a in w.Armies) a.Eco.Energy = 0;
            sim.Step();
            Assert.Equal(0, Shells());
        }
        Assert.True(WeaponEnergy.Fraction(w, mavor.Index, 0) < 0.1f);

        // With energy it charges (≈ 20000 E at ≤ cost × RateOfFire per second) and fires.
        int fired = -1;
        for (int t = 0; t < 300 && fired < 0; t++)
        {
            StepRich(sim);
            if (Shells() > 0) fired = t;
        }
        Assert.True(fired > 0, "the Mavor fired");
        Assert.True(WeaponEnergy.Fraction(w, mavor.Index, 0) < 0.2f, "the shot used the charge");

        // Follow the shell to its impact.
        Vector3 last = default;
        for (int t = 0; t < 120; t++)
        {
            int shell = -1;
            foreach (int p in w.Projectiles.AliveList) if (w.Projectiles.SourceBp[p] == mavorBp) shell = p;
            if (shell < 0) break;
            last = w.Projectiles.Pos[shell];
            StepRich(sim);
        }
        var impact = new Vector2(last.X, last.Z);
        Assert.True(Vector2.Distance(impact, site) > 200f, $"impact {impact} too close to the Mavor");
        Assert.True(Vector2.Distance(impact, tsite) < 60f, $"impact {impact} far from the target {tsite}");
        _ = target;
    }

    [Fact]
    public void QuantumGatewayBuildsScuAndScuDeathDoesNotDefeatArmy()
    {
        var sim = TestUtil.NewSkirmish();
        var w = sim.World;
        var u = w.Units;
        var gw = TestUtil.Spawn(sim, "uef_t3_quantum_gateway", 0, FindSite(w, "uef_t3_quantum_gateway", CommanderPos(w), 12, 50));
        StepRich(sim, new FactoryEnqueueCommand(0, [gw], Bp("uef_scu"), 1));
        int scu = -1;
        for (int t = 0; t < 900 && scu < 0; t++)
        {
            StepRich(sim);
            var l = UnitsOf(w, 0, "uef_scu").Where(u.IsComplete).ToList();
            if (l.Count > 0) scu = l[0];
        }
        Assert.True(scu >= 0, "the gateway built an SCU");
        var sbp = w.BpOf(scu);
        Assert.False(sbp.Has(UnitCategory.Commander));
        Assert.True(sbp.IsEnhanceable);
        Assert.True(sbp.Economy.BuildRate > 0);

        w.KillUnit(scu, UnitHandle.None);
        for (int t = 0; t < 5; t++) sim.Step();
        Assert.False(w.Armies[0].Defeated);
        Assert.True(u.IsActive(w.Armies[0].Commander));
    }

    [Fact]
    public void ExperimentalsAreDeterministicAcrossThreadCounts()
    {
        ulong Run(int threads)
        {
            int old = Par.MaxThreads;
            Par.MaxThreads = threads;
            try
            {
                var sim = TestUtil.NewSkirmish(seed: 11);
                var w = sim.World;
                var site = FindSite(w, "uef_x_fatboy", CommanderPos(w), 20, 60);
                var fb = TestUtil.Spawn(sim, "uef_x_fatboy", 0, site);
                var dir = TowardCenter(w, site);
                var tanks = new List<UnitHandle>();
                for (int k = 0; k < 12; k++)
                    tanks.Add(TestUtil.Spawn(sim, "uef_t1_tank", 1, site + dir * (6 + k % 4 * 2.5f) + new Vector2(-dir.Y, dir.X) * (k / 4 * 2.5f - 2.5f)));
                StepRich(sim,
                    new IssueOrderCommand(0, [fb], Order.MoveTo(w.Terrain.Clamp(site + dir * 30f)), false),
                    new FactoryEnqueueCommand(0, [fb], Bp("uef_t1_tank"), 3),
                    new IssueOrderCommand(1, tanks.ToArray(), Order.MoveTo(site), false));
                for (int t = 0; t < 120; t++) StepRich(sim);
                w.KillUnit(fb.Index, UnitHandle.None);
                for (int t = 0; t < 30; t++) StepRich(sim);
                return sim.StateHash();
            }
            finally { Par.MaxThreads = old; }
        }
        Assert.Equal(Run(1), Run(4));
    }
}
