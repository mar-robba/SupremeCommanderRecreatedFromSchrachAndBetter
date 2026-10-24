using System.Numerics;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Client.Sandbox;

/// <summary>Sandbox console actions for commander tests (SandboxConsoleParser.Commander.cs): Overcharge on/off for
/// every commander of an army, Teleport orders for its commanders with a teleporter, and kill every unit in an area
/// (from the current snapshot).</summary>
public sealed partial class SandboxController
{
    private void ExecuteCommander(ConsoleAction action)
    {
        var snap = Ctx.Snapshots.Current;
        switch (action)
        {
            case OverchargeAction oc:
            {
                int total = 0;
                for (int army = 0; army < snap.Armies.Length; army++)
                {
                    if (oc.Army >= 0 && army != oc.Army) continue;
                    var units = new List<UnitHandle>();
                    for (int k = 0; k < snap.UnitCount; k++)
                    {
                        ref var v = ref snap.Units[k];
                        if (v.Army != army || (v.Flags & UnitFlags.UnderConstruction) != 0) continue;
                        if (UnitToggles.OverchargeOf(Ctx.Db[v.Bp]) != null) units.Add(new UnitHandle(v.Index, v.Gen));
                    }
                    if (units.Count == 0) continue;
                    Ctx.Runner.Enqueue(new SetOverchargeCommand(army, units.ToArray(), oc.On));
                    total += units.Count;
                }
                if (total == 0) Error($"No commander with Overcharge for {ArmyLabel(oc.Army)}");
                else Ok($"Overcharge {(oc.On ? "ON" : "OFF")}: {total} commander(s) of {ArmyLabel(oc.Army)}");
                break;
            }
            case TeleportAction ta:
            {
                int total = 0;
                var dest = Ctx.Terrain.Clamp(ta.At, 1f);
                for (int army = 0; army < snap.Armies.Length; army++)
                {
                    if (ta.Army >= 0 && army != ta.Army) continue;
                    var units = new List<UnitHandle>();
                    for (int k = 0; k < snap.UnitCount; k++)
                    {
                        ref var v = ref snap.Units[k];
                        if (v.Army != army || (v.Flags & UnitFlags.UnderConstruction) != 0) continue;
                        if (Ctx.Db[v.Bp].Teleport != null) units.Add(new UnitHandle(v.Index, v.Gen));
                    }
                    if (units.Count == 0) continue;
                    Ctx.Runner.Enqueue(new IssueOrderCommand(army, units.ToArray(), Order.TeleportTo(dest), false));
                    total += units.Count;
                }
                if (total == 0) Error($"No commander with a Personal Teleporter for {ArmyLabel(ta.Army)}");
                else Ok($"Teleport: {total} commander(s) of {ArmyLabel(ta.Army)} to {dest.X:0},{dest.Y:0}");
                break;
            }
            case KillAreaAction ka:
            {
                var units = new List<UnitHandle>();
                float r2 = ka.Radius * ka.Radius;
                for (int k = 0; k < snap.UnitCount; k++)
                {
                    ref var v = ref snap.Units[k];
                    if (Vector2.DistanceSquared(new Vector2(v.Pos.X, v.Pos.Z), ka.At) <= r2) units.Add(new UnitHandle(v.Index, v.Gen));
                }
                if (units.Count == 0) { Error($"No units within {ka.Radius:0} of {ka.At.X:0},{ka.At.Y:0}"); break; }
                Ctx.Runner.Enqueue(new SandboxUnitActionCommand(Ctx.LocalArmy, units.ToArray(), SandboxUnitAction.Kill, 0, -1));
                Ok($"Kill: {units.Count} unit(s) within {ka.Radius:0} of {ka.At.X:0},{ka.At.Y:0}");
                break;
            }
        }
    }
}
