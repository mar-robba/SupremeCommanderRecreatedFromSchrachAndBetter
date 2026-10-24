using Rts.Net.Headless;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim;
using Rts.Sim.Persistence;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>Lobby game options (victory, cap, resources, fog, speed, cheats, prebuilt): codec, every peer's sim, speed lock.</summary>
public class GameOptionsNetTests
{
    private static GameOptions Custom() => new()
    {
        Victory = VictoryCondition.Annihilation, UnitCap = 250, Resources = StartResources.High, Fog = FogMode.Explored,
        Speed = GameSpeedMode.Fast, Cheats = true, Prebuilt = true,
    };

    [Fact]
    public void LobbyOptionsRoundTripTheGameOptions()
    {
        var l = LobbyState.CreateDefault("Host");
        l.Options.Game = Custom();
        var w = new ByteWriter(256);
        GameStart.From(l, 2).Write(w);
        var back = GameStart.Read(new ByteReader(w.ToArray()));
        Assert.True(back.Lobby.Options.Game.SameAs(Custom()), back.Lobby.Options.Game.Describe());
        Assert.True(NetGameSetup.ToSkirmish(back).Options!.SameAs(Custom()));

        // Defaults: the lobby starts from the setup defaults (cheats off); Clone is deep.
        var d = new LobbyOptions();
        Assert.True(d.Game.SameAs(GameOptions.SetupDefaults()));
        var c = d.Clone();
        c.Game.UnitCap = 500;
        Assert.Equal(1000, d.Game.UnitCap);

        // A hostile cap is clamped on read.
        var lo = new LobbyOptions();
        lo.Game.UnitCap = 0;
        var w2 = new ByteWriter(64);
        lo.Write(w2);
        Assert.Equal(1, LobbyOptions.Read(new ByteReader(w2.ToArray())).Game.UnitCap);
    }

    [Fact]
    public void LoopbackMatchWithOptionsStaysInSync()
    {
        var want = new GameOptions
        {
            Victory = VictoryCondition.Annihilation, UnitCap = 250, Fog = FogMode.Explored, Prebuilt = true, Cheats = false,
        };
        var (host, clients) = MakeLobby(2, 1, o => o.Game = want.Clone());
        var peers = Start(host, clients);
        try
        {
            foreach (var p in peers)
            {
                Assert.True(p.Sim.World.Options.SameAs(want), p.Sim.World.Options.Describe());
                // Prebuilt: every army starts with more than its commander.
                foreach (var a in p.Sim.World.Armies) Assert.True(CountUnits(p, a.Index) > 1);
            }
            foreach (var p in peers) p.Run(300, 0.3, null);
            foreach (var p in peers) Assert.True(p.Wait(60000), $"{p.Session.LocalName} did not finish");
            foreach (var p in peers) Assert.Null(p.Fault);
            Assert.Equal(peers[0].Hashes.Count, peers[1].Hashes.Count);
            Assert.NotEmpty(peers[0].Hashes);
            for (int i = 0; i < peers[0].Hashes.Count; i++) Assert.Equal(peers[0].Hashes[i], peers[1].Hashes[i]);
            Assert.All(peers, p => Assert.Equal(-1, p.Game.DesyncTick));
        }
        finally { Close(host, clients, peers); }
    }

    private static int CountUnits(HeadlessPeer p, int army)
    {
        var u = p.Sim.World.Units;
        int n = 0;
        for (int i = 0; i < u.Capacity; i++) if (u.Alive[i] && u.Army[i] == army) n++;
        return n;
    }

    [Theory]
    [InlineData(GameSpeedMode.Normal, 1f)]
    [InlineData(GameSpeedMode.Fast, 2f)]
    public void LockedSpeedRefusesSpeedChangesButNotPause(GameSpeedMode mode, float locked)
    {
        var (host, clients) = MakeLobby(2, 0, o => o.Game.Speed = mode);
        var peers = Start(host, clients);
        try
        {
            Assert.Equal(locked, host.Game!.Speed);
            Assert.Equal(locked, clients[0].Game!.Speed);
            clients[0].Game!.RequestSpeed(4f, true);
            WaitFor(() => host.Game!.Paused && clients[0].Game!.Paused, "pause everywhere");
            Assert.Equal(locked, host.Game!.Speed);
            Assert.Equal(locked, clients[0].Game!.Speed);
            host.Game.RequestSpeed(0.5f, false);
            WaitFor(() => !clients[0].Game!.Paused, "resume on the client");
            Assert.Equal(locked, clients[0].Game!.Speed);
        }
        finally { Close(host, clients, peers); }
    }
}
