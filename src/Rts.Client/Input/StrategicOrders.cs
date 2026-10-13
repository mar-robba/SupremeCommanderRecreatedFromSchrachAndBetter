using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Strategic;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>
/// Launch orders for tactical/strategic missile launchers (CommandMode.Launch, hotkey L): a click on the ground sends a
/// Launch order to ONE selected launcher that has the target in range, preferring the one with the most missiles not
/// already assigned to queued launches (so repeated clicks spread over the launchers). Launch orders are always
/// queued: a launcher without stock keeps the order until its next missile is ready.
/// </summary>
public static class StrategicOrders
{
    public static bool IsLauncher(GameContext ctx, in UnitView v) =>
        ctx.Controls(v.Army) && (v.Flags & UnitFlags.UnderConstruction) == 0 && SiloSystem.IsLauncher(ctx.Db[v.Bp]);

    public static bool AnyLauncher(GameContext ctx, SelectionController sel)
    {
        var snap = ctx.Snapshots.Current;
        foreach (var h in sel.Selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && IsLauncher(ctx, v)) return true;
        return false;
    }

    /// <summary>Missiles in stock for a silo of the local team (0 if unknown).</summary>
    public static int StockOf(SimSnapshot snap, int unit, int gen)
    {
        for (int k = 0; k < snap.SiloCount; k++)
            if (snap.Silos[k].Unit == unit && snap.Silos[k].Gen == gen) return snap.Silos[k].Stock;
        return 0;
    }

    private static int PendingLaunches(SimSnapshot snap, in UnitView v)
    {
        int n = 0;
        for (int k = 0; k < v.OrderCount; k++)
            if (snap.Orders[v.OrderStart + k].Type == OrderType.Launch) n++;
        return n;
    }

    /// <summary>Best launcher for a target point, or false if no selected launcher can reach it.</summary>
    public static bool PickLauncher(GameContext ctx, SelectionController sel, Vector2 target, out UnitView best)
    {
        var snap = ctx.Snapshots.Current;
        best = default;
        float bestScore = float.MinValue;
        bool found = false;
        foreach (var h in sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !IsLauncher(ctx, v)) continue;
            var bp = ctx.Db[v.Bp];
            var from = new Vector2(v.Pos.X, v.Pos.Z);
            if (!SiloSystem.InRange(bp, from, target)) continue;
            int free = StockOf(snap, v.Index, v.Gen) - PendingLaunches(snap, v);
            float score = free * 100000f - Vector2.Distance(from, target);
            if (score > bestScore) { bestScore = score; best = v; found = true; }
        }
        return found;
    }

    /// <summary>Issues a launch at the ground point under the mouse. Returns false (error sound) if nothing can fire there.</summary>
    public static bool TryLaunch(GameContext ctx, SelectionController sel, Vector2 mouse)
    {
        if (ctx.PickGround(mouse) is not Vector3 g) return false;
        var p = new Vector2(g.X, g.Z);
        if (!PickLauncher(ctx, sel, p, out var v))
        {
            UiSounds.Error();
            return false;
        }
        ctx.Feedback.AddClickMarker(g, OrderType.Launch);
        ctx.Runner.Enqueue(new IssueOrderCommand(v.Army, [new UnitHandle(v.Index, v.Gen)], Order.LaunchAt(p), true));
        UiSounds.Confirm();
        return true;
    }

    /// <summary>L hotkey: enters launch mode if a launcher is selected.</summary>
    public static void HandleHotkey(GameContext ctx, SelectionController sel, bool ctrl)
    {
        if (!ctrl && Raylib.IsKeyPressed(KeyboardKey.L) && AnyLauncher(ctx, sel)) sel.SetMode(SelectionController.CommandMode.Launch);
    }
}
