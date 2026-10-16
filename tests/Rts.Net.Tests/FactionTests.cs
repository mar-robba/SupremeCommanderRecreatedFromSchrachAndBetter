using Rts.Net.Lobby;
using Rts.Net.Session;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>Lobby factions: mapped into the skirmish settings (every peer builds the same armies), checked by the host.</summary>
public class FactionTests
{
    [Fact]
    public void LobbyFactionsReachTheSimulation()
    {
        var l = LobbyState.CreateDefault("Host");
        l.Slots[1].Kind = NetSlotKind.AI;
        l.Slots[0].Faction = "random";
        l.Slots[1].Faction = "uef";
        var sim = NetGameSetup.CreateSimulation(Db, GameStart.From(l, 2));
        Assert.Equal(2, sim.World.Armies.Length);
        foreach (var a in sim.World.Armies) Assert.NotNull(Db.Factions.Get(a.Faction));
        Assert.Equal("uef", sim.World.Armies[1].Faction);
        // "random" is resolved from the seed: identical on every peer.
        Assert.Equal(sim.World.Armies[0].Faction, NetGameSetup.CreateSimulation(Db, GameStart.From(l, 2)).World.Armies[0].Faction);
    }

    [Fact]
    public void HostRefusesUnknownFactions()
    {
        var host = HostSession.Start("Host", 0, lanAnnounce: false);
        try
        {
            host.FactionKnown = id => Db.Factions.Get(id) != null;
            host.SetSlotKind(1, NetSlotKind.AI);
            host.SetSlot(1, faction: "nope");
            Assert.Contains("Unknown faction 'nope'", host.StartGame());
            Assert.Equal(NetPhase.Lobby, host.Phase);
            host.SetSlot(1, faction: "random");
            Assert.Null(host.StartGame());
        }
        finally { host.Leave(); }
    }
}
