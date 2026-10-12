using System.Numerics;
using Rts.Client.Sandbox;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Sandbox console: command parser, army tokens, fuzzy blueprint matching, Tab completion and hints.</summary>
public class SandboxConsoleTests
{
    private static readonly BlueprintDb RealDb = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
    private static readonly int Tank = RealDb.IndexOf("uef_t1_tank");

    private static BlueprintDb MakeDb(params (string Id, string Name)[] units)
    {
        var db = new BlueprintDb();
        foreach (var (id, name) in units) db.Add(new UnitBlueprint { Id = id, Name = name });
        return db;
    }

    private static readonly BlueprintDb SmallDb = MakeDb(
        ("uef_t1_tank", "MA12 Striker"),
        ("uef_t2_tank", "Pillar"),
        ("tank_hunter", "Hunter"),
        ("uef_t1_tankette", "Tiny"),
        ("uef_t1_scout", "Snoop"),
        ("uef_t1_air_scout", "Air Scout"),
        ("cybran_bot", "Big Tank Walker"));

    private static ConsoleContext Ctx(int local = 0, int count = 2, int[]? teams = null, BlueprintDb? db = null)
        => new() { Db = db ?? RealDb, LocalArmy = local, ArmyCount = count, Teams = teams };

    private static T Ok<T>(string line, ConsoleContext? ctx = null) where T : ConsoleAction
    {
        var r = SandboxConsoleParser.Parse(line, ctx ?? Ctx());
        Assert.True(r.Error == null, $"'{line}' failed: {r.Error}");
        return Assert.IsType<T>(r.Action);
    }

    private static string Err(string line, ConsoleContext? ctx = null)
    {
        var r = SandboxConsoleParser.Parse(line, ctx ?? Ctx());
        Assert.Null(r.Action);
        Assert.NotNull(r.Error);
        return r.Error!;
    }

    // ------------------------------------------------------------ fuzzy blueprint match

    [Fact]
    public void MatcherFindsExactIdCaseInsensitive()
    {
        Assert.Equal("uef_t2_tank", BlueprintMatcher.Find(SmallDb, "uef_t2_tank")?.Id);
        Assert.Equal("uef_t1_tank", BlueprintMatcher.Find(SmallDb, "UEF_T1_TANK")?.Id);
    }

    [Theory]
    [InlineData("MA12 Striker")]
    [InlineData("ma12_striker")]
    [InlineData("  ma12   striker ")]
    [InlineData("striker")]
    public void MatcherFindsNamesWithSpacesOrUnderscores(string query)
        => Assert.Equal("uef_t1_tank", BlueprintMatcher.Find(SmallDb, query)?.Id);

    [Fact]
    public void MatcherRanksSuffixThenPrefixThenContainsThenName()
    {
        var c = BlueprintMatcher.Candidates(SmallDb, "tank", 10).Select(b => b.Id).ToArray();
        // suffix (t1 before t2: same length, ordinal), prefix, id contains, name contains
        Assert.Equal(new[] { "uef_t1_tank", "uef_t2_tank", "tank_hunter", "uef_t1_tankette", "cybran_bot" }, c);
        Assert.Equal(2, BlueprintMatcher.Candidates(SmallDb, "tank", 2).Count);
        // Tie on suffix: the shortest id wins.
        Assert.Equal("uef_t1_scout", BlueprintMatcher.Find(SmallDb, "scout")?.Id);
        Assert.Equal("uef_t1_tank", BlueprintMatcher.Find(SmallDb, "t1 tank")?.Id);
    }

    [Fact]
    public void MatcherReturnsNullForUnknownOrEmpty()
    {
        Assert.Null(BlueprintMatcher.Find(SmallDb, "xyzzy"));
        Assert.Null(BlueprintMatcher.Find(SmallDb, ""));
        Assert.Null(BlueprintMatcher.Find(SmallDb, "   "));
        Assert.Empty(BlueprintMatcher.Candidates(SmallDb, "", 5));
    }

    [Fact]
    public void MatcherWorksOnTheRealBlueprints()
    {
        var tank = BlueprintMatcher.Find(RealDb, "tank");
        Assert.NotNull(tank);
        Assert.EndsWith("_tank", tank!.Id);
        Assert.EndsWith("_engineer", BlueprintMatcher.Find(RealDb, "engineer")!.Id);
        Assert.Equal(Tank, BlueprintMatcher.Find(RealDb, "ma12 striker")!.Index);
    }

    // ------------------------------------------------------------ armies

    [Theory]
    [InlineData("me", 1)]
    [InlineData("SELF", 1)]
    [InlineData("player", 1)]
    [InlineData("enemy", 0)]
    [InlineData("enemy1", 0)]
    [InlineData("enemy2", 2)]
    [InlineData("ai2", 2)]
    [InlineData("army1", 0)]
    [InlineData("p3", 2)]
    [InlineData("2", 1)]
    public void ArmyTokensResolveToIndices(string token, int expected)
    {
        Assert.True(SandboxConsoleParser.TryParseArmy(token, Ctx(local: 1, count: 3), out int army, out string? err), err);
        Assert.Equal(expected, army);
    }

    [Fact]
    public void EnemySkipsAlliesWhenTeamsAreKnown()
    {
        Assert.True(SandboxConsoleParser.TryParseArmy("enemy", Ctx(0, 3, [0, 0, 1]), out int army, out _));
        Assert.Equal(2, army);
        Assert.False(SandboxConsoleParser.TryParseArmy("enemy", Ctx(0, 2, [1, 1]), out _, out string? err));
        Assert.Contains("no enemy", err);
    }

    [Fact]
    public void ArmyOutOfRangeOrInvalidIsAnError()
    {
        Assert.False(SandboxConsoleParser.TryParseArmy("p3", Ctx(), out _, out string? err));
        Assert.Equal("army 3 doesn't exist (2 armies)", err);
        Assert.False(SandboxConsoleParser.TryParseArmy("enemy2", Ctx(), out _, out err));
        Assert.Contains("enemy2 doesn't exist", err);
        Assert.False(SandboxConsoleParser.TryParseArmy("all", Ctx(), out _, out err));
        Assert.NotNull(err);
        Assert.False(SandboxConsoleParser.TryParseArmy("banana", Ctx(), out _, out err));
        Assert.Contains("not an army", err);
    }

    // ------------------------------------------------------------ spawn

    [Fact]
    public void SpawnWithCountArmyAndPosition()
    {
        var s = Ok<SpawnAction>("spawn tank 100 enemy at 140,128");
        Assert.Equal(new SpawnAction(Tank, 100, 1, new Vector2(140, 128), false), s);

        s = Ok<SpawnAction>("spawn tank enemy 5");
        Assert.Equal(new SpawnAction(Tank, 5, 1, null, false), s);

        s = Ok<SpawnAction>("sp tank site 10.5,20 3 p1");
        Assert.Equal(new SpawnAction(Tank, 3, 0, new Vector2(10.5f, 20), true), s);

        s = Ok<SpawnAction>("s \"MA12 Striker\" at 5 6");
        Assert.Equal(new SpawnAction(Tank, 1, 0, new Vector2(5, 6), false), s);

        s = Ok<SpawnAction>("SPAWN Tank 2 ENEMY");
        Assert.Equal(new SpawnAction(Tank, 2, 1, null, false), s);
    }

    [Fact]
    public void SpawnDefaultsAndClampsCount()
    {
        Assert.Equal(new SpawnAction(Tank, 1, 1, null, false), Ok<SpawnAction>("spawn tank", Ctx(local: 1)));
        Assert.Equal(SandboxSystem.MaxSpawnCount, Ok<SpawnAction>("spawn tank 99999").Count);
        Assert.Equal(1, Ok<SpawnAction>("spawn tank 0").Count);
        Assert.Equal(10, Ok<SpawnAction>("spawn 10 tank").Count);
    }

    [Fact]
    public void SpawnErrors()
    {
        Assert.StartsWith("spawn: missing unit", Err("spawn"));
        Assert.Equal("unknown unit 'xyzzy'", Err("spawn xyzzy 5"));
        Assert.Contains("army 9 doesn't exist", Err("spawn tank army9"));
        Assert.Contains("twice", Err("spawn tank 5 6"));
        Assert.Contains("'at' needs a position", Err("spawn tank at"));
        Assert.Contains("unexpected 'bogus'", Err("spawn tank bogus"));
    }

    // ------------------------------------------------------------ nuke, toggles, fog, ai

    [Fact]
    public void NukeWithAndWithoutPosition()
    {
        Assert.Null(Ok<NukeAction>("nuke").At);
        Assert.Equal(new Vector2(100, 200), Ok<NukeAction>("nuke 100,200").At);
        Assert.Equal(new Vector2(100, 200), Ok<NukeAction>("nuke 100 200").At);
        Assert.StartsWith("nuke:", Err("nuke here"));
        Assert.False(Ok<NukeAction>("nuke 100,200").Instant); // default: the real strategic missile
    }

    [Fact]
    public void NukeInstantIsTheBlastWithoutMissile()
    {
        var a = Ok<NukeAction>("nuke instant 100,200");
        Assert.True(a.Instant);
        Assert.Equal(new Vector2(100, 200), a.At);
        Assert.True(Ok<NukeAction>("nuke 100,200 instant").Instant);
        Assert.True(Ok<NukeAction>("NUKE Now at 5,6").Instant);
        var click = Ok<NukeAction>("nuke instant");
        Assert.True(click.Instant);
        Assert.Null(click.At);
        Assert.StartsWith("nuke:", Err("nuke instant here"));
    }

    [Fact]
    public void SpawnListGroupsTheStrategicBlueprints()
    {
        foreach (var id in new[] { "uef_t2_shield_generator", "uef_t3_heavy_shield_generator", "uef_t2_mobile_shield",
                     "uef_t2_tactical_missile_launcher", "uef_t2_tactical_missile_defense", "uef_t3_strategic_missile_launcher",
                     "uef_t3_strategic_missile_defense", "uef_t2_artillery_installation", "uef_t3_heavy_artillery" })
        {
            Assert.True(RealDb.TryGet(id, out var bp), id);
            Assert.Equal("Strategic & shields", SandboxPanel.GroupOf(bp).name);
        }
        Assert.Equal("Land T1", SandboxPanel.GroupOf(RealDb["uef_t1_tank"]).name);
        Assert.Equal("Land T1", SandboxPanel.GroupOf(RealDb["uef_t1_artillery"]).name); // mobile artillery stays with land
        Assert.Equal("uef_t3_strategic_missile_launcher", BlueprintMatcher.Find(RealDb, "strategic missile launcher")!.Id);
    }

    [Fact]
    public void TogglesParseArmyAndOnOffInAnyOrder()
    {
        var ctx = Ctx(local: 0, count: 3);
        Assert.Equal(new ToggleAction(SandboxToggle.InfiniteResources, 0, null), Ok<ToggleAction>("money", ctx));
        Assert.Equal(new ToggleAction(SandboxToggle.InfiniteResources, -1, true), Ok<ToggleAction>("infinite all on", ctx));
        Assert.Equal(new ToggleAction(SandboxToggle.InstantBuild, 1, false), Ok<ToggleAction>("instabuild enemy off", ctx));
        Assert.Equal(new ToggleAction(SandboxToggle.InstantBuild, 2, true), Ok<ToggleAction>("instant yes p3", ctx));
        Assert.Equal(new ToggleAction(SandboxToggle.InfiniteResources, 1, null), Ok<ToggleAction>("res 2", ctx));
        Assert.Equal(new ToggleAction(SandboxToggle.BuildAnywhere, 0, true), Ok<ToggleAction>("buildanywhere 1", ctx));
        Assert.Equal(new ToggleAction(SandboxToggle.BuildAnywhere, 0, false), Ok<ToggleAction>("anywhere 1 off", ctx));
        Assert.Contains("unexpected", Err("money maybe", ctx));
    }

    [Fact]
    public void FogAndReveal()
    {
        Assert.Null(Ok<FogAction>("fog").On);
        Assert.False(Ok<FogAction>("fog off").On);
        Assert.True(Ok<FogAction>("fog 1").On);
        Assert.False(Ok<FogAction>("reveal").On);
    }

    [Fact]
    public void AiVariants()
    {
        var ctx = Ctx(local: 0, count: 3);
        Assert.Equal(new AiAction(1, true, null), Ok<AiAction>("ai 2 on", ctx));
        Assert.Equal(new AiAction(-1, false, null), Ok<AiAction>("ai all off", ctx));
        Assert.Equal(new AiAction(1, true, AiDifficulty.Hard), Ok<AiAction>("ai enemy hard", ctx));
        Assert.Equal(new AiAction(2, true, AiDifficulty.Easy), Ok<AiAction>("ai p3 on easy", ctx));
        Assert.Equal(new AiAction(2, true, AiDifficulty.Normal), Ok<AiAction>("ai ai2 normal", ctx));
        Assert.StartsWith("ai: which army", Err("ai", ctx));
        Assert.StartsWith("ai: expected", Err("ai 2", ctx));
        Assert.Contains("doesn't exist", Err("ai 7 on", ctx));
    }

    // ------------------------------------------------------------ selection actions

    [Theory]
    [InlineData("damage", 0.5f)]
    [InlineData("damage 50", 0.5f)]
    [InlineData("damage 50%", 0.5f)]
    [InlineData("damage 0.5", 0.5f)]
    [InlineData("dmg 25%", 0.25f)]
    [InlineData("damage 300", 1f)]
    public void DamagePercentForms(string line, float expected)
    {
        var a = Ok<UnitActionAction>(line);
        Assert.Equal(SandboxUnitAction.Damage, a.Action);
        Assert.Equal(expected, a.Amount, 4);
    }

    [Fact]
    public void GiveAndSimpleSelectionActions()
    {
        var ctx = Ctx(local: 0, count: 3);
        Assert.Equal(new UnitActionAction(SandboxUnitAction.ChangeOwner, 0, 1), Ok<UnitActionAction>("give 2", ctx));
        Assert.Equal(new UnitActionAction(SandboxUnitAction.ChangeOwner, 0, 1), Ok<UnitActionAction>("owner enemy", ctx));
        Assert.StartsWith("give: which army", Err("give", ctx));
        Assert.StartsWith("give:", Err("give all", ctx));
        Assert.Equal(SandboxUnitAction.Kill, Ok<UnitActionAction>("kill").Action);
        Assert.Equal(SandboxUnitAction.Delete, Ok<UnitActionAction>("del").Action);
        Assert.Equal(SandboxUnitAction.Delete, Ok<UnitActionAction>("remove").Action);
        Assert.Equal(SandboxUnitAction.Heal, Ok<UnitActionAction>("heal").Action);
        Assert.Equal(SandboxUnitAction.Complete, Ok<UnitActionAction>("finish").Action);
        Assert.Contains("unexpected", Err("kill now"));
        Assert.StartsWith("damage:", Err("damage lots"));
    }

    // ------------------------------------------------------------ clear, speed, misc

    [Fact]
    public void ClearDefaultsAndClamps()
    {
        Assert.Equal(new ClearAction(20, null), Ok<ClearAction>("clear"));
        Assert.Equal(new ClearAction(50, null), Ok<ClearAction>("clear 50"));
        Assert.Equal(new ClearAction(500, new Vector2(10, 20)), Ok<ClearAction>("clear 9999 at 10,20"));
        Assert.Equal(new ClearAction(1, new Vector2(3, 4)), Ok<ClearAction>("clear 3,4 0.1"));
    }

    [Theory]
    [InlineData("speed 2", 2f)]
    [InlineData("speed 2x", 2f)]
    [InlineData("speed x4", 4f)]
    [InlineData("speed 0.5", 0.5f)]
    [InlineData("speed 100", 8f)]
    [InlineData("speed 0.01", 0.125f)]
    public void SpeedForms(string line, float expected) => Assert.Equal(expected, Ok<SpeedAction>(line).Speed);

    [Fact]
    public void SpeedAndVictoryErrors()
    {
        Assert.StartsWith("speed: expected a number", Err("speed"));
        Assert.StartsWith("speed: expected a number", Err("speed fast"));
        Assert.StartsWith("victory: expected on or off", Err("victory"));
        Assert.True(Ok<VictoryAction>("victory on").On);
        Assert.False(Ok<VictoryAction>("victory off").On);
    }

    [Fact]
    public void PauseCameraWaitHelpPanelCls()
    {
        Assert.Null(Ok<PauseAction>("pause").On);
        Assert.False(Ok<PauseAction>("pause off").On);
        Assert.Equal(new CameraAction(new Vector2(100, 200), 80), Ok<CameraAction>("camera 100,200 80"));
        Assert.Equal(new CameraAction(new Vector2(100, 200), null), Ok<CameraAction>("camera 100 200"));
        Assert.Equal(new CameraAction(new Vector2(100, 200), 80), Ok<CameraAction>("camera 100,200,80")); // --camera format
        Assert.True(Ok<ShowConsoleAction>("console on").On);
        Assert.Null(Ok<ShowConsoleAction>("console").On);
        Assert.StartsWith("camera:", Err("camera 100"));
        Assert.Equal(30, Ok<WaitAction>("wait 30").Frames);
        Assert.StartsWith("wait:", Err("wait"));
        Ok<HelpAction>("help");
        Ok<HelpAction>("?");
        Ok<HelpAction>("h");
        Assert.True(Ok<PanelAction>("panel on").On);
        Ok<ClearLogAction>("cls");
    }

    [Fact]
    public void UnknownCommandAndEmptyLines()
    {
        Assert.Equal("unknown command 'foo' - type help", Err("foo bar"));
        Assert.Equal(default(ParseResult), SandboxConsoleParser.Parse("", Ctx()));
        Assert.Equal(default(ParseResult), SandboxConsoleParser.Parse("   \t ", Ctx()));
    }

    [Fact]
    public void SplitScriptOnSemicolonsAndNewlines()
    {
        var lines = SandboxConsoleParser.SplitScript("money on; spawn tank 10\n\n  wait 30 ;;\r\nnuke 1,2  ");
        Assert.Equal(new[] { "money on", "spawn tank 10", "wait 30", "nuke 1,2" }, lines);
        Assert.Empty(SandboxConsoleParser.SplitScript(" ; \n "));
    }

    [Fact]
    public void HelpLinesCoverEveryCommand()
    {
        var names = SandboxConsoleParser.CommandNames;
        var help = SandboxConsoleParser.HelpLines;
        Assert.Equal(names.Count, help.Count);
        for (int i = 0; i < names.Count; i++) Assert.StartsWith(names[i], help[i]);
        Assert.Contains("spawn <unit> [count] [army] [at x,z] [site]  spawn units", help);
        foreach (var n in new[] { "spawn", "nuke", "money", "instabuild", "buildanywhere", "fog", "reveal", "ai", "kill",
                     "delete", "heal", "damage", "give", "complete", "clear", "speed", "pause", "victory", "camera", "wait",
                     "help", "panel", "cls" })
            Assert.Contains(n, names);
    }

    // ------------------------------------------------------------ completion and hints

    [Fact]
    public void CompleteCommandNames()
    {
        var ctx = Ctx();
        Assert.Equal("spawn ", SandboxConsoleParser.Complete("spa", ctx));
        Assert.Equal("instabuild ", SandboxConsoleParser.Complete("INS", ctx));
        Assert.Equal("sp", SandboxConsoleParser.Complete("s", ctx));   // spawn, speed: common prefix
        Assert.Null(SandboxConsoleParser.Complete("sp", ctx));         // still ambiguous
        Assert.Null(SandboxConsoleParser.Complete("xyz", ctx));
        Assert.Null(SandboxConsoleParser.Complete("", ctx));
    }

    [Fact]
    public void CompleteSpawnUnitAndArgumentWords()
    {
        var ctx = Ctx(db: SmallDb);
        Assert.Equal("spawn uef_t1_tank ", SandboxConsoleParser.Complete("spawn t1_ta", ctx));
        Assert.Equal("spawn tank_hunter ", SandboxConsoleParser.Complete("spawn ta", ctx)); // id prefix beats id contains
        Assert.Equal("s uef_t1_tank ", SandboxConsoleParser.Complete("s striker", ctx));
        Assert.Null(SandboxConsoleParser.Complete("spawn qqq", ctx));
        Assert.Equal("spawn tank 5 enemy ", SandboxConsoleParser.Complete("spawn tank 5 en", ctx));
        Assert.Equal("ai 2 hard ", SandboxConsoleParser.Complete("ai 2 ha", ctx));
        Assert.Equal("money enemy ", SandboxConsoleParser.Complete("money en", ctx));
    }

    [Fact]
    public void SuggestionsListUnitsAndCommands()
    {
        var ctx = Ctx();
        var units = SandboxConsoleParser.Suggestions("spawn ta", ctx);
        Assert.NotEmpty(units);
        Assert.True(units.Count <= 6);
        Assert.StartsWith("uef_t1_tank  MA12 Striker (T1)", units[0]);

        var cmds = SandboxConsoleParser.Suggestions("sp", ctx);
        Assert.Contains(cmds, l => l.StartsWith("spawn "));
        Assert.Contains(cmds, l => l.StartsWith("speed "));

        string speedHelp = SandboxConsoleParser.HelpLines[SandboxConsoleParser.CommandNames.ToList().IndexOf("speed")];
        Assert.Equal(speedHelp, Assert.Single(SandboxConsoleParser.Suggestions("speed ", ctx)));
        Assert.Empty(SandboxConsoleParser.Suggestions("", ctx));
    }
}
