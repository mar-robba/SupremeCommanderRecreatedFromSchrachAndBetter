using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Experimental;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Low-arc ballistic weapons with <see cref="WeaponBlueprint.HighArcWhenBlocked"/> (the Mavor) lob the shell
/// over a ridge that blocks the low arc; the same weapon without the flag keeps the low arc and hits the ridge.</summary>
public class MavorHighArcTests
{
    private const float RidgeX0 = 95, RidgeX1 = 110, Ground = 10, RidgeTop = 50;

    /// <summary>Flat 512 map at height 10 with a north-south ridge (height 50) at x 95..110.</summary>
    private static Simulation RidgeWorld(BlueprintDb db, bool ridge)
    {
        const int size = 512;
        var heights = new float[(size + 1) * (size + 1)];
        for (int z = 0; z <= size; z++)
        for (int x = 0; x <= size; x++)
            heights[z * (size + 1) + x] = ridge && x >= RidgeX0 && x <= RidgeX1 ? RidgeTop : Ground;
        var terrain = new Terrain(size, size, heights, 0f, [], [new Vector2(20, 20), new Vector2(size - 20, size - 20)], 1);
        var arr = new[] { new Army { Index = 0, Team = 0, Name = "A0" }, new Army { Index = 1, Team = 1, Name = "A1" } };
        return new Simulation(new SimWorld(db, terrain, arr, 1, unitCapacity: 256, projectileCapacity: 1024));
    }

    /// <summary>A private db copy: the Mavor without spread (so a shell that gets there hits), flag on or off.</summary>
    private static BlueprintDb MavorDb(bool flag)
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var wbp = db["uef_x_mavor"].Weapons[0];
        wbp.Spread = 0;
        wbp.HighArcWhenBlocked = flag;
        return db;
    }

    private sealed record Shot(Vector3 Launch, Vector2 Impact, bool TargetAlive, float TargetHealthFraction);

    /// <summary>Mavor at (60, 256), an enemy power generator at (340, 256) with an omni sensor for intel; runs until
    /// the first shell has landed.</summary>
    private static Shot FireOnce(bool flag, bool ridge)
    {
        var db = MavorDb(flag);
        var sim = RidgeWorld(db, ridge);
        var w = sim.World;
        var u = w.Units;
        var mavor = w.SpawnUnit(db.IndexOf("uef_x_mavor"), 0, new Vector2(60, 256), 0, complete: true);
        var target = w.SpawnUnit(db.IndexOf("uef_t1_power_generator"), 1, new Vector2(340, 256), 0, complete: true);
        w.SpawnUnit(db.IndexOf("uef_t3_omni_sensor"), 0, new Vector2(330, 280), 0, complete: true);
        int mavorBp = db.IndexOf("uef_x_mavor");
        float cost = db["uef_x_mavor"].Weapons[0].EnergyPerShot;

        int Shell()
        {
            var pr = w.Projectiles;
            for (int p = 0; p < pr.Capacity; p++) if (pr.Alive[p] && pr.SourceBp[p] == mavorBp) return p;
            return -1;
        }

        int shell = -1;
        Vector3 launch = default;
        for (int t = 0; t < 400 && shell < 0; t++)
        {
            if (ExperimentalState.TryGet(w, out var es)) es.Charge[mavor.Index * UnitStore.MaxWeapons] = cost; // charged
            sim.Step();
            shell = Shell();
            if (shell >= 0) launch = w.Projectiles.Vel[shell];
        }
        Assert.True(shell >= 0, "the Mavor fired");
        Vector3 last = w.Projectiles.Pos[shell];
        for (int t = 0; t < 600; t++)
        {
            shell = Shell();
            if (shell < 0) break;
            last = w.Projectiles.Pos[shell];
            sim.Step();
        }
        Assert.True(Shell() < 0, "the shell landed");
        bool alive = u.IsActive(target);
        return new Shot(launch, new Vector2(last.X, last.Z), alive, alive ? u.Health[target.Index] / db["uef_t1_power_generator"].MaxHealth : 0);
    }

    private static float Pitch(Vector3 v) => MathF.Atan2(v.Y, new Vector2(v.X, v.Z).Length());

    [Fact]
    public void ArcClearanceSeesTheRidge()
    {
        var sim = RidgeWorld(MavorDb(true), ridge: true);
        var from = new Vector3(60, 16, 256);
        var to = new Vector3(340, 11, 256);
        Assert.True(Ballistics.SolveArc(from, to, 125, Ballistics.Gravity, false, out var low, out float lt));
        Assert.True(Ballistics.SolveArc(from, to, 125, Ballistics.Gravity, true, out var high, out float ht));
        Assert.False(LineOfFire.IsArcClear(sim.World.Terrain, from, low, lt), "the low arc runs into the ridge");
        Assert.True(LineOfFire.IsArcClear(sim.World.Terrain, from, high, ht), "the high arc clears it");
        var flat = RidgeWorld(MavorDb(true), ridge: false);
        Assert.True(LineOfFire.IsArcClear(flat.World.Terrain, from, low, lt), "no ridge: the low arc is clear");
    }

    [Fact]
    public void MavorLobsOverRidgeAndHits()
    {
        var shot = FireOnce(flag: true, ridge: true);
        Assert.True(Pitch(shot.Launch) > MathF.PI / 4, $"high arc expected, pitch {Pitch(shot.Launch)}");
        Assert.True(Vector2.Distance(shot.Impact, new Vector2(340, 256)) < 8f, $"impact {shot.Impact} not at the target");
        Assert.False(shot.TargetAlive, "the target was destroyed");
    }

    [Fact]
    public void MavorKeepsLowArcWhenClear()
    {
        var shot = FireOnce(flag: true, ridge: false);
        Assert.True(Pitch(shot.Launch) < MathF.PI / 8, $"low arc expected on open ground, pitch {Pitch(shot.Launch)}");
        Assert.False(shot.TargetAlive, "the target was destroyed");
    }

    [Fact]
    public void WithoutFlagLowArcShellHitsTheRidge()
    {
        var shot = FireOnce(flag: false, ridge: true);
        Assert.True(Pitch(shot.Launch) < MathF.PI / 8, $"low arc expected, pitch {Pitch(shot.Launch)}");
        Assert.True(shot.Impact.X < RidgeX1 + 2f, $"the low-arc shell should hit the ridge, impact {shot.Impact}");
        Assert.True(shot.TargetAlive && shot.TargetHealthFraction > 0.99f, "the target behind the ridge is untouched");
    }
}
