using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;

namespace Rts.Client.UI;

/// <summary>
/// (10b-2) One-click enhancement swap / chain. The sim only installs an enhancement whose slot is empty or holds
/// exactly its prerequisite (<see cref="EnhancementCatalog.CanInstall"/>), and removes a slot's whole chain at once,
/// instantly (<see cref="EnhancementSystem.Remove"/>). So a click on any enhancement that is not installed plans, per
/// unit: remove the slot if it holds something outside the target's chain (sent with the first step, same frame: the
/// removal completes in the same command phase), then install the chain steps one by one (root → target). The
/// later steps wait here, client side, and are sent when the unit finishes the previous one; a step that is
/// cancelled, refused or never starts (<see cref="StartTimeoutTicks"/>) drops the rest. Pure: no raylib, not saved
/// (after a load the unit just stops after the step it was building).
/// </summary>
public sealed class EnhancementChain
{
    /// <summary>Sim ticks a sent step may take to show up as started before the plan is dropped (2 s).</summary>
    public const int StartTimeoutTicks = 20;

    private struct Pending
    {
        public UnitHandle Unit;
        public int[] Steps;   // enhancement indices, root first
        public int Next;      // index into Steps of the step sent last
        public bool Started;  // the sent step was seen in progress
        public int SentTick;
    }

    private readonly List<Pending> _pending = new();

    /// <summary>Units with a plan in progress (for tests and the tooltip).</summary>
    public int Count => _pending.Count;

    /// <summary>True if unit <paramref name="h"/> has queued steps after the current one.</summary>
    public bool Has(UnitHandle h)
    {
        foreach (var p in _pending) if (p.Unit == h) return true;
        return false;
    }

    /// <summary>
    /// The plan to get enhancement <paramref name="e"/> on a unit of blueprint <paramref name="cur"/>: the steps to
    /// install (root first, ending with e) and whether the slot must be cleared first. Empty when e is installed
    /// already (or invalid).
    /// </summary>
    public static int[] Plan(UnitBlueprint cur, int e, out bool removeFirst)
    {
        removeFirst = false;
        if (!cur.IsEnhanceable || (uint)e >= (uint)cur.Enhancements.Length || EnhancementCatalog.HasInstalled(cur, e))
            return Array.Empty<int>();
        int inSlot = cur.Installed[(int)cur.Enhancements[e].Slot];
        int n = 0;
        for (int x = e; x >= 0 && n <= cur.Enhancements.Length; x = cur.Enhancements[x].PrerequisiteIndex) n++;
        var chain = new int[n];
        int k = n - 1, keep = -1;
        for (int x = e; x >= 0 && k >= 0; x = cur.Enhancements[x].PrerequisiteIndex)
        {
            if (x == inSlot) keep = k;
            chain[k--] = x;
        }
        if (keep >= 0) return chain[(keep + 1)..]; // the slot holds part of the chain: continue from there
        removeFirst = inSlot >= 0;
        return chain;
    }

    /// <summary>Text of a plan for the tooltip, e.g. "remove Gun, install Shield → Heavy Shield".</summary>
    public static string Describe(UnitBlueprint cur, int[] steps, bool removeFirst)
    {
        var parts = new List<string>();
        if (removeFirst) parts.Add($"remove {cur.Enhancements[cur.Installed[(int)cur.Enhancements[steps[0]].Slot]].Name} (no refund)");
        if (steps.Length > 0) parts.Add("install " + string.Join(" → ", steps.Select(s => cur.Enhancements[s].Name)));
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Starts the plan for one idle unit: sends the removal (if needed) and the first step now, keeps the rest.
    /// Returns false (nothing sent) if there is nothing to do. Replaces an older plan of the same unit.
    /// </summary>
    public bool Begin(UnitHandle h, UnitBlueprint cur, int e, int army, int tick, Action<Command> send)
    {
        var steps = Plan(cur, e, out bool remove);
        if (steps.Length == 0) return false;
        Drop(h);
        var one = new[] { h };
        if (remove) send(new RemoveEnhancementCommand(army, one, cur.Enhancements[e].Slot));
        send(new EnhanceCommand(army, one, cur.Enhancements[steps[0]].Id));
        if (steps.Length > 1)
            _pending.Add(new Pending { Unit = h, Steps = steps, Next = 0, SentTick = tick });
        return true;
    }

    /// <summary>Forgets the plan of unit <paramref name="h"/> (e.g. the player cancelled it).</summary>
    public void Drop(UnitHandle h)
    {
        for (int i = _pending.Count - 1; i >= 0; i--) if (_pending[i].Unit == h) _pending.RemoveAt(i);
    }

    /// <summary>The unit state the chain needs: its current blueprint and whether it is upgrading / enhancing.</summary>
    public delegate bool UnitLookup(UnitHandle h, out UnitBlueprint bp, out bool busy);

    /// <summary>Once per frame: sends the next step of every unit that finished the previous one; drops plans of
    /// dead units, cancelled steps and steps that never started.</summary>
    public void Update(UnitLookup lookup, int army, int tick, Action<Command> send)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (!lookup(p.Unit, out var bp, out bool busy)) { _pending.RemoveAt(i); continue; }
            if (busy) { p.Started = true; _pending[i] = p; continue; }
            int cur = p.Steps[p.Next];
            if (EnhancementCatalog.HasInstalled(bp, cur))
            {
                int next = p.Steps[p.Next + 1];
                if (!EnhancementCatalog.CanInstall(bp, next)) { _pending.RemoveAt(i); continue; }
                send(new EnhanceCommand(army, new[] { p.Unit }, bp.Enhancements[next].Id));
                p.Next++;
                p.Started = false;
                p.SentTick = tick;
                if (p.Next >= p.Steps.Length - 1) { _pending.RemoveAt(i); continue; } // last step sent
                _pending[i] = p;
            }
            else if (p.Started || tick - p.SentTick > StartTimeoutTicks) _pending.RemoveAt(i); // cancelled / refused
        }
    }
}
