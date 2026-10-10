using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Sim;

public sealed class SkirmishSettings
{
    public int MapSize = 256;
    public int Seed = 42;
    public int Players = 2;
    /// <summary>Which armies are AI-controlled (army 0 is the local human by default).</summary>
    public bool[] AiPlayers = [false, true];
    /// <summary>Team per army; default = every army on its own team (free for all).</summary>
    public int[]? Teams;
    public string CommanderBlueprint = "uef_commander";
    public int UnitCapacity = 16384;
    public float StartMass = 650;
    public float StartEnergy = 4000;
}

public static class GameSetup
{
    /// <summary>Army colors as 0xRRGGBBAA: blue, red, green, yellow, orange, purple, cyan, white.</summary>
    public static readonly uint[] ArmyColors =
    [
        0x2F6FE0FF, 0xE03A2FFF, 0x3CB043FF, 0xE8C31AFF, 0xF07F17FF, 0x9B4FD8FF, 0x2FC9D8FF, 0xE6E6E6FF,
    ];

    public static Simulation CreateSkirmish(BlueprintDb db, SkirmishSettings s)
    {
        var terrain = MapGenerator.Generate(new MapGenSettings { Size = s.MapSize, Players = s.Players, Seed = s.Seed });
        var armies = new Army[s.Players];
        for (int a = 0; a < s.Players; a++)
        {
            armies[a] = new Army
            {
                Index = a,
                Name = a == 0 ? "Player" : $"AI {a}",
                Team = s.Teams != null && a < s.Teams.Length ? s.Teams[a] : a,
                Color = ArmyColors[a % ArmyColors.Length],
                IsAI = a < s.AiPlayers.Length && s.AiPlayers[a],
            };
            armies[a].Eco.Mass = s.StartMass;
            armies[a].Eco.Energy = s.StartEnergy;
        }

        var world = new SimWorld(db, terrain, armies, (ulong)s.Seed, s.UnitCapacity);
        int cmd = db.IndexOf(s.CommanderBlueprint);
        if (cmd < 0) throw new InvalidOperationException($"Commander blueprint '{s.CommanderBlueprint}' not found");
        var center = new Vector2(terrain.Width * 0.5f, terrain.Height * 0.5f);
        for (int a = 0; a < s.Players; a++)
        {
            var sp = terrain.StartPositions[a % terrain.StartPositions.Length];
            var h = world.SpawnUnit(cmd, a, sp, DMath.HeadingOf(center - sp), complete: true);
            armies[a].Commander = h;
        }
        return new Simulation(world);
    }
}
