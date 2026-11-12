using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.UI;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Transport;
using Rts.Sim.World;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.Input;

/// <summary>What the selection holds, as far as transports are concerned (own, finished units only). Pure.</summary>
public struct CargoSelection
{
    /// <summary>Land units that can ride a transport, and the smallest of them in slots (0 = none).</summary>
    public int LandCargo, SmallestLandCargo;
    /// <summary>Aircraft that can dock at an air carrier (air staging / Atlantis), transports included.</summary>
    public int AirCargo;
    /// <summary>Air transports (VTOL land carriers), those with cargo, and the most free slots among them.</summary>
    public int Transports, LoadedTransports, MaxTransportFree;
    /// <summary>Air carriers (air staging, Atlantis) with docked aircraft; the mobile ones (Atlantis) among them.</summary>
    public int LoadedAirCarriers, MobileLoadedAirCarriers;

    /// <summary>Adds one selected unit (<paramref name="used"/> = cargo slots in use if it is a carrier).</summary>
    public void Add(UnitBlueprint bp, int used)
    {
        if (TransportOrders.IsLandCargo(bp))
        {
            LandCargo++;
            if (SmallestLandCargo == 0 || bp.CargoSize < SmallestLandCargo) SmallestLandCargo = bp.CargoSize;
        }
        if (TransportInput.IsAirCargo(bp)) AirCargo++;
        if (bp.Cargo == null) return;
        if (bp.Cargo.Layer == CargoLayer.Land)
        {
            Transports++;
            if (used > 0) LoadedTransports++;
            MaxTransportFree = Math.Max(MaxTransportFree, bp.Cargo.Capacity - used);
        }
        else if (used > 0)
        {
            LoadedAirCarriers++;
            if (bp.IsMobile) MobileLoadedAirCarriers++;
        }
    }

    public static CargoSelection Of(GameContext ctx, IEnumerable<UnitHandle> selected, SimSnapshot snap)
    {
        var s = new CargoSelection();
        foreach (var h in selected)
            if (TransportInput.TryOwnUnit(ctx, snap, h, out var v))
                s.Add(ctx.Db[v.Bp], TransportInput.UsedSlots(snap, v.Index, v.Gen));
        return s;
    }
}

/// <summary>Transport order buttons that apply to a selection (see <see cref="TransportInput.ButtonsFor"/>).</summary>
[Flags]
public enum TransportButtons : byte { None = 0, Unload = 1, Launch = 2, TransportTo = 4, Ferry = 8, Dock = 16 }

/// <summary>
/// Client side of the cargo mechanic (sim: Rts.Sim/Transport/TransportOrders). Context right-click
/// (<see cref="TargetContext"/> / <see cref="GroundContext"/>): land units on an own transport = Load (board),
/// a transport on an own land unit = Load (pick up), aircraft on an own air carrier = Dock, a transport with cargo on
/// the ground = Unload there. Order modes / hotkeys: U unload (air carriers: launch), E transport-to, F ferry
/// (transports: pickup beacon then drop point; land units: a beacon), D dock at the nearest air carrier
/// (E and D are shared with capture / dive, see <see cref="SharedHotkeys"/>).
/// Only sends <see cref="IssueOrderCommand"/>s; the sim gives each unit the part of the order that applies to it.
/// </summary>
public static class TransportInput
{
    public static readonly Color Cyan = new(100, 220, 240, 255);

    /// <summary>Ferry mode: the pickup beacon clicked first (transports selected), null before the first click.</summary>
    public static Vector2? PendingBeacon { get; private set; }
    /// <summary>Order mode of the last frame (for the 3D preview of TransportOverlay).</summary>
    public static Mode CurrentMode { get; private set; }

    public static bool IsAirCargo(UnitBlueprint bp) => bp.CargoKind == CargoLayer.Air && bp.CargoSize > 0;
    public static bool IsTransportMode(Mode m) => m is Mode.Unload or Mode.TransportTo or Mode.Ferry;

    // ------------------------------------------------------------------ pure decisions (unit-tested)

    /// <summary>Buttons shown in the orders panel for this selection.</summary>
    public static TransportButtons ButtonsFor(in CargoSelection s)
    {
        var b = TransportButtons.None;
        if (s.LoadedTransports > 0) b |= TransportButtons.Unload;
        else if (s.LoadedAirCarriers > 0) b |= TransportButtons.Launch;
        if (s.Transports > 0) b |= TransportButtons.TransportTo;
        if (s.Transports > 0 || s.LandCargo > 0) b |= TransportButtons.Ferry;
        if (s.AirCargo > 0) b |= TransportButtons.Dock;
        return b;
    }

    /// <summary>
    /// Right-click on an own, finished unit: Load (land units board a transport with room for the smallest of them),
    /// Dock (aircraft on an air carrier), PickUp (a transport with room picks up a land unit) or None (default handling).
    /// </summary>
    public static ContextKind TargetContext(in CargoSelection s, UnitBlueprint target, int targetUsed, bool targetSelected)
    {
        if (target.Cargo is { } c)
        {
            if (c.Layer == CargoLayer.Land && s.LandCargo > 0 && c.Capacity - targetUsed >= s.SmallestLandCargo) return ContextKind.Load;
            if (c.Layer == CargoLayer.Air && s.AirCargo > 0) return ContextKind.Dock;
        }
        if (!targetSelected && TransportOrders.IsLandCargo(target) && s.Transports > 0 && s.MaxTransportFree >= target.CargoSize)
            return ContextKind.PickUp;
        return ContextKind.None;
    }

    /// <summary>Right-click on the ground: transports with cargo unload there (SupCom); otherwise a plain move.</summary>
    public static ContextKind GroundContext(in CargoSelection s) => s.LoadedTransports > 0 ? ContextKind.Unload : ContextKind.Move;

    /// <summary>Does the transport context order apply to this selected unit (others get the plain guard / move)?</summary>
    public static bool Applies(ContextKind k, UnitBlueprint bp, int used) => k switch
    {
        ContextKind.Load => TransportOrders.IsLandCargo(bp),
        ContextKind.PickUp => bp.IsVtol,
        ContextKind.Dock => IsAirCargo(bp),
        ContextKind.Unload => bp.Cargo is { Layer: CargoLayer.Land } && used > 0,
        _ => false,
    };

    /// <summary>Index of the ferry beacon (pickup, or drop when <paramref name="drops"/>) of <paramref name="army"/>
    /// closest to p within <paramref name="radius"/>, or -1.</summary>
    public static int NearestFerryPoint(ReadOnlySpan<FerryView> ferries, int army, Vector2 p, float radius, bool drops)
    {
        int best = -1;
        float bestD = radius * radius;
        for (int k = 0; k < ferries.Length; k++)
        {
            if (ferries[k].Army != army) continue;
            var q = drops ? ferries[k].Drop : ferries[k].Pickup;
            float d = Vector2.DistanceSquared(new Vector2(q.X, q.Z), p);
            if (d <= bestD) { bestD = d; best = k; }
        }
        return best;
    }

    // ------------------------------------------------------------------ snapshot helpers

    public static bool TryOwnUnit(GameContext ctx, SimSnapshot snap, UnitHandle h, out UnitView v) =>
        snap.TryGetUnit(h.Index, h.Generation, out v) && v.Army == ctx.LocalArmy && (v.Flags & UnitFlags.UnderConstruction) == 0;

    public static bool TryGetCargo(SimSnapshot snap, int unit, int gen, out CargoView cv)
    {
        for (int k = 0; k < snap.CargoViewCount; k++)
        {
            if (snap.CargoViews[k].Unit != unit || snap.CargoViews[k].Gen != gen) continue;
            cv = snap.CargoViews[k];
            return true;
        }
        cv = default;
        return false;
    }

    public static int UsedSlots(SimSnapshot snap, int unit, int gen) => TryGetCargo(snap, unit, gen, out var cv) ? cv.Used : 0;

    // ------------------------------------------------------------------ input

    /// <summary>Per frame (from SelectionController's hotkey handling): resets the ferry state, handles U / E / F / D.</summary>
    public static void Update(GameContext ctx, SelectionController sel, bool ctrl)
    {
        if (sel.Mode != Mode.Ferry) PendingBeacon = null;
        CurrentMode = sel.Mode;
        if (sel.Selected.Count == 0 || ctrl) return;
        bool u = Hotkeys.Unload.Pressed(), e = Hotkeys.TransportTo.Pressed();
        bool f = Hotkeys.Ferry.Pressed(), d = Hotkeys.Dock.Pressed();
        if (!(u || e || f || d)) return;
        var snap = ctx.Snapshots.Current;
        var b = ButtonsFor(CargoSelection.Of(ctx, sel.Selected, snap));
        bool queue = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        // U also upgrades selected structures (UpgradeUi): leave it to the upgrade when one can start.
        if (u && (b & (TransportButtons.Unload | TransportButtons.Launch)) != 0 && !(Hotkeys.SameBinding(Hotkeys.Unload, Hotkeys.Upgrade) && UpgradeWantsU(ctx, sel, snap))) StartUnload(ctx, sel, queue);
        // E / D are shared with capture / dive (SharedHotkeys): transports win E; D docks the aircraft while
        // UnitToggles dives the subs of the same selection.
        if (e && (b & TransportButtons.TransportTo) != 0) StartMode(sel, Mode.TransportTo);
        if (f && (b & TransportButtons.Ferry) != 0) StartMode(sel, Mode.Ferry);
        if (d && (b & TransportButtons.Dock) != 0) DockNow(ctx, sel, queue);
        CurrentMode = sel.Mode;
    }

    private static bool UpgradeWantsU(GameContext ctx, SelectionController sel, SimSnapshot snap)
    {
        List<UnitHandle> ready = new(), upgrading = new();
        UpgradeUi.Collect(ctx, sel.Selected, snap, ready, upgrading);
        return ready.Count > 0;
    }

    public static void StartMode(SelectionController sel, Mode mode)
    {
        PendingBeacon = null;
        sel.SetMode(mode);
    }

    /// <summary>Unload / Launch button: air carriers that cannot move (air staging) launch at once, otherwise the
    /// player clicks the drop point (Unload mode).</summary>
    public static void StartUnload(GameContext ctx, SelectionController sel, bool queue)
    {
        var snap = ctx.Snapshots.Current;
        var s = CargoSelection.Of(ctx, sel.Selected, snap);
        if (s.LoadedTransports > 0 || s.MobileLoadedAirCarriers > 0) { StartMode(sel, Mode.Unload); return; }
        var carriers = new List<UnitHandle>();
        Vector2 at = default;
        foreach (var h in sel.Selected)
        {
            if (!TryOwnUnit(ctx, snap, h, out var v) || ctx.Db[v.Bp].Cargo is not { Layer: CargoLayer.Air } || UsedSlots(snap, v.Index, v.Gen) == 0) continue;
            if (carriers.Count == 0) at = new Vector2(v.Pos.X, v.Pos.Z);
            carriers.Add(h);
        }
        if (carriers.Count == 0) { UiSounds.Error(); return; }
        Send(ctx, carriers, TransportOrders.Unload(at), queue); // structures launch where they are
        UiSounds.Confirm();
    }

    /// <summary>Dock button / D: the selected aircraft go to the nearest air carrier (repair, refuel).</summary>
    public static void DockNow(GameContext ctx, SelectionController sel, bool queue)
    {
        var snap = ctx.Snapshots.Current;
        var units = new List<UnitHandle>();
        foreach (var h in sel.Selected)
            if (TryOwnUnit(ctx, snap, h, out var v) && IsAirCargo(ctx.Db[v.Bp])) units.Add(h);
        if (units.Count == 0) { UiSounds.Error(); return; }
        Send(ctx, units, TransportOrders.Dock(UnitHandle.None), queue);
        UiSounds.Confirm();
    }

    /// <summary>Click in an Unload / TransportTo / Ferry mode. Returns true when an order was issued (the mode ends
    /// unless Shift); the first ferry click (pickup beacon) returns false and keeps the mode.</summary>
    public static bool TryIssueMode(GameContext ctx, SelectionController sel, Vector2 mouse, bool queue, Mode mode) =>
        ctx.PickGround(mouse) is Vector3 g && TryIssueModeAt(ctx, sel, new Vector2(g.X, g.Z), queue, mode);

    /// <summary><see cref="TryIssueMode"/> at a world point (also minimap clicks).</summary>
    public static bool TryIssueModeAt(GameContext ctx, SelectionController sel, Vector2 p, bool queue, Mode mode)
    {
        var snap = ctx.Snapshots.Current;
        switch (mode)
        {
            case Mode.Unload:
            {
                var carriers = new List<UnitHandle>();
                foreach (var h in sel.Selected)
                    if (TryOwnUnit(ctx, snap, h, out var v) && ctx.Db[v.Bp].Cargo != null) carriers.Add(h);
                if (carriers.Count == 0) { UiSounds.Error(); return false; }
                Send(ctx, carriers, TransportOrders.Unload(p), queue);
                break;
            }
            case Mode.TransportTo:
                Send(ctx, sel.Selected, TransportOrders.TransportTo(p), queue);
                break;
            case Mode.Ferry:
            {
                var s = CargoSelection.Of(ctx, sel.Selected, snap);
                float snapR = 4f + ctx.Camera.Distance * 0.02f;
                var ferries = snap.Ferries.AsSpan(0, snap.FerryCount);
                if (s.Transports == 0 || PendingBeacon == null)
                {
                    int k = NearestFerryPoint(ferries, ctx.LocalArmy, p, snapR, drops: false);
                    if (k >= 0) p = new Vector2(ferries[k].Pickup.X, ferries[k].Pickup.Z);
                }
                if (s.Transports == 0)
                {
                    // Land units only: walk to the beacon and wait for a ferry.
                    Send(ctx, sel.Selected, TransportOrders.Ferry(p, p), queue);
                    break;
                }
                if (PendingBeacon is not Vector2 beacon)
                {
                    PendingBeacon = p;
                    ctx.Feedback.AddClickMarker(new Vector3(p.X, ctx.Terrain.GetSurfaceHeight(p), p.Y), OrderType.Ferry);
                    UiSounds.Click();
                    return false;
                }
                int j = NearestFerryPoint(ferries, ctx.LocalArmy, p, snapR, drops: true);
                if (j >= 0) p = new Vector2(ferries[j].Drop.X, ferries[j].Drop.Z);
                Send(ctx, sel.Selected, TransportOrders.Ferry(beacon, p), queue);
                PendingBeacon = null;
                break;
            }
            default:
                return false;
        }
        ctx.Feedback.AddClickMarker(new Vector3(p.X, ctx.Terrain.GetSurfaceHeight(p), p.Y), OrderType.Move);
        UiSounds.Confirm();
        return true;
    }

    /// <summary>Issues a context transport order (Load / PickUp / Dock / Unload, see ContextOrders): the units it
    /// applies to get it, the rest of the selection guards the target / moves to the point (the plain right-click).</summary>
    public static bool IssueContext(GameContext ctx, SelectionController sel, in ContextAction a, bool queue)
    {
        var snap = ctx.Snapshots.Current;
        var doers = new List<UnitHandle>();
        var others = new List<UnitHandle>();
        foreach (var h in sel.Selected)
        {
            if (!TryOwnUnit(ctx, snap, h, out var v)) continue;
            if (h == a.Target) continue;
            (Applies(a.Kind, ctx.Db[v.Bp], UsedSlots(snap, v.Index, v.Gen)) ? doers : others).Add(h);
        }
        if (doers.Count == 0) return false;
        var order = a.Kind switch
        {
            ContextKind.Dock => TransportOrders.Dock(a.Target),
            ContextKind.Unload => TransportOrders.Unload(a.Pos),
            _ => TransportOrders.Load(a.Target),
        };
        Send(ctx, doers, order, queue);
        if (others.Count > 0) Send(ctx, others, a.Kind == ContextKind.Unload ? Order.MoveTo(a.Pos) : Order.GuardUnit(a.Target), queue);
        ctx.Feedback.AddClickMarker(new Vector3(a.Pos.X, ctx.Terrain.GetSurfaceHeight(a.Pos), a.Pos.Y), OrderType.Move);
        UiSounds.Confirm();
        return true;
    }

    private static void Send(GameContext ctx, IEnumerable<UnitHandle> units, in Order order, bool queue)
    {
        var arr = units.ToArray();
        if (arr.Length > 0) ctx.Runner.Enqueue(new IssueOrderCommand(ctx.LocalArmy, arr, order, queue));
    }

    // ------------------------------------------------------------------ HUD

    /// <summary>Banner / cursor text / color for the transport order modes (null for other modes).</summary>
    public static (string? banner, string? cursor, Color color)? ModeHint(GameContext ctx, SelectionController sel) => sel.Mode switch
    {
        Mode.Unload => ("UNLOAD: click the drop point (air carriers: launch point)  ·  Shift: queue  ·  RMB cancel", "Unload here", Cyan),
        Mode.TransportTo => ("TRANSPORT: click the destination, the selected transports carry the selected units there  ·  RMB cancel", "Transport to", Cyan),
        Mode.Ferry when CargoSelection.Of(ctx, sel.Selected, ctx.Snapshots.Current).Transports == 0 =>
            ("FERRY: click a ferry beacon, the units walk there and wait for the transports  ·  RMB cancel", "Wait at beacon", Cyan),
        Mode.Ferry when PendingBeacon == null => ("FERRY 1/2: click the pickup beacon  ·  RMB cancel", "Pickup beacon", Cyan),
        Mode.Ferry => ("FERRY 2/2: click the drop point (Shift: keep adding routes)  ·  RMB cancel", "Drop point", Cyan),
        _ => null,
    };
}
