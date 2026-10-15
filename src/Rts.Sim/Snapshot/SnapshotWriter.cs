using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Snapshot;

/// <summary>Copies the world into a snapshot (on the sim thread, between ticks).</summary>
public static class SnapshotWriter
{
    public static void Write(SimWorld w, SimSnapshot s) => Write(w, s, -1);

    /// <summary>viewArmy &gt;= 0: fog of war, the snapshot only contains what that army knows (IntelSnapshotWriter).</summary>
    public static void Write(SimWorld w, SimSnapshot s, int viewArmy)
    {
        var u = w.Units;
        var view = IntelSnapshotWriter.Begin(w, s, viewArmy);
        s.Tick = w.Tick;
        s.WinningTeam = w.WinningTeam;
        if (s.SlotToView.Length != u.Capacity) s.SlotToView = new int[u.Capacity];
        Array.Fill(s.SlotToView, -1);
        Ensure(ref s.Units, u.AliveCount);
        s.UnitCount = 0;
        s.OrderCount = 0;
        s.FactoryQueueCount = 0;

        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i)) continue;
            if (view != null && view[i] != IntelSnapshotWriter.Visible) continue;
            var bp = w.BpOf(i);
            ref var v = ref s.Units[s.UnitCount];
            v.Index = i;
            v.Gen = u.Gen[i];
            v.Bp = u.Bp[i];
            v.Army = u.Army[i];
            v.Pos = DMath.To3(u.Pos[i], u.Elevation[i]);
            v.Heading = u.Heading[i];
            v.TurretHeading = bp.Weapons.Length > 0 ? u.Weapons[i * UnitStore.MaxWeapons].TurretHeading : u.Heading[i];
            float maxHp = Combat.Veterancy.MaxHealth(bp, u.Kills[i]); // veterancy-effective max health
            v.MaxHealth = maxHp;
            v.HealthFrac = maxHp > 0 ? u.Health[i] / maxHp : 1;
            v.BuildFrac = u.BuildFraction[i];
            v.Flags = u.Flags[i];
            v.FactoryRepeat = u.Factory[i].Repeat;
            v.Kills = u.Kills[i];
            v.FireState = u.FireStates[i];
            ref readonly var econ = ref u.Econ[i];
            v.AdjMassProd = econ.AdjMassProd;
            v.AdjEnergyProd = econ.AdjEnergyProd;
            v.AdjEnergyUse = econ.AdjEnergyUse;
            ref readonly var bs = ref u.Builder[i];
            v.WorkMode = bs.Mode;
            v.CaptureFrac = Systems.CaptureSystem.Progress(w, i); // an enemy is capturing this unit
            v.WorkProgress = -1;
            var building = u.Factory[i].Current.IsNone ? u.Builder[i].Target : u.Factory[i].Current;
            if (u.IsActive(building)) v.WorkProgress = u.BuildFraction[building.Index];
            ref readonly var up = ref u.Upgrade[i];
            v.UpgradeBp = up.Active ? up.TargetBp : -1;
            v.UpgradeFrac = up.Active ? up.Fraction : 0;
            if (up.Active) v.WorkProgress = up.Fraction; // an upgrading structure works on its upgrade
            else if (u.Builder[i].Mode == BuilderMode.Upgrade && u.IsActive(building) && u.Upgrade[building.Index].Active)
                v.WorkProgress = u.Upgrade[building.Index].Fraction; // engineer assisting an upgrade
            else if (bs.Mode == BuilderMode.Capture && u.IsActive(building))
                v.WorkProgress = Systems.CaptureSystem.Progress(w, building.Index); // captor: the target's capture progress

            // Orders.
            var orders = view != null && w.IsEnemy(viewArmy, u.Army[i]) ? default : u.Orders[i].AsSpan();
            v.OrderStart = s.OrderCount;
            v.OrderCount = Math.Min(orders.Length, 32);
            Ensure(ref s.Orders, s.OrderCount + v.OrderCount);
            for (int o = 0; o < v.OrderCount; o++)
            {
                var dest = orders[o].Type is OrderType.Attack or OrderType.Guard or OrderType.Repair or OrderType.Reclaim or OrderType.Load or OrderType.Dock
                           && u.IsActive(orders[o].Target)
                    ? u.Pos[orders[o].Target.Index]
                    : orders[o].Destination;
                // Fog of war: don't reveal where an unseen target is (the order line ends at the unit).
                if (view != null && u.IsActive(orders[o].Target) && !w.Intel.IsVisible(viewArmy, orders[o].Target.Index)) dest = u.Pos[i];
                s.Orders[s.OrderCount++] = new OrderView
                {
                    Type = orders[o].Type,
                    Pos = DMath.To3(dest, w.Terrain.GetSurfaceHeight(dest)),
                    BlueprintIndex = orders[o].BlueprintIndex,
                };
            }

            // Factory queue.
            var q = view != null && w.IsEnemy(viewArmy, u.Army[i]) ? null : u.Factory[i].Queue;
            v.QueueStart = s.FactoryQueueCount;
            v.QueueCount = q?.Count ?? 0;
            if (q != null && q.Count > 0)
            {
                Ensure(ref s.FactoryQueue, s.FactoryQueueCount + q.Count);
                foreach (int b in q) s.FactoryQueue[s.FactoryQueueCount++] = b;
            }

            s.SlotToView[i] = s.UnitCount;
            s.UnitCount++;
        }

        var p = w.Projectiles;
        Ensure(ref s.Projectiles, p.AliveCount);
        s.ProjectileCount = 0;
        for (int k = 0; k < p.AliveCount; k++)
        {
            int i = p.AliveList[k];
            if (!p.Alive[i]) continue;
            if (view != null && !IntelSnapshotWriter.ProjectileVisible(w, viewArmy, p.Army[i], p.Pos[i])) continue;
            s.Projectiles[s.ProjectileCount++] = new ProjectileView
            {
                Id = p.Id[i], Pos = p.Pos[i], Vel = p.Vel[i], Army = p.Army[i], SourceBp = p.SourceBp[i], WeaponIndex = p.WeaponIndex[i],
            };
        }

        var props = w.Props;
        var propList = props.AliveList;
        Ensure(ref s.Props, propList.Length);
        s.PropCount = 0;
        foreach (int i in propList)
        {
            s.Props[s.PropCount++] = new PropView
            {
                Id = props.Id[i], Bp = props.BlueprintIndex[i], Pos = DMath.To3(props.Pos[i], w.Terrain.GetHeight(props.Pos[i])),
                Heading = props.Heading[i], Radius = props.Radius[i],
                Mass = props.Mass[i], MassMax = props.MassMax[i], Energy = props.Energy[i], EnergyMax = props.EnergyMax[i],
            };
        }

        if (s.Armies.Length != w.Armies.Length) s.Armies = new ArmyView[w.Armies.Length];
        for (int a = 0; a < w.Armies.Length; a++)
        {
            var ar = w.Armies[a];
            var e = ar.Eco;
            s.Armies[a] = new ArmyView
            {
                Index = a, Team = ar.Team, Color = ar.Color, Faction = ar.Faction, Defeated = ar.Defeated, UnitCount = ar.UnitCount,
                Mass = e.Mass, MassMax = e.MassMax, MassIncome = e.MassIncome, MassDemand = e.MassDemand, MassEfficiency = e.MassEfficiency,
                Energy = e.Energy, EnergyMax = e.EnergyMax, EnergyIncome = e.EnergyIncome, EnergyDemand = e.EnergyDemand, EnergyEfficiency = e.EnergyEfficiency,
            };
        }

        Strategic.StrategicSnapshotWriter.Write(w, s, viewArmy);
        Transport.TransportSnapshotWriter.Write(w, s, viewArmy);
        s.Effects.Clear();
        if (view != null) IntelSnapshotWriter.AddVisibleEffects(w, s, viewArmy);
        else s.Effects.AddRange(w.Events.Effects);
        w.Events.Effects.Clear();
    }

    private static void Ensure<T>(ref T[] arr, int size)
    {
        if (arr.Length < size) Array.Resize(ref arr, Math.Max(size, arr.Length * 2));
    }
}
