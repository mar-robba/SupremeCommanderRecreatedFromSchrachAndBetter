using System.Numerics;
using Rts.Client.Rendering;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>(9b) Death sequences: the kind chosen per unit, timing, the aircraft fall and ship sink paths, when the
/// wreck shows, the shader packing and the ACU warp-in rule (<see cref="DeathAnim"/>, <see cref="DeathAnimator"/>).</summary>
public class DeathAnimTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    private static UnitBlueprint Bp(string id) => Db.TryGet(id, out var bp) ? bp : throw new Exception("no blueprint " + id);
    private static UnitBlueprint First(Func<UnitBlueprint, bool> f) => Enumerable.Range(0, Db.Count).Select(i => Db[i]).First(f);

    [Fact]
    public void KindIsChosenPerUnitKind()
    {
        var air = First(b => b.Movement.Layer == MotionLayer.Air && !b.Has(UnitCategory.Experimental));
        Assert.Equal(DeathKind.AirCrash, DeathAnim.Choose(air, 12f, 0f, 0f));
        Assert.Equal(DeathKind.None, DeathAnim.Choose(air, 0.5f, 0f, 0f)); // landed / taking off
        var ship = First(b => b.Movement.Layer == MotionLayer.Water && !b.IsStructure);
        Assert.Equal(DeathKind.ShipSink, DeathAnim.Choose(ship, 0f, 5f, 0f));
        var sub = First(b => b.Movement.Layer == MotionLayer.Sub);
        Assert.Equal(DeathKind.None, DeathAnim.Choose(sub, 0f, 5f, 2f)); // submerged: the regular death
        Assert.Equal(DeathKind.ShipSink, DeathAnim.Choose(sub, 0f, 5f, 0f)); // surfaced
        var structure = First(b => b.IsStructure && !b.Has(UnitCategory.Experimental));
        Assert.Equal(DeathKind.Collapse, DeathAnim.Choose(structure, 0f, 0f, 0f));
        var big = First(b => b.Has(UnitCategory.Experimental) && b.Movement.Layer == MotionLayer.Land && !b.IsStructure);
        Assert.Equal(DeathKind.BigFall, DeathAnim.Choose(big, 0f, 0f, 0f));
        var acu = First(b => b.Has(UnitCategory.Commander) && b.DeathBlast != null);
        Assert.Equal(DeathKind.CommanderFall, DeathAnim.Choose(acu, 0f, 0f, 0f));
        var tank = First(b => b.Movement.Layer == MotionLayer.Land && !b.IsStructure && !b.Has(UnitCategory.Experimental)
                              && !b.Has(UnitCategory.Commander) && b.DeathBlast == null);
        Assert.Equal(DeathKind.None, DeathAnim.Choose(tank, 0f, 0f, 0f));
        Assert.False(DeathAnim.MayAnimate(tank));
        Assert.True(DeathAnim.MayAnimate(air) && DeathAnim.MayAnimate(ship) && DeathAnim.MayAnimate(structure));
    }

    private static DeathPlan AirPlan(float y, float water = 0f, bool huge = false, float seed = 0.3f) => DeathAnim.Plan(new DeathStart
    {
        Kind = DeathKind.AirCrash, Pos = new Vector3(10, y, 20), End = new Vector3(11, y, 20), EndTime = 0.1f,
        Vel = new Vector3(15, 0, 0), Heading = 0, Height = 1, Radius = 1, Floor = 2f, Water = water, Seed = seed, Huge = huge,
    });

    [Fact]
    public void AircraftSpiralsDownAndEndsOnItsWreck()
    {
        var p = AirPlan(20f);
        Assert.InRange(p.Duration, 1.5f, 3.2f);
        Assert.Equal(2f, p.CrashY);
        var start = DeathAnim.Pose(in p, 0f);
        Assert.Equal(20f, start.Pos.Y, 3);
        float prevY = start.Pos.Y;
        for (float t = 0.1f; t < p.Duration; t += 0.1f)
        {
            var q = DeathAnim.Pose(in p, t);
            Assert.True(q.Pos.Y < prevY, $"falls at t={t}");
            Assert.True(q.Pos.Y >= p.CrashY - 1e-3f);
            Assert.Equal(1f, q.Visible);
            prevY = q.Pos.Y;
        }
        var end = DeathAnim.AirPose(in p, p.Duration);
        Assert.True(Vector3.Distance(end.Pos, new Vector3(11, 2f, 20)) < 1e-3f, $"ends on the wreck: {end.Pos}");
        Assert.Equal(0f, DeathAnim.Pose(in p, p.Duration + 0.01f).Visible);
        // The loop goes out sideways (a spiral, not a straight drop).
        float maxSide = 0;
        for (float t = 0; t < p.Duration; t += 0.05f) maxSide = MathF.Max(maxSide, MathF.Abs(DeathAnim.Pose(in p, t).Pos.Z - 20f));
        Assert.True(maxSide > 1f, $"spirals: {maxSide}");
        // Over water it crashes at the water surface; huge aircraft fall for longer.
        Assert.Equal(5f, AirPlan(20f, water: 5f).CrashY);
        Assert.True(AirPlan(20f, huge: true).Duration > p.Duration);
    }

    private static DeathPlan ShipPlan(float floor, float h = 2f) => DeathAnim.Plan(new DeathStart
    {
        Kind = DeathKind.ShipSink, Pos = new Vector3(0, 0, 0), End = new Vector3(0, 0, 0), Vel = new Vector3(3, 0, 0),
        Heading = 0, Height = h, Radius = 2, Floor = floor, Water = 0f, Seed = 0.7f,
    });

    [Fact]
    public void ShipListsSinksAndFades()
    {
        var deep = ShipPlan(-10f);
        Assert.True(deep.Deep);
        Assert.InRange(deep.Duration, 6f, 14f);
        Assert.True(deep.SinkTo < -2f, $"sinks under the water line: {deep.SinkTo}");
        var early = DeathAnim.Pose(in deep, deep.Duration * 0.2f);
        Assert.True(MathF.Abs(early.Roll) > 0.1f, "lists first");
        var late = DeathAnim.Pose(in deep, deep.Duration * 0.95f);
        Assert.True(late.Pos.Y < -2f && late.Visible < 0.5f, $"under water and fading: {late.Pos.Y} {late.Visible}");
        Assert.True(DeathAnim.Pose(in deep, deep.Duration).Pos.Y <= deep.SinkTo + 1e-3f);
        Assert.Equal(0f, DeathAnim.WreckVisibility(in deep, deep.Duration * 0.9f)); // the deep wreck sinks by itself
        // Shallow water: shorter, never sinks under the sea floor much, the wreck fades in at the end.
        var shallow = ShipPlan(-0.6f);
        Assert.False(shallow.Deep);
        Assert.True(shallow.Duration < deep.Duration);
        Assert.True(shallow.SinkTo >= -0.6f - 2f * 0.2f - 1e-3f);
        Assert.True(DeathAnim.WreckVisibility(in shallow, shallow.Duration * 0.95f) > 0f);
        Assert.Equal(1f, DeathAnim.WreckVisibility(in shallow, shallow.Duration));
    }

    [Fact]
    public void WreckHiddenUntilTheSequenceEnds()
    {
        var s = new DeathStart { Kind = DeathKind.Collapse, Pos = Vector3.Zero, End = Vector3.Zero, Height = 3, Radius = 2, Seed = 0.4f };
        var collapse = DeathAnim.Plan(in s);
        Assert.InRange(collapse.Duration, 1.6f, 3.5f);
        Assert.Equal(0f, DeathAnim.WreckVisibility(in collapse, 0.1f));
        Assert.Equal(1f, DeathAnim.WreckVisibility(in collapse, collapse.Duration));
        var mid = DeathAnim.Pose(in collapse, collapse.Duration * 0.8f);
        Assert.True(mid.Squash < 1f && mid.Pos.Y < 0f && mid.Char > 0.5f);
        // Commander: hidden until its death blast, the wreck never shows during the sequence.
        var acu = DeathAnim.Plan(new DeathStart { Kind = DeathKind.CommanderFall, Height = 3, Radius = 1, BlastDelay = 1.5f });
        Assert.Equal(1.5f, acu.ImpactTime);
        Assert.Equal(1f, DeathAnim.Pose(in acu, 1.4f).Visible);
        Assert.Equal(0f, DeathAnim.Pose(in acu, 1.6f).Visible);
        Assert.Equal(0f, DeathAnim.WreckVisibility(in acu, 1.6f));
        // Experimental: staggers until its death blast, then falls (longer than a structure).
        var big = DeathAnim.Plan(new DeathStart { Kind = DeathKind.BigFall, Height = 8, Radius = 5, BlastDelay = 1f, Huge = true });
        Assert.Equal(1f, big.ImpactTime);
        Assert.True(big.Duration > 3f);
        Assert.True(MathF.Abs(DeathAnim.Pose(in big, big.Duration * 0.9f).Roll) > 0.2f, "falls over");
        Assert.Equal(1f, DeathAnim.WreckVisibility(in big, big.Duration));
        // No sequence: the wreck shows at once.
        var none = DeathAnim.Plan(new DeathStart { Kind = DeathKind.None });
        Assert.Equal(1f, DeathAnim.WreckVisibility(in none, 0f));
    }

    [Fact]
    public void ShaderWPacksFadeAndChar()
    {
        Assert.Equal(0f, DeathAnim.PackShaderW(0f, 1f)); // a living unit
        float w = DeathAnim.PackShaderW(0.5f, 0.5f);
        Assert.Equal(8f, MathF.Floor(w));
        Assert.Equal(0.495f, w - MathF.Floor(w), 3);
        Assert.Equal(16f, MathF.Floor(DeathAnim.PackShaderW(1f, 0f)));
        Assert.True(DeathAnim.PackShaderW(1f, 0f) - 16f < 1f);
    }

    [Fact]
    public void WarpInOnlyAtTheStartOfAGame()
    {
        Assert.True(DeathAnimator.WarpsAtStart(0));
        Assert.True(DeathAnimator.WarpsAtStart(DeathAnimator.WarpMaxStartTick));
        Assert.False(DeathAnimator.WarpsAtStart(DeathAnimator.WarpMaxStartTick + 1)); // a loaded save / replay seek
        Assert.True(DeathAnimator.WarpProgress(0f, out float f0) && f0 == 0f); // flash first
        Assert.True(DeathAnimator.WarpProgress(DeathAnimator.WarpDelay + DeathAnimator.WarpRise * 0.5f, out float half));
        Assert.Equal(0.5f, half, 3);
        Assert.False(DeathAnimator.WarpProgress(DeathAnimator.WarpDelay + DeathAnimator.WarpRise, out float done));
        Assert.Equal(1f, done);
        Assert.True(DeathAnimator.WarpDuration >= DeathAnimator.WarpDelay + DeathAnimator.WarpRise);
    }
}
