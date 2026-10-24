using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.Menus;

/// <summary>
/// Skirmish setup: 8 player slots (Human / AI / Closed, AI difficulty, team, color) and the map (size, preset,
/// water, seed + randomize) with a live preview generated in the background. Start → loading screen.
/// </summary>
public sealed class SkirmishSetupScreen : IScreen
{
    private readonly AppHost _app;
    private readonly GameConfig _cfg;
    private readonly MapPreview _preview = new();
    private string _seedText;
    private readonly bool _sandbox;

    /// <param name="sandbox">Sandbox entry: own config (<see cref="AppHost.SandboxConfig"/>), "Enemy AI" switch.</param>
    public SkirmishSetupScreen(AppHost app, bool sandbox = false)
    {
        _app = app;
        _sandbox = sandbox;
        _cfg = sandbox ? app.SandboxConfig : app.SetupConfig;
        _seedText = _cfg.Seed.ToString();
    }

    public string ScreenshotInfo => _preview.Terrain != null ? $"(preview {_cfg.Preset} {_cfg.MapSize}, {_preview.LastGenMs:F0} ms)" : "(preview pending)";

    public void Frame(float dt)
    {
        _app.Backdrop.Draw(dt, 1.0f);
        MenuUi.Dim(70);
        MenuUi.BeginFrame();
        _preview.Request(_cfg.MapSettings());
        _preview.Update();

        float x0 = Ui.P(48), y0 = Ui.P(30);
        if (_sandbox) MenuUi.ScreenTitle("Sandbox", "Test freely: spawn any unit, cheats, nukes, no victory", x0, y0);
        else MenuUi.ScreenTitle("Skirmish", "Players, teams, map and game options", x0, y0);

        float top = y0 + Ui.P(84);
        float bottomBar = Ui.Sh - Ui.P(62);
        float mapW = Ui.P(470);
        var mapPanel = Ui.R(Ui.Sw - Ui.P(48) - mapW, top, mapW, bottomBar - top - Ui.P(14));
        var playersPanel = Ui.R(x0, top, MathF.Min(Ui.P(660), mapPanel.X - x0 - Ui.P(20)), Ui.P(8 * 40 + 58));
        DrawPlayers(playersPanel);
        if (!_sandbox)
        {
            // Game options under the players (the sandbox has its own rules: no victory, cheats on).
            float oy = playersPanel.Y + playersPanel.Height + Ui.P(26);
            var optPanel = Ui.R(x0, oy, playersPanel.Width, MathF.Min(GameOptionsBlock.Height(3), bottomBar - Ui.P(14) - oy));
            GameOptionsBlock.Draw(optPanel, _cfg.Options, true, 3);
        }
        DrawMap(mapPanel);
        DrawBottom(x0, bottomBar, mapPanel.X + mapPanel.Width);
        Ui.DrawTooltip();
    }

    // ------------------------------------------------------------------ players

    private void DrawPlayers(Rectangle p)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, $"Players ({_cfg.Players} / {GameConfig.MaxSlots})");
        float x = p.X + Ui.P(12), y = p.Y + Ui.P(30);
        float rowH = Ui.P(32), gap = Ui.P(8);
        float wNum = Ui.P(26), wKind = Ui.P(162), wFac = Ui.P(112), wDiff = Ui.P(100), wTeam = Ui.P(86), wCol = Ui.P(64);
        float cxF = x + wNum + wKind + gap;
        float cx1 = x + wNum, cx2 = cxF + wFac + gap, cx3 = cx2 + wDiff + gap, cx4 = cx3 + wTeam + gap;
        int hs = Ui.F(10.5f);
        Ui.Text("PLAYER", cx1, y, hs, Ui.TextFaint, true);
        Ui.Text("FACTION", cxF, y, hs, Ui.TextFaint, true);
        Ui.Text("AI LEVEL", cx2, y, hs, Ui.TextFaint, true);
        Ui.Text("TEAM", cx3, y, hs, Ui.TextFaint, true);
        Ui.Text("COLOR", cx4, y, hs, Ui.TextFaint, true);
        y += Ui.P(18);

        int startNo = 0;
        for (int i = 0; i < GameConfig.MaxSlots; i++)
        {
            var s = _cfg.Slots[i];
            bool open = s.Kind != SlotKind.Closed;
            var row = Ui.R(p.X + Ui.P(6), y - Ui.P(3), p.Width - Ui.P(12), rowH + Ui.P(6));
            if (open) Raylib.DrawRectangleRec(row, new Color(30, 42, 56, 120));
            string num = open ? (++startNo).ToString() : "–";
            Ui.Text(num, x + Ui.P(4), y + (rowH - Ui.F(13)) * 0.5f - 1, Ui.F(13), open ? Ui.TextColor : Ui.TextFaint, true);

            string kind = s.Kind switch { SlotKind.Human => "You (human)", SlotKind.AI => _sandbox && !_cfg.SandboxAi ? "Army (AI off)" : "Computer AI", _ => "Closed" };
            var kc = s.Kind switch { SlotKind.Human => Ui.Accent, SlotKind.AI => Ui.TextColor, _ => Ui.TextFaint };
            int d = MenuUi.Cycle(Ui.R(cx1, y, wKind, rowH), kind, true, kc);
            if (d != 0) SetKind(i, (SlotKind)(((int)s.Kind + d + 3) % 3));

            var (facName, facColor) = FactionLabel(s.Faction);
            d = MenuUi.Cycle(Ui.R(cxF, y, wFac, rowH), open ? facName : "—", open, facColor);
            if (d != 0) s.Faction = GameConfig.NextFaction(_app.Db.Factions, s.Faction, d);

            bool ai = s.Kind == SlotKind.AI;
            d = MenuUi.Cycle(Ui.R(cx2, y, wDiff, rowH), ai ? s.Difficulty.ToString() : "—", ai,
                s.Difficulty switch { AiDifficulty.Easy => Ui.Good, AiDifficulty.Hard => Ui.Bad, _ => Ui.Warn });
            if (d != 0) s.Difficulty = (AiDifficulty)(((int)s.Difficulty + d + 3) % 3);

            d = MenuUi.Cycle(Ui.R(cx3, y, wTeam, rowH), s.Team == 0 ? "None" : $"Team {s.Team}", open);
            if (d != 0) s.Team = (s.Team + d + 5) % 5;

            var sw = Ui.R(cx4, y + Ui.P(3), wCol, rowH - Ui.P(6));
            var col = ColorUtil.FromRgba(GameSetup.ArmyColors[s.ColorIndex % GameSetup.ArmyColors.Length]);
            if (!open) col = ColorUtil.Scale(col, 0.35f);
            bool hover = open && Ui.Hover(sw);
            MenuUi.ColorSwatch(sw, col, hover);
            if (open && Ui.LeftClickIn(sw)) NextColor(i, +1);
            if (open && Ui.RightClickIn(sw)) NextColor(i, -1);
            y += rowH + Ui.P(8);
        }
        Ui.Text("Left click: next · right click: previous. Teams share victory; \"None\" = free for all.",
            p.X + Ui.P(12), p.Y + p.Height + Ui.P(8), Ui.F(10.5f), Ui.TextFaint);
    }

    private (string Name, Color Color) FactionLabel(string id)
    {
        if (string.Equals(id, FactionDb.Random, StringComparison.OrdinalIgnoreCase)) return ("Random", Ui.TextDim);
        var f = _app.Db.Factions.Get(id);
        if (f == null) return (id + "?", Ui.Bad);
        return (f.Name, f.ColorRgba != 0 ? ColorUtil.Scale(ColorUtil.FromRgba(f.ColorRgba), 1.25f) : Ui.TextColor);
    }

    private void SetKind(int i, SlotKind kind)
    {
        if (kind == SlotKind.Human)
            foreach (var o in _cfg.Slots)
                if (o.Kind == SlotKind.Human) o.Kind = SlotKind.AI;
        _cfg.Slots[i].Kind = kind;
        // A slot that opens with a color already used by another open slot takes a free one.
        if (kind != SlotKind.Closed && _cfg.Slots.Where((o, j) => j != i && o.Kind != SlotKind.Closed).Any(o => o.ColorIndex == _cfg.Slots[i].ColorIndex))
            NextColor(i, +1);
    }

    private void NextColor(int i, int dir)
    {
        int n = GameSetup.ArmyColors.Length;
        var used = _cfg.Slots.Where((o, j) => j != i && o.Kind != SlotKind.Closed).Select(o => o.ColorIndex).ToHashSet();
        int c = _cfg.Slots[i].ColorIndex;
        for (int k = 0; k < n; k++)
        {
            c = ((c + dir) % n + n) % n;
            if (!used.Contains(c)) break;
        }
        _cfg.Slots[i].ColorIndex = c;
    }

    // ------------------------------------------------------------------ map

    private void DrawMap(Rectangle p)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "Map");
        float pad = Ui.P(14);
        float size = MathF.Min(p.Width - 2 * pad, p.Height * 0.48f);
        var pr = Ui.R(p.X + (p.Width - size) * 0.5f, p.Y + Ui.P(28), size, size);
        var colors = _cfg.OpenSlots.Select(s => ColorUtil.FromRgba(GameSetup.ArmyColors[s.ColorIndex % GameSetup.ArmyColors.Length])).ToList();
        _preview.Draw(pr, colors);

        float y = pr.Y + pr.Height + Ui.P(6);
        var t = _preview.Terrain;
        string info = t == null ? "Generating…"
            : $"{t.StartPositions.Length} start positions · {t.Deposits.Count(d => d.Type == DepositType.Mass)} mass · {t.Deposits.Count(d => d.Type == DepositType.Hydrocarbon)} hydrocarbon";
        Ui.TextCentered(info, Ui.R(p.X, y, p.Width, Ui.P(16)), Ui.F(10.5f), Ui.TextDim);
        y += Ui.P(22);

        float lx = p.X + pad, cx = p.X + Ui.P(92), cw = p.X + p.Width - pad - cx, rowH = Ui.P(28), gap = Ui.P(8);

        MenuUi.Label("Size", lx, y, rowH);
        int si = Array.IndexOf(GameConfig.MapSizes, _cfg.MapSize);
        int nsi = MenuUi.Segmented(Ui.R(cx, y, cw, rowH), ["5 km · 256", "10 km · 512", "20 km · 1024"], si, true, 11.5f);
        if (nsi != si && nsi >= 0) _cfg.MapSize = GameConfig.MapSizes[nsi];
        y += rowH + gap;

        MenuUi.Label("Terrain", lx, y, rowH);
        var presets = MapPresets.All;
        int pi = Array.IndexOf(presets, _cfg.Preset);
        int npi = MenuUi.Segmented(Ui.R(cx, y, cw, rowH), presets.Select(MapPresets.Name).ToArray(), pi, true, 10.5f);
        if (npi != pi && npi >= 0)
        {
            _cfg.Preset = presets[npi];
            _cfg.WaterAmount = -1; // back to the preset's default
        }
        y += rowH + Ui.P(4);
        var desc = Ui.Wrap(MapPresets.Description(_cfg.Preset), cw, Ui.F(10.5f));
        foreach (var line in desc.Take(2))
        {
            Ui.Text(line, cx, y, Ui.F(10.5f), MapPresets.NeedsNaval(_cfg.Preset) ? Ui.Warn : Ui.TextDim);
            y += Ui.F(10.5f) + Ui.P(2);
        }
        y += gap;

        MenuUi.Label("Water", lx, y, rowH);
        float water = _cfg.EffectiveWater;
        float nw = MenuUi.Slider(2, Ui.R(cx, y + Ui.P(9), cw - Ui.P(56), Ui.P(10)), water, $"{water * 100:0}%");
        if (MathF.Abs(nw - water) > 0.004f) _cfg.WaterAmount = MathF.Round(nw * 50f) / 50f;
        y += rowH + gap;

        MenuUi.Label("Seed", lx, y, rowH);
        var fr = Ui.R(cx, y, cw - Ui.P(118), rowH);
        if (MenuUi.NumberField(3, fr, ref _seedText) && int.TryParse(_seedText, out int seed)) _cfg.Seed = seed;
        var rb = Ui.R(fr.X + fr.Width + Ui.P(8), y, Ui.P(110), rowH);
        if (MenuUi.BigButton(rb, "Randomize", true, false, 12))
        {
            _cfg.Seed = Random.Shared.Next(1, 1_000_000);
            _seedText = _cfg.Seed.ToString();
        }
    }

    // ------------------------------------------------------------------ bottom bar

    private void DrawBottom(float x0, float y, float right)
    {
        float bh = Ui.P(42);
        if (MenuUi.BigButton(Ui.R(x0, y, Ui.P(150), bh), "Back") || (MenuUi.Key(KeyboardKey.Escape) && !MenuUi.FieldFocused))
        {
            _app.ToMainMenu();
            return;
        }
        string? problem = _cfg.Problem(_app.Db.Factions);
        var start = Ui.R(right - Ui.P(220), y, Ui.P(220), bh);
        if (problem != null)
            Ui.TextRight(problem, start.X - Ui.P(14), y + (bh - Ui.F(12)) * 0.5f - 1, Ui.F(12), Ui.Warn, true);
        else if (MapPresets.NeedsNaval(_cfg.Preset))
            Ui.TextRight("Bases are on separate islands", start.X - Ui.P(14), y + (bh - Ui.F(12)) * 0.5f - 1, Ui.F(12), Ui.TextDim);
        if (_sandbox)
        {
            // Enemy AI switch (off: the other armies just sit there; the in-game sandbox panel can turn it on later).
            float lx = x0 + Ui.P(170);
            MenuUi.Label("Enemy AI", lx, y, bh);
            _cfg.SandboxAi = MenuUi.Toggle(Ui.R(lx + Ui.P(84), y + Ui.P(5), Ui.P(64), bh - Ui.P(10)), _cfg.SandboxAi);
        }
        bool go = MenuUi.BigButton(start, _sandbox ? "Start Sandbox" : "Start Game", problem == null, true, 16)
                  || (problem == null && !MenuUi.FieldFocused && (MenuUi.Key(KeyboardKey.Enter) || MenuUi.Key(KeyboardKey.KpEnter)))
                  || (_app.Options.AutoStart && problem == null && _preview.Terrain != null);
        if (go)
        {
            var s = AppSettings.Current;
            s.MapSize = _cfg.MapSize;
            s.Preset = MapPresets.Name(_cfg.Preset);
            s.WaterAmount = _cfg.WaterAmount;
            s.Save();
            _app.StartGame(_cfg, _app.MenuGameOptions());
        }
    }

    public void Dispose() => _preview.Dispose();
}
