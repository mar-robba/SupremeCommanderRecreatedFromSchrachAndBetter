using Raylib_cs;
using Rts.Client.Persistence;
using Rts.Client.UI;
using Rts.Net.Session;

namespace Rts.Client.Menus;

/// <summary>
/// (10a-2) Observers and the vote to drop a stalled player.
/// <list type="bullet">
/// <item>An observer (lobby observer slot, or a defeated player who resigned) has a read-only session without fog;
///   clicking a row of the players panel shows that army's view (fog of war, resources), the top row = all armies.</item>
/// <item>"Waiting for X…": after <see cref="NetGame.DropVoteDelaySeconds"/> a countdown turns into a "Drop X" button
///   for every other player (not for observers, not against the host); the tally comes from the host, which drops X
///   once a strict majority of the remaining players voted (the host's silence timeout still applies).</item>
/// </list>
/// </summary>
public sealed partial class NetInGameScreen
{
    private Rectangle _playersRect, _waitRect;
    private int _waitTurn = -1;
    private bool _observing;
    private const string DemoName = "Bob"; // --net-vote-demo

    /// <summary>The local peer only watches (observer slot, or resigned after its defeat).</summary>
    private bool Observing => _observing || _game.IsObserver;

    /// <summary>Army whose view is shown (-1 = all, no fog); F7 toggles the fog too, so read it back.</summary>
    private int ViewArmy => _s.Fog.Enabled ? _s.Ctx.LocalArmy : -1;

    private void InitObserver()
    {
        _s.MouseOverExtraUi = () => _state == State.Playing && ((Observing && Ui.Hover(_playersRect)) || Ui.Hover(_waitRect));
        if (!_game.IsObserver) return;
        _observing = true;
        _s.Ctx.ReadOnly = true;
        SetView(-1);
        Toasts.Show("Observing: click a player at the right to follow its view");
    }

    /// <summary>Defeated while the game goes on: stop sending turns (the others drop this army as defeated and never
    /// wait for it again) and keep watching the whole map.</summary>
    private void BecomeObserver()
    {
        _game.Resign();
        _observing = true;
        _s.Ctx.ReadOnly = true;
        SetView(-1);
    }

    private void SetView(int army)
    {
        if (army >= 0)
        {
            _s.Ctx.LocalArmy = army;
            _s.Fog.SetEnabled(true);
        }
        else _s.Fog.SetEnabled(false);
        _s.Selection.Selected.Clear();
    }

    /// <summary>Clickable background of one players-panel row (observers); army -1 = the "all armies" row.</summary>
    private void ViewRow(Rectangle panel, float y, float lineH, int army, int fs)
    {
        var r = Ui.R(panel.X + Ui.P(3), y - Ui.P(2), panel.Width - Ui.P(6), lineH);
        bool sel = ViewArmy == army;
        if (Ui.Button(r, _state == State.Playing, sel, sel ? MenuUi.PrimaryTint : null).Left) SetView(army);
        if (army < 0) Ui.Text("All armies (no fog)", panel.X + Ui.P(22), y, fs, sel ? Ui.Accent : Ui.TextColor, true);
        if (Ui.Hover(r)) Ui.Tooltip(r, army < 0 ? "Whole map" : "Follow this army", army < 0 ? "No fog of war" : "Its fog of war, resources and alerts");
    }

    /// <summary>Under the "Waiting for X…" banner: the countdown, then the Drop button and the host's tally.</summary>
    private void DrawDropVote(Rectangle banner, double waited)
    {
        _waitRect = default;
        int turn = _game.WaitingForTurn;
        DropVoteTally? votes = _game.Votes;
        string name = _opt.NetVoteDemo ? DemoName : _game.WaitingFor ?? "?";
        bool canVote = _game.CanVoteDrop(turn);
        if (_opt.NetVoteDemo) // screenshot hook: pretend the wait is long enough and one vote is in
        {
            canVote = true;
            waited = NetGame.DropVoteDelaySeconds + 1;
            votes = new DropVoteTally(turn, 1, NetGame.VotesNeeded(2), new[] { -2 }); // another player's vote
        }
        else if (turn != _waitTurn)
        {
            _waitTurn = turn; // another player stalls now: the countdown restarts
            _waitSince = Raylib.GetTime();
            waited = 0;
        }
        if (votes != null && votes.Target != turn) votes = null;
        float cx = banner.X + banner.Width * 0.5f, y = banner.Y + banner.Height + Ui.P(5);
        int fs = Ui.F(11.5f);
        string tally = votes != null ? $"{votes.Votes}/{votes.Needed} votes to drop {name}" : "";
        if (!canVote)
        {
            string info = turn >= 0 && turn == _game.HostTurn ? "Waiting for the host (it can't be voted out)" : tally;
            if (info.Length > 0) Ui.TextShadow(info, cx - Ui.Measure(info, fs) * 0.5f, y, fs, Ui.TextDim);
            return;
        }
        double left = NetGame.DropVoteDelaySeconds - waited;
        if (left > 0)
        {
            string t = $"Vote to drop {name} in {Math.Ceiling(left):0} s";
            Ui.TextShadow(t, cx - Ui.Measure(t, fs) * 0.5f, y, fs, Ui.TextDim);
            return;
        }
        bool voted = votes?.HasVoted(_game.LocalTurn) == true;
        string label = voted ? "Cancel my vote" : "Drop " + name;
        float bw = Ui.Measure(label, Ui.F(13), true) + Ui.P(36);
        var br = Ui.R(cx - bw * 0.5f, y, bw, Ui.P(28));
        _waitRect = br;
        var st = Ui.Button(br, _state == State.Playing, false, voted ? null : MenuUi.DangerTint);
        Ui.TextCentered(label, br, Ui.F(13), Color.White, true);
        if (st.Left && !_opt.NetVoteDemo) _game.VoteDrop(turn, !voted);
        if (Ui.Hover(br)) Ui.Tooltip(br, voted ? "Withdraw the vote" : $"Vote to drop {name}",
            $"A majority of the other players drops {name} (its army: as set in the lobby)");
        string below = tally.Length > 0 ? tally : $"{NetGame.VotesNeeded(Math.Max(1, _game.PlayerStatus().Count(p => !p.Dropped) - 1))} votes needed";
        Ui.TextShadow(below, cx - Ui.Measure(below, fs) * 0.5f, br.Y + br.Height + Ui.P(4), fs, Ui.TextDim);
    }
}
