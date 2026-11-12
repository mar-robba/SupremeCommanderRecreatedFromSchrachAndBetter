using Rts.Client.Menus;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Skirmish setup → simulation wiring (AI slots, difficulty, local army) and command-line start screens.</summary>
public class GameConfigTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void SetupSlotsBecomeArmiesDrivenByTheSkirmishAi()
    {
        var cfg = new GameConfig { Seed = 42, MapSize = 256 };
        cfg.Slots[0].Kind = SlotKind.AI;
        cfg.Slots[0].Difficulty = AiDifficulty.Hard;
        cfg.Slots[1].Kind = SlotKind.Human;
        cfg.Slots[2].Kind = SlotKind.AI;
        cfg.Slots[2].Difficulty = AiDifficulty.Easy;
        Assert.Null(cfg.Problem());

        var sim = GameSession.CreateSimulation(Db, cfg, out int local);
        var armies = sim.World.Armies;
        Assert.Equal(1, local);
        Assert.Equal(3, armies.Length);
        Assert.True(armies[0].IsAI);
        Assert.Equal(AiDifficulty.Hard, armies[0].AiDifficulty);
        Assert.False(armies[1].IsAI);
        Assert.True(armies[2].IsAI);
        Assert.Equal(AiDifficulty.Easy, armies[2].AiDifficulty);
        Assert.Equal(3, armies.Select(a => a.Team).Distinct().Count()); // free for all

        // One game minute: the AI armies build, the (idle) human doesn't.
        for (int t = 0; t < 600; t++) sim.Step();
        Assert.True(armies[0].UnitCount > 1, $"Hard AI has {armies[0].UnitCount} units");
        Assert.True(armies[2].UnitCount > 1, $"Easy AI has {armies[2].UnitCount} units");
        Assert.Equal(1, armies[1].UnitCount);
    }

    [Theory]
    [InlineData("", StartScreen.MainMenu)]
    [InlineData("--nosound --nofog --volume 0.5 --size 1280x720", StartScreen.MainMenu)]
    [InlineData("--setup --autostart --select commander", StartScreen.Setup)]
    [InlineData("--demo", StartScreen.Game)]
    [InlineData("--map 512 --preset canyon", StartScreen.Game)]
    [InlineData("--screenshot x.png --frames 10", StartScreen.Game)]
    public void CommandLineChoosesTheStartScreen(string args, StartScreen expected)
    {
        var o = ClientOptions.Parse(args.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(expected, o.StartScreen);
    }

    [Fact]
    public void AudioAndFogOptionsAreParsed()
    {
        var o = ClientOptions.Parse(["--nosound", "--nofog", "--music-volume", "0.25"]);
        Assert.False(o.Audio.Enabled);
        Assert.True(o.NoFog);
        Assert.True(o.VolumeGiven);
        Assert.Equal(0.25f, o.Audio.Music);
        Assert.False(ClientOptions.Parse(["--demo"]).VolumeGiven);
    }

    /// <summary>(8b-2) Map picker: cycling, player-count check, --mapfile, a game on the file's spread starts.</summary>
    [Fact]
    public void MapFilePickerChecksPlayersAndStartsTheGameOnTheFile()
    {
        var maps = MapFileCache.List;
        Assert.True(maps.Count >= 8, $"{maps.Count} pack maps");
        string? id = null;
        for (int i = 0; i <= maps.Count; i++) id = MapFileCache.Next(id, 1);
        Assert.Null(id); // Random map → every pack map → back to Random map
        Assert.Equal(maps[^1].Id, MapFileCache.Next(null, -1));

        var cfg = GameConfig.FromOptions(ClientOptions.Parse(["--mapfile", "twin_meadows"]));
        Assert.Equal("twin_meadows", cfg.MapFile);
        Assert.Null(cfg.Problem());
        cfg.Slots[2].Kind = SlotKind.AI;
        Assert.Contains("room for 2", cfg.Problem());
        cfg.MapFile = "zz_no_such_map";
        Assert.Contains("not found", cfg.Problem());

        cfg.MapFile = "four_ponds"; // 4 starts, 3 players: a spread subset
        Assert.Null(cfg.Problem());
        var sim = GameSession.CreateSimulation(Db, cfg, out _);
        var file = MapFileCache.Get("four_ponds");
        Assert.Equal(3, sim.World.Terrain.StartPositions.Length);
        Assert.Equal(file.Terrain.Heights, sim.World.Terrain.Heights);
        Assert.Equal(MapFile.SpreadStarts(file.Terrain.StartPositions, 3).Select(i => file.Terrain.StartPositions[i]),
            sim.World.Terrain.StartPositions);
    }
}
