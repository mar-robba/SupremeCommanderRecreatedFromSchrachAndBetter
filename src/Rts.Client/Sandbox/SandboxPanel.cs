using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Client.Sandbox;

/// <summary>
/// Sandbox side panel (F9), right side under the top bar: army selector, cheat toggles, AI, victory, game speed,
/// actions on the selection, map tools (nuke / clear) and the spawn list: every blueprint of the
/// <see cref="BlueprintDb"/> grouped by category and tech level (units added later appear automatically), with a
/// search box and count buttons. Clicking a unit arms the spawn tool; clicks on the map spawn at the cursor.
/// Immediate-mode like the HUD: widgets are drawn and hit-tested in <see cref="Draw"/>.
/// </summary>
public sealed class SandboxPanel
{
    private readonly SandboxController _c;
    public bool Visible = true;
    public Rectangle Rect;
    private Rectangle _listRect, _searchRect;
    private readonly SandboxTextField _search = new() { MaxLength = 40 };
    private float _scroll, _contentH;
    private readonly HashSet<string> _collapsed = new();
    private readonly List<(string name, List<UnitBlueprint> items)> _groups;

    private static readonly int[] Counts = [1, 10, 50, 100];
    private static readonly float[] Speeds = [0.5f, 1f, 2f, 4f, 8f];
    private static readonly float[] Radii = [10f, 20f, 40f, 80f];

    public SandboxPanel(SandboxController c)
    {
        _c = c;
        _groups = c.Ctx.Db.All
            .Where(b => !b.IsVariant) // generated enhancement combinations: spawn them by exact id (console)
            .GroupBy(GroupOf)
            .OrderBy(g => g.Key.order)
            .Select(g => (g.Key.name, g.OrderBy(b => b.Has(UnitCategory.Engineer) ? 0 : 1).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Id, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>List group of a blueprint: commanders, then land / air / naval per tech level, strategic (shields,
    /// silos, missile defense, artillery installations), experimentals, structures per tech level.</summary>
    public static (int order, string name) GroupOf(UnitBlueprint b)
    {
        string tech = b.Tech >= 4 ? "Experimental" : $"T{b.Tech}";
        if (b.Has(UnitCategory.Commander)) return (0, "Commanders");
        if (b.Tech >= 4) return (380, "Experimental");
        if (IsStrategic(b)) return (390, "Strategic & shields");
        if (b.IsStructure) return (400 + b.Tech, $"Structures {tech}");
        if (b.Has(UnitCategory.Air)) return (200 + b.Tech, $"Air {tech}");
        if (b.Has(UnitCategory.Naval)) return (300 + b.Tech, $"Naval {tech}");
        return (100 + b.Tech, $"Land {tech}");
    }

    /// <summary>Shields (static and mobile), missile silos (TML, nuke, anti-nuke), missile defenses and artillery
    /// installations: listed together so the strategic toys are easy to find.</summary>
    public static bool IsStrategic(UnitBlueprint b) =>
        b.Shield != null || b.Silo != null || b.MissileDefense != null || (b.IsStructure && b.Has(UnitCategory.Artillery));

    public bool SearchFocused => Visible && _search.Focused;
    public void UnfocusSearch() => _search.Focused = false;

    private void Layout()
    {
        float w = Ui.P(290);
        float top = Ui.P(58), bottom = Ui.Sh - Ui.P(184); // the row under the top bar is left to the perf overlay (F8)
        Rect = Ui.R(Ui.Sw - w - Ui.P(4), top, w, MathF.Max(Ui.P(200), bottom - top));
    }

    public bool MouseOver()
    {
        if (!Visible) return false;
        Layout();
        return Ui.Hover(Rect);
    }

    /// <summary>Keyboard / wheel work (before the world input).</summary>
    public void UpdateInput()
    {
        if (!Visible) return;
        Layout();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !Ui.Hover(_searchRect)) _search.Focused = false;
        if (_search.HandleInput())
        {
            // Enter: arm the best match.
            var first = Filtered().SelectMany(g => g.items).FirstOrDefault();
            if (first != null) PickBlueprint(first);
            _search.Focused = false;
        }
        if (Ui.Hover(_listRect))
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0) _scroll -= wheel * Ui.P(20) * 3;
        }
    }

    private IEnumerable<(string name, List<UnitBlueprint> items)> Filtered()
    {
        string q = _search.Text.Trim();
        if (q.Length == 0) return _groups;
        return _groups
            .Select(g => (g.name, g.items.Where(b => b.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                     || b.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(g => g.Item2.Count > 0);
    }

    private void PickBlueprint(UnitBlueprint b)
    {
        _c.SpawnBlueprint = b.Index;
        _c.ArmSpawn(b.Index, _c.SpawnCount, _c.TargetArmy, _c.SpawnSite);
    }

    // ------------------------------------------------------------------ draw

    private static bool Btn(Rectangle r, string label, bool active = false, bool enabled = true, Color? tint = null, string? tip = null, Color? text = null)
    {
        var st = Ui.Button(r, enabled, active, tint);
        Ui.TextCentered(Ui.Fit(label, r.Width - 4, Ui.F(10.5f), true), r, Ui.F(10.5f), enabled ? text ?? (active ? Color.White : Ui.TextColor) : Ui.TextFaint, true);
        if (tip != null && st.Hover) Ui.Tooltip(r, "", tip, above: false);
        return st.Left;
    }

    /// <summary>Equal-width cells of a row (after the label column).</summary>
    private static Rectangle Cell(float x, float y, float w, float h, int i, int n, float gap)
    {
        float cw = (w - gap * (n - 1)) / n;
        return Ui.R(x + i * (cw + gap), y, cw, h);
    }

    public void Draw()
    {
        if (!Visible) return;
        Layout();
        var ctx = _c.Ctx;
        var snap = ctx.Snapshots.Current;
        var st = snap.Sandbox;
        int me = ctx.LocalArmy;
        int t = _c.TargetArmy;
        Ui.Panel(Rect);

        float pad = Ui.P(8), gap = Ui.P(3), rowH = Ui.P(22), labelW = Ui.P(54);
        float x = Rect.X + pad, w = Rect.Width - 2 * pad;
        float y = Rect.Y + Ui.P(6);
        float cx = x + labelW, cw = w - labelW;

        // Title row.
        Ui.Text("SANDBOX", x, y + Ui.P(2), Ui.F(13), Ui.Warn, true);
        if (st != null && st.PendingSpawns > 0) Ui.Text($"spawning {st.PendingSpawns}…", x + Ui.P(76), y + Ui.P(4), Ui.F(10), Ui.TextDim);
        float bs = Ui.P(20);
        var close = Ui.R(Rect.X + Rect.Width - pad - bs, y, bs, bs);
        if (Btn(close, "×", tip: "Hide the panel (F9)")) { _c.TogglePanel(); return; }
        var cons = Ui.R(close.X - gap - Ui.P(30), y, Ui.P(30), bs);
        if (Btn(cons, ">_", _c.ConsoleUi.Open, tip: "Console (F10 or `)")) _c.ToggleConsole();
        var help = Ui.R(cons.X - gap - bs, y, bs, bs);
        if (Btn(help, "?", _c.HelpVisible, tip: "Commands and hotkeys")) _c.HelpVisible = !_c.HelpVisible;
        y += bs + Ui.P(6);

        void Label(string s) => Ui.Text(s, x, y + (rowH - Ui.F(10)) * 0.5f - 1, Ui.F(10), Ui.TextDim, true);

        // Army selector.
        Label("ARMY");
        int n = Math.Max(1, snap.Armies.Length);
        for (int a = 0; a < snap.Armies.Length; a++)
        {
            var r = Cell(cx, y, cw, rowH, a, n, gap);
            var col = (uint)a < (uint)ctx.ArmyColors.Length ? ctx.ArmyColors[a] : Color.Gray;
            if (Btn(r, _c.ArmyShort(a), a == t, tint: ColorUtil.Scale(col, 0.45f), tip: $"Spawns, cheats and \"Give\" act on {_c.ArmyLabel(a)}"))
            {
                _c.TargetArmy = a;
                _c.RefreshSpawnTool();
            }
            Raylib.DrawRectangle((int)r.X + 2, (int)(r.Y + r.Height - 3), (int)r.Width - 4, 2, col);
        }
        y += rowH + gap * 2;

        // Cheats for the selected army.
        Label("CHEATS");
        var toggles = new[] { (SandboxToggle.InfiniteResources, "Free res"), (SandboxToggle.InstantBuild, "Instant"), (SandboxToggle.BuildAnywhere, "Anywhere") };
        for (int i = 0; i < 3; i++)
        {
            var (tg, label) = toggles[i];
            bool on = SandboxController.ToggleState(st, tg, t);
            string tip = tg switch
            {
                SandboxToggle.InfiniteResources => "Infinite resources: storage always full, building never stalls",
                SandboxToggle.InstantBuild => "Instant build: structures and factory units finish at once",
                _ => "Build anywhere: extractors don't need a deposit",
            };
            if (Btn(Cell(cx, y, cw, rowH, i, 3, gap), label, on, tint: on ? new Color(40, 110, 60, 255) : null, tip: $"{tip} ({_c.ArmyLabel(t)})"))
                _c.Execute(new ToggleAction(tg, t, !on));
        }
        y += rowH + gap;
        {
            bool fogOn = ctx.Fog?.Enabled ?? false;
            if (Btn(Cell(cx, y, cw, rowH, 0, 3, gap), "Reveal", !fogOn, tint: !fogOn ? new Color(40, 110, 60, 255) : null, tip: "Reveal the whole map (fog of war off, client only)"))
                _c.Execute(new FogAction(!fogOn));
            bool victory = st?.Victory ?? false;
            if (Btn(Cell(cx, y, cw, rowH, 1, 3, gap), victory ? "Victory" : "No end", victory, tip: "Victory on: the game ends when one team is left. Off (default): it never ends"))
                _c.Execute(new VictoryAction(!victory));
            bool aiOn = st != null && (uint)t < (uint)st.AiOn.Length && st.AiOn[t];
            var diff = st != null && (uint)t < (uint)st.AiDifficulty.Length ? st.AiDifficulty[t] : AiDifficulty.Normal;
            var aiR = Cell(cx, y, cw, rowH, 2, 3, gap);
            if (Btn(aiR, aiOn ? $"AI {diff}" : "AI off", aiOn, tint: aiOn ? new Color(110, 60, 40, 255) : null,
                    tip: $"Skirmish AI of {_c.ArmyLabel(t)}: click cycles off → easy → normal → hard"))
            {
                if (!aiOn) _c.Execute(new AiAction(t, true, AiDifficulty.Easy));
                else if (diff == AiDifficulty.Hard) _c.Execute(new AiAction(t, false, null));
                else _c.Execute(new AiAction(t, true, (AiDifficulty)((int)diff + 1)));
            }
        }
        y += rowH + gap * 2;

        // Game speed.
        Label("SPEED");
        var runner = ctx.Runner;
        if (Btn(Cell(cx, y, cw, rowH, 0, Speeds.Length + 1, gap), runner.Paused ? "Play" : "Pause", runner.Paused, tip: "Pause / resume (Space)"))
            _c.Execute(new PauseAction(!runner.Paused));
        for (int i = 0; i < Speeds.Length; i++)
        {
            float s = Speeds[i];
            if (Btn(Cell(cx, y, cw, rowH, i + 1, Speeds.Length + 1, gap), s < 1 ? "½x" : $"{s:0}x", MathF.Abs(runner.Speed - s) < 0.01f, tip: "Game speed (+ / - keys)"))
                _c.Execute(new SpeedAction(s));
        }
        y += rowH + gap * 2;

        // Actions on the selection.
        int sel = _c.SelectedCount();
        Label(sel > 0 ? $"SEL {sel}" : "SEL");
        bool any = sel > 0;
        var acts = new (string label, ConsoleAction act, string tip)[]
        {
            ("Kill", new UnitActionAction(SandboxUnitAction.Kill), "Destroy the selected units (explosion + wreck)"),
            ("Del", new UnitActionAction(SandboxUnitAction.Delete), "Remove the selected units silently (no wreck)"),
            ("Heal", new UnitActionAction(SandboxUnitAction.Heal), "Full health"),
            ("-50%", new UnitActionAction(SandboxUnitAction.Damage, 0.5f), "Damage: lose half of max health"),
            ("Give", new UnitActionAction(SandboxUnitAction.ChangeOwner, 0, t), $"Give the selected units to {_c.ArmyLabel(t)} (army selector)"),
            ("Done", new UnitActionAction(SandboxUnitAction.Complete), "Finish construction sites instantly"),
        };
        for (int i = 0; i < acts.Length; i++)
            if (Btn(Cell(cx, y, cw, rowH, i, acts.Length, gap), acts[i].label, false, any, tip: acts[i].tip + (any ? "" : " — select units first")))
                _c.Execute(acts[i].act);
        y += rowH + gap * 2;

        // Map tools.
        Label("TOOLS");
        bool nukeOn = _c.Tool == SandboxTool.Nuke && !_c.NukeToolInstant, blastOn = _c.NukeToolInstant;
        if (Btn(Cell(cx, y, cw, rowH, 0, 4, gap), "NUKE", nukeOn, tint: new Color(120, 40, 30, 255), text: new Color(255, 210, 150, 255),
                tip: "Strategic missile: click the target on the map (Shift+click: several). Launched from your nuke silo, " +
                     "else your commander, else the map edge; launch warning, enemy anti-nukes can shoot it down"))
        {
            if (nukeOn) _c.CancelTool();
            else _c.Execute(new NukeAction(null));
        }
        if (Btn(Cell(cx, y, cw, rowH, 1, 4, gap), "Blast", blastOn, tint: new Color(90, 40, 30, 255), text: new Color(255, 190, 140, 255),
                tip: "Instant nuke: the blast right where you click, no missile (Shift+click: several)"))
        {
            if (blastOn) _c.CancelTool();
            else _c.Execute(new NukeAction(null, Instant: true));
        }
        if (Btn(Cell(cx, y, cw, rowH, 2, 4, gap), "Clear", _c.Tool == SandboxTool.Clear, tip: "Remove every unit in a circle: click the area (commanders are spared)"))
        {
            if (_c.Tool == SandboxTool.Clear) _c.CancelTool();
            else _c.Execute(new ClearAction(_c.ClearRadius, null));
        }
        var rr = Cell(cx, y, cw, rowH, 3, 4, gap);
        if (Btn(rr, $"R {_c.ClearRadius:0}", tip: "Clear radius (click: next size)"))
        {
            int ri = Array.IndexOf(Radii, _c.ClearRadius);
            _c.ClearRadius = Radii[(ri + 1) % Radii.Length];
            _c.ToolRadius = _c.ClearRadius;
        }
        y += rowH + gap * 2;

        // Spawn settings.
        Label("SPAWN");
        for (int i = 0; i < Counts.Length; i++)
            if (Btn(Cell(cx, y, cw, rowH, i, Counts.Length + 1, gap), Counts[i].ToString(), _c.SpawnCount == Counts[i], tip: "How many units each click spawns"))
            {
                _c.SpawnCount = Counts[i];
                _c.RefreshSpawnTool();
            }
        if (Btn(Cell(cx, y, cw, rowH, Counts.Length, Counts.Length + 1, gap), "Site", _c.SpawnSite, tip: "Spawn construction sites (unfinished) instead of finished units"))
        {
            _c.SpawnSite = !_c.SpawnSite;
            _c.RefreshSpawnTool();
        }
        y += rowH + gap * 2;

        // Search.
        _searchRect = Ui.R(x, y, w, rowH + Ui.P(2));
        if (Ui.LeftClickIn(_searchRect)) _search.Focus();
        _search.Draw(_searchRect, "Search units… (click, type, Enter)");
        y += _searchRect.Height + gap * 2;

        DrawList(Ui.R(x, y, w, Rect.Y + Rect.Height - pad - y));
    }

    private void DrawList(Rectangle r)
    {
        _listRect = r;
        float headH = Ui.P(18), rowH = Ui.P(20);
        bool searching = _search.Text.Trim().Length > 0;
        _scroll = Math.Clamp(_scroll, 0, MathF.Max(0, _contentH - r.Height));
        Raylib.DrawRectangleRec(r, new Color(8, 12, 18, 150));
        Raylib.BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        float y = r.Y - _scroll;
        float y0 = y;
        UnitBlueprint? hovered = null;
        Rectangle hoveredR = default;
        int shown = 0;
        foreach (var (name, items) in Filtered())
        {
            bool collapsed = !searching && _collapsed.Contains(name);
            var hr = Ui.R(r.X, y, r.Width, headH);
            if (hr.Y + hr.Height > r.Y && hr.Y < r.Y + r.Height)
            {
                bool hov = Ui.Hover(hr) && Ui.Hover(r);
                Raylib.DrawRectangleRec(hr, hov ? new Color(44, 60, 80, 255) : new Color(30, 42, 56, 255));
                Ui.Text($"{(collapsed ? "+" : "–")} {name.ToUpperInvariant()}", hr.X + Ui.P(6), hr.Y + (headH - Ui.F(10)) * 0.5f - 1, Ui.F(10), Ui.Accent, true);
                Ui.TextRight(items.Count.ToString(), hr.X + hr.Width - Ui.P(6), hr.Y + (headH - Ui.F(10)) * 0.5f - 1, Ui.F(10), Ui.TextFaint);
                if (!searching && Ui.Hover(r) && Ui.LeftClickIn(hr))
                {
                    if (!_collapsed.Remove(name)) _collapsed.Add(name);
                }
            }
            y += headH;
            if (collapsed) continue;
            foreach (var b in items)
            {
                var ir = Ui.R(r.X, y, r.Width, rowH);
                y += rowH;
                shown++;
                if (ir.Y + ir.Height <= r.Y || ir.Y >= r.Y + r.Height) continue;
                bool picked = _c.SpawnBlueprint == b.Index;
                bool hov = Ui.Hover(ir) && Ui.Hover(r);
                if (picked) Raylib.DrawRectangleRec(ir, new Color(40, 80, 130, 220));
                else if (hov) Raylib.DrawRectangleRec(ir, new Color(40, 54, 72, 200));
                int fs = Ui.F(11);
                float ty = ir.Y + (rowH - fs) * 0.5f - 1;
                string tech = b.Tech >= 4 ? "EXP" : $"T{b.Tech}";
                float techW = Ui.P(30);
                Ui.Text(tech, ir.X + Ui.P(8), ty, Ui.F(10), b.Tech switch { 1 => Ui.TextDim, 2 => Ui.Accent, 3 => Ui.Warn, _ => Ui.Bad }, true);
                Ui.Text(Ui.Fit(b.Name.Length > 0 ? b.Name : b.Id, ir.Width - techW - Ui.P(70), fs), ir.X + techW, ty, fs, picked ? Color.White : Ui.TextColor);
                Ui.TextRight(Ui.Fit(b.Faction.ToUpperInvariant(), Ui.P(60), Ui.F(9.5f)), ir.X + ir.Width - Ui.P(6), ty + 1, Ui.F(9.5f), Ui.TextFaint);
                if (hov)
                {
                    hovered = b;
                    hoveredR = ir;
                    if (Ui.LeftClickIn(ir)) PickBlueprint(b);
                }
            }
        }
        _contentH = y - y0;
        Raylib.EndScissorMode();
        if (shown == 0 && searching) Ui.TextCentered("No unit matches", Ui.R(r.X, r.Y + Ui.P(10), r.Width, Ui.P(20)), Ui.F(11), Ui.TextFaint);
        // Scrollbar.
        if (_contentH > r.Height)
        {
            float k = r.Height / _contentH;
            float bh = MathF.Max(Ui.P(16), r.Height * k);
            float by = r.Y + (r.Height - bh) * (_scroll / MathF.Max(1, _contentH - r.Height));
            Raylib.DrawRectangle((int)(r.X + r.Width - Ui.P(3)), (int)by, (int)Ui.P(3), (int)bh, new Color(140, 170, 200, 160));
        }
        if (hovered != null)
        {
            var e = hovered.Economy;
            string body = $"{hovered.Id} · T{hovered.Tech} · {hovered.MaxHealth:0} HP · {e.BuildCostMass:0} mass, {e.BuildCostEnergy:0} energy";
            if (hovered.Description.Length > 0) body = hovered.Description + "\n" + body;
            Ui.Tooltip(hoveredR, hovered.Name, body + "\nClick: spawn it with left clicks on the map", above: false);
        }
    }
}
