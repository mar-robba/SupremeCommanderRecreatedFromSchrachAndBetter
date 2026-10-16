using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;

namespace Rts.Client.Input;

/// <summary>
/// Fire state toggle (SupCom): Fire at will → Return fire → Hold fire, per unit (<see cref="SetFireStateCommand"/>,
/// sim: Combat/FireStates). Hotkey <see cref="Hotkeys.FireState"/> (Shift: backwards), order button "Fire".
/// Applies to selected own units with weapons.
/// </summary>
public static partial class UnitToggles
{
    /// <summary>Fire state of the selection: Count = armed units, State = the common state (or the first one's when
    /// Mixed).</summary>
    public readonly record struct FireSelection(int Count, FireState State, bool Mixed)
    {
        public bool Any => Count > 0;
    }

    public static FireSelection FireStateOf(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var snap = ctx.Snapshots.Current;
        int n = 0;
        var first = FireState.FireAtWill;
        bool mixed = false;
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !ctx.Controls(v.Army)) continue;
            if (ctx.Db[v.Bp].Weapons.Length == 0) continue;
            if (n == 0) first = v.FireState;
            else if (v.FireState != first) mixed = true;
            n++;
        }
        return new FireSelection(n, first, mixed);
    }

    /// <summary>The state after <paramref name="s"/> going <paramref name="dir"/> (+1 / -1) through the three states.
    /// A mixed selection goes to the state after the first unit's (pure, unit-tested).</summary>
    public static FireState NextFireState(FireState s, int dir)
    {
        const int n = (int)FireState.HoldFire + 1;
        return (FireState)((((int)s + dir) % n + n) % n);
    }

    public static void CycleFireState(GameContext ctx, IReadOnlyList<UnitHandle> selected, int dir)
    {
        var st = FireStateOf(ctx, selected);
        if (!st.Any) { UiSounds.Error(); return; }
        var next = NextFireState(st.State, dir);
        ArmyCommands.Send(ctx, selected, (army, units) => new SetFireStateCommand(army, units, next));
        UiSounds.Confirm();
    }

    public static string FireStateName(FireState s) => s switch
    {
        FireState.ReturnFire => "Return fire",
        FireState.HoldFire => "Hold fire",
        _ => "Fire at will",
    };

    /// <summary>Short button label.</summary>
    public static string FireStateShort(FireState s) => s switch
    {
        FireState.ReturnFire => "Return",
        FireState.HoldFire => "Hold",
        _ => "At will",
    };
}
