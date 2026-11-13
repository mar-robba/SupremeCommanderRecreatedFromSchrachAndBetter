using Raylib_cs;
using Rts.Client.UI;
using Rts.Net.Lobby;
using Rts.Sim;

namespace Rts.Client.Menus;

/// <summary>
/// (10a-3) The players panel of a load lobby (a multiplayer save the host opened): one row per saved army with its saved
/// player, fixed faction / team / colour, who holds the slot now (a player who joined with the saved name takes it; a
/// free slot can be taken with "Take" or handed to the AI by the host) and each person's copy of the save (present,
/// downloading with its percentage). The reason the start is blocked is the lobby's Problem() in the bottom bar.
/// </summary>
public sealed partial class LobbyScreen
{
    private void DrawLoadPlayers(Rectangle p, LobbyState l)
    {
        var load = l.Load!;
        Ui.Panel(p);
        Ui.PanelTitle(p, $"Saved game \"{load.SaveName}\" · {TimeSpan.FromSeconds(load.Tick / 10):h\\:mm\\:ss} played · {load.FileBytes / 1024} KB");
        float x = p.X + Ui.P(12), y = p.Y + Ui.P(28);
        float rowH = Ui.P(28), gap = Ui.P(6);
        float wNum = Ui.P(24), wSaved = Ui.P(150), wFac = Ui.P(96), wTeam = Ui.P(58), wCol = Ui.P(34), wNow = Ui.P(150);
        float cx1 = x + wNum, cxF = cx1 + wSaved + gap, cxT = cxF + wFac + gap, cxC = cxT + wTeam + gap, cxN = cxC + wCol + gap;
        float cxS = cxN + wNow + gap, wStat = p.X + p.Width - Ui.P(12) - cxS;
        int hs = Ui.F(10.5f);
        Ui.Text("SAVED PLAYER", cx1, y, hs, Ui.TextFaint, true);
        Ui.Text("FACTION", cxF, y, hs, Ui.TextFaint, true);
        Ui.Text("TEAM", cxT, y, hs, Ui.TextFaint, true);
        Ui.Text("COLOR", cxC, y, hs, Ui.TextFaint, true);
        Ui.Text("PLAYED BY", cxN, y, hs, Ui.TextFaint, true);
        Ui.Text("SAVE FILE", cxS, y, hs, Ui.TextFaint, true);
        y += Ui.P(17);

        bool seated = l.Slots.Exists(s => s.Kind == NetSlotKind.Human && s.PlayerId == _s.PlayerId);
        int armyNo = 0;
        for (int i = 0; i < l.Slots.Count; i++)
        {
            var s = l.Slots[i];
            if (!s.IsArmy && !LobbyState.IsFreeSavedSlot(s)) continue;
            bool mine = s.Kind == NetSlotKind.Human && s.PlayerId == _s.PlayerId;
            var row = Ui.R(p.X + Ui.P(6), y - Ui.P(3), p.Width - Ui.P(12), rowH + Ui.P(6));
            Raylib.DrawRectangleRec(row, mine ? new Color(34, 58, 84, 150) : new Color(30, 42, 56, 120));
            float ty = y + (rowH - Ui.F(13)) * 0.5f - 1, sy = y + (rowH - Ui.F(12)) * 0.5f - 1;
            Ui.Text((++armyNo).ToString(), x + Ui.P(4), ty, Ui.F(13), Ui.TextColor, true);

            string saved = s.SavedName.Length > 0 ? s.SavedName : s.Kind == NetSlotKind.AI ? $"AI {s.Difficulty}" : s.Name;
            Ui.Text(Ui.Fit(saved, wSaved - Ui.P(8), Ui.F(13), true), cx1 + Ui.P(4), ty, Ui.F(13), Ui.TextColor, true);
            var (facName, facColor) = FactionLabel(s.Faction);
            Ui.Text(Ui.Fit(facName, wFac, Ui.F(12)), cxF, sy, Ui.F(12), facColor);
            Ui.Text(s.Team == 0 ? "None" : $"Team {s.Team}", cxT, sy, Ui.F(12), Ui.TextDim);
            var col = ColorUtil.FromRgba(GameSetup.ArmyColors[s.Color % GameSetup.ArmyColors.Length]);
            MenuUi.ColorSwatch(Ui.R(cxC, y + Ui.P(3), wCol, rowH - Ui.P(6)), col, false);

            // Played by: the person in the slot, the AI, or free (Take / give to the AI).
            if (s.Kind == NetSlotKind.Human)
            {
                string who = s.PlayerId == 0 ? s.Name + "  (host)" : s.Name;
                Ui.Text(Ui.Fit(who, wNow - Ui.P(4), Ui.F(13), true), cxN, ty, Ui.F(13), mine ? Ui.Accent : Ui.TextColor, true);
            }
            else if (s.Kind == NetSlotKind.AI)
            {
                Ui.Text(s.SavedName.Length > 0 ? $"AI {s.Difficulty} (takes over)" : $"AI {s.Difficulty}", cxN, sy, Ui.F(12), Ui.TextDim, true);
                if (_host != null && s.SavedName.Length > 0)
                {
                    var fb = Ui.R(cxS + wStat - Ui.P(64), y + Ui.P(3), Ui.P(62), rowH - Ui.P(6));
                    if (MenuUi.BigButton(fb, "Free", true, false, 11)) _host.SetSlotKind(i, NetSlotKind.Open);
                    if (Ui.Hover(fb)) Ui.Tooltip(fb, "Free the slot", $"Wait for {s.SavedName} to join instead of the AI");
                }
            }
            else
            {
                Ui.Text("free", cxN, sy, Ui.F(12), Ui.Warn, true);
                float bx = cxN + Ui.P(44);
                if (!seated)
                {
                    var tb = Ui.R(bx, y + Ui.P(3), Ui.P(52), rowH - Ui.P(6));
                    if (MenuUi.BigButton(tb, "Take", true, false, 11)) MoveTo(i);
                    if (Ui.Hover(tb)) Ui.Tooltip(tb, "Take this slot", $"Play {s.SavedName}'s army");
                }
                if (_host != null)
                {
                    var ab = Ui.R(cxS + wStat - Ui.P(64), y + Ui.P(3), Ui.P(62), rowH - Ui.P(6));
                    if (MenuUi.BigButton(ab, "AI plays", true, false, 11)) _host.SetSlotKind(i, NetSlotKind.AI);
                    if (Ui.Hover(ab)) Ui.Tooltip(ab, "Hand to the AI", $"{s.SavedName} is not coming: the AI plays this army from the saved tick");
                }
            }

            // Save file: the copy of the person in the slot (the host always has it).
            if (s.Kind == NetSlotKind.Human)
            {
                if (s.PlayerId == 0) Ui.Text("Host's copy", cxS, sy, Ui.F(12), Ui.Accent, true);
                else if (!s.SaveReady) Ui.Text(s.SaveProgress > 0 ? $"Downloading {s.SaveProgress} %" : "Checking…", cxS, sy, Ui.F(12), Ui.Warn, true);
                else Ui.Text(s.Ready ? "Ready" : "Not ready", cxS, sy, Ui.F(12), s.Ready ? Ui.Good : Ui.Warn, true);
                if (_host != null && s.PlayerId != 0)
                {
                    var kb = Ui.R(cxS + wStat - Ui.P(26), y + Ui.P(3), Ui.P(24), rowH - Ui.P(6));
                    if (MenuUi.BigButton(kb, "×", true, false, 12)) _host.Kick(i);
                    if (Ui.Hover(kb)) Ui.Tooltip(kb, "Kick", $"Remove {s.Name} from the game");
                }
            }
            y += rowH + Ui.P(8);
        }
        if (_s.SaveTransfer is { } t && t.State != Rts.Net.Session.MapTransferState.Present)
        {
            string text = t.State switch
            {
                Rts.Net.Session.MapTransferState.Downloading => $"Your copy of the save: downloading {t.Percent} %",
                Rts.Net.Session.MapTransferState.Verifying => "Your copy of the save: verifying…",
                _ => "Your copy of the save: " + (t.Error ?? "failed"),
            };
            Ui.Text(text, x, y + Ui.P(4), Ui.F(12), t.State == Rts.Net.Session.MapTransferState.Failed ? Ui.Bad : Ui.Warn, true);
        }
        else if (!seated && !_s.IsLocalObserver)
            Ui.Text("Take a free slot to play (players who join with a saved name get their slot)", x, y + Ui.P(4), Ui.F(11.5f), Ui.TextDim);
        DrawObservers(Ui.R(p.X + Ui.P(6), p.Y + p.Height - ObserversRowH - Ui.P(4), p.Width - Ui.P(12), ObserversRowH - Ui.P(2)), l);
    }
}
