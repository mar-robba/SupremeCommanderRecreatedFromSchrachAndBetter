using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Client.Rendering;
using Rts.Client.UI;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;

namespace Rts.Client.Sandbox;

/// <summary>Map tool armed by the panel or the console: the next left click on the map applies it.</summary>
public enum SandboxTool { None, Spawn, Nuke, Clear }

/// <summary>
/// Client side of the sandbox mode (owned by <see cref="GameSession"/>, null outside sandbox). Owns the side panel
/// (F9), the console (F10 / the key left of 1), the map tools (spawn / nuke / clear at the clicked point), the
/// <c>--exec</c> start script and the help overlay. Everything the panel and the console do goes through
/// <see cref="Execute"/>, which turns <see cref="ConsoleAction"/>s into sandbox commands for the runner
/// (<see cref="SandboxCommand"/>) or client-only changes (fog, speed, camera).
/// <para>Input: while a text field is focused, game hotkeys, camera keys and selection are blocked; while a map tool
/// is armed, selection is blocked (LMB applies, RMB / Esc cancels).</para>
/// </summary>
public sealed class SandboxController : IDisposable
{
    public readonly GameContext Ctx;
    public readonly SelectionController Selection;
    public readonly SandboxConsole ConsoleUi = new();
    public readonly SandboxPanel Panel;
    public readonly ConsoleContext ParseCtx;

    /// <summary>Army that spawns / toggles / "give" act on (panel army selector).</summary>
    public int TargetArmy;
    public int SpawnCount = 10;
    public bool SpawnSite;
    public int SpawnBlueprint = -1;
    public float ClearRadius = 20;
    public bool HelpVisible;

    public SandboxTool Tool { get; private set; }
    private int _toolBp, _toolCount, _toolArmy;
    private bool _toolSite;
    private float _toolRadius;

    private readonly Queue<string> _script = new();
    private int _wait;
    private bool _cameraLocked, _cameraWasEnabled;
    private string _toast = "";
    private Color _toastColor;
    private double _toastTime = -10;
    private Rectangle _panelBtn, _consoleBtn, _helpRect;

    public SandboxController(GameContext ctx, SelectionController selection, ClientOptions opt)
    {
        Ctx = ctx;
        Selection = selection;
        TargetArmy = ctx.LocalArmy;
        ParseCtx = new ConsoleContext { Db = ctx.Db, LocalArmy = ctx.LocalArmy, ArmyCount = ctx.ArmyColors.Length };
        Panel = new SandboxPanel(this);
        // F9 / F10 belong to the sandbox here (outside sandbox they are effect debug keys).
        EffectsRenderer.DebugKeysEnabled = false;
        if (opt.Exec != null)
            foreach (var line in SandboxConsoleParser.SplitScript(opt.Exec)) _script.Enqueue(line);
        ConsoleUi.Print("Sandbox mode. Type help for the commands, F9 toggles the panel.", SandboxConsole.Kind.Info);
    }

    /// <summary>A text field (console input, panel search) has the keyboard: game hotkeys must ignore it.</summary>
    public bool TextFocused => ConsoleUi.Focused || Panel.SearchFocused;

    /// <summary>Esc belongs to the sandbox this frame (closes the console/help, cancels the tool) instead of opening the pause menu.</summary>
    public bool WantsEscape => ConsoleUi.Open || Panel.SearchFocused || HelpVisible || Tool != SandboxTool.None;

    public void Dispose()
    {
        EffectsRenderer.DebugKeysEnabled = true;
        PlacementGrid.IgnoreDeposits = false;
        UnlockCamera();
    }

    // ------------------------------------------------------------------ frame

    /// <summary>Input for this frame (call after the HUD's hit test, before camera/selection). Returns true if the
    /// mouse is over a sandbox widget (the world must ignore it).</summary>
    public bool Update(float dt, bool interactive, bool overUi)
    {
        var snap = Ctx.Snapshots.Current;
        if (snap.Armies.Length > 0)
        {
            ParseCtx.ArmyCount = snap.Armies.Length;
            ParseCtx.Teams ??= snap.Armies.Select(a => a.Team).ToArray();
            if ((uint)TargetArmy >= (uint)snap.Armies.Length) TargetArmy = Ctx.LocalArmy;
        }
        var st = snap.Sandbox;
        PlacementGrid.IgnoreDeposits = st != null && (uint)Ctx.LocalArmy < (uint)st.BuildAnywhere.Length && st.BuildAnywhere[Ctx.LocalArmy];
        RunScript();
        if (!interactive)
        {
            UnlockCamera();
            return false;
        }
        Ui.BeginFrame();

        bool searchFocused = Panel.SearchFocused;
        if (Raylib.IsKeyPressed(KeyboardKey.F9)) TogglePanel();
        if (Raylib.IsKeyPressed(KeyboardKey.F10) || (!searchFocused && Raylib.IsKeyPressed(KeyboardKey.Grave))) ToggleConsole();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (ConsoleUi.Focused) ConsoleUi.Hide();
            else if (Panel.SearchFocused) Panel.UnfocusSearch();
            else if (HelpVisible) HelpVisible = false;
            else if (Tool != SandboxTool.None) CancelTool();
            else if (ConsoleUi.Open) ConsoleUi.Hide();
        }

        string? line = ConsoleUi.Update(ParseCtx);
        if (line != null) ExecuteLine(line);
        Panel.UpdateInput();

        bool overMine = Panel.MouseOver() || ConsoleUi.MouseOver() || (HelpVisible && Ui.Hover(_helpRect))
                        || (!Panel.Visible && (Ui.Hover(_panelBtn) || Ui.Hover(_consoleBtn)));
        if (HelpVisible && Raylib.IsMouseButtonPressed(MouseButton.Left) && !Panel.MouseOver()) HelpVisible = false;

        if (Tool != SandboxTool.None)
        {
            Selection.InputBlocked = true;
            if (!overUi && !overMine)
            {
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g)
                    ApplyTool(new Vector2(g.X, g.Z));
                else if (Raylib.IsMouseButtonPressed(MouseButton.Right)) CancelTool();
            }
        }
        if (TextFocused)
        {
            Selection.InputBlocked = true;
            LockCamera();
        }
        else UnlockCamera();
        return overMine;
    }

    public void Draw()
    {
        Ui.BeginFrame();
        DrawTool3D();
        Panel.Draw();
        DrawCornerButtons();
        ConsoleUi.Draw();
        if (HelpVisible) DrawHelp();
        DrawToolHint();
        DrawToast();
        Ui.DrawTooltip();
    }

    public void TogglePanel()
    {
        Panel.Visible = !Panel.Visible;
        if (!Panel.Visible) Panel.UnfocusSearch();
    }

    public void ToggleConsole()
    {
        if (ConsoleUi.Open) ConsoleUi.Hide();
        else
        {
            Panel.UnfocusSearch();
            ConsoleUi.Show();
        }
    }

    private void LockCamera()
    {
        if (_cameraLocked) return;
        _cameraLocked = true;
        _cameraWasEnabled = Ctx.Camera.InputEnabled;
        Ctx.Camera.InputEnabled = false;
    }

    private void UnlockCamera()
    {
        if (!_cameraLocked) return;
        _cameraLocked = false;
        Ctx.Camera.InputEnabled = _cameraWasEnabled;
    }

    // ------------------------------------------------------------------ script (--exec)

    private void RunScript()
    {
        if (_wait > 0)
        {
            _wait--;
            return;
        }
        while (_script.Count > 0 && _wait == 0) ExecuteLine(_script.Dequeue());
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Parses and runs one console line (echoed in the console; errors open it).</summary>
    public void ExecuteLine(string line)
    {
        ConsoleUi.Print(line, SandboxConsole.Kind.Input);
        var r = SandboxConsoleParser.Parse(line, ParseCtx);
        if (r.Error != null)
        {
            Error(r.Error);
            if (!ConsoleUi.Open) ConsoleUi.Show(focus: false);
        }
        else if (r.Action != null) Execute(r.Action);
    }

    /// <summary>Runs an action from the console or the panel.</summary>
    public void Execute(ConsoleAction action)
    {
        int me = Ctx.LocalArmy;
        var st = Ctx.Snapshots.Current.Sandbox;
        switch (action)
        {
            case SpawnAction s:
                if (s.At is Vector2 at) Spawn(s.Blueprint, s.Count, s.Army, at, s.Site);
                else ArmSpawn(s.Blueprint, s.Count, s.Army, s.Site);
                break;
            case NukeAction n:
                if (n.At is Vector2 nt) Nuke(nt);
                else ArmTool(SandboxTool.Nuke, "Click on the map to pick the nuke target (right click / Esc cancels)");
                break;
            case ToggleAction t:
            {
                int ref0 = t.Army < 0 ? me : t.Army;
                bool on = t.On ?? !ToggleState(st, t.Toggle, ref0);
                Ctx.Runner.Enqueue(new SandboxToggleCommand(me, t.Army, t.Toggle, on));
                Ok($"{ToggleName(t.Toggle)} {(on ? "ON" : "OFF")} for {ArmyLabel(t.Army)}");
                break;
            }
            case FogAction f:
            {
                if (Ctx.Fog == null) break;
                bool on = f.On ?? !Ctx.Fog.Enabled;
                Ctx.Fog.SetEnabled(on);
                Ok(on ? "Fog of war on" : "Map revealed (fog of war off)");
                break;
            }
            case AiAction ai:
            {
                var armies = ai.Army >= 0 ? [ai.Army] : Enumerable.Range(0, ParseCtx.ArmyCount).Where(a => a != me).ToArray();
                foreach (int a in armies) Ctx.Runner.Enqueue(new SandboxSetAiCommand(me, a, ai.On, ai.Difficulty));
                string diff = ai.Difficulty is { } d ? $" ({d})" : "";
                Ok($"AI {(ai.On ? "ON" + diff : "OFF")} for {ArmyLabel(ai.Army)}");
                break;
            }
            case UnitActionAction u:
            {
                var units = SelectedAlive();
                if (units.Length == 0)
                {
                    Error("Select some units first");
                    break;
                }
                Ctx.Runner.Enqueue(new SandboxUnitActionCommand(me, units, u.Action, u.Amount, u.TargetArmy));
                string what = u.Action switch
                {
                    SandboxUnitAction.Damage => $"Damaged {u.Amount * 100:0}%:",
                    SandboxUnitAction.ChangeOwner => $"Given to {ArmyLabel(u.TargetArmy)}:",
                    SandboxUnitAction.Complete => "Finished:",
                    _ => u.Action + ":",
                };
                Ok($"{what} {units.Length} unit(s)");
                if (u.Action is SandboxUnitAction.Kill or SandboxUnitAction.Delete or SandboxUnitAction.ChangeOwner) Selection.Selected.Clear();
                break;
            }
            case ClearAction c:
                if (c.At is Vector2 ca) Clear(ca, c.Radius);
                else
                {
                    _toolRadius = c.Radius;
                    ArmTool(SandboxTool.Clear, $"Click on the map to clear a {c.Radius:0} radius area (right click / Esc cancels)");
                }
                break;
            case SpeedAction sp:
                Ctx.Runner.Speed = Math.Clamp(sp.Speed, 0.125f, 8f);
                Ok($"Game speed {Ctx.Runner.Speed:0.###}x");
                break;
            case PauseAction pa:
                Ctx.Runner.Paused = pa.On ?? !Ctx.Runner.Paused;
                Ok(Ctx.Runner.Paused ? "Paused" : "Running");
                break;
            case VictoryAction v:
                Ctx.Runner.Enqueue(new SandboxSetVictoryCommand(me, v.On));
                Ok(v.On ? "Victory ON: the game ends when only one team is left" : "Victory OFF: the game never ends");
                break;
            case CameraAction cam:
                Ctx.Camera.LookAt(cam.At.X, cam.At.Y, cam.Distance ?? Ctx.Camera.Distance);
                break;
            case WaitAction w:
                _wait = Math.Max(_wait, w.Frames);
                break;
            case HelpAction:
                HelpVisible = !HelpVisible;
                if (ConsoleUi.Open) foreach (var h in SandboxConsoleParser.HelpLines) ConsoleUi.Print(h);
                break;
            case PanelAction p:
                Panel.Visible = p.On ?? !Panel.Visible;
                break;
            case ShowConsoleAction sc:
                if (sc.On ?? !ConsoleUi.Open) ConsoleUi.Show();
                else ConsoleUi.Hide();
                break;
            case ClearLogAction:
                ConsoleUi.ClearLog();
                break;
        }
    }

    public void Spawn(int bp, int count, int army, Vector2 at, bool site)
    {
        if ((uint)bp >= (uint)Ctx.Db.Count) return;
        at = Ctx.Terrain.Clamp(at, 2f);
        Ctx.Runner.Enqueue(new SandboxSpawnCommand(Ctx.LocalArmy, army, bp, at, count, Complete: !site));
        Ok($"Spawning {count} × {Ctx.Db[bp].Name}{(site ? " (construction site)" : "")} for {ArmyLabel(army)} at {at.X:0},{at.Y:0}");
    }

    public void Nuke(Vector2 at)
    {
        Ctx.Runner.Enqueue(new SandboxNukeCommand(Ctx.LocalArmy, at));
        Ok($"Nuke at {at.X:0},{at.Y:0}");
    }

    public void Clear(Vector2 at, float radius)
    {
        Ctx.Runner.Enqueue(new SandboxClearAreaCommand(Ctx.LocalArmy, at, radius));
        Ok($"Cleared radius {radius:0} at {at.X:0},{at.Y:0}");
    }

    /// <summary>Arms the spawn tool: every left click on the map spawns until right click / Esc.</summary>
    public void ArmSpawn(int bp, int count, int army, bool site)
    {
        if ((uint)bp >= (uint)Ctx.Db.Count) return;
        _toolBp = bp;
        _toolCount = count;
        _toolArmy = army;
        _toolSite = site;
        ArmTool(SandboxTool.Spawn, $"Click on the map to spawn {count} × {Ctx.Db[bp].Name} for {ArmyLabel(army)} (right click / Esc stops)");
    }

    /// <summary>Panel spawn settings changed while the spawn tool is armed: keep the tool in sync.</summary>
    public void RefreshSpawnTool()
    {
        if (Tool == SandboxTool.Spawn && SpawnBlueprint >= 0) { _toolBp = SpawnBlueprint; _toolCount = SpawnCount; _toolArmy = TargetArmy; _toolSite = SpawnSite; }
    }

    public void ArmTool(SandboxTool tool, string hint)
    {
        Tool = tool;
        if (tool == SandboxTool.Clear && _toolRadius <= 0) _toolRadius = ClearRadius;
        ConsoleUi.Unfocus(); // the next click goes to the map
        Panel.UnfocusSearch();
        Info(hint);
    }

    public void CancelTool() => Tool = SandboxTool.None;

    private void ApplyTool(Vector2 p)
    {
        switch (Tool)
        {
            case SandboxTool.Spawn: Spawn(_toolBp, _toolCount, _toolArmy, p, _toolSite); break;
            case SandboxTool.Nuke:
                Nuke(p);
                if (!Ui.Shift) Tool = SandboxTool.None; // Shift: keep nuking
                break;
            case SandboxTool.Clear:
                Clear(p, _toolRadius);
                if (!Ui.Shift) Tool = SandboxTool.None;
                break;
        }
    }

    public float ToolRadius { get => _toolRadius; set => _toolRadius = value; }

    private Rts.Sim.Core.UnitHandle[] SelectedAlive()
    {
        var snap = Ctx.Snapshots.Current;
        return Selection.Selected.Where(h => snap.TryGetUnit(h.Index, h.Generation, out _)).ToArray();
    }

    public int SelectedCount()
    {
        var snap = Ctx.Snapshots.Current;
        int n = 0;
        foreach (var h in Selection.Selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out _)) n++;
        return n;
    }

    public static bool ToggleState(SandboxStatus? st, SandboxToggle t, int army)
    {
        if (st == null) return false;
        var arr = t switch { SandboxToggle.InfiniteResources => st.InfiniteResources, SandboxToggle.InstantBuild => st.InstantBuild, _ => st.BuildAnywhere };
        return (uint)army < (uint)arr.Length && arr[army];
    }

    public static string ToggleName(SandboxToggle t) => t switch
    {
        SandboxToggle.InfiniteResources => "Infinite resources",
        SandboxToggle.InstantBuild => "Instant build",
        _ => "Build anywhere",
    };

    /// <summary>"you", "enemy N" (N-th army other than the local one, like the console's enemyN), "all armies".</summary>
    public string ArmyLabel(int army)
    {
        if (army < 0) return "all armies";
        if (army == Ctx.LocalArmy) return "you";
        return $"enemy {(army < Ctx.LocalArmy ? army + 1 : army)}";
    }

    /// <summary>Short label for buttons: "Me", "E1", "E2"…</summary>
    public string ArmyShort(int army) => army == Ctx.LocalArmy ? "Me" : $"E{(army < Ctx.LocalArmy ? army + 1 : army)}";

    // ------------------------------------------------------------------ feedback

    public void Ok(string msg) => Feedback(msg, SandboxConsole.Kind.Ok, Ui.Good);
    public void Info(string msg) => Feedback(msg, SandboxConsole.Kind.Info, Ui.TextColor);
    public void Error(string msg) => Feedback(msg, SandboxConsole.Kind.Error, Ui.Bad);

    private void Feedback(string msg, SandboxConsole.Kind kind, Color c)
    {
        ConsoleUi.Print(msg, kind);
        _toast = msg;
        _toastColor = c;
        _toastTime = Raylib.GetTime();
        System.Console.WriteLine("[sandbox] " + msg);
    }

    private void DrawToast()
    {
        double age = Raylib.GetTime() - _toastTime;
        if (age > 3.0 || _toast.Length == 0 || ConsoleUi.Open) return;
        byte a = (byte)(255 * Math.Clamp((3.0 - age) / 0.5, 0, 1));
        int fs = Ui.F(13);
        float w = Ui.Measure(_toast, fs, true) + Ui.P(24);
        var r = Ui.R((Ui.Sw - w) * 0.5f, Ui.P(62), w, fs + Ui.P(12));
        Raylib.DrawRectangleRec(r, new Color(8, 12, 18, (int)(a * 0.8f)));
        Raylib.DrawRectangleLinesEx(r, 1, ColorUtil.WithAlpha(_toastColor, (byte)(a / 2)));
        Ui.TextCentered(_toast, r, fs, ColorUtil.WithAlpha(_toastColor, a), true);
    }

    // ------------------------------------------------------------------ map tool visuals

    private void DrawTool3D()
    {
        if (Tool == SandboxTool.None) return;
        if (Ctx.PickGround(Raylib.GetMousePosition()) is not Vector3 g) return;
        var p = new Vector2(g.X, g.Z);
        Raylib.BeginMode3D(Ctx.Camera.Camera);
        float t = (float)Raylib.GetTime();
        switch (Tool)
        {
            case SandboxTool.Spawn:
            {
                var bp = Ctx.Db[_toolBp];
                float cell = MathF.Max(MathF.Max(bp.FootprintX, bp.FootprintZ), bp.IsStructure ? 2f : 1.6f) + (bp.IsStructure ? 1f : 0.6f);
                float r = MathF.Max(1.5f, MathF.Sqrt(_toolCount) * cell * 0.62f);
                var col = (uint)_toolArmy < (uint)Ctx.ArmyColors.Length ? Ctx.ArmyColors[_toolArmy] : Color.White;
                GroundCircle(p, r, ColorUtil.WithAlpha(col, 230));
                GroundCircle(p, r * (0.25f + 0.1f * MathF.Sin(t * 5f)), ColorUtil.WithAlpha(col, 160));
                float h = Ctx.Terrain.GetSurfaceHeight(p);
                Raylib.DrawLine3D(new Vector3(p.X, h, p.Y), new Vector3(p.X, h + 4f, p.Y), col);
                break;
            }
            case SandboxTool.Nuke:
                GroundCircle(p, SandboxSystem.NukeInnerRadius, new Color(255, 70, 40, 230));
                GroundCircle(p, SandboxSystem.NukeOuterRadius, new Color(255, 170, 60, 200));
                GroundCircle(p, 3f + MathF.Abs(MathF.Sin(t * 4f)) * 3f, new Color(255, 60, 40, 255));
                break;
            case SandboxTool.Clear:
                GroundCircle(p, _toolRadius, new Color(120, 220, 255, 220));
                GroundCircle(p, _toolRadius * 0.96f, new Color(120, 220, 255, 90));
                break;
        }
        Raylib.EndMode3D();
    }

    /// <summary>Circle that follows the terrain surface.</summary>
    private void GroundCircle(Vector2 c, float r, Color col)
    {
        int n = Math.Clamp((int)(r * 2.5f), 24, 160);
        Vector3 prev = default;
        for (int i = 0; i <= n; i++)
        {
            float a = i * MathF.Tau / n;
            var q = Ctx.Terrain.Clamp(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
            var v = new Vector3(q.X, Ctx.Terrain.GetSurfaceHeight(q) + 0.35f, q.Y);
            if (i > 0) Raylib.DrawLine3D(prev, v, col);
            prev = v;
        }
    }

    private void DrawToolHint()
    {
        if (Tool == SandboxTool.None) return;
        string s = Tool switch
        {
            SandboxTool.Spawn => $"Spawn {_toolCount} × {Ctx.Db[_toolBp].Name}{(_toolSite ? " (site)" : "")} → {ArmyLabel(_toolArmy)}",
            SandboxTool.Nuke => "NUKE: click the target (Shift: keep nuking)",
            _ => $"CLEAR radius {_toolRadius:0}: click the area",
        };
        var m = Ui.Mouse;
        int fs = Ui.F(11);
        float w = Ui.Measure(s, fs, true) + Ui.P(12);
        var r = Ui.R(m.X + Ui.P(18), m.Y + Ui.P(18), w, fs + Ui.P(8));
        if (r.X + r.Width > Ui.Sw) r.X = m.X - r.Width - Ui.P(8);
        Raylib.DrawRectangleRec(r, new Color(8, 12, 18, 210));
        Ui.Text(s, r.X + Ui.P(6), r.Y + Ui.P(3), fs, Tool == SandboxTool.Nuke ? Ui.Warn : Ui.TextColor, true);
        Ui.Text("right click / Esc: cancel", r.X + Ui.P(6), r.Y + r.Height + Ui.P(1), Ui.F(9.5f), Ui.TextFaint);
    }

    // ------------------------------------------------------------------ buttons + help

    private void DrawCornerButtons()
    {
        _panelBtn = _consoleBtn = default;
        if (Panel.Visible) return;
        float bw = Ui.P(118), bh = Ui.P(24), y = Ui.P(58);
        _panelBtn = Ui.R(Ui.Sw - Ui.P(6) - bw, y, bw, bh);
        _consoleBtn = Ui.R(_panelBtn.X - Ui.P(6) - bw, y, bw, bh);
        var b = Ui.Button(_panelBtn, true, false, new Color(70, 52, 20, 255));
        Ui.TextCentered("SANDBOX  F9", _panelBtn, Ui.F(11), Ui.Warn, true);
        if (b.Left) TogglePanel();
        b = Ui.Button(_consoleBtn, true, ConsoleUi.Open);
        Ui.TextCentered("CONSOLE  F10", _consoleBtn, Ui.F(11), Ui.TextColor, true);
        if (b.Left) ToggleConsole();
    }

    public static readonly string[] HotkeyLines =
    [
        "F9  sandbox panel          F10 or `  console (Enter run, Tab complete, Up/Down history)",
        "Esc  close console / help, cancel the map tool       Space  pause      + / -  game speed",
        "Map tool (spawn / nuke / clear): left click apply, Shift+click repeat, right click cancel",
        "F7  fog of war on/off (client)      F8  performance overlay",
    ];

    private void DrawHelp()
    {
        var lines = SandboxConsoleParser.HelpLines;
        int fs = Ui.F(11);
        float lh = fs + Ui.P(4);
        float w = MathF.Min(Ui.P(940), Ui.Sw - Ui.P(320));
        int rows = (lines.Count + 1) / 2;
        float h = Ui.P(30) + (rows + HotkeyLines.Length + 1.5f) * lh + Ui.P(8);
        float y0 = MathF.Max(Ui.P(60), (Ui.Sh - h) * 0.4f);
        if (ConsoleUi.Open && ConsoleUi.Rect.Y + ConsoleUi.Rect.Height + Ui.P(6) + h < Ui.Sh) y0 = ConsoleUi.Rect.Y + ConsoleUi.Rect.Height + Ui.P(6);
        _helpRect = Ui.R(Ui.P(12), y0, w, h);
        var r = _helpRect;
        Ui.Panel(r);
        float y = Ui.PanelTitle(r, "Sandbox help — console commands", Ui.Warn) + Ui.P(4);
        float colW = (r.Width - Ui.P(36)) / 2;
        for (int i = 0; i < lines.Count; i++)
        {
            float x = r.X + Ui.P(12) + (i / rows) * (colW + Ui.P(12));
            Ui.Text(Ui.Fit(lines[i], colW, fs), x, y + (i % rows) * lh, fs, Ui.TextColor);
        }
        y += rows * lh + lh * 0.5f;
        Ui.Text("HOTKEYS", r.X + Ui.P(12), y, Ui.F(10.5f), Ui.TextDim, true);
        y += lh;
        foreach (var l in HotkeyLines)
        {
            Ui.Text(Ui.Fit(l, r.Width - Ui.P(24), fs), r.X + Ui.P(12), y, fs, Ui.TextDim);
            y += lh;
        }
        Ui.TextRight("armies: me, enemy, enemy2, army3, all · click / Esc to close", r.X + r.Width - Ui.P(10), r.Y + Ui.P(5), Ui.F(10), Ui.TextFaint);
    }
}
