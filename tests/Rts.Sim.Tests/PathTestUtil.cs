using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Builds small hand-made worlds for pathfinding/movement tests.</summary>
public static class PathTestUtil
{
    private static BlueprintDb? _navalDb;

    /// <summary>Repository blueprints plus a test frigate (naval, Water layer, radius 0.8 -> clearance 2).</summary>
    public static BlueprintDb NavalDb
    {
        get
        {
            if (_navalDb != null) return _navalDb;
            var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
            db.Add(new UnitBlueprint
            {
                Id = "test_frigate",
                Name = "Test Frigate",
                Categories = ["Mobile", "Naval"],
                MaxHealth = 1000,
                Radius = 0.8f,
                Movement = new MovementBlueprint { Layer = MotionLayer.Water, MaxSpeed = 3f, Acceleration = 2f, TurnRate = 70f },
            });
            db.FinalizeDb();
            return _navalDb = db;
        }
    }

    /// <summary>A world of n x n cells whose vertex heights come from height(x, z).</summary>
    public static Simulation World(int n, Func<int, int, float> height, float waterLevel = 2f, BlueprintDb? db = null, ulong seed = 1)
    {
        var h = new float[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
            h[z * (n + 1) + x] = height(x, z);
        var terrain = new Terrain(n, n, h, waterLevel, [], [new Vector2(8, 8), new Vector2(n - 8, n - 8)], 1);
        var armies = new[] { new Army { Name = "A", Team = 0 }, new Army { Name = "B", Team = 1 } };
        var world = new SimWorld(db ?? TestUtil.Db, terrain, armies, seed, unitCapacity: 4096, projectileCapacity: 1024);
        return new Simulation(world);
    }

    public static UnitHandle Spawn(Simulation sim, string bp, int army, Vector2 pos, float heading = 0) =>
        sim.World.SpawnUnit(sim.World.Blueprints.IndexOf(bp), army, pos, heading, complete: true);

    public static int CellX(Vector2 p) => (int)MathF.Floor(p.X);
    public static int CellZ(Vector2 p) => (int)MathF.Floor(p.Y);
}
