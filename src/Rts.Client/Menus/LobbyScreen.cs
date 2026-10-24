using Raylib_cs;
using Rts.Client.UI;
using Rts.Net.Lobby;
using Rts.Net.Session;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Client.Menus;

/// <summary>
/// Multiplayer lobby (host or client): 8 slots (human / AI / open / closed) with faction, AI level, team, color and
/// ready flags; the map options (the host edits them, the others see them) with a live preview; chat; Ready (clients)
/// and Start (host). Everything shown comes from the session's lobby copy: edits are requests the host applies.
/// GameStarting → <see cref="NetLoadingScreen"/> (the session moves on with it); a lost connection → the
/// multiplayer menu. Leaving the screen any other way leaves the session.
/// </summary>
public sealed class LobbyScreen : IScreen
{
    private readonly AppHost _app;
    private readonly NetSession _s;
    private readonly HostSession? _host;
    private readonly ClientSession? _client;
    private readonly MapPreview _preview = new();
    private readonly NetTextField _chat = new("", NetSession.MaxChat);
    private string _seedText;
    private string? _status;
    private bool _handedOff;
    private bool _autoDone;
    private int _frames;
    private bool _escBlocked;

    /// <param name="newHost">A lobby just hosted: map options from the skirmish setup, AI slots from --players.</param>
    public LobbyScreen(AppHost app, NetSession session, string? status = null, bool newHost = false)
    {
        _app = app;
        _s = session;
        _host = session as HostSession;
        _client = session as ClientSession;
        _status = status;
        if (newHost && _host != null) InitHostOptions();
        _seedText = _s.Lobby.Options.Seed.ToString();
    }

    /// <summary>First visit of a new host lobby: map options from the skirmish setup, --players fills AI slots.</summary>
    private void InitHostOptions()
    {
        var c = _app.SetupConfig;
        var o = _host!.Lobby.Options.Clone();
        o.MapSize = c.MapSize;
        o.Preset = c.Preset;
        o.Water = c.WaterAmount;
        o.Seed = c.Seed;
        o.Game = c.Options.Clone(); // game options from the setup screen (or --options)
        _host.SetOptions(o);
        if (_app.Options.PlayersGiven)
            for (int i = 1; i < Math.Min(_app.Options.Players, LobbyState.MaxSlots); i++)
                _host.SetSlotKind(i, NetSlotKind.AI);
        _host.SetSlot(0, faction: _app.SetupConfig.Slots.FirstOrDefault(s => s.Kind == SlotKind.Human)?.Faction ?? "");
    }

    public string ScreenshotInfo => $"(lobby: {_s.Lobby.HumanCount} humans, {_s.Lobby.ArmyCount} armies, {(_s.IsHost ? "host" : "client")})";

    public void Frame(float dt)
    {
        _frames++;
        if (HandleEvents()) return;
        _escBlocked = _chat.Focused || MenuUi.FieldFocused; // Esc in a field only unfocuses it
        var lobby = _s.Lobby;
        _app.Backdrop.Draw(dt, 1.0f);
        MenuUi.Dim(70);
        MenuUi.BeginFrame();
        _preview.Request(NetGameConfig.From(lobby, _s.PlayerId).MapSettings());
        _preview.Update();

        float x0 = Ui.P(48), y0 = Ui.P(30);
        string where = _host != null ? $"Hosting on port {_host.Port}" : $"Connected to {_client!.HostEndpoint} · ping {_client.PingMs:0} ms";
        MenuUi.ScreenTitle("Multiplayer Lobby", where, x0, y0);

        float top = y0 + Ui.P(84);
        float bottomBar = Ui.Sh - Ui.P(62);
        float mapW = Ui.P(430);
        var mapPanel = Ui.R(Ui.Sw - Ui.P(48) - mapW, top, mapW, bottomBar - top - Ui.P(14));
        float leftW = MathF.Min(Ui.P(700), mapPanel.X - x0 - Ui.P(20));
        var playersPanel = Ui.R(x0, top, leftW, Ui.P(8 * 36 + 52));
        float lowY = playersPanel.Y + playersPanel.Height + Ui.P(14), lowH = bottomBar - Ui.P(14) - lowY;
        // Game options (left) and chat (right) under the players.
        float optW = MathF.Min(Ui.P(400), leftW * 0.58f);
        var optPanel = Ui.R(x0, lowY, optW, MathF.Min(lowH, GameOptionsBlock.Height(2)));
        var chatPanel = Ui.R(x0 + optW + Ui.P(14), lowY, leftW - optW - Ui.P(14), lowH);
        DrawPlayers(playersPanel, lobby);
        DrawMap(mapPanel, lobby);
        DrawGameOptions(optPanel, lobby);
        DrawChat(chatPanel);
        DrawBottom(x0, bottomBar, mapPanel.X + mapPanel.Width, lobby);
        AutoStart(lobby);
        Ui.DrawTooltip();
    }

    /// <summary>Returns true if the screen was left.</summary>
    private bool HandleEvents()
    {
        while (_s.TryGetEvent(out var e))
        {
            switch (e)
            {
                case GameStartingEvent gs:
                    _handedOff = true;
                    _app.Go(new NetLoadingScreen(_app, _s, gs.Game));
                    return true;
                case DisconnectedEvent d:
                    _handedOff = true; // already closed
                    _app.Go(new MultiplayerScreen(_app, "Disconnected: " + d.Reason));
                    return true;
                case StartAbortedEvent a:
                    _status = "Start aborted: " + a.Reason;
                    break;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ players

    private bool CanEdit(LobbyState l, int i)
    {
        var s = l.Slots[i];
        if (!s.IsArmy) return false;
        if (s.Kind == NetSlotKind.Human) return s.PlayerId == _s.PlayerId;
        return _host != null; // AI slots: host only
    }

    private void Edit(int slot, int team = -1, int color = -1, string? faction = null, AiDifficulty? difficulty = null)
    {
        if (_host != null) _host.SetSlot(slot, team, color, faction, difficulty);
        else if (_client != null && slot == _s.LocalSlot) _client.Request(new LobbyRequest { Team = team, Color = color, Faction = faction });
    }

    private void MoveTo(int slot)
    {
        if (_host != null) _host.MoveHost(slot);
        else _client?.Request(new LobbyRequest { MoveToSlot = slot });
    }

    private void DrawPlayers(Rectangle p, LobbyState l)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, $"Players ({l.ArmyCount} / {LobbyState.MaxSlots})");
        float x = p.X + Ui.P(12), y = p.Y + Ui.P(28);
        float rowH = Ui.P(28), gap = Ui.P(6);
        float wNum = Ui.P(24), wKind = Ui.P(170), wFac = Ui.P(104), wDiff = Ui.P(84), wTeam = Ui.P(78), wCol = Ui.P(50);
        float cx1 = x + wNum, cxF = cx1 + wKind + gap, cx2 = cxF + wFac + gap, cx3 = cx2 + wDiff + gap, cx4 = cx3 + wTeam + gap;
        float cx5 = cx4 + wCol + gap, wStat = p.X + p.Width - Ui.P(12) - cx5;
        int hs = Ui.F(10.5f);
        Ui.Text("PLAYER", cx1, y, hs, Ui.TextFaint, true);
        Ui.Text("FACTION", cxF, y, hs, Ui.TextFaint, true);
        Ui.Text("AI LEVEL", cx2, y, hs, Ui.TextFaint, true);
        Ui.Text("TEAM", cx3, y, hs, Ui.TextFaint, true);
        Ui.Text("COLOR", cx4, y, hs, Ui.TextFaint, true);
        Ui.Text("STATUS", cx5, y, hs, Ui.TextFaint, true);
        y += Ui.P(17);

        int armyNo = 0;
        for (int i = 0; i < l.Slots.Count; i++)
        {
            var s = l.Slots[i];
            bool army = s.IsArmy, mine = s.Kind == NetSlotKind.Human && s.PlayerId == _s.PlayerId, edit = CanEdit(l, i);
            var row = Ui.R(p.X + Ui.P(6), y - Ui.P(3), p.Width - Ui.P(12), rowH + Ui.P(6));
            if (army) Raylib.DrawRectangleRec(row, mine ? new Color(34, 58, 84, 150) : new Color(30, 42, 56, 120));
            Ui.Text(army ? (++armyNo).ToString() : "–", x + Ui.P(4), y + (rowH - Ui.F(13)) * 0.5f - 1, Ui.F(13), army ? Ui.TextColor : Ui.TextFaint, true);

            // Player column: the host cycles non-human slots Open → AI → Closed; anybody may take an open slot.
            var kr = Ui.R(cx1, y, wKind, rowH);
            if (s.Kind == NetSlotKind.Human)
            {
                string name = s.PlayerId == 0 ? s.Name + "  (host)" : s.Name;
                Ui.Text(Ui.Fit(name, wKind - Ui.P(8), Ui.F(13), true), cx1 + Ui.P(8), y + (rowH - Ui.F(13)) * 0.5f - 1, Ui.F(13), mine ? Ui.Accent : Ui.TextColor, true);
            }
            else if (_host != null)
            {
                string kind = s.Kind switch { NetSlotKind.AI => "Computer AI", NetSlotKind.Open => "Open", _ => "Closed" };
                int d = MenuUi.Cycle(kr, kind, true, s.Kind == NetSlotKind.Closed ? Ui.TextFaint : Ui.TextColor);
                if (d != 0)
                {
                    NetSlotKind[] order = [NetSlotKind.Open, NetSlotKind.AI, NetSlotKind.Closed];
                    int k = Array.IndexOf(order, s.Kind);
                    _host.SetSlotKind(i, order[((k + d) % 3 + 3) % 3]);
                }
            }
            else
            {
                string kind = s.Kind switch { NetSlotKind.AI => "Computer AI", NetSlotKind.Open => "Open", _ => "Closed" };
                Ui.Text(kind, cx1 + Ui.P(8), y + (rowH - Ui.F(13)) * 0.5f - 1, Ui.F(13), s.Kind == NetSlotKind.AI ? Ui.TextColor : Ui.TextFaint);
            }

            if (s.Kind == NetSlotKind.Open)
            {
                var take = Ui.R(cxF, y, wFac + gap + wDiff, rowH);
                if (MenuUi.BigButton(take, "Take this slot", true, false, 11.5f)) MoveTo(i);
            }
            else
            {
                var (facName, facColor) = FactionLabel(s.Faction);
                int d = MenuUi.Cycle(Ui.R(cxF, y, wFac, rowH), army ? facName : "—", edit, facColor);
                if (d != 0) Edit(i, faction: GameConfig.NextFaction(_app.Db.Factions, FactionOrDefault(s.Faction), d));

                bool ai = s.Kind == NetSlotKind.AI;
                d = MenuUi.Cycle(Ui.R(cx2, y, wDiff, rowH), ai ? s.Difficulty.ToString() : "—", ai && _host != null,
                    s.Difficulty switch { AiDifficulty.Easy => Ui.Good, AiDifficulty.Hard => Ui.Bad, _ => Ui.Warn });
                if (d != 0) Edit(i, difficulty: (AiDifficulty)(((int)s.Difficulty + d + 3) % 3));
            }

            if (army)
            {
                int d = MenuUi.Cycle(Ui.R(cx3, y, wTeam, rowH), s.Team == 0 ? "None" : $"Team {s.Team}", edit);
                if (d != 0) Edit(i, team: (s.Team + d + 5) % 5);

                var sw = Ui.R(cx4, y + Ui.P(3), wCol, rowH - Ui.P(6));
                var col = ColorUtil.FromRgba(GameSetup.ArmyColors[s.Color % GameSetup.ArmyColors.Length]);
                MenuUi.ColorSwatch(sw, col, edit && Ui.Hover(sw));
                if (edit && Ui.LeftClickIn(sw)) Edit(i, color: NextColor(l, i, +1));
                if (edit && Ui.RightClickIn(sw)) Edit(i, color: NextColor(l, i, -1));

                // Status: host / ready / AI; the host can kick other humans.
                float sy = y + (rowH - Ui.F(12)) * 0.5f - 1;
                if (s.Kind == NetSlotKind.AI) Ui.Text("AI", cx5, sy, Ui.F(12), Ui.TextDim, true);
                else if (s.PlayerId == 0) Ui.Text("Host", cx5, sy, Ui.F(12), Ui.Accent, true);
                else
                {
                    Ui.Text(s.Ready ? "Ready" : "Not ready", cx5, sy, Ui.F(12), s.Ready ? Ui.Good : Ui.Warn, true);
                    if (_host != null)
                    {
                        var kb = Ui.R(cx5 + wStat - Ui.P(26), y + Ui.P(3), Ui.P(24), rowH - Ui.P(6));
                        if (MenuUi.BigButton(kb, "×", true, false, 12)) _host.Kick(i);
                        if (Ui.Hover(kb)) Ui.Tooltip(kb, "Kick", $"Remove {s.Name} from the game");
                    }
                }
            }
            y += rowH + Ui.P(8);
        }
    }

    private static string FactionOrDefault(string id) => string.IsNullOrWhiteSpace(id) ? FactionDb.DefaultFaction : id;

    private (string Name, Color Color) FactionLabel(string id)
    {
        id = FactionOrDefault(id);
        if (string.Equals(id, FactionDb.Random, StringComparison.OrdinalIgnoreCase)) return ("Random", Ui.TextDim);
        var f = _app.Db.Factions.Get(id);
        if (f == null) return (id + "?", Ui.Bad);
        return (f.Name, f.ColorRgba != 0 ? ColorUtil.Scale(ColorUtil.FromRgba(f.ColorRgba), 1.25f) : Ui.TextColor);
    }

    private static int NextColor(LobbyState l, int i, int dir)
    {
        int n = GameSetup.ArmyColors.Length;
        var used = l.Slots.Where((o, j) => j != i && o.IsArmy).Select(o => o.Color % n).ToHashSet();
        int c = l.Slots[i].Color % n;
        for (int k = 0; k < n; k++)
        {
            c = ((c + dir) % n + n) % n;
            if (!used.Contains(c)) break;
        }
        return c;
    }

    // ------------------------------------------------------------------ map

    private void DrawMap(Rectangle p, LobbyState l)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "Map");
        float pad = Ui.P(14);
        float size = MathF.Min(p.Width - 2 * pad, p.Height * 0.46f);
        var pr = Ui.R(p.X + (p.Width - size) * 0.5f, p.Y + Ui.P(28), size, size);
        var colors = l.Slots.Where(s => s.IsArmy).Select(s => ColorUtil.FromRgba(GameSetup.ArmyColors[s.Color % GameSetup.ArmyColors.Length])).ToList();
        _preview.Draw(pr, colors);

        float y = pr.Y + pr.Height + Ui.P(6);
        var t = _preview.Terrain;
        string info = t == null ? "Generating…"
            : $"{t.StartPositions.Length} start positions · {t.Deposits.Count(d => d.Type == DepositType.Mass)} mass · {t.Deposits.Count(d => d.Type == DepositType.Hydrocarbon)} hydrocarbon";
        Ui.TextCentered(info, Ui.R(p.X, y, p.Width, Ui.P(16)), Ui.F(10.5f), Ui.TextDim);
        y += Ui.P(22);

        var o = l.Options;
        float lx = p.X + pad, cx = p.X + Ui.P(92), cw = p.X + p.Width - pad - cx, rowH = Ui.P(28), gap = Ui.P(8);
        if (_host == null)
        {
            // Clients: read-only summary.
            void Line(string label, string value)
            {
                MenuUi.Label(label, lx, y, rowH);
                Ui.Text(value, cx, y + (rowH - Ui.F(13)) * 0.5f - 1, Ui.F(13), Ui.TextColor, true);
                y += rowH + Ui.P(2);
            }
            Line("Size", $"{o.MapSize / 51.2f:0} km · {o.MapSize}");
            Line("Terrain", MapPresets.Name(o.Preset));
            Line("Water", $"{(o.Water < 0 ? MapPresets.DefaultWater(o.Preset) : o.Water) * 100:0}%");
            Line("Seed", o.Seed.ToString());
            Line("On drop", o.OnDrop == DropAction.AiTakeover ? "AI takes over" : "Defeated");
            Ui.Text("The host chooses the map.", lx, y + Ui.P(4), Ui.F(10.5f), Ui.TextFaint);
            return;
        }

        var n = o.Clone();
        MenuUi.Label("Size", lx, y, rowH);
        int si = Array.IndexOf(GameConfig.MapSizes, o.MapSize);
        int nsi = MenuUi.Segmented(Ui.R(cx, y, cw, rowH), ["5 km · 256", "10 km · 512", "20 km · 1024"], si, true, 11.5f);
        if (nsi != si && nsi >= 0) n.MapSize = GameConfig.MapSizes[nsi];
        y += rowH + gap;

        MenuUi.Label("Terrain", lx, y, rowH);
        var presets = MapPresets.All;
        int pi = Array.IndexOf(presets, o.Preset);
        int npi = MenuUi.Segmented(Ui.R(cx, y, cw, rowH), presets.Select(MapPresets.Name).ToArray(), pi, true, 10.5f);
        if (npi != pi && npi >= 0)
        {
            n.Preset = presets[npi];
            n.Water = -1;
        }
        y += rowH + gap;

        MenuUi.Label("Water", lx, y, rowH);
        float water = o.Water < 0 ? MapPresets.DefaultWater(o.Preset) : o.Water;
        float nw = MenuUi.Slider(2, Ui.R(cx, y + Ui.P(9), cw - Ui.P(56), Ui.P(10)), water, $"{water * 100:0}%");
        if (MathF.Abs(nw - water) > 0.004f) n.Water = MathF.Round(nw * 50f) / 50f;
        y += rowH + gap;

        MenuUi.Label("Seed", lx, y, rowH);
        var fr = Ui.R(cx, y, cw - Ui.P(118), rowH);
        if (MenuUi.NumberField(3, fr, ref _seedText) && int.TryParse(_seedText, out int seed)) n.Seed = seed;
        if (!MenuUi.FieldFocused && _seedText != o.Seed.ToString()) _seedText = o.Seed.ToString();
        var rb = Ui.R(fr.X + fr.Width + Ui.P(8), y, Ui.P(110), rowH);
        if (MenuUi.BigButton(rb, "Randomize", true, false, 12))
        {
            n.Seed = Random.Shared.Next(1, 1_000_000);
            _seedText = n.Seed.ToString();
        }
        y += rowH + gap;

        MenuUi.Label("On drop", lx, y, rowH);
        int di = o.OnDrop == DropAction.AiTakeover ? 0 : 1;
        int ndi = MenuUi.Segmented(Ui.R(cx, y, cw, rowH), ["AI takes over", "Defeated"], di, true, 11.5f);
        if (ndi != di && ndi >= 0) n.OnDrop = ndi == 0 ? DropAction.AiTakeover : DropAction.Defeat;

        if (n.MapSize != o.MapSize || n.Preset != o.Preset || n.Water != o.Water || n.Seed != o.Seed || n.OnDrop != o.OnDrop)
            _host.SetOptions(n);
    }

    // ------------------------------------------------------------------ game options

    /// <summary>Victory, cap, resources, fog, speed, cheats, prebuilt: the host edits a clone (→ SetOptions), clients read.</summary>
    private void DrawGameOptions(Rectangle p, LobbyState l)
    {
        if (_host == null)
        {
            GameOptionsBlock.Draw(p, l.Options.Game, false, 2, "set by the host");
            return;
        }
        var n = l.Options.Clone();
        if (GameOptionsBlock.Draw(p, n.Game, true, 2)) _host.SetOptions(n);
    }

    // ------------------------------------------------------------------ chat

    private void DrawChat(Rectangle p)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "Chat");
        float x = p.X + Ui.P(12), w = p.Width - Ui.P(24), lineH = Ui.F(12) + Ui.P(4);
        var input = Ui.R(x, p.Y + p.Height - Ui.P(36), w, Ui.P(28));
        float top = p.Y + Ui.P(28), bottom = input.Y - Ui.P(6);
        int maxLines = Math.Max(0, (int)((bottom - top) / lineH));
        var lines = new List<(string who, string text, Color c)>();
        var lobby = _s.Lobby;
        foreach (var l in _s.ChatLog)
        {
            if (l.IsSystem) { lines.Add(("", l.Text, Ui.TextFaint)); continue; }
            int slot = lobby.SlotOf(l.PlayerId);
            var c = slot >= 0 ? ColorUtil.Scale(ColorUtil.FromRgba(GameSetup.ArmyColors[lobby.Slots[slot].Color % GameSetup.ArmyColors.Length]), 1.3f) : Ui.Accent;
            lines.Add((l.Name + ": ", l.Text, c));
        }
        float y = bottom - lineH;
        for (int k = lines.Count - 1; k >= 0 && k >= lines.Count - maxLines; k--, y -= lineH)
        {
            var (who, text, c) = lines[k];
            float tx = x;
            if (who.Length > 0)
            {
                Ui.Text(who, tx, y, Ui.F(12), c, true);
                tx += Ui.Measure(who, Ui.F(12), true);
                Ui.Text(Ui.Fit(text, x + w - tx, Ui.F(12)), tx, y, Ui.F(12), Ui.TextColor);
            }
            else Ui.Text(Ui.Fit(text, w, Ui.F(12)), tx, y, Ui.F(12), c);
        }
        if (_chat.Update(input) && _chat.Text.Trim().Length > 0)
        {
            _s.SendChat(_chat.Text);
            _chat.Text = "";
        }
        _chat.Draw(input, "Type a message, Enter to send");
    }

    // ------------------------------------------------------------------ bottom bar

    private void DrawBottom(float x0, float y, float right, LobbyState l)
    {
        float bh = Ui.P(42);
        if (MenuUi.BigButton(Ui.R(x0, y, Ui.P(150), bh), "Leave") || (MenuUi.Key(KeyboardKey.Escape) && !_escBlocked && _frames > 1))
        {
            _app.Go(new MultiplayerScreen(_app));
            return;
        }
        var start = Ui.R(right - Ui.P(220), y, Ui.P(220), bh);
        float ty = y + (bh - Ui.F(12)) * 0.5f - 1;
        if (_host != null)
        {
            string? problem = l.Problem();
            Ui.TextRight(_status ?? problem ?? "Everybody is ready", start.X - Ui.P(14), ty, Ui.F(12), _status != null || problem != null ? Ui.Warn : Ui.Good, true);
            if (MenuUi.BigButton(start, "Start Game", problem == null, true, 16)) TryStart();
        }
        else
        {
            var me = l.SlotOfPlayer(_s.PlayerId);
            bool ready = me?.Ready == true;
            string text = _status ?? (ready ? "Waiting for the host to start" : "Press Ready when you are set");
            Ui.TextRight(text, start.X - Ui.P(14), ty, Ui.F(12), _status != null ? Ui.Warn : Ui.TextDim, true);
            if (MenuUi.BigButton(start, ready ? "Not Ready" : "Ready", me != null, !ready, 16)) _client!.SetReady(!ready);
        }
    }

    private void TryStart()
    {
        _status = _host!.StartGame();
    }

    /// <summary>--autostart: clients report ready, the host starts once everybody is in (2+ humans, or AI slots from --players).</summary>
    private void AutoStart(LobbyState l)
    {
        if (!_app.Options.AutoStart || _autoDone || _frames < 5) return;
        if (_client != null)
        {
            _autoDone = true;
            _client.SetReady(true);
        }
        else if (_host != null && l.Problem() == null && (l.HumanCount >= 2 || _app.Options.PlayersGiven) && _preview.Terrain != null)
        {
            _autoDone = true;
            TryStart();
        }
    }

    public void Dispose()
    {
        _preview.Dispose();
        if (!_handedOff) _s.Leave();
    }
}
