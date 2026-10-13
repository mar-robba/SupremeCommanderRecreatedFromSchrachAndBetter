using System.Numerics;
using Rts.Sim.AI;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Hard AI micro: focus fire target choice and the difficulty knobs that enable it.</summary>
public class AiMicroTests
{
    [Fact]
    public void FocusFirePicksTheWeakestEnemyInRangeThenSpillsOver()
    {
        Vector2[] enemies = [new(10, 0), new(12, 0), new(40, 0)];
        float[] left = [300f, 100f, 10f]; // the 10-hp one is out of range
        // Two shooters with 60 damage each over the horizon saturate the 100-hp target; the third goes to the next.
        Assert.Equal(1, AiMilitary.PickFocusTarget(Vector2.Zero, 20f, enemies, left, 60f));
        Assert.Equal(1, AiMilitary.PickFocusTarget(Vector2.Zero, 20f, enemies, left, 60f));
        Assert.Equal(0, AiMilitary.PickFocusTarget(Vector2.Zero, 20f, enemies, left, 60f));
        Assert.Equal(240f, left[0]);
        Assert.Equal(-20f, left[1]);
        Assert.Equal(10f, left[2]);
    }

    [Fact]
    public void FocusFireFallsBackToSaturatedTargetsAndIgnoresOutOfRange()
    {
        Vector2[] enemies = [new(5, 0), new(50, 0)];
        float[] left = [-10f, 50f];
        // Only the saturated target is in range: still better than idling.
        Assert.Equal(0, AiMilitary.PickFocusTarget(Vector2.Zero, 20f, enemies, left, 30f));
        // Nothing in range.
        Assert.Equal(-1, AiMilitary.PickFocusTarget(new Vector2(-100, 0), 20f, enemies, left, 30f));
    }

    [Fact]
    public void FocusFireTiesGoToTheNearestEnemy()
    {
        Vector2[] enemies = [new(15, 0), new(5, 0), new(10, 0)];
        float[] left = [300f, 300f, 300f];
        Assert.Equal(1, AiMilitary.PickFocusTarget(Vector2.Zero, 20f, enemies, left, 50f));
        // Now the nearest has less left: it stays the pick.
        Assert.Equal(1, AiMilitary.PickFocusTarget(Vector2.Zero, 20f, enemies, left, 50f));
    }

    [Fact]
    public void OnlyHardMicrosAndNoDifficultyRetreatsArmies()
    {
        Assert.True(AiConfig.For(AiDifficulty.Hard).FocusFire);
        Assert.False(AiConfig.For(AiDifficulty.Normal).FocusFire);
        Assert.False(AiConfig.For(AiDifficulty.Easy).FocusFire);
        // Matrix results: retreating beaten armies lost more games than fighting on (piecemeal pressure ends games).
        foreach (var d in new[] { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard })
            Assert.Equal(0f, AiConfig.For(d).RetreatFraction);
    }
}
