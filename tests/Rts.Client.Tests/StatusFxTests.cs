using System.Numerics;
using Rts.Client.Rendering;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.Tests;

/// <summary>
/// Stealth/cloak/EMP status visuals (<see cref="StatusFx"/>, drawn by UnitRenderer.Status.cs): which effects a status
/// gets, the stealth field radius from the blueprint (enhancement variants included) and the deterministic spark
/// flicker. GPU-free.
/// </summary>
public class StatusFxTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void VisualsFollowTheStatusFlags()
    {
        Assert.Equal(StatusVisual.None, StatusFx.VisualsFor(UnitStatus.None, false));
        // Radar/sonar stealth alone: no visual.
        Assert.Equal(StatusVisual.None, StatusFx.VisualsFor(UnitStatus.RadarStealth | UnitStatus.SonarStealth, false));
        Assert.Equal(StatusVisual.Cloak, StatusFx.VisualsFor(UnitStatus.Cloaked | UnitStatus.RadarStealth | UnitStatus.SonarStealth, false));
        Assert.Equal(StatusVisual.Stun, StatusFx.VisualsFor(UnitStatus.Stunned, false));
        Assert.Equal(StatusVisual.Field, StatusFx.VisualsFor(UnitStatus.StealthField | UnitStatus.RadarStealth, false));
        Assert.Equal(StatusVisual.Cloak | StatusVisual.Stun | StatusVisual.Field,
            StatusFx.VisualsFor(UnitStatus.Cloaked | UnitStatus.Stunned | UnitStatus.StealthField, false));
        // Under construction: drawn as a hologram, no status visuals.
        Assert.Equal(StatusVisual.None, StatusFx.VisualsFor(UnitStatus.Cloaked | UnitStatus.Stunned, true));
    }

    [Fact]
    public void FieldRadiusComesFromTheBlueprintOnlyWhileTheFieldIsActive()
    {
        var deceiver = Db["cybran_t2_mobile_stealth"];
        var generator = Db["cybran_t2_stealth_field_generator"];
        Assert.True(deceiver.Intel.StealthFieldRadius > 0);
        Assert.Equal(deceiver.Intel.StealthFieldRadius, StatusFx.FieldRadius(deceiver, UnitStatus.StealthField | UnitStatus.RadarStealth));
        Assert.Equal(generator.Intel.StealthFieldRadius, StatusFx.FieldRadius(generator, UnitStatus.StealthField));
        Assert.True(generator.Intel.StealthFieldRadius > deceiver.Intel.StealthFieldRadius);
        // Paused / energy-stalled generator: the flag is off, no ring.
        Assert.Equal(0f, StatusFx.FieldRadius(generator, UnitStatus.None));
        Assert.Equal(0f, StatusFx.FieldRadius(Db["cybran_t1_artillery"], UnitStatus.StealthField));
    }

    [Fact]
    public void EnhancementVariantsCarryTheirIntelIntoTheSnapshotBlueprint()
    {
        // UnitView.Bp of an enhanced commander is the variant, so the renderer reads the enhancement's effects there.
        Assert.True(Db.TryGet("cybran_commander+CloakingGenerator", out var cloaked));
        Assert.True(cloaked.IsVariant);
        Assert.True(cloaked.Intel.Cloak);
        Assert.False(Db["cybran_commander"].Intel.Cloak);
        // A variant whose enhancement sets a field radius reports it (none in data today: check the rule on any variant).
        for (int i = 0; i < Db.Count; i++)
        {
            var bp = Db[i];
            if (!bp.IsVariant) continue;
            Assert.Equal(bp.Intel.StealthFieldRadius, StatusFx.FieldRadius(bp, UnitStatus.StealthField));
        }
    }

    [Fact]
    public void SparkFlickerIsStablePerUnitAndBucket()
    {
        Assert.Equal(StatusFx.TimeBucket(10.01f), StatusFx.TimeBucket(10.02f));
        Assert.NotEqual(StatusFx.TimeBucket(10.0f), StatusFx.TimeBucket(10.0f + 1.5f / StatusFx.BucketsPerSecond));
        Assert.Equal(StatusFx.FlickerSeed(42, 7, 1), StatusFx.FlickerSeed(42, 7, 1));
        Assert.NotEqual(StatusFx.FlickerSeed(42, 7, 1), StatusFx.FlickerSeed(43, 7, 1));
        Assert.NotEqual(StatusFx.FlickerSeed(42, 7, 1), StatusFx.FlickerSeed(42, 8, 1));
        Assert.NotEqual(StatusFx.FlickerSeed(42, 7, 1), StatusFx.FlickerSeed(42, 7, 2));

        Span<Vector3> a = stackalloc Vector3[StatusFx.BoltPoints];
        Span<Vector3> b = stackalloc Vector3[StatusFx.BoltPoints];
        var c = new Vector3(100, 5, 80);
        uint seed = StatusFx.FlickerSeed(42, 7, 0);
        StatusFx.Bolt(seed, c, 2f, a);
        StatusFx.Bolt(seed, c, 2f, b);
        Assert.True(a.SequenceEqual(b));
        // Endpoints sit on the (bowed) sphere around the unit, kinks stay near it; sparks favour the top half.
        Assert.InRange(Vector3.Distance(a[0], c), 1.99f, 2.01f);
        Assert.InRange(Vector3.Distance(a[^1], c), 1.99f, 2.01f);
        foreach (var p in a) Assert.InRange(Vector3.Distance(p, c), 0.5f, 4f);
        Assert.True(a[0].Y > c.Y && a[^1].Y > c.Y);
        StatusFx.Bolt(StatusFx.FlickerSeed(42, 8, 0), c, 2f, b);
        Assert.False(a.SequenceEqual(b));
    }

    [Fact]
    public void FirstBoltAlwaysShowsAndOthersBlink()
    {
        int shown = 0, total = 0;
        for (int bucket = 0; bucket < 400; bucket++)
        {
            Assert.True(StatusFx.BoltVisible(StatusFx.FlickerSeed(5, bucket, 0), 0));
            for (int b = 1; b < StatusFx.BoltsPerUnit; b++)
            {
                total++;
                if (StatusFx.BoltVisible(StatusFx.FlickerSeed(5, bucket, b), b)) shown++;
            }
            Assert.InRange(StatusFx.BoltIntensity(StatusFx.FlickerSeed(5, bucket, 0)), 0.45f, 1f);
        }
        Assert.InRange(shown, total / 3, total * 6 / 7); // they blink: neither always on nor always off
    }
}
