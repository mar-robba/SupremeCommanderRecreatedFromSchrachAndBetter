using Rts.Net.Lobby;
using Rts.Net.Session;
using static Rts.Net.Tests.NetTestUtil;

namespace Rts.Net.Tests;

/// <summary>(6b) "To allies" chat: the host relays an allies-only line only to the sender and its lobby team.</summary>
public class AlliesChatTests
{
    [Fact]
    public void AlliesOnlyLinesReachOnlyTheSendersTeam()
    {
        var (host, clients) = MakeLobby(3, 0);
        var p1 = clients[0];
        var p2 = clients[1];
        try
        {
            host.SetSlot(0, team: 1);
            p1.Request(new LobbyRequest { Team = 1 });
            p2.Request(new LobbyRequest { Team = 2 });
            WaitFor(() => p2.Lobby.Slots[1].Team == 1 && p2.Lobby.Slots[2].Team == 2 && p1.Lobby.Slots[0].Team == 1, "teams");

            // P1 (team 1) to allies: the host (team 1) and P1 itself see it, P2 (team 2) does not.
            p1.SendChat("push left", alliesOnly: true);
            p1.SendChat("public after");
            WaitFor(() => p2.ChatLog.Any(l => l.Text == "public after"), "public line on the enemy");
            WaitFor(() => host.ChatLog.Any(l => l.Text == "public after") && p1.ChatLog.Any(l => l.Text == "public after"), "public line");
            Assert.Contains(host.ChatLog, l => l.Text == "push left" && l.AlliesOnly && l.Name == "P1");
            Assert.Contains(p1.ChatLog, l => l.Text == "push left" && l.AlliesOnly);
            Assert.DoesNotContain(p2.ChatLog, l => l.Text == "push left");
            Assert.Contains(p2.ChatLog, l => l.Text == "public after" && !l.AlliesOnly);

            // The host to allies: P1 gets it, P2 does not.
            host.SendChat("host to team", alliesOnly: true);
            host.SendChat("host public");
            WaitFor(() => p2.ChatLog.Any(l => l.Text == "host public") && p1.ChatLog.Any(l => l.Text == "host public"), "host public");
            Assert.Contains(p1.ChatLog, l => l.Text == "host to team" && l.AlliesOnly && l.Name == "Host");
            Assert.Contains(host.ChatLog, l => l.Text == "host to team");
            Assert.DoesNotContain(p2.ChatLog, l => l.Text == "host to team");

            // P2 alone on team 2 (team 0 behaves the same): only P2 sees its own allies line.
            p2.Request(new LobbyRequest { Team = 0 });
            WaitFor(() => p2.Lobby.Slots[2].Team == 0, "team 0");
            p2.SendChat("nobody hears", alliesOnly: true);
            p2.SendChat("p2 public");
            WaitFor(() => p1.ChatLog.Any(l => l.Text == "p2 public") && host.ChatLog.Any(l => l.Text == "p2 public")
                && p2.ChatLog.Any(l => l.Text == "p2 public"), "p2 public");
            Assert.Contains(p2.ChatLog, l => l.Text == "nobody hears" && l.AlliesOnly);
            Assert.DoesNotContain(p1.ChatLog, l => l.Text == "nobody hears");
            Assert.DoesNotContain(host.ChatLog, l => l.Text == "nobody hears");
        }
        finally
        {
            foreach (var c in clients) c.Leave();
            host.Leave();
        }
    }
}
