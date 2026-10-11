using System.Numerics;
using System.Text.Json;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Client-facing data used by audio/UI: weapon index on combat effects, kills in the snapshot.</summary>
public class SnapshotFxTests
{
    /// <summary>The repository blueprints plus a tank variant with three weapons (AA-only, cannon, beam).</summary>
    private static BlueprintDb DbWithMultiWeaponTank()
    {
        var db = new BlueprintDb();
        var files = Directory.EnumerateFiles(DataPaths.UnitBlueprintDir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        foreach (var f in files) db.Add(JsonSerializer.Deserialize<UnitBlueprint>(File.ReadAllText(f), BlueprintDb.JsonOptions)!);
        var t = JsonSerializer.Deserialize<UnitBlueprint>(File.ReadAllText(files.First(f => f.EndsWith("uef_t1_tank.json"))), BlueprintDb.JsonOptions)!;
        t.Id = "test_three_guns";
        t.Weapons =
        [
            new WeaponBlueprint { Name = "AA only", Range = 18, Damage = 5, TargetLayers = ["Air"], ProjectileVisual = "missile" },
            new WeaponBlueprint { Name = "Cannon", Range = 18, Damage = 18, MuzzleVelocity = 30 },
            new WeaponBlueprint { Name = "Beam", Range = 18, Damage = 4, MuzzleVelocity = 0, ProjectileVisual = "laser" },
        ];
        db.Add(t);
        db.FinalizeDb();
        return db;
    }

    [Fact]
    public void CombatEffectsCarryTheFiringWeaponIndex()
    {
        var db = DbWithMultiWeaponTank();
        const int size = 128;
        var heights = new float[(size + 1) * (size + 1)];
        Array.Fill(heights, 10f);
        var terrain = new Terrain(size, size, heights, 0f, [], [new Vector2(20, 20), new Vector2(size - 20, size - 20)], 1);
        var armies = new[] { new Army { Index = 0, Team = 0, Name = "A" }, new Army { Index = 1, Team = 1, Name = "B" } };
        var sim = new Simulation(new SimWorld(db, terrain, armies, 1, unitCapacity: 256, projectileCapacity: 1024));
        int gunBp = db.IndexOf("test_three_guns");
        sim.World.SpawnUnit(gunBp, 0, new Vector2(50, 64), 0);
        sim.World.SpawnUnit(db.IndexOf("uef_t1_tank"), 1, new Vector2(62, 64), MathF.PI);

        var seen = new HashSet<(EffectKind, int)>();
        for (int t = 0; t < 120; t++)
        {
            sim.Step();
            foreach (var e in sim.World.Events.Effects)
                if (e.BlueprintIndex == gunBp && e.Kind is EffectKind.MuzzleFlash or EffectKind.Impact or EffectKind.Explosion)
                    seen.Add((e.Kind, e.WeaponIndex));
            sim.World.Events.Effects.Clear();
        }
        Assert.Contains((EffectKind.MuzzleFlash, 1), seen);
        Assert.Contains((EffectKind.MuzzleFlash, 2), seen);
        Assert.Contains((EffectKind.Impact, 2), seen); // beams hit instantly
        Assert.Contains((EffectKind.Impact, 1), seen); // cannon shells hit later
        Assert.DoesNotContain(seen, s => s.Item2 == 0); // the AA-only weapon has no ground targets
    }

    [Fact]
    public void SnapshotReportsKills()
    {
        var sim = CombatWorld.Flat();
        var a = CombatWorld.Spawn(sim, "uef_t1_tank", 0, 50, 64, heading: 0);
        var b = CombatWorld.Spawn(sim, "uef_t1_tank", 1, 64, 64, heading: 0);
        for (int t = 0; t < 600 && CombatWorld.Alive(sim, b); t++) sim.Step();
        Assert.False(CombatWorld.Alive(sim, b));
        var snap = new SimSnapshot();
        sim.WriteSnapshot(snap);
        Assert.True(snap.TryGetUnit(a.Index, a.Generation, out var v));
        Assert.Equal(1, v.Kills);
    }
}
