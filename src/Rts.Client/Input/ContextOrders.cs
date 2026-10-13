using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Input;

public enum ContextKind : byte { None, Move, Attack, Guard, Assist, Repair, Finish, Reclaim, Capture }

/// <summary>What a right-click at the cursor would do (shown as a cursor hint, see UI/CursorHint).</summary>
public readonly record struct ContextAction(ContextKind Kind, UnitHandle Target, Vector2 Pos, int PropId = -1, float Mass = 0, float Radius = 0)
{
    public static readonly ContextAction None = new(ContextKind.None, UnitHandle.None, default);

    public string Label => Kind switch
    {
        ContextKind.Attack => "Attack",
        ContextKind.Guard => "Guard",
        ContextKind.Assist => "Assist",
        ContextKind.Repair => "Repair",
        ContextKind.Finish => "Assist construction",
        ContextKind.Reclaim => Mass >= 1f ? $"Reclaim  +{Mass:0} mass" : "Reclaim",
        ContextKind.Capture => "Capture",
        _ => "",
    };
}

/// <summary>
/// SupCom-like context-sensitive right-click for selections containing engineers / the commander:
/// wreck → Reclaim, own/allied unfinished unit → Repair (finishes it), own factory → Guard (assist production),
/// damaged own/allied unit → Repair, other own engineer → Guard (assist). Non-builders in the same selection get
/// the plain order (guard the target / move to the wreck). Without builders the default right-click applies.
/// A selection of ONLY engineers / commanders captures a capturable enemy (complete, not a commander) instead of
/// attacking it. Relations are evaluated from the point of view of one army (the sandbox splits mixed selections).
/// </summary>
public static class ContextOrders
{
    /// <summary>Evaluates the action under the mouse for the current selection (Normal mode).</summary>
    public static ContextAction Evaluate(GameContext ctx, SelectionController sel, Vector2 mouse)
    {
        int army = ArmyCommands.PrimaryArmy(ctx, sel.Selected);
        return Evaluate(ctx, sel, army, ArmyCommands.OfArmy(ctx, sel.Selected, army), mouse);
    }

    /// <summary>The action under the mouse for <paramref name="units"/> (all of <paramref name="army"/>).</summary>
    public static ContextAction Evaluate(GameContext ctx, SelectionController sel, int army, IReadOnlyList<UnitHandle> units, Vector2 mouse)
    {
        var snap = ctx.Snapshots.Current;
        bool builders = HasBuilders(snap, ctx, army, units, out bool onlyBuilders);
        var hover = sel.PickUnit(mouse);
        if (hover is UnitView t)
        {
            var h = new UnitHandle(t.Index, t.Gen);
            var tbp = ctx.Db[t.Bp];
            if (ctx.AreEnemies(army, t.Army))
                return new ContextAction(onlyBuilders && CanCapture(tbp, t) ? ContextKind.Capture : ContextKind.Attack, h, new Vector2(t.Pos.X, t.Pos.Z));
            if (sel.Selected.Contains(h)) return new ContextAction(ContextKind.Move, UnitHandle.None, default);
            bool own = t.Army == army;
            var pos = new Vector2(t.Pos.X, t.Pos.Z);
            if (builders)
            {
                if ((t.Flags & UnitFlags.UnderConstruction) != 0)
                {
                    // A mobile unit being built sits inside its factory: assist the factory instead.
                    if (tbp.IsMobile && FactoryAt(ctx, snap, army, t) is UnitView f)
                        return new ContextAction(ContextKind.Assist, new UnitHandle(f.Index, f.Gen), new Vector2(f.Pos.X, f.Pos.Z));
                    return new ContextAction(ContextKind.Finish, h, pos);
                }
                if (own && tbp.Has(UnitCategory.Factory)) return new ContextAction(ContextKind.Assist, h, pos);
                if (own && t.UpgradeBp >= 0) return new ContextAction(ContextKind.Assist, h, pos); // assist the upgrade
                if (t.HealthFrac < 0.995f) return new ContextAction(ContextKind.Repair, h, pos);
                if (own && tbp.Economy.BuildRate > 0) return new ContextAction(ContextKind.Assist, h, pos);
            }
            return own ? new ContextAction(ContextKind.Guard, h, pos) : new ContextAction(ContextKind.Move, UnitHandle.None, default);
        }
        if (builders && PickProp(ctx, snap, mouse) is int k)
        {
            ref var p = ref snap.Props[k];
            return new ContextAction(ContextKind.Reclaim, UnitHandle.None, new Vector2(p.Pos.X, p.Pos.Z), p.Id, p.Mass, p.Radius);
        }
        return new ContextAction(ContextKind.Move, UnitHandle.None, default);
    }

    /// <summary>
    /// Issues the builder-specific context order at the mouse (Reclaim / Repair / Finish / Assist). Returns false
    /// when the default right-click handling (attack / guard / move) should run instead.
    /// </summary>
    public static bool TryIssue(GameContext ctx, SelectionController sel, int army, IReadOnlyList<UnitHandle> units, Vector2 mouse, bool queue)
    {
        var a = Evaluate(ctx, sel, army, units, mouse);
        if (a.Kind is not (ContextKind.Reclaim or ContextKind.Repair or ContextKind.Finish or ContextKind.Assist or ContextKind.Capture)) return false;
        var snap = ctx.Snapshots.Current;
        var builders = new List<UnitHandle>();
        var others = new List<UnitHandle>();
        foreach (var h in units)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || v.Army != army) continue;
            (IsBuilder(ctx.Db[v.Bp]) ? builders : others).Add(h);
        }
        if (builders.Count == 0) return false;
        Order forBuilders, forOthers;
        switch (a.Kind)
        {
            case ContextKind.Reclaim:
                forBuilders = Order.ReclaimAt(UnitHandle.None, a.Pos);
                forOthers = Order.MoveTo(a.Pos);
                break;
            case ContextKind.Assist:
                forBuilders = forOthers = Order.GuardUnit(a.Target);
                break;
            case ContextKind.Capture:
                forBuilders = Order.CaptureUnit(a.Target);
                forOthers = Order.AttackUnit(a.Target);
                break;
            default: // Repair / Finish
                forBuilders = Order.RepairUnit(a.Target);
                forOthers = Order.GuardUnit(a.Target);
                break;
        }
        ctx.Runner.Enqueue(new IssueOrderCommand(army, builders.ToArray(), forBuilders, queue));
        if (others.Count > 0) ctx.Runner.Enqueue(new IssueOrderCommand(army, others.ToArray(), forOthers, queue));
        float y = ctx.Terrain.GetSurfaceHeight(a.Pos);
        var marker = a.Kind switch { ContextKind.Reclaim => OrderType.Reclaim, ContextKind.Capture => OrderType.Capture, _ => OrderType.Repair };
        ctx.Feedback.AddClickMarker(new Vector3(a.Pos.X, y, a.Pos.Y), marker);
        UiSounds.Confirm();
        return true;
    }

    /// <summary>Mobile units with build power (engineers, commander).</summary>
    public static bool IsBuilder(UnitBlueprint bp) => bp.IsMobile && bp.Economy.BuildRate > 0;

    /// <summary>Can an engineer / commander capture this unit? (complete, not a commander, actually seen.)</summary>
    public static bool CanCapture(UnitBlueprint bp, in UnitView t) =>
        (t.Flags & (UnitFlags.UnderConstruction | UnitFlags.RadarOnly | UnitFlags.Dead)) == 0 && !bp.Has(UnitCategory.Commander);

    /// <summary>Any complete builder of the army among the units; onlyBuilders = every unit of the army is one.</summary>
    private static bool HasBuilders(SimSnapshot snap, GameContext ctx, int army, IReadOnlyList<UnitHandle> units, out bool onlyBuilders)
    {
        bool any = false, other = false;
        foreach (var h in units)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || v.Army != army) continue;
            if (IsBuilder(ctx.Db[v.Bp]) && (v.Flags & UnitFlags.UnderConstruction) == 0) any = true;
            else other = true;
        }
        onlyBuilders = any && !other;
        return any;
    }

    /// <summary>The allied factory whose footprint contains unit u (a unit being produced), if any.</summary>
    private static UnitView? FactoryAt(GameContext ctx, SimSnapshot snap, int army, in UnitView u)
    {
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var f = ref snap.Units[k];
            var fbp = ctx.Db[f.Bp];
            if (!fbp.Has(UnitCategory.Factory) || ctx.AreEnemies(army, f.Army)) continue;
            float half = MathF.Max(fbp.FootprintX, fbp.FootprintZ) * 0.5f;
            if (MathF.Abs(f.Pos.X - u.Pos.X) <= half && MathF.Abs(f.Pos.Z - u.Pos.Z) <= half) return f;
        }
        return null;
    }

    /// <summary>Index in snap.Props of the wreck under the mouse (closest in screen space), or null.</summary>
    public static int? PickProp(GameContext ctx, SimSnapshot snap, Vector2 mouse)
    {
        float best = float.MaxValue;
        int? result = null;
        var cam = ctx.Camera.Camera;
        for (int k = 0; k < snap.PropCount; k++)
        {
            ref var p = ref snap.Props[k];
            var c = p.Pos + new Vector3(0, 0.3f, 0);
            var sp = Raylib.GetWorldToScreen(c, cam);
            var edge = Raylib.GetWorldToScreen(c + new Vector3(MathF.Max(0.8f, p.Radius), 0, 0), cam);
            float rPx = MathF.Max(9f, Vector2.Distance(sp, edge));
            float d = Vector2.Distance(sp, mouse);
            if (d < rPx && d < best) { best = d; result = k; }
        }
        return result;
    }
}
