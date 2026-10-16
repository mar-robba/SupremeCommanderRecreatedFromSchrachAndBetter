using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;

namespace Rts.Client.Input;

/// <summary>
/// Selection (click / box / double-click same type), control groups, right-click orders, order modes
/// (move / attack-move / patrol / guard / launch / capture) and structure placement (<see cref="BuildPlacement"/>).
/// Talks to the sim ONLY via SimRunner.Enqueue(Command). In the sandbox (<see cref="GameContext.ControlAnyArmy"/>)
/// units of any army can be selected; orders are then sent once per army (<see cref="ArmyCommands"/>).
/// </summary>
public sealed class SelectionController
{
    private readonly GameContext _ctx;
    public readonly List<UnitHandle> Selected = new();
    private readonly List<UnitHandle>[] _groups = Enumerable.Range(0, 10).Select(_ => new List<UnitHandle>()).ToArray();
    private Vector2? _dragStart;
    private double _lastClickTime;
    private UnitHandle _lastClicked = UnitHandle.None;

    public enum CommandMode { Normal, AttackMove, Patrol, Move, Guard, Launch, Capture, Unload, TransportTo, Ferry }
    public CommandMode Mode = CommandMode.Normal;

    /// <summary>Structure placement mode (build menu).</summary>
    public readonly BuildPlacement Placement;

    /// <summary>Units waiting for self-destruct confirmation (set by Ctrl+K / the order button; the HUD confirms).</summary>
    public UnitHandle[]? PendingSelfDestruct;

    /// <summary>Set by the HUD while a modal dialog is open: world input and hotkeys are ignored.</summary>
    public bool InputBlocked;

    /// <summary>What a right-click would do at the cursor (context orders, shown as a cursor hint).</summary>
    public ContextAction HoverAction = ContextAction.None;

    public SelectionController(GameContext ctx)
    {
        _ctx = ctx;
        Placement = new BuildPlacement(ctx);
    }

    public bool IsDragging => _dragStart.HasValue && Vector2.Distance(_dragStart.Value, Raylib.GetMousePosition()) > 6;
    public Rectangle DragRect
    {
        get
        {
            var a = _dragStart ?? Vector2.Zero;
            var b = Raylib.GetMousePosition();
            return new Rectangle(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));
        }
    }

    public void Update(bool mouseOverUi)
    {
        var snap = _ctx.Snapshots.Current;
        PruneDead(snap);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (InputBlocked) { _dragStart = null; return; }

        HandleHotkeys(ctrl, shift);
        bool hover = !mouseOverUi && !_ctx.ReadOnly && !Placement.Active && Selected.Count > 0 && !IsDragging;
        HoverAction = !hover ? ContextAction.None
            : Mode == CommandMode.Normal ? ContextOrders.Evaluate(_ctx, this, Raylib.GetMousePosition())
            : Mode == CommandMode.Capture ? UnitToggles.CaptureHover(_ctx, this, Raylib.GetMousePosition())
            : ContextAction.None;

        if (Placement.Active)
        {
            _dragStart = null;
            Placement.Update(mouseOverUi, Selected, shift);
            return;
        }

        if (!mouseOverUi && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Mode != CommandMode.Normal)
            {
                if (IssueAtCursor(shift, Mode) && !shift) Mode = CommandMode.Normal;
                return;
            }
            _dragStart = Raylib.GetMousePosition();
        }
        if (_dragStart.HasValue && Raylib.IsMouseButtonReleased(MouseButton.Left))
        {
            if (IsDragging) BoxSelect(DragRect, shift);
            else ClickSelect(Raylib.GetMousePosition(), shift, ctrl);
            _dragStart = null;
        }
        if (!mouseOverUi && !_ctx.ReadOnly && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            if (Mode != CommandMode.Normal) Mode = CommandMode.Normal;
            else IssueAtCursor(shift, CommandMode.Normal);
        }
    }

    private void HandleHotkeys(bool ctrl, bool shift)
    {
        if (Raylib.IsKeyPressed(Hotkeys.Menu.Key)) { Mode = CommandMode.Normal; }
        if (Selected.Count > 0 && !_ctx.ReadOnly) // replays: no orders
        {
            if (Hotkeys.Stop.Pressed()) IssueStop();
            if (Hotkeys.AttackMove.Pressed()) SetMode(CommandMode.AttackMove);
            if (Hotkeys.Patrol.Pressed()) SetMode(CommandMode.Patrol);
            if (Hotkeys.Move.Pressed()) SetMode(CommandMode.Move);
            if (Hotkeys.Guard.Pressed()) SetMode(CommandMode.Guard);
            if (Hotkeys.SelfDestruct.Pressed()) RequestSelfDestruct();
            StrategicOrders.HandleHotkey(_ctx, this, ctrl);
            UnitToggles.HandleHotkeys(_ctx, this, ctrl);
        }
        else if (Mode != CommandMode.Normal) Mode = CommandMode.Normal;
        TransportInput.Update(_ctx, this, ctrl || _ctx.ReadOnly); // U unload / E transport / F ferry / D dock (replays: none)
        int set = Hotkeys.GroupSet.PressedIndex();
        if (set >= 0) { _groups[set].Clear(); _groups[set].AddRange(Selected); }
        int g = Hotkeys.GroupRecall.PressedIndex();
        if (g >= 0)
        {
            _groups[g].RemoveAll(h => !_ctx.Snapshots.Current.TryGetUnit(h.Index, h.Generation, out _));
            if (_groups[g].Count > 0)
            {
                if (!shift) Selected.Clear();
                foreach (var h in _groups[g]) if (!Selected.Contains(h)) Selected.Add(h);
                // Double-tap: center the camera on the group.
                double now = Raylib.GetTime();
                if (_lastGroup == g && now - _lastGroupTime < DoubleTapSeconds) CenterOn(_groups[g]);
                _lastGroup = g;
                _lastGroupTime = now;
            }
        }
    }

    /// <summary>Double-click / double-tap window (seconds).</summary>
    public const double DoubleTapSeconds = 0.35;
    private int _lastGroup = -1;
    private double _lastGroupTime;

    /// <summary>Moves the camera (keeping the zoom) to the unit of the group nearest to the group's centroid.</summary>
    public void CenterOn(IReadOnlyList<UnitHandle> units)
    {
        var snap = _ctx.Snapshots.Current;
        var pts = new List<Vector2>(units.Count);
        foreach (var h in units)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v)) pts.Add(new Vector2(v.Pos.X, v.Pos.Z));
        if (SelectionMath.FocusPoint(pts) is Vector2 p) _ctx.Camera.LookAt(p.X, p.Y, _ctx.Camera.Distance);
    }

    /// <summary>Enters an order mode (cancels structure placement).</summary>
    public void SetMode(CommandMode mode)
    {
        if (Selected.Count == 0) return;
        Placement.Cancel();
        Mode = mode;
    }

    public void IssueStop()
    {
        if (Selected.Count == 0) return;
        ArmyCommands.Send(_ctx, Selected, (army, units) => new StopCommand(army, units));
    }

    /// <summary>Asks for self-destruct confirmation (Ctrl+K twice or the dialog confirms).</summary>
    public void RequestSelfDestruct()
    {
        if (Selected.Count == 0) return;
        if (PendingSelfDestruct != null) { ConfirmSelfDestruct(); return; }
        PendingSelfDestruct = Selected.ToArray();
    }

    public void ConfirmSelfDestruct()
    {
        if (PendingSelfDestruct is { Length: > 0 } units)
            ArmyCommands.Send(_ctx, units, (army, group) => new SelfDestructCommand(army, group));
        PendingSelfDestruct = null;
    }

    private void PruneDead(SimSnapshot snap)
    {
        Selected.RemoveAll(h => !snap.TryGetUnit(h.Index, h.Generation, out _));
    }

    // ------------------------------------------------------------------ selection helpers (used by the HUD)

    public void SelectOnly(IEnumerable<UnitHandle> units)
    {
        Selected.Clear();
        foreach (var h in units) if (!Selected.Contains(h)) Selected.Add(h);
    }

    /// <summary>Keeps only selected units of blueprint bp.</summary>
    public void NarrowToType(int bp)
    {
        var snap = _ctx.Snapshots.Current;
        Selected.RemoveAll(h => !snap.TryGetUnit(h.Index, h.Generation, out var v) || v.Bp != bp);
    }

    /// <summary>Removes selected units of blueprint bp.</summary>
    public void RemoveType(int bp)
    {
        var snap = _ctx.Snapshots.Current;
        Selected.RemoveAll(h => snap.TryGetUnit(h.Index, h.Generation, out var v) && v.Bp == bp);
    }

    /// <summary>Order at a world point (minimap clicks). Mode Normal = move (rally for factories).</summary>
    public void IssueAtPoint(Vector2 p, CommandMode mode, bool queue)
    {
        if (Selected.Count == 0) return;
        var order = mode switch
        {
            CommandMode.AttackMove => Order.AttackMoveTo(p),
            CommandMode.Patrol => Order.PatrolTo(p),
            _ => Order.MoveTo(p),
        };
        _ctx.Feedback.AddClickMarker(new Vector3(p.X, _ctx.Terrain.GetSurfaceHeight(p), p.Y), order.Type);
        ArmyCommands.Send(_ctx, Selected, (army, units) => new IssueOrderCommand(army, units, order, queue));
        UiSounds.Confirm();
    }

    /// <summary>
    /// Minimap click in any order mode: the mode's order at world point p. Guard / Capture take the nearest fitting
    /// unit within <paramref name="pickRadius"/> (guard: an own unit, or an enemy = attack it; capture: a capturable
    /// enemy). Returns true when an order was issued (the caller ends the mode unless Shift).
    /// </summary>
    public bool IssueAtWorldPoint(Vector2 p, CommandMode mode, bool queue, float pickRadius)
    {
        if (Selected.Count == 0) return false;
        switch (mode)
        {
            case CommandMode.Normal or CommandMode.Move or CommandMode.AttackMove or CommandMode.Patrol:
                IssueAtPoint(p, mode, queue);
                return true;
            case CommandMode.Launch:
                return StrategicOrders.TryLaunchAt(_ctx, this, p);
            case CommandMode.Capture:
            {
                var armies = ArmyCommands.Groups(_ctx, Selected).Select(g => g.Army).ToArray();
                var t = NearestUnit(p, pickRadius, v => ContextOrders.CanCapture(_ctx.Db[v.Bp], v) && armies.Any(a => _ctx.AreEnemies(a, v.Army)));
                if (t is not UnitView tv) { UiSounds.Error(); return false; }
                return UnitToggles.TryCaptureUnit(_ctx, this, tv, queue);
            }
            case CommandMode.Guard:
            {
                var t = NearestUnit(p, pickRadius, v => !Selected.Contains(new UnitHandle(v.Index, v.Gen)));
                if (t is not UnitView tv) { UiSounds.Error(); return false; }
                bool issued = false;
                foreach (var (army, units) in ArmyCommands.Groups(_ctx, Selected))
                {
                    var h = new UnitHandle(tv.Index, tv.Gen);
                    Order order;
                    if (_ctx.AreEnemies(army, tv.Army)) order = Order.AttackUnit(h);
                    else if (tv.Army == army) order = Order.GuardUnit(h);
                    else continue;
                    _ctx.Runner.Enqueue(new IssueOrderCommand(army, units, order, queue));
                    issued = true;
                }
                if (!issued) { UiSounds.Error(); return false; }
                _ctx.Feedback.AddClickMarker(tv.Pos, OrderType.Guard);
                UiSounds.Confirm();
                return true;
            }
            default:
                return TransportInput.IsTransportMode(mode) && TransportInput.TryIssueModeAt(_ctx, this, p, queue, mode);
        }
    }

    /// <summary>The visible unit nearest to world point p within radius r that passes the filter, or null.</summary>
    public UnitView? NearestUnit(Vector2 p, float r, Func<UnitView, bool> filter)
    {
        var snap = _ctx.Snapshots.Current;
        float best = r * r;
        UnitView? result = null;
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            float d = Vector2.DistanceSquared(p, new Vector2(v.Pos.X, v.Pos.Z));
            if (d <= best && filter(v)) { best = d; result = v; }
        }
        return result;
    }

    // ------------------------------------------------------------------ picking

    /// <summary>Unit under the cursor (closest screen-space hit), or null.</summary>
    public UnitView? PickUnit(Vector2 mouse)
    {
        var snap = _ctx.Snapshots.Current;
        float best = float.MaxValue;
        UnitView? result = null;
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            var bp = _ctx.Db[v.Bp];
            var center = v.Pos + new Vector3(0, bp.Visual.Size.Y * 0.5f, 0);
            var sp = Raylib.GetWorldToScreen(center, _ctx.Camera.Camera);
            float radiusPx = MathF.Max(10f, ScreenRadius(center, MathF.Max(bp.Radius, bp.Visual.Size.X * 0.5f)));
            float d = Vector2.Distance(sp, mouse);
            if (d < radiusPx && d < best) { best = d; result = v; }
        }
        return result;
    }

    private float ScreenRadius(Vector3 worldPos, float r)
    {
        var a = Raylib.GetWorldToScreen(worldPos, _ctx.Camera.Camera);
        var b = Raylib.GetWorldToScreen(worldPos + new Vector3(r, 0, 0), _ctx.Camera.Camera);
        return Vector2.Distance(a, b);
    }

    private void ClickSelect(Vector2 mouse, bool shift, bool ctrl)
    {
        var hit = PickUnit(mouse);
        if (hit is not UnitView v || !_ctx.Controls(v.Army))
        {
            if (!shift) Selected.Clear();
            return;
        }
        var h = new UnitHandle(v.Index, v.Gen);
        double now = Raylib.GetTime();
        bool dbl = now - _lastClickTime < DoubleTapSeconds && _lastClicked == h;
        _lastClickTime = now;
        _lastClicked = h;
        if (dbl || ctrl)
        {
            // Double-click / Ctrl+click: every on-screen unit of that type and army; with Shift (Ctrl+Shift+click,
            // Shift+double-click) they are added to the selection.
            if (!shift) Selected.Clear();
            else if (dbl && !Selected.Contains(h)) Selected.Add(h); // the first Shift-click of the pair toggled it off
            SelectTypeOnScreen(v.Bp, v.Army);
            return;
        }
        if (shift)
        {
            if (!Selected.Remove(h)) Selected.Add(h);
        }
        else
        {
            Selected.Clear();
            Selected.Add(h);
        }
    }

    /// <summary>Adds every unit of blueprint bp and army on screen to the selection (double-click, Ctrl+click,
    /// Ctrl+click on a unit panel icon).</summary>
    public void SelectTypeOnScreen(int bp, int army)
    {
        var snap = _ctx.Snapshots.Current;
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var u = ref snap.Units[k];
            if (u.Army != army || u.Bp != bp) continue;
            var sp = Raylib.GetWorldToScreen(u.Pos, _ctx.Camera.Camera);
            if (sp.X < 0 || sp.Y < 0 || sp.X > sw || sp.Y > sh) continue;
            var uh = new UnitHandle(u.Index, u.Gen);
            if (!Selected.Contains(uh)) Selected.Add(uh);
        }
    }

    private void BoxSelect(Rectangle r, bool shift)
    {
        if (!shift) Selected.Clear();
        var snap = _ctx.Snapshots.Current;
        bool anyMobile = false;
        var picked = new List<(UnitHandle h, bool mobile)>();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            if (!_ctx.Controls(v.Army)) continue;
            var sp = Raylib.GetWorldToScreen(v.Pos, _ctx.Camera.Camera);
            if (!Raylib.CheckCollisionPointRec(sp, r)) continue;
            bool mobile = _ctx.Db[v.Bp].IsMobile;
            anyMobile |= mobile;
            picked.Add((new UnitHandle(v.Index, v.Gen), mobile));
        }
        // SupCom: box selection prefers mobile units over structures.
        foreach (var (h, mobile) in picked)
            if ((mobile || !anyMobile) && !Selected.Contains(h)) Selected.Add(h);
    }

    /// <summary>Issues the order for the mode at the cursor. Returns false if nothing was issued.</summary>
    private bool IssueAtCursor(bool queue, CommandMode mode)
    {
        if (Selected.Count == 0) return false;
        var mouse = Raylib.GetMousePosition();
        if (mode == CommandMode.Launch) return StrategicOrders.TryLaunch(_ctx, this, mouse);
        if (mode == CommandMode.Capture) return UnitToggles.TryCapture(_ctx, this, mouse, queue);
        if (TransportInput.IsTransportMode(mode)) return TransportInput.TryIssueMode(_ctx, this, mouse, queue, mode);
        var target = PickUnit(mouse);
        bool targetIsSelected = target is UnitView sv && Selected.Contains(new UnitHandle(sv.Index, sv.Gen));
        Vector3? ground = null;
        bool groundPicked = false, issued = false, marker = false;
        // One order per army (normal games: just the local army); relations are judged from each army's side.
        foreach (var (army, units) in ArmyCommands.Groups(_ctx, Selected))
        {
            // Engineers / commander: reclaim, repair, finish, assist or capture; transports: load / unload / dock (see ContextOrders).
            if (mode == CommandMode.Normal && ContextOrders.TryIssue(_ctx, this, army, units, mouse, queue)) { issued = true; continue; }
            Order order;
            if (mode is CommandMode.Normal or CommandMode.AttackMove or CommandMode.Guard && target is UnitView tv && _ctx.AreEnemies(army, tv.Army))
                order = Order.AttackUnit(new UnitHandle(tv.Index, tv.Gen));
            else if (mode is CommandMode.Normal or CommandMode.Guard && target is UnitView av && av.Army == army && !targetIsSelected)
                order = Order.GuardUnit(new UnitHandle(av.Index, av.Gen));
            else if (mode == CommandMode.Guard)
                continue; // Guard/assist needs a friendly unit under the cursor.
            else
            {
                if (!groundPicked) { ground = _ctx.PickGround(mouse); groundPicked = true; }
                if (ground is not Vector3 g) continue;
                var p = new Vector2(g.X, g.Z);
                order = mode switch
                {
                    CommandMode.AttackMove => Order.AttackMoveTo(p),
                    CommandMode.Patrol => Order.PatrolTo(p),
                    _ => Order.MoveTo(p),
                };
                if (!marker) { _ctx.Feedback.AddClickMarker(g, order.Type); marker = true; }
            }
            _ctx.Runner.Enqueue(new IssueOrderCommand(army, units, order, queue));
            issued = true;
        }
        if (!issued)
        {
            if (mode == CommandMode.Guard) UiSounds.Error();
            return false;
        }
        UiSounds.Confirm();
        return true;
    }
}
