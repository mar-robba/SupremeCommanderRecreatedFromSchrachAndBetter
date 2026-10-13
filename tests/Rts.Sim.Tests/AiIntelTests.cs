using System.Numerics;
using System.Reflection;
using Rts.Sim.AI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>Fog-of-war fairness, scouting/radar and robustness of the skirmish AI.</summary>
public class AiIntelTests
{
    private static Simulation NewAiGame(int seed, int mapSize, bool[] ai, params AiDifficulty[] difficulty)
    {
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings { Seed = seed, MapSize = mapSize, Players = ai.Length, AiPlayers = ai });
        for (int a = 0; a < ai.Length; a++)
            sim.World.Armies[a].AiDifficulty = difficulty.Length == 0 ? AiDifficulty.Normal : difficulty[Math.Min(a, difficulty.Length - 1)];
        return sim;
    }

    private static void Step(Simulation sim)
    {
        sim.Step();
        sim.World.Events.Effects.Clear();
    }

    /// <summary>The commands the AI issued during the last step (Simulation keeps them in a private list).</summary>
    private static List<Command> AiCommandsOf(Simulation sim) =>
        (List<Command>)typeof(Simulation).GetField("_aiCommands", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sim)!;

    [Fact]
    public void AiWithFogOfWarOnlyAttacksUnitsItCanSee()
    {
        var sim = NewAiGame(42, 256, [true, true], AiDifficulty.Hard, AiDifficulty.Normal);
        var w = sim.World;
        var st = AiSystem.StateOf(w);
        int cap = w.Units.Capacity;
        // Visibility per army at the end of the previous step = what the AI saw when it thought this step.
        var visible = new bool[2, cap];
        int attackOrders = 0, attackMoves = 0;
        for (int t = 0; t < 12 * 60 * SimWorld.TicksPerSecond; t++)
        {
            Step(sim);
            foreach (var c in AiCommandsOf(sim))
            {
                if (c is not IssueOrderCommand io) continue;
                if (io.Order.Type == OrderType.AttackMove) attackMoves++;
                if (io.Order.Type != OrderType.Attack || io.Order.Target.IsNone) continue;
                var h = io.Order.Target;
                attackOrders++;
                Assert.True(visible[io.Army, h.Index], $"army {io.Army} ordered an attack on unit {h.Index} it could not see (tick {w.Tick})");
            }
            for (int a = 0; a < 2; a++)
                for (int i = 0; i < cap; i++)
                    visible[a, i] = w.Units.IsActive(i) && w.IsEnemy(a, w.Units.Army[i]) && w.Intel.IsVisible(a, i);
        }
        st = AiSystem.StateOf(w)!;
        Assert.True(st.Brain(0).Perception.RespectIntel && st.Brain(1).Perception.RespectIntel, "fog-fair by default");
        Assert.True(attackMoves > 0, "the AIs should have attacked");
    }

    [Fact]
    public void AiScoutsTheEnemyAndBuildsRadar()
    {
        var sim = NewAiGame(7, 256, [false, true]);
        var w = sim.World;
        var db = w.Blueprints;
        int scoutBp = db.IndexOf("uef_t1_scout"), radarBp = db.IndexOf("uef_t1_radar");
        bool sawScout = false;
        int radars = 0;
        bool sawCommander = false;
        var enemyStart = w.Units.Pos[w.Armies[0].Commander.Index];
        Assert.False(w.Intel.IsPositionExplored(1, enemyStart), "the enemy base starts unexplored");
        for (int t = 0; t < 8 * 60 * SimWorld.TicksPerSecond; t++)
        {
            Step(sim);
            if (!sawCommander && !w.Armies[0].Defeated && AiSystem.StateOf(w)!.Brain(1).Perception.EnemyCommanderPos[0] != null) sawCommander = true;
            if (t % 50 != 0) continue;
            radars = 0;
            for (int i = 0; i < w.Units.Capacity; i++)
            {
                if (!w.Units.IsActive(i) || w.Units.Army[i] != 1) continue;
                int bp = w.Units.Bp[i];
                if (bp == scoutBp) sawScout = true;
                if (bp == radarBp && w.Units.IsComplete(i)) radars++;
            }
        }
        Assert.True(sawScout, "the AI should build a land scout");
        Assert.True(radars >= 1, "the AI should build a radar");
        Assert.True(w.Intel.IsPositionExplored(1, enemyStart), "the AI should have scouted the enemy base");
        // The AI learns where the enemy commander is only by seeing it (here it also went on to kill it).
        Assert.True(sawCommander, "the scouts should have spotted the enemy commander at its base");
    }

    [Fact]
    public void AiHandlesHigherTechEnemiesWithoutCrashing()
    {
        // An AI that only builds T1 meets T2/T3 units and structures near its base: it must see them as threats
        // (defend) and keep running.
        var sim = NewAiGame(3, 256, [false, true]);
        var w = sim.World;
        for (int t = 0; t < 2400; t++) Step(sim);
        var basePos = w.Units.Pos[w.Armies[1].Commander.Index];
        var toward = Vector2.Normalize(w.Units.Pos[w.Armies[0].Commander.Index] - basePos);
        string[] names = ["uef_t2_heavy_tank", "uef_t3_assault_bot", "uef_t2_point_defense", "uef_t2_gunship", "uef_t3_mobile_artillery", "uef_t2_mass_extractor"];
        int k = 0;
        foreach (var n in names)
        {
            if (w.Blueprints.IndexOf(n) < 0) continue;
            TestUtil.Spawn(sim, n, 0, basePos + toward * (30f + 4f * k) + new Vector2(k % 2 == 0 ? 3f : -3f, 0));
            k++;
        }
        Assert.True(k > 0, "T2/T3 blueprints should exist");
        bool defended = false;
        for (int t = 0; t < 900; t++)
        {
            Step(sim);
            if (t % 10 != 0) continue;
            for (int i = 0; i < w.Units.Capacity; i++)
                if (w.Units.IsActive(i) && w.Units.Army[i] == 1 && AiSystem.StateOf(w)!.Info(i).Role == AiRole.Defend) { defended = true; break; }
        }
        Assert.True(defended, "the AI should respond to the high-tech intruders");
        Assert.False(w.Armies[1].Defeated);
    }
}
