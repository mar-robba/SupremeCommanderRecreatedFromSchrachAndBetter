using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Naval;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;
using Rts.Client.UI;

namespace Rts.Client.Input;

/// <summary>
/// Toggle orders for the selection: pause (<see cref="PauseHotkey"/>: factories, engineers, upgrades, silos, upkeep
/// users), dive / surface for submarines (<see cref="DiveHotkey"/>) and the capture targeting mode
/// (<see cref="CaptureHotkey"/>, CommandMode.Capture: click an enemy unit, engineers / commanders capture it).
/// D and E are shared with the transport orders (<see cref="SharedHotkeys"/>).
/// Commands are split per army (<see cref="ArmyCommands"/>).
/// </summary>
public static partial class UnitToggles
{
    public static KeyboardKey PauseHotkey => Hotkeys.Pause.Key;
    public static string PauseKeyName => Hotkeys.Pause.Label;
    public static KeyboardKey DiveHotkey => Hotkeys.Dive.Key;
    public static string DiveKeyName => Hotkeys.Dive.Label;
    public static KeyboardKey CaptureHotkey => Hotkeys.Capture.Key;
    public static string CaptureKeyName => Hotkeys.Capture.Label;

    /// <summary>State of the selection for a toggle: Count = units it applies to, On = how many have it on.</summary>
    public readonly record struct ToggleState(int Count, int On)
    {
        public bool Any => Count > 0;
        public bool AllOn => Count > 0 && On == Count;
    }

    /// <summary>Pausable selected units (complete, controllable) and how many are paused.</summary>
    public static ToggleState PauseState(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var snap = ctx.Snapshots.Current;
        int n = 0, on = 0;
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !ctx.Controls(v.Army)) continue;
            if (!PauseSystem.CanPause(ctx.Db[v.Bp])) continue;
            n++;
            if ((v.Flags & UnitFlags.Paused) != 0) on++;
        }
        return new ToggleState(n, on);
    }

    /// <summary>Selected submarines and how many are submerged (On = submerged).</summary>
    public static ToggleState DiveState(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var snap = ctx.Snapshots.Current;
        int n = 0, on = 0;
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !ctx.Controls(v.Army)) continue;
            if (!Submarines.CanDive(ctx.Db[v.Bp])) continue;
            n++;
            if ((v.Flags & UnitFlags.Surfaced) == 0) on++;
        }
        return new ToggleState(n, on);
    }

    /// <summary>Pause toggle: pauses everything if any pausable unit is running, else resumes all.</summary>
    public static void TogglePause(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var st = PauseState(ctx, selected);
        if (!st.Any) { UiSounds.Error(); return; }
        bool pause = !st.AllOn;
        ArmyCommands.Send(ctx, selected, (army, units) => new SetPausedCommand(army, units, pause));
        UiSounds.Confirm();
    }

    /// <summary>Dive toggle: dives if any selected sub is surfaced, else surfaces all.</summary>
    public static void ToggleDive(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var st = DiveState(ctx, selected);
        if (!st.Any) { UiSounds.Error(); return; }
        bool dive = !st.AllOn;
        ArmyCommands.Send(ctx, selected, (army, units) => new SetSubmergedCommand(army, units, dive));
        UiSounds.Confirm();
    }

    /// <summary>Any complete, controllable engineer / commander in the selection (capture button enabled).</summary>
    public static bool AnyCapturer(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var snap = ctx.Snapshots.Current;
        foreach (var h in selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && ctx.Controls(v.Army) && ContextOrders.IsBuilder(ctx.Db[v.Bp])
                && (v.Flags & UnitFlags.UnderConstruction) == 0)
                return true;
        return false;
    }

    /// <summary>The unit under the mouse if some selected builder could capture it (Capture mode cursor), else null.</summary>
    public static UnitView? CaptureTarget(GameContext ctx, SelectionController sel, Vector2 mouse)
    {
        if (sel.PickUnit(mouse) is not UnitView t || !ContextOrders.CanCapture(ctx.Db[t.Bp], t)) return null;
        foreach (var (army, _) in ArmyCommands.Groups(ctx, sel.Selected))
            if (ctx.AreEnemies(army, t.Army)) return t;
        return null;
    }

    /// <summary>Cursor hint in Capture mode: the capturable target under the mouse.</summary>
    public static ContextAction CaptureHover(GameContext ctx, SelectionController sel, Vector2 mouse) =>
        CaptureTarget(ctx, sel, mouse) is UnitView t
            ? new ContextAction(ContextKind.Capture, new UnitHandle(t.Index, t.Gen), new Vector2(t.Pos.X, t.Pos.Z))
            : ContextAction.None;

    /// <summary>Capture mode click: every selected engineer / commander hostile to the unit under the mouse gets a
    /// capture order (Shift queues). Returns false (error sound) if nothing can capture it.</summary>
    public static bool TryCapture(GameContext ctx, SelectionController sel, Vector2 mouse, bool queue)
    {
        if (CaptureTarget(ctx, sel, mouse) is not UnitView t) { UiSounds.Error(); return false; }
        return TryCaptureUnit(ctx, sel, t, queue);
    }

    /// <summary>Capture order on unit t for every selected builder hostile to it (also minimap clicks).</summary>
    public static bool TryCaptureUnit(GameContext ctx, SelectionController sel, UnitView t, bool queue)
    {
        var snap = ctx.Snapshots.Current;
        var target = new UnitHandle(t.Index, t.Gen);
        bool issued = false;
        foreach (var (army, units) in ArmyCommands.Groups(ctx, sel.Selected))
        {
            if (!ctx.AreEnemies(army, t.Army)) continue;
            var builders = units.Where(h => snap.TryGetUnit(h.Index, h.Generation, out var v) && ContextOrders.IsBuilder(ctx.Db[v.Bp])
                                            && (v.Flags & UnitFlags.UnderConstruction) == 0).ToArray();
            if (builders.Length == 0) continue;
            ctx.Runner.Enqueue(new IssueOrderCommand(army, builders, Order.CaptureUnit(target), queue));
            issued = true;
        }
        if (!issued) { UiSounds.Error(); return false; }
        ctx.Feedback.AddClickMarker(t.Pos, OrderType.Capture);
        UiSounds.Confirm();
        return true;
    }

    /// <summary>Hotkeys (called by SelectionController with a selection, not while typing).</summary>
    public static void HandleHotkeys(GameContext ctx, SelectionController sel, bool ctrl)
    {
        if (ctrl) return;
        if (Hotkeys.Pause.Pressed() && PauseState(ctx, sel.Selected).Any) TogglePause(ctx, sel.Selected);
        if (Hotkeys.Dive.Pressed() && DiveState(ctx, sel.Selected).Any) ToggleDive(ctx, sel.Selected);
        OverchargeHotkeys(ctx, sel); // UnitToggles.Overcharge.cs
        if (Hotkeys.FireState.Pressed() && FireStateOf(ctx, sel.Selected).Any) CycleFireState(ctx, sel.Selected, Ui.Shift ? -1 : 1);
        // D and E are shared with the transport orders (SharedHotkeys): D also docks aircraft (TransportInput);
        // E is transport-to as soon as transports are selected.
        if (Hotkeys.Capture.Pressed() && EKey(ctx, sel.Selected) == SharedHotkeys.EAction.Capture)
            sel.SetMode(SelectionController.CommandMode.Capture);
    }

    /// <summary>What E does for this selection (capture or transport-to, see <see cref="SharedHotkeys.ResolveE"/>).</summary>
    public static SharedHotkeys.EAction EKey(GameContext ctx, IReadOnlyList<UnitHandle> selected) =>
        SharedHotkeys.ResolveE(AnyCapturer(ctx, selected), CargoSelection.Of(ctx, selected, ctx.Snapshots.Current).Transports > 0);
}
