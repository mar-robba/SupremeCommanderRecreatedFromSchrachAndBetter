using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Cybran sim features: EMP stun, seabed walkers (amphibious), shield upgrade chain.</summary>
public class EmpAmphibiousShieldTests
{
    private static BlueprintDb? _empDb;

    /// <summary>Private copy: the UEF T2 heavy tank's gun stuns for 2 s in a radius of 5.</summary>
    private static BlueprintDb EmpDb => _empDb ??= BuildEmpDb();

    private static BlueprintDb BuildEmpDb()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var wb = db["uef_t2_heavy_tank"].Weapons[0];
        wb.StunDuration = 2;
        wb.StunRadius = 5;
        return db;
    }

    private static UnitHandle Spawn(Simulation sim, string bp, int army, float x, float z, float heading = 0) =>
        CybranTestWorld.Spawn(sim, bp, army, x, z, heading);

    private static bool Stunned(Simulation sim, UnitHandle h) => EmpStun.IsStunned(sim.World, h.Index);

    // ------------------------------------------------------------------ EMP stun

    [Fact]
    public void StunnedTankDoesNotMoveOrFireThenResumes()
    {
        var sim = CybranTestWorld.Flat(TestUtil.Db, 128);
        var w = sim.World;
        var shooter = Spawn(sim, "uef_t1_tank", 0, 40, 64);
        var mover = Spawn(sim, "uef_t1_tank", 0, 40, 30);
        var target = Spawn(sim, "uef_t1_tank", 1, 54, 64, heading: MathF.PI);
        EmpStun.Stun(w, shooter.Index, 3);
        EmpStun.Stun(w, mover.Index, 3);
        var moverStart = w.Units.Pos[mover.Index];
        sim.Step([new SetFireStateCommand(1, [target], FireState.HoldFire), new IssueOrderCommand(0, [mover], Order.MoveTo(new Vector2(90, 30)), false)]);
        CybranTestWorld.Run(sim, 25);
        Assert.True(Stunned(sim, shooter) && Stunned(sim, mover));
        Assert.Equal(w.BpOf(target.Index).MaxHealth, w.Units.Health[target.Index]);
        Assert.True(CybranTestWorld.Target(sim, shooter).IsNone, "stunned: no target");
        Assert.True(Vector2.Distance(moverStart, w.Units.Pos[mover.Index]) < 0.01f, "stunned: no movement");
        Assert.True((StealthSystem.StatusOf(w, shooter.Index, 1) & UnitStatus.Stunned) != 0, "the stun bit is visible to everyone");
        Assert.True(EmpStun.SecondsLeft(w, shooter.Index) > 0);

        CybranTestWorld.Run(sim, 10); // 3 s are over
        Assert.False(Stunned(sim, shooter));
        Assert.False(Stunned(sim, mover));
        CybranTestWorld.Run(sim, 40);
        Assert.True(w.Units.Health[target.Index] < w.BpOf(target.Index).MaxHealth, "fires again after the stun");
        Assert.True(w.Units.Pos[mover.Index].X > moverStart.X + 3f, "moves again after the stun");
    }

    [Fact]
    public void StunnedFactoryStopsProducing()
    {
        var sim = CybranTestWorld.Flat(TestUtil.Db, 128);
        var w = sim.World;
        Spawn(sim, "uef_commander", 0, 20, 100); // storage + some income
        CybranTestWorld.Power(sim, 0, 10);
        var factory = Spawn(sim, "uef_t1_land_factory", 0, 64, 64);
        sim.Step();
        w.Armies[0].Eco.Mass = w.Armies[0].Eco.MassMax;
        w.Armies[0].Eco.Energy = w.Armies[0].Eco.EnergyMax;
        sim.Step([new FactoryEnqueueCommand(0, [factory], w.Blueprints.IndexOf("uef_t1_tank"), 1)]);
        CybranTestWorld.Run(sim, 5);
        EmpStun.Stun(w, factory.Index, 2);
        sim.Step(); // the request of the last tick is still paid (same as pausing)
        var current = w.Units.Factory[factory.Index].Current;
        Assert.True(w.Units.IsActive(current));
        float frac = w.Units.BuildFraction[current.Index];
        CybranTestWorld.Run(sim, 15);
        Assert.Equal(frac, w.Units.BuildFraction[current.Index]);
        CybranTestWorld.Run(sim, 15);
        Assert.True(!w.Units.IsActive(current) || w.Units.BuildFraction[current.Index] > frac, "production resumes");
    }

    [Fact]
    public void StunAreaSkipsCommandersExperimentalsAlliesAndHighTech()
    {
        var sim = CybranTestWorld.Flat(TestUtil.Db, 128);
        var w = sim.World;
        var t1 = Spawn(sim, "uef_t1_tank", 1, 60, 64);
        var t3 = Spawn(sim, "uef_t3_assault_bot", 1, 64, 60);
        var acu = Spawn(sim, "uef_commander", 1, 66, 66);
        var exp = Spawn(sim, "uef_x_fatboy", 1, 70, 70);
        var own = Spawn(sim, "uef_t1_tank", 0, 62, 68);
        var far = Spawn(sim, "uef_t1_tank", 1, 100, 64);
        sim.Step();
        EmpStun.StunArea(w, 0, new Vector2(64, 64), 8, 2);
        Assert.True(Stunned(sim, t1));
        Assert.True(Stunned(sim, t3));
        Assert.False(Stunned(sim, acu), "commanders are immune");
        Assert.False(Stunned(sim, exp), "experimentals (tech 4) are immune");
        Assert.False(Stunned(sim, own), "no friendly stun");
        Assert.False(Stunned(sim, far));

        CybranTestWorld.Run(sim, 25);
        Assert.False(Stunned(sim, t1));
        EmpStun.StunArea(w, 0, new Vector2(64, 64), 8, 2, maxTech: 1);
        Assert.True(Stunned(sim, t1));
        Assert.False(Stunned(sim, t3), "StunMaxTech 1: a T3 unit is immune");
    }

    [Fact]
    public void EmpWeaponStunsUnitsAroundTheImpact()
    {
        var db = EmpDb;
        var sim = CybranTestWorld.Flat(db, 128);
        var w = sim.World;
        var emp = Spawn(sim, "uef_t2_heavy_tank", 0, 44, 64);
        var target = Spawn(sim, "uef_t1_tank", 1, 60, 64, heading: MathF.PI);
        var buddy = Spawn(sim, "uef_t1_tank", 1, 62, 66, heading: MathF.PI);
        var acu = Spawn(sim, "uef_commander", 1, 62, 61, heading: MathF.PI);
        sim.Step([new SetFireStateCommand(1, [target, buddy, acu], FireState.HoldFire)]);
        bool targetStunned = false, buddyStunned = false, acuStunned = false, selfStunned = false;
        for (int t = 0; t < 80; t++)
        {
            sim.Step();
            targetStunned |= w.Units.IsActive(target) && Stunned(sim, target);
            buddyStunned |= w.Units.IsActive(buddy) && Stunned(sim, buddy);
            acuStunned |= Stunned(sim, acu);
            selfStunned |= Stunned(sim, emp);
        }
        Assert.True(targetStunned, "the unit hit is stunned");
        Assert.True(buddyStunned, "a unit within StunRadius of the impact is stunned");
        Assert.False(acuStunned, "commanders are immune");
        Assert.False(selfStunned);
    }

    [Fact]
    public void SaveLoadMidStunContinuesIdentically()
    {
        var sim = CybranTestWorld.Flat(TestUtil.Db, 128);
        var w = sim.World;
        var a = Spawn(sim, "uef_t1_tank", 0, 40, 64);
        var b = Spawn(sim, "uef_t1_tank", 1, 54, 64, heading: MathF.PI);
        sim.Step([new IssueOrderCommand(0, [a], Order.MoveTo(new Vector2(40, 100)), false)]);
        EmpStun.Stun(w, a.Index, 3);
        EmpStun.Stun(w, b.Index, 1.5f);
        CybranTestWorld.Run(sim, 8);
        var saved = WorldState.Capture(sim);
        var copy = WorldState.Restore(saved, w.Blueprints, w.Terrain);
        Assert.True(EmpStun.IsStunned(copy.World, a.Index));
        Assert.Equal(EmpStun.SecondsLeft(w, a.Index), EmpStun.SecondsLeft(copy.World, a.Index));
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(copy)), "reload is not byte-identical");
        for (int t = 0; t < 60; t++)
        {
            sim.Step();
            copy.Step();
            Assert.Equal(sim.StateHash(), copy.StateHash());
            Assert.Equal(EmpStun.IsStunned(w, a.Index), EmpStun.IsStunned(copy.World, a.Index));
        }
        Assert.False(EmpStun.IsStunned(copy.World, a.Index));
        Assert.True(WorldState.Capture(sim).AsSpan().SequenceEqual(WorldState.Capture(copy)), "state differs after continuing");
    }

    [Fact]
    public void ReusedSlotIsNotStunned()
    {
        var sim = CybranTestWorld.Flat(TestUtil.Db, 128);
        var w = sim.World;
        var a = Spawn(sim, "uef_t1_tank", 0, 40, 64);
        sim.Step();
        EmpStun.Stun(w, a.Index, 5);
        w.KillUnit(a.Index, UnitHandle.None);
        CybranTestWorld.Run(sim, 2);
        var b = Spawn(sim, "uef_t1_tank", 0, 40, 64);
        sim.Step();
        Assert.False(EmpStun.IsStunned(w, b.Index));
    }

    // ------------------------------------------------------------------ seabed walkers

    private static BlueprintDb? _amphDb;

    /// <summary>Private copy: the UEF T2 heavy tank walks on the seabed (Movement.Layer Amphibious, like a Wagner).</summary>
    private static BlueprintDb AmphDb => _amphDb ??= BuildAmphDb();

    private static BlueprintDb BuildAmphDb()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        db["uef_t2_heavy_tank"].Movement.Layer = MotionLayer.Amphibious;
        return db;
    }

    /// <summary>128 map, land at height 10, a north-south channel x 40..88 (ramps 16 wide, bottom 2 = 4 deep at
    /// water level 6).</summary>
    private static Simulation ChannelWorld(BlueprintDb db)
    {
        const int size = 128;
        var heights = new float[(size + 1) * (size + 1)];
        for (int z = 0; z <= size; z++)
        for (int x = 0; x <= size; x++)
        {
            float h = x < 40 ? 10 : x < 56 ? 10 - (x - 40) * 0.5f : x <= 72 ? 2 : x < 88 ? 2 + (x - 72) * 0.5f : 10;
            heights[z * (size + 1) + x] = h;
        }
        var terrain = new Map.Terrain(size, size, heights, 6f, [], [new Vector2(20, 20), new Vector2(108, 108)], 1);
        var armies = new[] { new Army { Index = 0, Team = 0, Name = "A0" }, new Army { Index = 1, Team = 1, Name = "A1" } };
        return new Simulation(new SimWorld(db, terrain, armies, 1, unitCapacity: 512, projectileCapacity: 4096));
    }

    [Fact]
    public void SeabedWalkerCrossesChannelHiddenFromSurfaceGunsAndFiresOnLandOnly()
    {
        var sim = ChannelWorld(AmphDb);
        var w = sim.World;
        var u = w.Units;
        var walker = Spawn(sim, "uef_t2_heavy_tank", 0, 25, 64);
        var frigate = Spawn(sim, "uef_t1_frigate", 1, 64, 80);
        var target = Spawn(sim, "uef_t1_tank", 1, 110, 64, heading: MathF.PI);
        Assert.Equal(MotionLayer.Amphibious, w.BpOf(walker.Index).Movement.Layer);
        sim.Step([new IssueOrderCommand(0, [walker], Order.MoveTo(new Vector2(100, 64)), false)]);
        int underwaterTicks = 0;
        bool firedUnderwater = false, targetedUnderwater = false, firedOnLandAfter = false;
        float minElevation = float.MaxValue;
        for (int t = 0; t < 900 && u.IsActive(walker); t++)
        {
            sim.Step();
            int i = walker.Index;
            bool under = OrderSystem.LayerOf(w, i) == LayerMask.Sub;
            if (under)
            {
                underwaterTicks++;
                minElevation = MathF.Min(minElevation, u.Elevation[i]);
                Assert.True(Naval.Submarines.IsOnSeabed(w, i));
                firedUnderwater |= (u.Flags[i] & UnitFlags.Firing) != 0 || !CybranTestWorld.Target(sim, walker).IsNone;
                for (int wi = 0; wi < w.BpOf(frigate.Index).Weapons.Length; wi++)
                    targetedUnderwater |= u.IsActive(frigate) && CybranTestWorld.Target(sim, frigate, wi) == walker;
                targetedUnderwater |= u.IsActive(target) && CybranTestWorld.Target(sim, target) == walker;
            }
            else if (u.Pos[i].X > 88 && (u.Flags[i] & UnitFlags.Firing) != 0) firedOnLandAfter = true;
            if (firedOnLandAfter && u.Pos[i].X > 95) break;
        }
        Assert.True(u.IsActive(walker), "the walker survived");
        Assert.True(underwaterTicks > 30, $"walked under water ({underwaterTicks} ticks)");
        Assert.True(minElevation < w.Terrain.WaterLevel - 2f, $"on the seabed (elevation {minElevation})");
        Assert.False(firedUnderwater, "a non-submerged weapon doesn't fire under water");
        Assert.False(targetedUnderwater, "surface guns can't target a seabed walker");
        Assert.True(u.Pos[walker.Index].X > 88, "crossed the channel");
        Assert.True(firedOnLandAfter, "fires again on land");
    }

    [Fact]
    public void FiresSubmergedWeaponWorksOnTheSeabed()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        db["uef_t2_heavy_tank"].Movement.Layer = MotionLayer.Amphibious;
        var wb = db["uef_t2_heavy_tank"].Weapons[0];
        wb.FiresSubmerged = true;
        var sim = ChannelWorld(db);
        var w = sim.World;
        var walker = Spawn(sim, "uef_t2_heavy_tank", 0, 64, 64); // channel bottom
        var frigate = Spawn(sim, "uef_t1_frigate", 1, 64, 76);
        sim.Step([new SetFireStateCommand(1, [frigate], FireState.HoldFire)]);
        Assert.True(Naval.Submarines.IsOnSeabed(w, walker.Index));
        bool fired = false;
        for (int t = 0; t < 60 && !fired; t++)
        {
            sim.Step();
            fired = (w.Units.Flags[walker.Index] & UnitFlags.Firing) != 0;
        }
        Assert.True(fired, "a FiresSubmerged weapon fires from the seabed");
    }
}
