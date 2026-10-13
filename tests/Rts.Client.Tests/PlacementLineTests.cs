using System.Numerics;
using Rts.Client.Input;

namespace Rts.Client.Tests;

/// <summary>Shift+drag structure lines (pure layout, see Input/PlacementLine).</summary>
public class PlacementLineTests
{
    private static List<Vector2> Lay(Vector2 a, Vector2 c, int fx, int fz, int max = PlacementLine.MaxCount)
    {
        var l = new List<Vector2>();
        int n = PlacementLine.Layout(a, c, fx, fz, l, max);
        Assert.Equal(n, l.Count);
        return l;
    }

    [Fact]
    public void NoDragIsASingleGhostAtTheAnchor()
    {
        var l = Lay(new Vector2(11, 11), new Vector2(11, 11), 2, 2);
        Assert.Equal([new Vector2(11, 11)], l);
    }

    [Fact]
    public void HorizontalDragStepsOneFootprintAlongX()
    {
        // 2x2 footprint, drag 7 cells right and 1 down: dominant axis X, 7/2 = 3.5 footprints -> 4 steps, 5 ghosts.
        var l = Lay(new Vector2(11, 11), new Vector2(18, 12), 2, 2);
        Assert.Equal([new(11, 11), new(13, 11), new(15, 11), new(17, 11), new(19, 11)], l);
    }

    [Fact]
    public void VerticalDragGoesBackwardsAlongZ()
    {
        // 1x1 footprint (odd: centers on .5): straight up the Z axis toward smaller z.
        var l = Lay(new Vector2(20.5f, 30.5f), new Vector2(21.5f, 26.5f), 1, 1);
        Assert.Equal([new(20.5f, 30.5f), new(20.5f, 29.5f), new(20.5f, 28.5f), new(20.5f, 27.5f), new(20.5f, 26.5f)], l);
    }

    [Fact]
    public void DiagonalDragStepsOnBothAxesWithoutOverlap()
    {
        var l = Lay(new Vector2(10, 10), new Vector2(16, 4), 2, 2);
        Assert.Equal([new(10, 10), new(12, 8), new(14, 6), new(16, 4)], l);
        // Consecutive footprints never overlap: they are a full footprint apart on at least one axis.
        for (int i = 1; i < l.Count; i++)
            Assert.True(MathF.Abs(l[i].X - l[i - 1].X) >= 2 || MathF.Abs(l[i].Y - l[i - 1].Y) >= 2);
    }

    [Fact]
    public void NonSquareFootprintUsesItsOwnSizePerAxis()
    {
        // 3 wide (x) by 1 deep (z): a 6-cell drag on z is 6 footprints, on x only 2.
        Assert.Equal(7, Lay(new Vector2(5.5f, 5.5f), new Vector2(5.5f, 11.5f), 3, 1).Count);
        var lx = Lay(new Vector2(5.5f, 5.5f), new Vector2(11.5f, 5.5f), 3, 1);
        Assert.Equal([new(5.5f, 5.5f), new(8.5f, 5.5f), new(11.5f, 5.5f)], lx);
    }

    [Fact]
    public void LongDragIsCapped()
    {
        var l = Lay(new Vector2(0.5f, 0.5f), new Vector2(500.5f, 0.5f), 1, 1, max: 10);
        Assert.Equal(10, l.Count);
        Assert.Equal(new Vector2(9.5f, 0.5f), l[^1]);
    }
}
