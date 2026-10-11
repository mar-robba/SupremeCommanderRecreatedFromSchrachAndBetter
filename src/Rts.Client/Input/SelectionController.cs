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
/// (move / attack-move / patrol / guard) and structure placement (<see cref="BuildPlacement"/>).
/// Talks to the sim ONLY via SimRunner.Enqueue(Command).
/// </summary>
public sealed class SelectionController
{
    private readonly GameContext _ctx;
    public readonly List<UnitHandle> Selected = new();
    private readonly List<UnitHandle>[] _groups = Enumerable.Range(0, 10).Select(_ => new List<UnitHandle>()).ToArray();
    private Vector2? _dragStart;
    private double _lastClickTime;
    private UnitHandle _lastClicked = UnitHandle.None;

    public enum CommandMode { Normal, AttackMove, Patrol, Move, Guard }
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
        HoverAction = !mouseOverUi && Mode == CommandMode.Normal && !Placement.Active && Selected.Count > 0 && !IsDragging
            ? ContextOrders.Evaluate(_ctx, this, Raylib.GetMousePosition()) : ContextAction.None;

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
        if (!mouseOverUi && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            if (Mode != CommandMode.Normal) Mode = CommandMode.Normal;
            else IssueAtCursor(shift, CommandMode.Normal);
        }
    }

    private void HandleHotkeys(bool ctrl, bool shift)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Mode = CommandMode.Normal; }
        if (Selected.Count > 0)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.S) && !ctrl) IssueStop();
            if (Raylib.IsKeyPressed(KeyboardKey.A) && !ctrl) SetMode(CommandMode.AttackMove);
            if (Raylib.IsKeyPressed(KeyboardKey.P) && !ctrl) SetMode(CommandMode.Patrol);
            if (Raylib.IsKeyPressed(KeyboardKey.M) && !ctrl) SetMode(CommandMode.Move);
            if (Raylib.IsKeyPressed(KeyboardKey.G) && !ctrl) SetMode(CommandMode.Guard);
            if (ctrl && Raylib.IsKeyPressed(KeyboardKey.K)) RequestSelfDestruct();
        }
        else if (Mode != CommandMode.Normal) Mode = CommandMode.Normal;
        for (int g = 0; g <= 9; g++)
        {
            if (!Raylib.IsKeyPressed(KeyboardKey.Zero + g)) continue;
            if (ctrl) { _groups[g].Clear(); _groups[g].AddRange(Selected); }
            else if (_groups[g].Count > 0)
            {
                if (!shift) Selected.Clear();
                foreach (var h in _groups[g]) if (!Selected.Contains(h)) Selected.Add(h);
            }
        }
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
        _ctx.Runner.Enqueue(new StopCommand(_ctx.LocalArmy, Selected.ToArray()));
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
            _ctx.Runner.Enqueue(new SelfDestructCommand(_ctx.LocalArmy, units));
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
        _ctx.Runner.Enqueue(new IssueOrderCommand(_ctx.LocalArmy, Selected.ToArray(), order, queue));
        UiSounds.Confirm();
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
        if (hit is not UnitView v || v.Army != _ctx.LocalArmy)
        {
            if (!shift) Selected.Clear();
            return;
        }
        var h = new UnitHandle(v.Index, v.Gen);
        double now = Raylib.GetTime();
        bool dbl = now - _lastClickTime < 0.35 && _lastClicked == h;
        _lastClickTime = now;
        _lastClicked = h;
        if (dbl || ctrl)
        {
            // Select all on-screen own units of the same type.
            if (!shift) Selected.Clear();
            var snap = _ctx.Snapshots.Current;
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            for (int k = 0; k < snap.UnitCount; k++)
            {
                ref var u = ref snap.Units[k];
                if (u.Army != _ctx.LocalArmy || u.Bp != v.Bp) continue;
                var sp = Raylib.GetWorldToScreen(u.Pos, _ctx.Camera.Camera);
                if (sp.X < 0 || sp.Y < 0 || sp.X > sw || sp.Y > sh) continue;
                var uh = new UnitHandle(u.Index, u.Gen);
                if (!Selected.Contains(uh)) Selected.Add(uh);
            }
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

    private void BoxSelect(Rectangle r, bool shift)
    {
        if (!shift) Selected.Clear();
        var snap = _ctx.Snapshots.Current;
        bool anyMobile = false;
        var picked = new List<(UnitHandle h, bool mobile)>();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            if (v.Army != _ctx.LocalArmy) continue;
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
        // Engineers / commander: reclaim wrecks, repair, finish or assist (see ContextOrders).
        if (mode == CommandMode.Normal && ContextOrders.TryIssue(_ctx, this, mouse, queue)) return true;
        var units = Selected.ToArray();
        var target = PickUnit(mouse);
        bool targetIsSelected = target is UnitView sv && Selected.Contains(new UnitHandle(sv.Index, sv.Gen));
        Order order;
        if (mode is CommandMode.Normal or CommandMode.AttackMove or CommandMode.Guard && target is UnitView tv && _ctx.IsEnemy(tv.Army))
            order = Order.AttackUnit(new UnitHandle(tv.Index, tv.Gen));
        else if (mode is CommandMode.Normal or CommandMode.Guard && target is UnitView av && av.Army == _ctx.LocalArmy && !targetIsSelected)
            order = Order.GuardUnit(new UnitHandle(av.Index, av.Gen));
        else if (mode == CommandMode.Guard)
        {
            UiSounds.Error();
            return false; // Guard/assist needs a friendly unit under the cursor.
        }
        else
        {
            if (_ctx.PickGround(mouse) is not Vector3 g) return false;
            var p = new Vector2(g.X, g.Z);
            order = mode switch
            {
                CommandMode.AttackMove => Order.AttackMoveTo(p),
                CommandMode.Patrol => Order.PatrolTo(p),
                _ => Order.MoveTo(p),
            };
            _ctx.Feedback.AddClickMarker(g, order.Type);
        }
        _ctx.Runner.Enqueue(new IssueOrderCommand(_ctx.LocalArmy, units, order, queue));
        UiSounds.Confirm();
        return true;
    }
}
