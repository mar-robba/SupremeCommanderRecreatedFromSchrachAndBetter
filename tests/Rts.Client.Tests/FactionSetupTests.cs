using Rts.Client.Menus;
using Rts.Client.Persistence;
using Rts.Client.Sandbox;
using Rts.Sim.Data;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Factions on the client side: --faction/--factions, setup slot choices, validation, saved client data, sandbox filter.</summary>
public class FactionSetupTests
{
    private static readonly BlueprintDb Db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);

    [Fact]
    public void FactionOptionsAreParsedAndStartAGame()
    {
        var o = ClientOptions.Parse(["--faction", "UEF"]);
        Assert.Equal("UEF", o.Faction);
        Assert.Equal(StartScreen.Game, o.StartScreen);
        o = ClientOptions.Parse(["--factions", "uef,random,uef", "--players", "3"]);
        Assert.Equal(["uef", "random", "uef"], o.Factions);
        Assert.Null(o.Faction);
        Assert.Null(ClientOptions.Parse(["--demo"]).Factions);
    }

    [Fact]
    public void FactionsGoToTheSlotsAndTheHumanOverride()
    {
        var o = ClientOptions.Parse(["--players", "3", "--factions", "random, Random ,uef", "--faction", "UEF"]);
        var cfg = GameConfig.FromOptions(o);
        Assert.Equal("uef", cfg.Slots[0].Faction); // the human slot: --faction wins
        Assert.Equal("random", cfg.Slots[1].Faction);
        Assert.Equal("uef", cfg.Slots[2].Faction);
        Assert.Equal(FactionDb.DefaultFaction, cfg.Slots[3].Faction);

        var settings = cfg.ToSkirmish(out _);
        Assert.Equal(["uef", "random", "uef"], settings.Factions);
        var sim = GameSession.CreateSimulation(Db, cfg, out _);
        Assert.All(sim.World.Armies, a => Assert.NotNull(Db.Factions.Get(a.Faction))); // "random" resolved to a real faction
    }

    [Fact]
    public void UnknownFactionsBlockTheStart()
    {
        var cfg = new GameConfig();
        Assert.Null(cfg.Problem(Db.Factions));
        cfg.Slots[1].Faction = "random";
        Assert.Null(cfg.Problem(Db.Factions));
        cfg.Slots[1].Faction = "zz_no_such_faction";
        Assert.Contains("zz_no_such_faction", cfg.Problem(Db.Factions));
        Assert.Null(cfg.Problem()); // without a faction db only the slot rules are checked
        cfg.Slots[1].Kind = SlotKind.Closed;
        cfg.Slots[2].Kind = SlotKind.AI;
        Assert.Null(cfg.FactionProblem(Db.Factions)); // closed slots don't count
    }

    [Fact]
    public void SlotChoicesListEveryFactionThenRandom()
    {
        var choices = GameConfig.FactionChoices(Db.Factions);
        Assert.Equal(Db.Factions.Count + 1, choices.Count);
        Assert.Equal(Db.Factions.All.Select(f => f.Id), choices.Take(Db.Factions.Count));
        Assert.Equal(FactionDb.Random, choices[^1]);

        // Cycling visits every choice once and wraps both ways.
        string c = choices[0];
        var seen = new List<string>();
        for (int k = 0; k < choices.Count; k++) { seen.Add(c); c = GameConfig.NextFaction(Db.Factions, c, 1); }
        Assert.Equal(choices, seen);
        Assert.Equal(choices[0], c);
        Assert.Equal(choices[^1], GameConfig.NextFaction(Db.Factions, choices[0], -1));
        Assert.Equal(choices[0], GameConfig.NextFaction(Db.Factions, "zz_unknown", 1));
    }

    [Fact]
    public void SlotFactionsSurviveClientDataAndSaveHeaders()
    {
        var cfg = new GameConfig();
        cfg.Slots[1].Faction = "random";
        var back = ClientGameData.FromJson(ClientGameData.From(cfg).ToJson())!.ToConfig();
        Assert.Equal("uef", back.Slots[0].Faction);
        Assert.Equal("random", back.Slots[1].Faction);

        // Old client data without factions → default; save headers carry the resolved faction per army.
        Assert.Equal(FactionDb.DefaultFaction, ClientGameData.FromJson("{\"Slots\":[{\"Kind\":\"Human\"},{\"Kind\":\"AI\"}]}")!.ToConfig().Slots[1].Faction);
        var info = new GameInfo { LocalArmy = 0, Players = [new PlayerInfo { Faction = "uef" }, new PlayerInfo { Faction = "uef" }] };
        var fromHeader = ClientGameData.ConfigFor(info);
        Assert.Equal(["uef", "uef"], fromHeader.OpenSlots.Select(s => s.Faction));
    }

    [Fact]
    public void SandboxFactionFilter()
    {
        var tank = Db["uef_t1_tank"];
        Assert.True(SandboxPanel.MatchesFaction(tank, null));
        Assert.True(SandboxPanel.MatchesFaction(tank, "UEF"));
        Assert.False(SandboxPanel.MatchesFaction(tank, "zz_other"));
        foreach (var f in Db.Factions.All)
            Assert.Contains(Db.All, b => SandboxPanel.MatchesFaction(b, f.Id));
    }
}
