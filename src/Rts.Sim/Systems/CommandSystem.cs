using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>Applies validated player commands at the start of a tick. Sequential.</summary>
public static class CommandSystem
{
    public static void Run(SimWorld w, IReadOnlyList<Command> commands)
    {
        foreach (var c in commands)
        {
            if (c is SandboxCommand sb) { SandboxSystem.Apply(w, sb); continue; } // ignored outside sandbox
            if ((uint)c.Army >= (uint)w.Armies.Length || w.Armies[c.Army].Defeated) continue;
            switch (c)
            {
                case IssueOrderCommand io: ApplyIssueOrder(w, io); break;
                case StopCommand sc:
                    foreach (var h in sc.Units)
                    {
                        if (!Owns(w, c.Army, h)) continue;
                        StopUnit(w, h.Index);
                        // Stop also cancels a commander/SCU enhancement in progress (refund, like any new order).
                        if (w.Units.Upgrade[h.Index].Active && w.BpOf(h.Index).IsEnhanceable) UpgradeSystem.Cancel(w, h.Index);
                    }
                    break;
                case FactoryEnqueueCommand fe: FactorySystem.Enqueue(w, fe); break;
                case FactoryDequeueCommand fd: FactorySystem.Dequeue(w, fd); break;
                case FactoryRepeatCommand fr:
                    foreach (var h in fr.Factories)
                        if (Owns(w, c.Army, h)) w.Units.Factory[h.Index].Repeat = fr.Repeat;
                    break;
                case UpgradeCommand uc: UpgradeSystem.Apply(w, uc); break;
                case CancelUpgradeCommand cu: UpgradeSystem.Apply(w, cu); break;
                case SetPausedCommand sp: PauseSystem.Apply(w, sp); break;
                case SetSubmergedCommand ss: Naval.Submarines.Apply(w, ss); break;
                case SetFireStateCommand fs: Combat.FireStates.Apply(w, fs); break;
                case EnhanceCommand en: Commander.EnhancementSystem.Apply(w, en); break;
                case RemoveEnhancementCommand re: Commander.EnhancementSystem.Apply(w, re); break;
                case PlayerLeftCommand pl: MultiplayerSystem.Apply(w, pl); break;
                case SelfDestructCommand sd:
                    foreach (var h in sd.Units)
                        if (Owns(w, c.Army, h)) w.KillUnit(h.Index, UnitHandle.None);
                    break;
                case DebugSpawnCommand ds:
                    if (!w.CheatsEnabled || (uint)ds.BlueprintIndex >= (uint)w.Blueprints.Count) break;
                    for (int k = 0; k < Math.Min(ds.Count, 500); k++)
                    {
                        var off = new Vector2(k % 10, k / 10) * 2f;
                        w.SpawnUnit(ds.BlueprintIndex, ds.Army, ds.Pos + off, 0, complete: true);
                    }
                    break;
            }
        }
    }

    public static bool Owns(SimWorld w, int army, UnitHandle h) => w.Units.IsActive(h) && w.Units.Army[h.Index] == army;

    public static void StopUnit(SimWorld w, int i)
    {
        w.Units.Orders[i].Clear();
        w.Units.Move[i].HasGoal = false;
        foreach (ref var ws in w.Units.WeaponsOf(i)) { ws.HasGroundTarget = false; }
        ConstructionSystem.StopBuilder(w, i);
    }

    private static void ApplyIssueOrder(SimWorld w, IssueOrderCommand io)
    {
        // Collect valid units (deduplicated, deterministic order by index).
        var units = new List<int>(io.Units.Length);
        foreach (var h in io.Units)
            if (Owns(w, io.Army, h) && !units.Contains(h.Index)) units.Add(h.Index);
        if (units.Count == 0) return;
        units.Sort();

        var order = io.Order;
        if (Transport.TransportOrders.Handles(order.Type)) { Transport.TransportOrders.Issue(w, io, units); return; }
        if (order.Type == OrderType.Build && !CanBuild(w, units, order.BlueprintIndex)) return;

        bool formation = order.Type is OrderType.Move or OrderType.AttackMove or OrderType.Patrol;
        Vector2[]? offsets = formation && units.Count > 1 ? FormationOffsets(w, units, order.Pos) : null;
        if (!io.Queue) MovementSystem.AssignGroup(w, units, formation); // formation pace (pathfinding)

        for (int k = 0; k < units.Count; k++)
        {
            int i = units[k];
            var bp = w.BpOf(i);
            var o = order;
            if (offsets != null) o.FormationOffset = offsets[k];
            var q = w.Units.Orders[i];

            // Structures: only factories accept orders (they become rally orders for produced units); silos accept
            // Launch orders (Strategic module), and only silo launchers do.
            bool launch = o.Type == OrderType.Launch;
            if (launch && !Strategic.SiloSystem.IsLauncher(bp)) continue;
            if (!launch && bp.IsStructure && !bp.Has(UnitCategory.Factory)) continue;
            if (o.Type == OrderType.Build && !Array.Exists(bp.BuildIndices, b => b == o.BlueprintIndex)) continue;

            if (!io.Queue)
            {
                StopUnit(w, i);
            }

            if (o.Type == OrderType.Patrol)
            {
                // Patrol cycles between the previous waypoint (or current position) and the new point.
                o.Aux = q.IsEmpty ? w.Units.Pos[i] : q.At(q.Count - 1).Destination;
                q.Enqueue(o);
                if (!io.Queue)
                {
                    var back = o;
                    back.Pos = w.Units.Pos[i];
                    back.FormationOffset = Vector2.Zero;
                    back.Aux = o.Destination;
                    q.Enqueue(back);
                }
            }
            else
            {
                q.Enqueue(o);
            }
        }
    }

    private static bool CanBuild(SimWorld w, List<int> units, int bpIndex)
    {
        if ((uint)bpIndex >= (uint)w.Blueprints.Count) return false;
        foreach (int i in units)
            if (Array.IndexOf(w.BpOf(i).BuildIndices, bpIndex) >= 0) return true;
        return false;
    }

    /// <summary>
    /// Box formation centered on the target, facing from the group centroid to the target.
    /// Units are assigned to slots by their position so they don't cross each other.
    /// </summary>
    public static Vector2[] FormationOffsets(SimWorld w, List<int> units, Vector2 target)
    {
        int n = units.Count;
        var centroid = Vector2.Zero;
        float maxR = 0.5f;
        foreach (int i in units)
        {
            centroid += w.Units.Pos[i];
            maxR = MathF.Max(maxR, w.BpOf(i).Radius);
        }
        centroid /= n;
        var fwd = target - centroid;
        fwd = fwd.LengthSquared() > 1e-4f ? Vector2.Normalize(fwd) : new Vector2(1, 0);
        var right = new Vector2(-fwd.Y, fwd.X);
        float spacing = maxR * 2f + 1.2f;
        int cols = (int)MathF.Ceiling(MathF.Sqrt(n * 2f));
        cols = Math.Clamp(cols, 1, n);
        int rows = (n + cols - 1) / cols;

        // Slots: row 0 is the front.
        var slots = new Vector2[n];
        for (int s = 0; s < n; s++)
        {
            int r = s / cols, c = s % cols;
            int inRow = r == rows - 1 ? n - r * cols : cols;
            float x = (c - (inRow - 1) * 0.5f) * spacing;
            float z = -(r - (rows - 1) * 0.5f) * spacing;
            slots[s] = right * x + fwd * z;
        }

        // Sort units: front-most first, then left to right (stable by index for ties).
        var order = units.Select((u, k) => (k, f: Vector2.Dot(w.Units.Pos[u], fwd), r: Vector2.Dot(w.Units.Pos[u], right), u))
            .OrderByDescending(t => MathF.Round(t.f / spacing)).ThenBy(t => t.r).ThenBy(t => t.u).ToList();
        var result = new Vector2[n];
        for (int s = 0; s < n; s++) result[order[s].k] = slots[s];
        return result;
    }
}
