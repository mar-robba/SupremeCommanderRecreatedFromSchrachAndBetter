using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.Transport;

/// <summary>
/// Generic cargo mechanic (data-driven by <see cref="CargoBlueprint"/> / <see cref="UnitBlueprint.CargoSize"/>):
/// air transports carry land units, air staging and the Atlantis dock aircraft. SEQUENTIAL, runs at the start of
/// the tick before the AliveList rebuild, so units that board this tick are already off the map (not in AliveList,
/// spatial grid, targeting, intel, economy) and unloaded units are back on it in the same tick.
/// <list type="bullet">
/// <item>Executes the transport orders (Load, Unload, Ferry, Dock; see OrderType) by setting movement goals and
///   VTOL <see cref="CargoState.AirMode"/>; OrderSystem ignores these order types.</item>
/// <item>Carried units follow their carrier, docked aircraft are repaired/refuelled and undock when done (air
///   staging) or stay until an Unload ("launch") order (aircraft carriers, <see cref="CargoBlueprint.KeepDocked"/>).</item>
/// <item>Aircraft burn fuel while flying; out of fuel = <see cref="OutOfFuelSpeed"/> × speed. Idle aircraft that are
///   badly damaged or out of fuel return to the nearest own air carrier with room.</item>
/// <item>A destroyed carrier kills its cargo (SupCom: everything inside dies, no emergency drop).</item>
/// </list>
/// </summary>
public static class TransportSystem
{
    /// <summary>Speed multiplier of aircraft with an empty fuel tank.</summary>
    public const float OutOfFuelSpeed = 0.5f;
    /// <summary>Idle aircraft below this health fraction return to an air carrier by themselves.</summary>
    public const float AutoDockHealth = 0.5f;
    public const int AutoDockInterval = 20;
    /// <summary>Ticks without auto-return after leaving a carrier.</summary>
    public const int AutoDockCooldown = 300;
    /// <summary>Ferry transports leave the beacon this many ticks after the last unit boarded (or at once when full).</summary>
    public const int FerryDepartDelay = 30;
    /// <summary>Units waiting within this distance of a ferry beacon are picked up there.</summary>
    public const float FerryGatherRadius = 9f;
    /// <summary>Two ferry beacons closer than this are the same beacon.</summary>
    public const float FerryBeaconMatch = 6f;

    public static void InitUnit(SimWorld w, int i, UnitBlueprint bp)
    {
        var c = w.Cargo;
        if ((w.Units.Flags[i] & UnitFlags.Carried) == 0) c.Carrier[i] = -1;
        c.Fuel[i] = bp.FuelSeconds;
        c.AirMode[i] = AirModes.Fly;
        c.WantLandTick[i] = -1000;
        c.LastBoardTick[i] = -1000;
        c.NoAutoDockUntil[i] = 0;
        if (bp.Cargo != null) c.Holds[i] ??= new List<UnitHandle>();
    }

    /// <summary>Carrier death kills its cargo; a carried unit dying leaves its carrier's hold.</summary>
    public static void OnUnitDeath(SimWorld w, int i, UnitBlueprint bp)
    {
        var c = w.Cargo;
        var u = w.Units;
        int carrier = c.Carrier[i];
        if (carrier >= 0)
        {
            RemoveFromHold(c, carrier, i);
            c.Carrier[i] = -1;
        }
        var holds = c.Holds[i];
        if (holds is { Count: > 0 })
        {
            var inside = holds.ToArray(); // rare; nested carriers recurse through KillUnit -> OnUnitDeath
            holds.Clear();
            foreach (var h in inside)
            {
                c.Carrier[h.Index] = -1;
                if (u.IsActive(h)) w.KillUnit(h.Index, UnitHandle.None);
            }
        }
    }

    public static float AirSpeedFactor(SimWorld w, int i, UnitBlueprint bp) =>
        bp.FuelSeconds > 0 && w.Cargo.Fuel[i] <= 0 ? OutOfFuelSpeed : 1f;

    public static void Run(SimWorld w)
    {
        var u = w.Units;
        var c = w.Cargo;
        c.LoadedThisTick.Clear();
        for (int k = 0; k < u.AliveCount; k++) // last tick's list: new units join next tick
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i) || (u.Flags[i] & UnitFlags.Carried) != 0 || !u.IsComplete(i)) continue;
            DropAttackOnCarried(u, i);
            var bp = w.BpOf(i);
            if (bp.Cargo == null && !bp.IsMobile) continue; // ordinary structures
            if (c.Fuel[i] > 0 && bp.Movement.Layer == MotionLayer.Air) c.Fuel[i] = MathF.Max(0f, c.Fuel[i] - SimWorld.Dt);

            var q = u.Orders[i];
            bool busy = !q.IsEmpty && Exec(w, i, bp, q);
            if (!busy)
            {
                // Idle transports come down when units walk up to board them.
                if (bp.IsVtol) c.AirMode[i] = q.IsEmpty && c.WantLandTick[i] >= w.Tick - 1 ? AirModes.Land : AirModes.Fly;
                if (q.IsEmpty && bp.Movement.Layer == MotionLayer.Air && (w.Tick + i) % AutoDockInterval == 0) TryAutoDock(w, i, bp);
            }
            if (bp.Cargo != null && (u.Flags[i] & UnitFlags.Carried) == 0) UpdateCarrier(w, i, bp);
        }
        if (c.LoadedThisTick.Count > 0) PurgeReferences(w);
    }

    // ------------------------------------------------------------------ orders

    /// <summary>Runs the front order if it is a transport order. Returns true if it was one.</summary>
    private static bool Exec(SimWorld w, int i, UnitBlueprint bp, OrderQueue q)
    {
        switch (q.Current.Type)
        {
            case OrderType.Load:
                if (bp.Cargo != null && bp.Cargo.Layer == CargoLayer.Land) ExecPickup(w, i, bp, q);
                else if (bp.CargoKind == CargoLayer.Land && bp.CargoSize > 0) ExecBoard(w, i, bp, q);
                else PopStop(w, i, q);
                return true;
            case OrderType.Unload:
                if (bp.Cargo != null) ExecUnload(w, i, bp, q);
                else PopStop(w, i, q);
                return true;
            case OrderType.Ferry:
                if (bp.IsVtol) ExecFerryTransport(w, i, bp, q);
                else if (bp.CargoKind == CargoLayer.Land && bp.CargoSize > 0) ExecFerryWait(w, i, q);
                else PopStop(w, i, q);
                return true;
            case OrderType.Dock:
                if (bp.CargoKind == CargoLayer.Air && bp.CargoSize > 0) ExecDock(w, i, bp, q);
                else PopStop(w, i, q);
                return true;
            case OrderType.TransportTo: // issue-only; never valid in a queue
                PopStop(w, i, q);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Land unit walking to a transport to board it.</summary>
    private static void ExecBoard(SimWorld w, int i, UnitBlueprint bp, OrderQueue q)
    {
        var u = w.Units;
        var t = q.Current.Target;
        if (!u.IsActive(t) || !CanCarry(w, t.Index, i)) { PopStop(w, i, q); return; }
        int ti = t.Index;
        var tbp = w.BpOf(ti);
        float range = BoardRange(tbp, bp);
        float d2 = Vector2.DistanceSquared(u.Pos[i], u.Pos[ti]);
        if (d2 <= range * range && IsLow(w, ti, tbp)) { Board(w, ti, i); return; }
        if (d2 <= Sq(range + 14f)) w.Cargo.WantLandTick[ti] = w.Tick;
        ref var mv = ref u.Move[i];
        if (d2 <= Sq(range * 0.6f)) mv.HasGoal = false; // wait under it for the landing
        else OrderSystem.SetGoal(ref mv, w.Terrain.Clamp(u.Pos[ti]), MathF.Max(1f, range * 0.5f));
    }

    /// <summary>Transport flying to a land unit to pick it up.</summary>
    private static void ExecPickup(SimWorld w, int i, UnitBlueprint bp, OrderQueue q)
    {
        var u = w.Units;
        var c = w.Cargo;
        // Units already aboard (they walked in) complete their pickup orders at once.
        for (int guard = 0; guard < 64 && !q.IsEmpty && q.Current.Type == OrderType.Load; guard++)
        {
            var x = q.Current.Target;
            if (u.IsActive(x) && c.Carrier[x.Index] == i) { q.Pop(); continue; }
            if (!u.IsActive(x) || (u.Flags[x.Index] & UnitFlags.Carried) != 0 || !CanCarry(w, i, x.Index)) { q.Pop(); continue; }
            break;
        }
        ref var mv = ref u.Move[i];
        if (q.IsEmpty || q.Current.Type != OrderType.Load) { mv.HasGoal = false; c.AirMode[i] = AirModes.Fly; return; }
        int xi = q.Current.Target.Index;
        var xbp = w.BpOf(xi);
        float range = BoardRange(bp, xbp);
        float d = Vector2.Distance(u.Pos[i], u.Pos[xi]);
        if (d > range * 0.4f) OrderSystem.SetGoal(ref mv, w.Terrain.Clamp(u.Pos[xi]), MathF.Max(0.8f, range * 0.3f));
        else mv.HasGoal = false;
        c.AirMode[i] = d < range + 8f ? AirModes.Land : AirModes.Fly;
        if (d <= range && IsLow(w, i, bp))
        {
            Board(w, i, xi);
            q.Pop();
        }
    }

    private static void ExecUnload(SimWorld w, int i, UnitBlueprint bp, OrderQueue q)
    {
        var u = w.Units;
        var c = w.Cargo;
        ref var mv = ref u.Move[i];
        ref var o = ref q.Current;
        if (c.Count(i) == 0) { PopStop(w, i, q); c.AirMode[i] = AirModes.Fly; return; }
        var dest = w.Terrain.Clamp(o.Destination);
        float d = Vector2.Distance(u.Pos[i], dest);
        if (bp.Cargo!.Layer == CargoLayer.Air)
        {
            // Aircraft carrier: sail to the point first (structures launch where they are).
            if (bp.IsMobile && d > bp.Radius + 4f) { OrderSystem.SetGoal(ref mv, dest, bp.Radius + 2f); return; }
            LaunchAll(w, i, bp);
            PopStop(w, i, q);
            return;
        }
        if (!AtDropPoint(w, i, bp, dest, d)) return;
        DropAll(w, i, bp);
        PopStop(w, i, q);
        c.AirMode[i] = AirModes.Fly;
    }

    /// <summary>Flies a land transport to <paramref name="dest"/> and lands; true once it is low there.</summary>
    private static bool AtDropPoint(SimWorld w, int i, UnitBlueprint bp, Vector2 dest, float d)
    {
        ref var mv = ref w.Units.Move[i];
        if (d > 1.5f)
        {
            OrderSystem.SetGoal(ref mv, dest, 1f);
            w.Cargo.AirMode[i] = d < 10f ? AirModes.Land : AirModes.Fly;
            return false;
        }
        mv.HasGoal = false;
        w.Cargo.AirMode[i] = AirModes.Land;
        return IsLow(w, i, bp);
    }

    /// <summary>Ferry transport: State 0 = at/going to the pickup beacon (Pos), 1 = carrying to the drop point (Aux).</summary>
    private static void ExecFerryTransport(SimWorld w, int i, UnitBlueprint bp, OrderQueue q)
    {
        var u = w.Units;
        var c = w.Cargo;
        ref var mv = ref u.Move[i];
        ref var o = ref q.Current;
        if (o.State == 1)
        {
            if (c.Count(i) == 0) { o.State = 0; return; }
            var drop = w.Terrain.Clamp(o.Aux);
            if (!AtDropPoint(w, i, bp, drop, Vector2.Distance(u.Pos[i], drop))) return;
            DropAll(w, i, bp);
            if (c.Count(i) == 0) o.State = 0; // could not drop everything: try again next tick
            c.AirMode[i] = AirModes.Fly;
            return;
        }

        var beacon = w.Terrain.Clamp(o.Pos);
        int free = bp.Cargo!.Capacity - c.UsedSlots(w, i);
        bool waiting = AnyFerryWaiting(w, i, beacon, free, out _);
        if (c.Count(i) > 0 && (!waiting || free <= 0) && w.Tick - c.LastBoardTick[i] >= FerryDepartDelay)
        {
            o.State = 1;
            c.AirMode[i] = AirModes.Fly;
            return;
        }
        float d = Vector2.Distance(u.Pos[i], beacon);
        if (d > 1.5f)
        {
            OrderSystem.SetGoal(ref mv, beacon, 1f);
            c.AirMode[i] = waiting && d < 10f ? AirModes.Land : AirModes.Fly;
            return;
        }
        mv.HasGoal = false;
        c.AirMode[i] = waiting || c.Count(i) > 0 ? AirModes.Land : AirModes.Fly;
        if (!waiting || !IsLow(w, i, bp)) return;
        // Board every waiting unit that is close enough and fits (index order = deterministic).
        for (int k = 0; k < u.AliveCount && free > 0; k++)
        {
            int j = u.AliveList[k];
            if (!IsFerryWaiter(w, j, i, beacon)) continue;
            var jbp = w.BpOf(j);
            if (jbp.CargoSize > free) continue;
            if (Vector2.DistanceSquared(u.Pos[j], u.Pos[i]) > Sq(BoardRange(bp, jbp) + 5f)) continue;
            Board(w, i, j);
            free -= jbp.CargoSize;
        }
    }

    private static bool IsFerryWaiter(SimWorld w, int j, int transport, Vector2 beacon)
    {
        var u = w.Units;
        if (!u.IsActive(j) || (u.Flags[j] & UnitFlags.Carried) != 0 || u.Army[j] != u.Army[transport] || !u.IsComplete(j)) return false;
        var q = u.Orders[j];
        if (q.IsEmpty || q.Current.Type != OrderType.Ferry) return false;
        var jbp = w.BpOf(j);
        if (jbp.CargoKind != CargoLayer.Land || jbp.CargoSize <= 0 || jbp.Cargo != null) return false;
        if (Vector2.DistanceSquared(q.Current.Pos, beacon) > FerryBeaconMatch * FerryBeaconMatch) return false;
        return Vector2.DistanceSquared(u.Pos[j], beacon) <= FerryGatherRadius * FerryGatherRadius;
    }

    private static bool AnyFerryWaiting(SimWorld w, int transport, Vector2 beacon, int free, out int count)
    {
        var u = w.Units;
        count = 0;
        if (free <= 0) return false;
        // Waiters stand near the beacon: scan the spatial grid around it (last tick's positions).
        var near = w.Cargo.Near;
        near.Clear();
        w.Grid.QueryRadius(u, beacon, FerryGatherRadius + 1f, near);
        foreach (int j in near)
            if (IsFerryWaiter(w, j, transport, beacon) && w.BpOf(j).CargoSize <= free) count++;
        return count > 0;
    }

    /// <summary>Land unit ordered to a ferry beacon: walk there and wait for a ferry transport.</summary>
    private static void ExecFerryWait(SimWorld w, int i, OrderQueue q)
    {
        var u = w.Units;
        var beacon = w.Terrain.Clamp(q.Current.Pos);
        ref var mv = ref u.Move[i];
        if (Vector2.DistanceSquared(u.Pos[i], beacon) > 3.5f * 3.5f) OrderSystem.SetGoal(ref mv, beacon, 3f);
        else mv.HasGoal = false;
    }

    /// <summary>Aircraft flying to an air carrier to dock (waits nearby while the carrier is full).</summary>
    private static void ExecDock(SimWorld w, int i, UnitBlueprint bp, OrderQueue q)
    {
        var u = w.Units;
        ref var o = ref q.Current;
        if (!IsAirCarrierFor(w, o.Target, i))
        {
            o.Target = FindAirCarrier(w, i, bp, needRoom: false);
            if (o.Target.IsNone) { PopStop(w, i, q); return; }
        }
        int ti = o.Target.Index;
        var tbp = w.BpOf(ti);
        float dockR = ConstructionSystem.HalfSize(tbp) + tbp.Cargo!.LoadRange
                      + (bp.IsVtol ? 1f : MovementSystem.OrbitRadius(bp.Movement) * 1.2f);
        bool room = w.Cargo.UsedSlots(w, ti) + bp.CargoSize <= tbp.Cargo.Capacity;
        if (room && Vector2.DistanceSquared(u.Pos[i], u.Pos[ti]) <= dockR * dockR) { Board(w, ti, i); return; }
        OrderSystem.SetGoal(ref u.Move[i], w.Terrain.Clamp(u.Pos[ti]), MathF.Max(1f, ConstructionSystem.HalfSize(tbp)));
        if (bp.IsVtol) w.Cargo.AirMode[i] = AirModes.Fly;
    }

    private static void TryAutoDock(SimWorld w, int i, UnitBlueprint bp)
    {
        var u = w.Units;
        var c = w.Cargo;
        if (bp.CargoKind != CargoLayer.Air || bp.CargoSize <= 0 || w.Tick < c.NoAutoDockUntil[i]) return;
        bool hurt = u.Health[i] < bp.MaxHealth * AutoDockHealth;
        bool dry = bp.FuelSeconds > 0 && c.Fuel[i] <= 0;
        if (!hurt && !dry) return;
        var t = FindAirCarrier(w, i, bp, needRoom: true);
        if (!t.IsNone) u.Orders[i].Enqueue(TransportOrders.Dock(t));
    }

    // ------------------------------------------------------------------ carriers

    /// <summary>Keeps cargo with its carrier; air carriers repair/refuel docked aircraft and release finished ones.</summary>
    private static void UpdateCarrier(SimWorld w, int i, UnitBlueprint bp)
    {
        var u = w.Units;
        var c = w.Cargo;
        var holds = c.Holds[i];
        if (holds == null || holds.Count == 0) return;
        var cb = bp.Cargo!;
        for (int k = 0; k < holds.Count; k++)
        {
            int j = holds[k].Index;
            u.Pos[j] = u.Pos[i];
            u.Elevation[j] = u.Elevation[i];
            u.Heading[j] = u.Heading[i];
            if (cb.Layer != CargoLayer.Air) continue;
            var jbp = w.BpOf(j);
            if (u.Health[j] < jbp.MaxHealth) u.Health[j] = MathF.Min(jbp.MaxHealth, u.Health[j] + cb.RepairRate * jbp.MaxHealth * SimWorld.Dt);
            float fuelMax = jbp.FuelSeconds;
            if (fuelMax > 0 && c.Fuel[j] < fuelMax) c.Fuel[j] = MathF.Min(fuelMax, c.Fuel[j] + cb.RefuelRate * fuelMax * SimWorld.Dt);
            bool done = u.Health[j] >= jbp.MaxHealth && (fuelMax <= 0 || c.Fuel[j] >= fuelMax);
            if (done && (!cb.KeepDocked || u.Orders[j].Count > 1))
            {
                Undock(w, i, bp, j, k);
                k--;
            }
        }
    }

    // ------------------------------------------------------------------ boarding / release

    /// <summary>True if carrier <paramref name="t"/> can take unit <paramref name="j"/> now (layer, army, room).</summary>
    public static bool CanCarry(SimWorld w, int t, int j)
    {
        var u = w.Units;
        if (t == j || !u.IsActive(t) || !u.IsActive(j) || !u.IsComplete(t) || !u.IsComplete(j)) return false;
        if (u.Army[t] != u.Army[j] || ((u.Flags[t] | u.Flags[j]) & UnitFlags.Carried) != 0) return false;
        var tbp = w.BpOf(t);
        var jbp = w.BpOf(j);
        if (tbp.Cargo == null || jbp.CargoSize <= 0 || jbp.CargoKind != tbp.Cargo.Layer) return false;
        if (jbp.Cargo != null && jbp.Cargo.Layer == CargoLayer.Air) return false; // no carriers inside carriers of aircraft
        return w.Cargo.UsedSlots(w, t) + jbp.CargoSize <= tbp.Cargo.Capacity;
    }

    /// <summary>Puts unit j inside carrier t (off the map). SEQUENTIAL. Pops j's Load/Ferry order; a Dock order stays
    /// at the front until the aircraft undocks; any other orders of a unit picked up by a transport are cleared.</summary>
    public static void Board(SimWorld w, int t, int j)
    {
        var u = w.Units;
        var c = w.Cargo;
        var q = u.Orders[j];
        if (!q.IsEmpty && q.Current.Type is OrderType.Load or OrderType.Ferry) q.Pop();
        else if (!q.IsEmpty && q.Current.Type != OrderType.Dock) q.Clear();
        ConstructionSystem.StopBuilder(w, j);
        u.Flags[j] = (u.Flags[j] | UnitFlags.Carried) & ~(UnitFlags.Moving | UnitFlags.Firing | UnitFlags.Building);
        c.Carrier[j] = t;
        (c.Holds[t] ??= new List<UnitHandle>()).Add(u.HandleOf(j));
        c.LastBoardTick[t] = w.Tick;
        c.AirMode[j] = AirModes.Fly;
        c.LoadedThisTick.Add(j);
        foreach (ref var ws in u.WeaponsOf(j))
        {
            ws.Target = UnitHandle.None;
            ws.HasGroundTarget = false;
        }
        u.Pos[j] = u.Pos[t];
        u.Elevation[j] = u.Elevation[t];
        ResetMove(w, j);
    }

    /// <summary>Drops every land unit of transport i around it on standable ground (spiral rings). Units resume their
    /// queued orders. Units that find no free spot stay aboard.</summary>
    public static void DropAll(SimWorld w, int i, UnitBlueprint bp)
    {
        var u = w.Units;
        var holds = w.Cargo.Holds[i];
        if (holds == null || holds.Count == 0) return;
        float maxR = 0.5f;
        foreach (var h in holds) maxR = MathF.Max(maxR, w.BpOf(h.Index).Radius);
        float spacing = maxR * 2f + 0.8f;
        var center = u.Pos[i];
        float heading = u.Heading[i];
        int ring = 1, slot = 0;
        var inside = holds.ToArray();
        foreach (var h in inside)
        {
            int j = h.Index;
            var layer = w.BpOf(j).Movement.Layer;
            bool placed = false;
            while (ring <= 8 && !placed)
            {
                int count = ring * 6;
                float ang = heading + DMath.Pi + slot * (2f * DMath.Pi / count) + ring * 0.5f;
                var p = w.Terrain.Clamp(center + DMath.Dir(ang) * (ring * spacing));
                if (++slot >= count) { slot = 0; ring++; }
                if (!w.Paths.CanStand(layer, p)) continue;
                ReleaseLand(w, i, j, p);
                placed = true;
            }
            if (!placed) break;
        }
    }

    private static void ReleaseLand(SimWorld w, int carrier, int j, Vector2 p)
    {
        var u = w.Units;
        var c = w.Cargo;
        RemoveFromHold(c, carrier, j);
        c.Carrier[j] = -1;
        u.Flags[j] &= ~UnitFlags.Carried;
        u.Pos[j] = p;
        u.Heading[j] = u.Heading[carrier];
        u.Elevation[j] = w.BpOf(j).Movement.Layer is MotionLayer.Land or MotionLayer.Amphibious
            ? w.Terrain.GetHeight(p) : w.Terrain.GetSurfaceHeight(p);
        ResetMove(w, j);
    }

    /// <summary>Air carrier launches every docked aircraft (Unload on staging / Atlantis).</summary>
    public static void LaunchAll(SimWorld w, int i, UnitBlueprint bp)
    {
        var holds = w.Cargo.Holds[i];
        if (holds == null) return;
        while (holds.Count > 0) Undock(w, i, bp, holds[0].Index, 0);
    }

    /// <summary>Aircraft j leaves air carrier i (slot k in its hold): pops its Dock order, takes off from the deck.</summary>
    private static void Undock(SimWorld w, int i, UnitBlueprint bp, int j, int k)
    {
        var u = w.Units;
        var c = w.Cargo;
        var q = u.Orders[j];
        if (!q.IsEmpty && q.Current.Type == OrderType.Dock) q.Pop();
        RemoveFromHold(c, i, j);
        c.Carrier[j] = -1;
        u.Flags[j] &= ~UnitFlags.Carried;
        float r = ConstructionSystem.HalfSize(bp) + 1f;
        var p = w.Terrain.Clamp(u.Pos[i] + DMath.Dir(u.Heading[i] + k * 2.39996f) * r); // golden-angle spread
        u.Pos[j] = p;
        u.Heading[j] = u.Heading[i] + k * 2.39996f;
        u.Elevation[j] = u.Elevation[i] + bp.Visual.Size.Y + 0.5f;
        c.NoAutoDockUntil[j] = w.Tick + AutoDockCooldown;
        ResetMove(w, j);
    }

    private static void RemoveFromHold(CargoState c, int carrier, int j)
    {
        var holds = c.Holds[carrier];
        if (holds == null) return;
        for (int k = 0; k < holds.Count; k++)
            if (holds[k].Index == j) { holds.RemoveAt(k); return; }
    }

    private static void ResetMove(SimWorld w, int j)
    {
        ref var mv = ref w.Units.Move[j];
        mv.Velocity = Vector2.Zero;
        mv.Speed = 0;
        mv.HasGoal = false;
        mv.Arrived = false;
        mv.PathId = -1;
        mv.PathRequestTick = 0;
        mv.RepathRequested = false;
        mv.PathPartial = false;
        mv.PathToNearest = false;
        mv.StuckTime = 0;
        mv.BlockedTime = 0;
        mv.StuckRepaths = 0;
        mv.GroupId = 0;
        mv.OrbitCenter = w.Units.Pos[j];
    }

    /// <summary>Units that boarded this tick vanish: drop weapon locks and Attack orders aimed at them.</summary>
    private static void PurgeReferences(SimWorld w)
    {
        var u = w.Units;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i) || (u.Flags[i] & UnitFlags.Carried) != 0) continue;
            foreach (ref var ws in u.WeaponsOf(i))
                if (!ws.Target.IsNone && IsCarried(u, ws.Target)) ws.Target = UnitHandle.None;
            var q = u.Orders[i];
            if (!q.IsEmpty && q.Current.Type == OrderType.Attack && !q.Current.Target.IsNone && IsCarried(u, q.Current.Target))
            {
                q.Pop();
                u.Move[i].HasGoal = false;
            }
        }
    }

    /// <summary>An Attack order issued at a unit that is already aboard a transport (e.g. ordered after boarding)
    /// would keep forcing weapons onto it: drop the order and the weapon locks on that unit. O(1) unless it hits.</summary>
    private static void DropAttackOnCarried(UnitStore u, int i)
    {
        var q = u.Orders[i];
        if (q.IsEmpty || q.Current.Type != OrderType.Attack || q.Current.Target.IsNone) return;
        var t = q.Current.Target;
        if (!IsCarried(u, t)) return;
        foreach (ref var ws in u.WeaponsOf(i))
            if (ws.Target == t) ws.Target = UnitHandle.None;
        q.Pop();
        u.Move[i].HasGoal = false;
    }

    private static bool IsCarried(UnitStore u, UnitHandle h) =>
        (uint)h.Index < (uint)u.Capacity && u.Gen[h.Index] == h.Generation && (u.Flags[h.Index] & UnitFlags.Carried) != 0;

    // ------------------------------------------------------------------ helpers

    /// <summary>Boarding distance between a carrier and a unit (centers).</summary>
    public static float BoardRange(UnitBlueprint carrier, UnitBlueprint unit) =>
        carrier.Radius + unit.Radius + (carrier.Cargo?.LoadRange ?? 3f);

    /// <summary>VTOL transports must be landed (near HoverHeight) to load/unload; other carriers always can.</summary>
    public static bool IsLow(SimWorld w, int i, UnitBlueprint bp)
    {
        if (bp.Movement.Layer != MotionLayer.Air || bp.Cargo == null) return true;
        float above = w.Units.Elevation[i] - w.Terrain.GetSurfaceHeight(w.Units.Pos[i]);
        return above <= bp.Cargo.HoverHeight + 1f;
    }

    private static bool IsAirCarrierFor(SimWorld w, UnitHandle t, int aircraft)
    {
        var u = w.Units;
        if (!u.IsActive(t) || !u.IsComplete(t.Index) || u.Army[t.Index] != u.Army[aircraft]) return false;
        if ((u.Flags[t.Index] & UnitFlags.Carried) != 0) return false;
        var tbp = w.BpOf(t.Index);
        return tbp.Cargo != null && tbp.Cargo.Layer == CargoLayer.Air && w.BpOf(aircraft).CargoSize <= tbp.Cargo.Capacity;
    }

    /// <summary>Nearest own air carrier (air staging / Atlantis) for an aircraft; with needRoom only carriers with a
    /// free slot now. Ties: lowest index. Scans the alive list (rare: issue time / auto-return).</summary>
    public static UnitHandle FindAirCarrier(SimWorld w, int i, UnitBlueprint bp, bool needRoom)
    {
        var u = w.Units;
        int best = -1;
        float bestD = float.MaxValue;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int t = u.AliveList[k];
            var tbp = w.BpOf(t);
            if (tbp.Cargo == null || tbp.Cargo.Layer != CargoLayer.Air) continue;
            if (!IsAirCarrierFor(w, u.HandleOf(t), i)) continue;
            if (needRoom && w.Cargo.UsedSlots(w, t) + bp.CargoSize > tbp.Cargo.Capacity) continue;
            float d = Vector2.DistanceSquared(u.Pos[i], u.Pos[t]);
            if (d < bestD) { bestD = d; best = t; }
        }
        return best < 0 ? UnitHandle.None : u.HandleOf(best);
    }

    private static void PopStop(SimWorld w, int i, OrderQueue q)
    {
        q.Pop();
        w.Units.Move[i].HasGoal = false;
    }

    private static float Sq(float v) => v * v;

    /// <summary>Hash of the transport state (determinism tests / desync checks).</summary>
    public static ulong Hash(SimWorld w)
    {
        var u = w.Units;
        var c = w.Cargo;
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        for (int i = 0; i < u.Capacity; i++)
        {
            if (!u.Alive[i]) continue;
            if (c.Carrier[i] >= 0) { Mix((ulong)i); Mix((ulong)c.Carrier[i]); }
            if (c.Fuel[i] > 0) Mix(BitConverter.SingleToUInt32Bits(c.Fuel[i]));
            if (c.AirMode[i] != 0) Mix((ulong)i * 31 + c.AirMode[i]);
        }
        return h;
    }
}
