using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Snapshot;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.UI;

/// <summary>
/// SupCom-like HUD: top resource bar, bottom-left minimap, unit info panel, order buttons, construction menu
/// (engineers) / factory queue panel, placement ghost, mode hints, self-destruct confirmation, tooltips.
/// Immediate-mode: input for panels is handled while drawing. <see cref="MouseOverUi"/> must be called first
/// each frame (it computes the layout and blocks world input under every panel).
/// </summary>
public sealed class Hud : IDisposable
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    /// <summary>Legacy unscaled top bar height (the real height is TopBarRect.Height).</summary>
    public const int TopBarHeight = 30;

    private readonly Minimap _minimap;
    private readonly UnitPanel _info;
    private readonly OrdersPanel _orders;
    private readonly BuildMenu _build;
    private readonly FactoryPanel _factory;
    private readonly UpgradePanel _upgrade;
    private UiStartup? _startup;
    private TechDemo? _techDemo;

    private enum BuildKind { None, Engineer, Factory, Upgrade }
    private BuildKind _buildKind;
    private Rectangle _top, _mini, _infoR, _ordersR, _buildR, _dialogR, _helpR;
    private bool _showSelectionPanels;
    private int _frame, _preFrame = -1, _dialogFrames;

    public Hud(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
        _minimap = new Minimap(ctx, sel);
        _info = new UnitPanel(ctx, sel);
        _orders = new OrdersPanel(ctx, sel);
        _build = new BuildMenu(ctx, sel);
        _factory = new FactoryPanel(ctx, sel);
        _upgrade = new UpgradePanel(ctx, sel);
    }

    public Rectangle TopBarRect => _top;

    /// <summary>Frees GPU resources (minimap texture) when the game ends.</summary>
    public void Dispose() => _minimap.Dispose();

    /// <summary>CLI helpers (--select, --ui-demo, --ui-place, --mouse) for automated screenshots.</summary>
    public void ApplyStartupOptions(ClientOptions o)
    {
        if (o.Select != null || o.UiDemo || o.UiPlace != null || o.MousePos != null) _startup = new UiStartup(_ctx, _sel, o);
        if (o.TechDemo) _techDemo = new TechDemo(_ctx, _sel, o);
    }

    /// <summary>True if the mouse is over a HUD panel (camera/selection should ignore it).</summary>
    public bool MouseOverUi()
    {
        PreFrame();
        Layout();
        if (_sel.PendingSelfDestruct != null || _minimap.Dragging) return true;
        var m = Ui.Mouse;
        // The outermost pixels belong to camera edge-scrolling, even over the top bar.
        if (m.X <= 3 || m.Y <= 3 || m.X >= Ui.Sw - 4 || m.Y >= Ui.Sh - 4) return false;
        bool Hit(Rectangle r) => r.Width > 0 && Raylib.CheckCollisionPointRec(m, r);
        if (Hit(_top) || Hit(_mini)) return true;
        if (_showSelectionPanels && (Hit(_infoR) || (!_ctx.ReadOnly && Hit(_ordersR)))) return true;
        return _buildKind != BuildKind.None && Hit(_buildR);
    }

    /// <summary>Once-per-frame work that must happen before world input (startup helpers, modal state).</summary>
    private void PreFrame()
    {
        if (_preFrame == _frame) return;
        _preFrame = _frame;
        Ui.BeginFrame();
        _startup?.Update();
        _techDemo?.Update();
        _sel.InputBlocked = _sel.PendingSelfDestruct != null;
    }

    private void Layout()
    {
        Ui.BeginFrame();
        var snap = _ctx.Snapshots.Current;
        float H = Ui.P(172), m = Ui.P(4), gap = Ui.P(5);
        _top = new Rectangle(0, 0, Ui.Sw, Ui.P(30));
        float y = Ui.Sh - H - m;
        _mini = new Rectangle(m, y, H, H);

        _showSelectionPanels = false;
        foreach (var h in _sel.Selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out _)) { _showSelectionPanels = true; break; }

        _infoR = new Rectangle(_mini.X + _mini.Width + gap, y, Ui.P(272), H);
        _ordersR = new Rectangle(_infoR.X + _infoR.Width + gap, y, OrdersPanel.Width, H);
        float bx = _ordersR.X + _ordersR.Width + gap;
        float avail = Ui.Sw - m - bx;
        _buildKind = BuildKind.None;
        if (_showSelectionPanels && avail > Ui.P(120) && !_ctx.ReadOnly) // replays: no build/factory panels
        {
            if (_build.Refresh(snap)) { _buildKind = BuildKind.Engineer; _buildR = new Rectangle(bx, y, MathF.Round(MathF.Min(avail, _build.DesiredWidth())), H); }
            else if (_factory.Refresh(snap)) { _buildKind = BuildKind.Factory; _buildR = new Rectangle(bx, y, MathF.Round(MathF.Min(avail, _factory.DesiredWidth())), H); }
            else if (_upgrade.Refresh(snap)) { _buildKind = BuildKind.Upgrade; _buildR = new Rectangle(bx, y, MathF.Round(MathF.Min(avail, _upgrade.DesiredWidth())), H); }
        }
    }

    public void Draw()
    {
        PreFrame();
        Ui.BeginDraw();
        Layout();
        var snap = _ctx.Snapshots.Current;

        // 3D overlays: placement ghost, queued build ghosts of the selection.
        Raylib.BeginMode3D(_ctx.Camera.Camera);
        _sel.Placement.Draw3D(_sel.Selected);
        Raylib.EndMode3D();

        bool dialog = _sel.PendingSelfDestruct != null;
        if (dialog) HandleDialogInput();
        else
        {
            _dialogFrames = 0;
            if (_sel.InputBlocked || _ctx.ReadOnly) { } // sandbox console typing / map tool / replay: no build hotkeys
            else if (_buildKind == BuildKind.Engineer) _build.HandleHotkeys();
            else if (_buildKind == BuildKind.Factory && !_sel.Placement.Active) _factory.HandleHotkeys(snap);
            if (!_sel.Placement.Active && !_ctx.ReadOnly) UpgradeUi.HandleHotkey(_ctx, _sel.Selected, snap);
        }

        DrawTopBar(snap);
        _minimap.Draw(_mini);
        if (_showSelectionPanels)
        {
            _info.Draw(_infoR, snap);
            if (!_ctx.ReadOnly) _orders.Draw(_ordersR, snap);
        }
        if (_buildKind == BuildKind.Engineer) _build.Draw(_buildR, snap);
        else if (_buildKind == BuildKind.Factory) _factory.Draw(_buildR, snap);
        else if (_buildKind == BuildKind.Upgrade) _upgrade.Draw(_buildR, snap);

        DrawModeHints();
        DrawEndOfGame(snap);
        if (dialog) DrawDialog();
        Ui.DrawTooltip();
        _frame++;
    }

    // ------------------------------------------------------------------ top bar

    private void DrawTopBar(SimSnapshot snap)
    {
        var r = _top;
        Raylib.DrawRectangleGradientV((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, new Color(26, 34, 46, 240), new Color(12, 16, 22, 235));
        Raylib.DrawLineEx(new Vector2(0, r.Height - 1), new Vector2(r.Width, r.Height - 1), 1, Ui.PanelBorder);
        if ((uint)_ctx.LocalArmy < (uint)snap.Armies.Length) ResourceBar.Draw(r, snap.Armies[_ctx.LocalArmy]);

        // Help button (far right) and stats.
        float hs = r.Height - Ui.P(8);
        _helpR = new Rectangle(r.Width - hs - Ui.P(6), Ui.P(4), hs, hs);
        var st = Ui.Button(_helpR);
        Ui.TextCentered("?", _helpR, Ui.F(13), Ui.TextColor, true);
        if (st.Hover) Ui.Tooltip(_helpR, HelpLines(), above: false, maxWidth: 640);

        int fs = Ui.F(11.5f);
        string speed = $"x{_ctx.Runner.Speed:0.##}{(_ctx.Runner.Paused ? "  PAUSED" : "")}";
        string stats = $"{Raylib.GetFPS()} fps  ·  tick {snap.Tick}  ·  sim {snap.TickDurationMs:F1} ms  ·  units {snap.UnitCount}  ·  {speed}";
        float sx = _helpR.X - Ui.P(10) - Ui.Measure(stats, fs);
        if (sx > r.Width * 0.55f)
            Ui.Text(stats, sx, (r.Height - fs) * 0.5f - 1, fs, _ctx.Runner.Paused ? Ui.Warn : Ui.TextDim);
        else
        {
            string shortStats = $"{Raylib.GetFPS()} fps · {speed}";
            Ui.Text(shortStats, _helpR.X - Ui.P(10) - Ui.Measure(shortStats, fs), (r.Height - fs) * 0.5f - 1, fs, Ui.TextDim);
        }
    }

    private static List<TipLine> HelpLines()
    {
        var c = Ui.TextColor;
        var d = Ui.TextDim;
        return
        [
            new("Controls", c, 14, true),
            new("Left click / drag: select · double-click or Ctrl+click: all of that type on screen", d, 11.5f),
            new("Right click: move / attack / assist · factory selected: set rally point · Shift: queue", d, 11.5f),
            new("S stop · M move · A attack-move · P patrol · G guard/assist · Ctrl+K self-destruct", d, 11.5f),
            new("Build menu: Z X C V B N select · Tab / Shift+Tab category · R rotate · Esc / right click cancel", d, 11.5f),
            new("Factory: Z X C V B N queue (+Shift: 5) · right-click a button: -1 · R repeat", d, 11.5f),
            new("T / Shift+T: tech tier filter (build menu, factory) · U: upgrade selected structures (factory, mex, radar) · right-click the upgrade tile: cancel", d, 11.5f),
            new("Ctrl+0-9 set group · 0-9 recall group (Shift: add)", d, 11.5f),
            new("WASD / arrows / screen edge / middle drag: pan · wheel: zoom · minimap: click/drag", d, 11.5f),
            new("+ / - game speed · Space pause · Esc (nothing selected) menu · F1 / F2 spawn tanks (debug, own / enemy)", d, 11.5f),
            new("Engineers: right-click wreck = reclaim, damaged / unfinished unit = repair, factory = assist · M (nothing selected) or Ctrl+M mute · Ctrl+Shift+M music", d, 11.5f),
            new("F7 fog of war on/off (debug: reveal all) · rings in the fog = radar contacts", d, 11.5f),
            new("F5 quicksave · F6 quickload · Esc menu: Save Game · main menu: Load Game, Replays", d, 11.5f),
        ];
    }

    // ------------------------------------------------------------------ mode hints

    private void DrawModeHints()
    {
        string? banner = null, cursor = null;
        var col = Ui.Warn;
        if (_sel.Placement.Active)
        {
            var bp = _sel.Placement.Blueprint!;
            banner = $"PLACE {bp.Name.ToUpperInvariant()}  ·  LMB place  ·  Shift+LMB queue  ·  R rotate  ·  RMB / Esc cancel";
            col = new Color(140, 230, 150, 255);
            if (_sel.Placement.HasPos && _sel.Placement.Error != PlacementError.None)
                cursor = PlacementGrid.Describe(_sel.Placement.Error);
        }
        else if (_sel.Mode != Mode.Normal)
        {
            (banner, cursor, col) = _sel.Mode switch
            {
                Mode.AttackMove => ("ATTACK-MOVE: click a target point or an enemy (Shift: queue, RMB cancel)", "Attack-move", new Color(255, 120, 100, 255)),
                Mode.Patrol => ("PATROL: click patrol point (Shift: add points, RMB cancel)", "Patrol", new Color(120, 190, 255, 255)),
                Mode.Move => ("MOVE: click destination (Shift: queue, RMB cancel)", "Move", new Color(130, 230, 130, 255)),
                Mode.Guard => ("GUARD / ASSIST: click a friendly unit (RMB cancel)", "Guard / assist", new Color(240, 210, 100, 255)),
                _ => (null, null, col),
            };
        }
        if (banner != null)
        {
            int fs = Ui.F(13);
            float w = Ui.Measure(banner, fs, true) + Ui.P(20);
            var br = new Rectangle(MathF.Round((Ui.Sw - w) * 0.5f), _top.Height + Ui.P(8), MathF.Round(w), fs + Ui.P(10));
            Raylib.DrawRectangleRec(br, new Color(10, 14, 20, 200));
            Raylib.DrawRectangleLinesEx(br, 1, ColorUtil.WithAlpha(col, 160));
            Ui.Text(banner, br.X + Ui.P(10), br.Y + Ui.P(5) - 1, fs, col, true);
        }
        if (cursor != null && !MouseOverPanels())
        {
            int fs = Ui.F(12);
            var m = Ui.Mouse;
            var tc = _sel.Placement.Active ? Ui.Bad : col;
            Ui.TextShadow(cursor, m.X + Ui.P(18), m.Y + Ui.P(14), fs, tc, true);
        }
    }

    private bool MouseOverPanels()
    {
        var m = Ui.Mouse;
        return Raylib.CheckCollisionPointRec(m, _mini) || Raylib.CheckCollisionPointRec(m, _top)
            || (_showSelectionPanels && (Raylib.CheckCollisionPointRec(m, _infoR) || Raylib.CheckCollisionPointRec(m, _ordersR)))
            || (_buildKind != BuildKind.None && Raylib.CheckCollisionPointRec(m, _buildR));
    }

    // ------------------------------------------------------------------ self-destruct confirmation

    private (Rectangle yes, Rectangle no) DialogButtons()
    {
        float w = Ui.P(380), h = Ui.P(118);
        _dialogR = new Rectangle(MathF.Round((Ui.Sw - w) * 0.5f), MathF.Round(Ui.Sh * 0.36f - h * 0.5f), w, h);
        float bw = Ui.P(150), bh = Ui.P(30);
        float by = _dialogR.Y + h - bh - Ui.P(12);
        var yes = new Rectangle(_dialogR.X + w * 0.5f - bw - Ui.P(6), by, bw, bh);
        var no = new Rectangle(_dialogR.X + w * 0.5f + Ui.P(6), by, bw, bh);
        return (yes, no);
    }

    private void HandleDialogInput()
    {
        var (yes, no) = DialogButtons();
        bool first = _dialogFrames++ == 0;
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Y)
                       || (!first && Ui.Ctrl && Raylib.IsKeyPressed(KeyboardKey.K))
                       || (click && Raylib.CheckCollisionPointRec(Ui.Mouse, yes));
        bool cancel = Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.N)
                      || (click && Raylib.CheckCollisionPointRec(Ui.Mouse, no))
                      || Raylib.IsMouseButtonPressed(MouseButton.Right);
        if (confirm) _sel.ConfirmSelfDestruct();
        else if (cancel) _sel.PendingSelfDestruct = null;
        Ui.ConsumeClicks();
    }

    private void DrawDialog()
    {
        if (_sel.PendingSelfDestruct is not { } units) return;
        Raylib.DrawRectangle(0, 0, Ui.Sw, Ui.Sh, new Color(0, 0, 0, 110));
        var (yes, no) = DialogButtons();
        Ui.Panel(_dialogR);
        Raylib.DrawRectangleLinesEx(_dialogR, 2, new Color(200, 70, 60, 255));
        Ui.Text("SELF-DESTRUCT", _dialogR.X + Ui.P(14), _dialogR.Y + Ui.P(12), Ui.F(15), new Color(255, 120, 100, 255), true);
        Ui.Text($"Destroy {units.Length} selected unit{(units.Length == 1 ? "" : "s")}? This cannot be undone.", _dialogR.X + Ui.P(14), _dialogR.Y + Ui.P(36), Ui.F(12), Ui.TextColor);
        Ui.Button(yes, true, false, new Color(120, 40, 36, 255));
        Ui.TextCentered("Destroy  [Enter]", yes, Ui.F(12), Color.White, true);
        Ui.Button(no);
        Ui.TextCentered("Cancel  [Esc]", no, Ui.F(12), Ui.TextColor, true);
    }

    // ------------------------------------------------------------------ overlays

    private void DrawEndOfGame(SimSnapshot snap)
    {
        if (snap.WinningTeam >= 0 && (uint)_ctx.LocalArmy < (uint)snap.Armies.Length)
        {
            bool won = snap.Armies[_ctx.LocalArmy].Team == snap.WinningTeam;
            string t = won ? "VICTORY" : "DEFEAT";
            int fs = Ui.F(64);
            float w = Ui.Measure(t, fs, true);
            Ui.TextShadow(t, (Ui.Sw - w) * 0.5f, Ui.Sh / 3f, fs, won ? Color.Gold : Color.Red, true);
        }
        if (_ctx.Runner.Fault is Exception e)
        {
            int sh = Ui.Sh, sw = Ui.Sw;
            Raylib.DrawRectangle(0, sh / 2 - 40, sw, 120, new Color(80, 0, 0, 230));
            Ui.Text("SIMULATION CRASHED: " + e.GetType().Name + ": " + e.Message, 20, sh / 2 - 30, Ui.F(16), Color.White, true);
            Ui.Text((e.StackTrace ?? "").Split('\n').FirstOrDefault() ?? "", 20, sh / 2, Ui.F(13), Color.LightGray);
        }
    }
}
