using Rts.Client.Menus;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>Multiplayer on the client side: CLI options and the lobby → GameConfig mapping (replays, preview).</summary>
public class NetGameConfigTests
{
    [Fact]
    public void MultiplayerOptionsAreParsed()
    {
        var o = ClientOptions.Parse(["--host", "--name", "Ann"]);
        Assert.Equal(NetSession.DefaultPort, o.HostPort);
        Assert.Equal("Ann", o.PlayerName);
        Assert.Equal(StartScreen.Multiplayer, o.StartScreen);
        Assert.Equal(5000, ClientOptions.Parse(["--host", "5000", "--autostart"]).HostPort);
        o = ClientOptions.Parse(["--join", "10.0.0.2:5000"]);
        Assert.Equal("10.0.0.2:5000", o.JoinAddress);
        Assert.Equal(0, o.HostPort);
        Assert.Equal(StartScreen.Multiplayer, o.StartScreen);
        Assert.Equal(StartScreen.MainMenu, ClientOptions.Parse(["--name", "Ann"]).StartScreen);
    }

    [Fact]
    public void LobbySlotsMapOntoTheConfig()
    {
        var l = LobbyState.CreateDefault("Host");
        var bob = l.Slots[1];
        bob.Kind = NetSlotKind.Human;
        bob.PlayerId = 7;
        bob.Name = "Bob";
        bob.Faction = "random";
        bob.Team = 2;
        bob.Color = 5;
        l.Slots[2].Kind = NetSlotKind.AI;
        l.Slots[2].Difficulty = AiDifficulty.Hard;
        l.Options.Seed = 77;
        var cfg = NetGameConfig.From(l, 7);
        // The local player is the Human slot; remote humans and AIs are AI slots (names come from the sim's armies).
        Assert.Equal([SlotKind.AI, SlotKind.Human, SlotKind.AI], cfg.OpenSlots.Select(s => s.Kind).ToArray());
        Assert.Equal(SlotKind.Closed, cfg.Slots[3].Kind);
        Assert.Equal(FactionDb.DefaultFaction, cfg.Slots[0].Faction);
        Assert.Equal("random", cfg.Slots[1].Faction);
        Assert.Equal(2, cfg.Slots[1].Team);
        Assert.Equal(5, cfg.Slots[1].ColorIndex);
        Assert.Equal(AiDifficulty.Hard, cfg.Slots[2].Difficulty);
        Assert.Equal(77, cfg.Seed);
        Assert.Equal(3, cfg.MapSettings().Players);
    }
}
