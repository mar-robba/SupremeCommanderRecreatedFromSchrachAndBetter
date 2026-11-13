using Rts.Sim.Data;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>
/// (11a-2) A real save written by an older build (tests/Rts.Sim.Tests/Fixtures, see DEV_GUIDE "How to change a saved
/// type") must keep loading and playing in every later build: the sim's saved layouts and the blueprints change, the
/// fixture does not. Regenerate it only when the format itself breaks on purpose:
/// <c>RTS_WRITE_FIXTURE=1 dotnet test tests/Rts.Sim.Tests --filter WriteFixture</c>.
/// </summary>
public class SaveFixtureTests
{
    public const string Name = "skirmish_2ai_256.rtssave";

    private static string BuiltPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", Name);

    [Fact]
    public void OldSaveLoadsAndPlays200Ticks()
    {
        Assert.True(File.Exists(BuiltPath), "fixture missing: " + BuiltPath);
        var g = SaveGame.Load(BuiltPath, TestUtil.Db);
        int tick = g.Sim.World.Tick;
        Assert.True(tick > 0);
        Console.WriteLine($"fixture: tick {tick}, migrated {g.Migration.Migrated}\n{g.Migration}");
        TestUtil.Run(g.Sim, 200);
        Assert.Equal(tick + 200, g.Sim.World.Tick);
        Assert.True(g.Sim.World.Units.AliveCount > 2);
    }

    /// <summary>Writes the fixture into the source folder (only with RTS_WRITE_FIXTURE=1).</summary>
    [Fact]
    public void WriteFixture()
    {
        if (Environment.GetEnvironmentVariable("RTS_WRITE_FIXTURE") != "1") return;
        var settings = new SkirmishSettings
        {
            Seed = 3, MapSize = 256, Players = 2, AiPlayers = [true, true],
            AiDifficulties = [AiDifficulty.Hard, AiDifficulty.Normal],
        };
        var sim = GameSetup.CreateSkirmish(TestUtil.Db, settings);
        TestUtil.Run(sim, 1800); // 3 minutes of game time
        var info = GameInfo.ForSkirmish(settings, sim, 0, false);
        var file = SaveGame.Encode(SaveGame.Capture(sim, info, null, "fixture"));
        var dir = Path.Combine(DataPaths.FindDataRoot(), "..", "tests", "Rts.Sim.Tests", "Fixtures");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, Name), file);
        Console.WriteLine($"fixture written: {file.Length / 1024} KB, tick {sim.World.Tick}");
    }
}
