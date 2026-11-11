using System.Numerics;
using Rts.Client.Rendering;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>Walking ships (Salem) ashore: hull lift, leg reach, gait amplitude and the leg-segment matrix.</summary>
public class WalkingShipRenderTests
{
    private const float MinY = -0.78f, H = 2.6f; // Salem: CyShipHull bottom = -0.3 h

    [Fact]
    public void AtSeaTheSnapshotHeightIsKept()
    {
        Assert.Equal(10f, UnitRenderer.WalkingShipY(10f, 4f, MinY, H));
        Assert.False(UnitRenderer.WalkingShipLegsOut(10f, 4f, MinY, H));
    }

    [Fact]
    public void OnLandTheKeelClearsTheGround()
    {
        float ground = 12f;
        float y = UnitRenderer.WalkingShipY(ground, ground, MinY, H);
        Assert.Equal(UnitRenderer.WalkClearance * H, y + MinY - ground, 4);
        Assert.True(UnitRenderer.WalkingShipLegsOut(y, ground, MinY, H));
    }

    [Fact]
    public void LiftIsContinuousFromShallowsToShore()
    {
        float water = 10f, last = float.MinValue;
        for (float g = 7f; g <= 11f; g += 0.05f)
        {
            float y = UnitRenderer.WalkingShipY(MathF.Max(g, water), g, MinY, H);
            Assert.True(y >= last - 1e-4f && y - last < 0.2f || last == float.MinValue, $"jump at ground {g}: {last} -> {y}");
            last = y;
        }
    }

    [Fact]
    public void GaitAmplitudeFollowsSpeed()
    {
        Assert.Equal(0f, UnitRenderer.WalkAmplitude(0f, 1.6f));
        Assert.Equal(0.5f, UnitRenderer.WalkAmplitude(0.8f, 1.6f), 4);
        Assert.Equal(1f, UnitRenderer.WalkAmplitude(5f, 1.6f));
        Assert.Equal(0f, UnitRenderer.WalkAmplitude(1f, 0f));
    }

    [Fact]
    public void SegmentMapsTheUnitBoxOntoTheLeg()
    {
        var a = new Vector3(1, 3, 2);
        var b = new Vector3(2.5f, 0.5f, 2.8f);
        var m = UnitRenderer.Segment(a, b, 0.2f);
        Assert.True(Vector3.Distance(a, Vector3.Transform(Vector3.Zero, m)) < 1e-4f);
        Assert.True(Vector3.Distance(b, Vector3.Transform(Vector3.UnitZ, m)) < 1e-4f);
        var side = Vector3.Transform(Vector3.UnitX, m) - a;
        Assert.Equal(0.2f, side.Length(), 4);
        Assert.Equal(0f, Vector3.Dot(side, b - a), 3);
        var vertical = UnitRenderer.Segment(a, a - Vector3.UnitY, 0.1f); // straight down: still a valid frame
        Assert.True(Vector3.Distance(a - Vector3.UnitY, Vector3.Transform(Vector3.UnitZ, vertical)) < 1e-4f);
    }

    [Fact]
    public void TheSalemIsTheWalkingShip()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        int i = db.IndexOf("cybran_t2_destroyer");
        Assert.True(i >= 0);
        Assert.True(Rts.Sim.Naval.WalkingShips.IsWalkingShip(db[i]));
        Assert.False(Rts.Sim.Naval.WalkingShips.IsWalkingShip(db[db.IndexOf("uef_t2_destroyer")]));
    }
}
