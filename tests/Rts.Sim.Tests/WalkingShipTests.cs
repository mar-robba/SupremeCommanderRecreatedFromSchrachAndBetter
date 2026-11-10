using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.Transport;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>The Cybran Salem destroyer (Naval + Movement.Layer Hover): a ship that walks on land.</summary>
public class WalkingShipTests
{
    private const string Salem = "cybran_t2_destroyer";
    private const float Shore = 60f;

    /// <summary>
    /// 128x128: sea (floor 2, water 6 = 4 deep) for x ≤ 52, a gentle beach (slope 0.5) up to x = 64, flat land at 8
    /// beyond. The shoreline is x = 60.
    /// </summary>
    private static Simulation Beach(int armies = 2)
    {
        const int size = 128;
        var heights = new float[(size + 1) * (size + 1)];
        for (int z = 0; z <= size; z++)
        for (int x = 0; x <= size; x++)
            heights[z * (size + 1) + x] = Math.Clamp(2f + (x - 52) * 0.5f, 2f, 8f);
        var terrain = new Terrain(size, size, heights, NavalWorld.Water, [], [new Vector2(20, 20), new Vector2(108, 108)], 1);
        var arr = new Army[armies];
        for (int a = 0; a < armies; a++) arr[a] = new Army { Index = a, Team = a, Name = $"A{a}" };
        return new Simulation(new SimWorld(TestUtil.Db, terrain, arr, 1, unitCapacity: 2048, projectileCapacity: 8192));
    }

    private static bool TorpedoInFlight(SimWorld w, int sourceBp)
    {
        for (int p = 0; p < w.Projectiles.Capacity; p++)
            if (w.Projectiles.Alive[p] && w.Projectiles.SourceBp[p] == sourceBp && (w.Projectiles.Flags[p] & ProjectileFlags.Torpedo) != 0)
                return true;
        return false;
    }

    [Fact]
    public void SalemBlueprintIsAWalkingShip()
    {
        var db = TestUtil.Db;
        var salem = db[Salem];
        Assert.True(WalkingShips.IsWalkingShip(salem));
        Assert.True(NavalMotion.IsNaval(salem));
        Assert.Equal(MotionLayer.Hover, salem.Movement.Layer);
        Assert.Equal(0.35f, salem.Movement.LandSpeedFactor, 3);
        Assert.Equal(0, salem.CargoSize); // never transportable
        Assert.False(Submarines.CanDive(salem));

        // The UEF destroyer stays a plain ship; ordinary hover units are not walking ships and keep full speed on land.
        var valiant = db["uef_t2_destroyer"];
        Assert.Equal(MotionLayer.Water, valiant.Movement.Layer);
        Assert.False(WalkingShips.IsWalkingShip(valiant));
        Assert.Equal(1f, valiant.Movement.LandSpeedFactor);
        foreach (var bp in db.All)
            if (bp.Movement.Layer == MotionLayer.Hover && !bp.HasAny(UnitCategory.Naval))
            {
                Assert.False(WalkingShips.IsWalkingShip(bp));
                Assert.Equal(1f, bp.Movement.LandSpeedFactor);
            }
    }

    [Fact]
    public void SalemWalksAshoreAtLandSpeed()
    {
        var sim = Beach();
        var w = sim.World;
        var salem = NavalWorld.Spawn(sim, Salem, 0, 20, 64);
        float max = w.BpOf(salem.Index).Movement.MaxSpeed;
        sim.Step();
        Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, salem.Index));
        Assert.False(WalkingShips.OnLand(w, salem.Index));
        Assert.True(WalkingShips.AtSea(w, salem.Index, w.BpOf(salem.Index)));

        var goal = new Vector2(100, 64);
        sim.Step([new IssueOrderCommand(0, [salem], Order.MoveTo(goal), false)]);
        float seaSpeed = 0, landMax = 0, landMin = float.MaxValue;
        int arrivedAt = -1;
        for (int t = 0; t < 900 && arrivedAt < 0; t++)
        {
            var before = w.Units.Pos[salem.Index];
            sim.Step();
            var after = w.Units.Pos[salem.Index];
            float speed = Vector2.Distance(before, after) / SimWorld.Dt;
            if (after.X < 50) seaSpeed = MathF.Max(seaSpeed, speed);
            else if (before.X > 68 && after.X < 90) { landMax = MathF.Max(landMax, speed); landMin = MathF.Min(landMin, speed); }
            if (Vector2.Distance(after, goal) < 3f) arrivedAt = t;
        }
        Assert.True(arrivedAt >= 0, $"the Salem did not reach the dry goal, stopped at {w.Units.Pos[salem.Index]}");
        Assert.InRange(seaSpeed, max * 0.95f, max * 1.02f);
        Assert.InRange(landMax, max * 0.35f * 0.9f, max * 0.35f * 1.05f);
        Assert.InRange(landMin, max * 0.35f * 0.9f, max * 0.35f * 1.05f);

        Assert.True(WalkingShips.OnLand(w, salem.Index));
        Assert.False(WalkingShips.AtSea(w, salem.Index, w.BpOf(salem.Index)));
        Assert.Equal(LayerMask.Land, OrderSystem.LayerOf(w, salem.Index));
        Assert.Equal(w.Terrain.GetHeight(w.Units.Pos[salem.Index]), w.Units.Elevation[salem.Index], 2);
    }

    [Fact]
    public void OnLandGunsFightLandUnitsAndTorpedoesAreHeld()
    {
        var sim = Beach();
        var w = sim.World;
        var salem = NavalWorld.Spawn(sim, Salem, 0, 70, 64);
        int salemBp = w.Units.Bp[salem.Index];
        // An enemy sub 30 away in the sea: in torpedo range (38) of the Salem, and the Salem in range of nothing it has.
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 1, 40, 64);
        var tanks = new[]
        {
            NavalWorld.Spawn(sim, "uef_t1_tank", 1, 84, 58), NavalWorld.Spawn(sim, "uef_t1_tank", 1, 85, 64),
            NavalWorld.Spawn(sim, "uef_t1_tank", 1, 84, 70),
        };
        sim.Step();
        Assert.Equal(LayerMask.Land, OrderSystem.LayerOf(w, salem.Index));
        float full = CombatWorld.Health(sim, salem);

        bool tankAimed = false;
        for (int t = 0; t < 300; t++)
        {
            sim.Step();
            Assert.False(TorpedoInFlight(w, salemBp), "the Salem fired a torpedo from dry ground");
            Assert.True(CombatWorld.Weapon(sim, salem, 1).Target.IsNone, "torpedo tube acquired a target on dry ground");
            Assert.True(CombatWorld.Weapon(sim, sub, 0).Target.IsNone, "a torpedo launcher targeted a ship standing on land");
            foreach (var tank in tanks)
                if (CombatWorld.Alive(sim, tank) && CombatWorld.Weapon(sim, tank, 0).Target == salem) tankAimed = true;
        }
        Assert.True(tankAimed, "land guns should target the Salem on land");
        Assert.True(CombatWorld.Health(sim, salem) < full, "land guns should damage the Salem on land");
        Assert.All(tanks, tank => Assert.False(CombatWorld.Alive(sim, tank), "the Salem's cannons should kill the tanks"));
        Assert.True(CombatWorld.Alive(sim, sub));
        Assert.Equal(TestUtil.Db["uef_t1_sub"].MaxHealth, CombatWorld.Health(sim, sub));

        // Back in the water it is a Water target again and the torpedoes work.
        sim.Step([new IssueOrderCommand(0, [salem], Order.MoveTo(new Vector2(52, 64)), false)]);
        bool torpedo = false, targetedBySub = false;
        for (int t = 0; t < 600 && CombatWorld.Alive(sim, sub); t++)
        {
            sim.Step();
            torpedo |= TorpedoInFlight(w, salemBp);
            if (CombatWorld.Alive(sim, sub) && CombatWorld.Weapon(sim, sub, 0).Target == salem)
            {
                targetedBySub = true;
                Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, salem.Index));
            }
            if (torpedo) Assert.False(WalkingShips.OnLand(w, salem.Index) && w.Units.Pos[salem.Index].X > Shore + 2f);
        }
        Assert.True(torpedo, "torpedoes should fire again once the Salem is back in the water");
        Assert.True(targetedBySub, "a sub should torpedo the Salem once it is back in the water");
        Assert.False(CombatWorld.Alive(sim, sub), "the Salem should sink the sub with torpedoes");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AntiTorpedoZapperWorksOnlyInTheWater(bool onLand)
    {
        var sim = Beach();
        var w = sim.World;
        // A friendly frigate in the shallows is torpedoed by an enemy sub; the Salem is ~6 away, on the beach or beside it.
        // (Hulls parallel so that the hull separation does not push the two apart.)
        float heading = onLand ? MathF.PI / 2 : 0;
        var frigate = NavalWorld.Spawn(sim, "uef_t1_frigate", 0, 55, 64, heading);
        var salem = onLand ? NavalWorld.Spawn(sim, Salem, 0, 60.5f, 64, heading) : NavalWorld.Spawn(sim, Salem, 0, 55, 70, heading);
        var sub = NavalWorld.Spawn(sim, "uef_t1_sub", 1, 32, 60);
        sim.Step([new SetFireStateCommand(0, [salem, frigate], FireState.HoldFire)]);
        Assert.Equal(onLand, WalkingShips.OnLand(w, salem.Index));

        bool zapped = false, torpedoNear = false;
        int subBp = w.Units.Bp[sub.Index];
        for (int t = 0; t < 400; t++)
        {
            sim.Step();
            zapped |= CombatWorld.Weapon(sim, salem, 2).Cooldown > 0;
            for (int p = 0; p < w.Projectiles.Capacity; p++)
                if (w.Projectiles.Alive[p] && w.Projectiles.SourceBp[p] == subBp
                    && Vector2.Distance(new Vector2(w.Projectiles.Pos[p].X, w.Projectiles.Pos[p].Z), w.Units.Pos[salem.Index]) < 8f)
                    torpedoNear = true;
        }
        // In the water the zapper kills torpedoes at its range (10); on the beach they run past it into the frigate.
        Assert.Equal(onLand, torpedoNear);
        Assert.Equal(!onLand, zapped);
        if (onLand) Assert.True(CombatWorld.Health(sim, frigate) < TestUtil.Db["uef_t1_frigate"].MaxHealth, "the frigate should be torpedoed");
    }

    [Fact]
    public void PlainDestroyerCannotLeaveTheWaterAndSalemIsNotTransportable()
    {
        var sim = Beach();
        var w = sim.World;
        var valiant = NavalWorld.Spawn(sim, "uef_t2_destroyer", 0, 30, 40);
        var salem = NavalWorld.Spawn(sim, Salem, 0, 75, 90);
        var transport = NavalWorld.Spawn(sim, "uef_t2_transport", 0, 75, 100);
        TestUtil.Run(sim, 600,
            new IssueOrderCommand(0, [valiant], Order.MoveTo(new Vector2(100, 40)), false),
            new IssueOrderCommand(0, [transport], TransportOrders.Load(salem), false));
        Assert.True(w.Terrain.IsUnderwater(w.Units.Pos[valiant.Index]), "a Water-layer ship left the water");
        Assert.True(w.Units.Pos[valiant.Index].X < Shore);
        Assert.Equal(LayerMask.Water, OrderSystem.LayerOf(w, valiant.Index));
        Assert.True((w.Units.Flags[salem.Index] & UnitFlags.Carried) == 0, "a transport picked up the Salem");
        Assert.True(WalkingShips.OnLand(w, salem.Index));
    }

    [Fact]
    public void OnLandSalemAndLandUnitsDoNotOverlap()
    {
        var sim = Beach(armies: 1);
        var w = sim.World;
        var salem = NavalWorld.Spawn(sim, Salem, 0, 80, 64);
        var tanks = new UnitHandle[4];
        for (int k = 0; k < tanks.Length; k++) tanks[k] = NavalWorld.Spawn(sim, "uef_t1_tank", 0, 96, 58 + 4 * k);
        var parked = NavalWorld.Spawn(sim, "uef_t1_tank", 0, 100, 100);
        float salemR = w.BpOf(salem.Index).Radius, tankR = w.BpOf(parked.Index).Radius;

        // Tanks drive onto the standing Salem.
        TestUtil.Run(sim, 250, new IssueOrderCommand(0, tanks, Order.MoveTo(new Vector2(80, 64)), false));
        foreach (var tank in tanks)
        {
            float d = Vector2.Distance(w.Units.Pos[tank.Index], w.Units.Pos[salem.Index]);
            Assert.True(d > (salemR + tankR) * 0.8f, $"a tank stands inside the Salem (distance {d:F2})");
        }

        // The Salem walks onto a parked tank.
        TestUtil.Run(sim, 400, new IssueOrderCommand(0, [salem], Order.MoveTo(new Vector2(100, 100)), false));
        float dp = Vector2.Distance(w.Units.Pos[parked.Index], w.Units.Pos[salem.Index]);
        Assert.True(dp > (salemR + tankR) * 0.8f, $"the Salem stands on a parked tank (distance {dp:F2})");
        Assert.True(Vector2.Distance(w.Units.Pos[salem.Index], new Vector2(100, 100)) < 8f, "the Salem should get near its goal");
    }
}
