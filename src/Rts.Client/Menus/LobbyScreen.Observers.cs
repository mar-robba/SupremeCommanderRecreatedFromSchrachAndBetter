using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.UI;
using Rts.Net.Lobby;

namespace Rts.Client.Menus;

/// <summary>
/// (10a-2) Lobby observers, pings and sounds.
/// <list type="bullet">
/// <item>Observers row under the slots: every observer with its ping; "Observe" / "Play" moves the local player out of
///   / back into a slot; the host also moves anybody ("Obs" next to a slot, "Play" next to an observer) and kicks
///   observers.</item>
/// <item>Per-slot ping (the host's 1 s ping list, <see cref="Rts.Net.Session.NetSession.PingOf"/>).</item>
/// <item>Sounds (existing UI cues): chat from others = click; somebody joined / became ready / the game starts =
///   confirm; somebody left = error. Join/leave/ready come from lobby diffs, so the host and clients behave alike.</item>
/// </list>
/// </summary>
public sealed partial class LobbyScreen
{
    /// <summary>Height of the observers row at the bottom of the players panel.</summary>
    private static float ObserversRowH => Ui.P(34);

    private Dictionary<int, bool>? _lastPeople; // player id → ready (observers count as ready)

    /// <summary>Plays the join / leave / ready cues from the change of the lobby since the last frame.</summary>
    private void LobbySounds(LobbyState l)
    {
        var now = new Dictionary<int, bool>();
        foreach (var s in l.People) now[s.PlayerId] = s.Ready || s.Kind == NetSlotKind.Observer;
        if (_lastPeople != null)
        {
            bool joined = false, left = false, ready = false;
            foreach (var (pid, r) in now)
            {
                if (!_lastPeople.TryGetValue(pid, out bool was)) joined = true;
                else if (r && !was) ready = true;
            }
            foreach (var pid in _lastPeople.Keys) if (!now.ContainsKey(pid)) left = true;
            if (joined || ready) UiSounds.Confirm();
            if (left) UiSounds.Error();
        }
        _lastPeople = now;
    }

    /// <summary>Ping text of a lobby person ("" for the local player, the host's own row or no value yet).</summary>
    private string PingText(int playerId, out Color color)
    {
        int ms = playerId == _s.PlayerId ? -1 : _s.PingOf(playerId);
        color = ms > 250 ? Ui.Warn : Ui.TextDim;
        return ms > 0 ? $"{ms} ms" : "";
    }

    /// <summary>Host only, next to a human slot: move that player to the observers.</summary>
    private void SlotToObserverButton(Rectangle r, NetSlot s)
    {
        if (_host == null) return;
        if (MenuUi.BigButton(r, "Obs", true, false, 10.5f)) _host.SetObserver(s.PlayerId, true);
        if (Ui.Hover(r)) Ui.Tooltip(r, "Make observer", $"{s.Name} watches the game instead of playing");
    }

    private void DrawObservers(Rectangle row, LobbyState l)
    {
        Raylib.DrawRectangleRec(row, new Color(24, 32, 44, 140));
        int fs = Ui.F(12);
        float x = row.X + Ui.P(8), ty = row.Y + (row.Height - fs) * 0.5f - 1, bh = row.Height - Ui.P(8);
        string head = $"OBSERVERS ({l.Observers.Count})";
        Ui.Text(head, x, ty, Ui.F(10.5f), Ui.TextFaint, true);
        x += Ui.Measure(head, Ui.F(10.5f), true) + Ui.P(12);

        // Own toggle at the right edge.
        bool me = l.IsObserver(_s.PlayerId);
        bool canPlay = l.Slots.Any(s => s.Kind == NetSlotKind.Open);
        string label = me ? "Play" : "Observe";
        var tb = Ui.R(row.X + row.Width - Ui.P(96), row.Y + Ui.P(4), Ui.P(88), bh);
        if (MenuUi.BigButton(tb, label, me ? canPlay : l.Observers.Count < LobbyState.MaxObservers, false, 12)) _s.Observe(!me);
        if (Ui.Hover(tb)) Ui.Tooltip(tb, me ? "Play" : "Observe", me ? (canPlay ? "Take the first open slot" : "No open slot") : "Watch the whole game without an army; chat with everybody");
        float right = tb.X - Ui.P(10);

        if (l.Observers.Count == 0)
        {
            Ui.Text("none: any number can watch the game", x, ty, Ui.F(11), Ui.TextFaint);
            return;
        }
        foreach (var o in l.Observers)
        {
            bool mine = o.PlayerId == _s.PlayerId;
            string name = o.PlayerId == 0 ? o.Name + " (host)" : o.Name;
            string ping = PingText(o.PlayerId, out var pc);
            float nw = Ui.Measure(name, fs, true), pw = ping.Length > 0 ? Ui.Measure(ping, Ui.F(10.5f)) + Ui.P(6) : 0;
            float hostW = _host != null && o.PlayerId != 0 ? Ui.P(72) : 0;
            if (x + nw + pw + hostW > right) { Ui.Text("…", x, ty, fs, Ui.TextDim); break; }
            Ui.Text(name, x, ty, fs, mine ? Ui.Accent : !o.MapReady ? Ui.TextDim : Ui.TextColor, true);
            x += nw + Ui.P(6);
            if (ping.Length > 0) { Ui.Text(ping, x, ty + Ui.P(1), Ui.F(10.5f), pc); x += pw; }
            if (hostW > 0)
            {
                var pb = Ui.R(x, row.Y + Ui.P(4), Ui.P(40), bh);
                if (MenuUi.BigButton(pb, "Play", canPlay, false, 10.5f)) _host!.SetObserver(o.PlayerId, false);
                if (Ui.Hover(pb)) Ui.Tooltip(pb, "Give a slot", $"{o.Name} takes the first open slot");
                var kb = Ui.R(pb.X + pb.Width + Ui.P(4), row.Y + Ui.P(4), Ui.P(24), bh);
                if (MenuUi.BigButton(kb, "×", true, false, 12)) _host!.KickObserver(o.PlayerId);
                if (Ui.Hover(kb)) Ui.Tooltip(kb, "Kick", $"Remove {o.Name} from the game");
                x = kb.X + kb.Width;
            }
            x += Ui.P(14);
        }
    }
}
