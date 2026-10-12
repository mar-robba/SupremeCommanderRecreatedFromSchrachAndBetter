using System.Numerics;
using System.Runtime.InteropServices;
using Rts.Sim.Core;

namespace Rts.Sim.Orders;

public enum OrderType : byte
{
    None,
    /// <summary>Move to Pos.</summary>
    Move,
    /// <summary>Move to Pos, engaging enemies met on the way.</summary>
    AttackMove,
    /// <summary>Attack Target unit (or ground at Pos if Target is None).</summary>
    Attack,
    /// <summary>Patrol between the unit's start and Pos (and further queued patrol points).</summary>
    Patrol,
    /// <summary>Follow and protect Target; engineers/factories assist it instead.</summary>
    Guard,
    /// <summary>Engineer: build BlueprintIndex at Pos with Heading.</summary>
    Build,
    /// <summary>Engineer: repair (or finish building) Target.</summary>
    Repair,
    /// <summary>Engineer: reclaim Target unit or the wreck/prop nearest to Pos.</summary>
    Reclaim,
    /// <summary>Capture Target (engineers, later).</summary>
    Capture,
    /// <summary>Missile silo (TML / nuke): launch one missile at the ground point Pos (waits for a missile if the
    /// stockpile is empty). Executed by Strategic/SiloSystem.</summary>
    Launch,
}

/// <summary>
/// One entry in a unit's order queue (SupCom-style: shift-click appends).
/// Fields not relevant to the type are ignored. <see cref="State"/> and <see cref="Spawned"/> are scratch
/// fields owned by the systems that execute the order.
/// </summary>
public struct Order
{
    public OrderType Type;
    public Vector2 Pos;
    public UnitHandle Target;
    public int BlueprintIndex;
    public float Heading;
    /// <summary>Offset from Pos assigned for group (formation) moves.</summary>
    public Vector2 FormationOffset;
    /// <summary>Execution sub-state, owned by the executing system.</summary>
    public int State;
    /// <summary>For Build orders: the structure created when construction started.</summary>
    public UnitHandle Spawned;
    /// <summary>Extra position (Patrol: the point the unit came from).</summary>
    public Vector2 Aux;

    public static Order MoveTo(Vector2 pos) => new() { Type = OrderType.Move, Pos = pos, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order AttackMoveTo(Vector2 pos) => new() { Type = OrderType.AttackMove, Pos = pos, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order AttackUnit(UnitHandle t) => new() { Type = OrderType.Attack, Target = t, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order PatrolTo(Vector2 pos) => new() { Type = OrderType.Patrol, Pos = pos, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order GuardUnit(UnitHandle t) => new() { Type = OrderType.Guard, Target = t, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order BuildAt(int blueprintIndex, Vector2 pos, float heading = 0) => new() { Type = OrderType.Build, Pos = pos, Heading = heading, Target = UnitHandle.None, BlueprintIndex = blueprintIndex, Spawned = UnitHandle.None };
    public static Order RepairUnit(UnitHandle t) => new() { Type = OrderType.Repair, Target = t, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order ReclaimAt(UnitHandle t, Vector2 pos) => new() { Type = OrderType.Reclaim, Target = t, Pos = pos, BlueprintIndex = -1, Spawned = UnitHandle.None };
    public static Order LaunchAt(Vector2 pos) => new() { Type = OrderType.Launch, Pos = pos, Target = UnitHandle.None, BlueprintIndex = -1, Spawned = UnitHandle.None };

    /// <summary>Destination including formation offset.</summary>
    public readonly Vector2 Destination => Pos + FormationOffset;
}

/// <summary>Per-unit FIFO order queue. Preallocated per unit slot; reuse, don't reallocate.</summary>
public sealed class OrderQueue
{
    public const int MaxOrders = 64;
    private readonly List<Order> _items = new(4);

    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;

    /// <summary>Mutable reference to the current (front) order. Check IsEmpty first.</summary>
    public ref Order Current => ref CollectionsMarshal.AsSpan(_items)[0];

    public ReadOnlySpan<Order> AsSpan() => CollectionsMarshal.AsSpan(_items);

    public ref Order At(int i) => ref CollectionsMarshal.AsSpan(_items)[i];

    public void Enqueue(in Order o)
    {
        if (_items.Count < MaxOrders) _items.Add(o);
    }

    public void Replace(in Order o)
    {
        _items.Clear();
        _items.Add(o);
    }

    /// <summary>Removes the current order. For patrol, callers re-append it to cycle.</summary>
    public void Pop()
    {
        if (_items.Count > 0) _items.RemoveAt(0);
    }

    public void Clear() => _items.Clear();
}
