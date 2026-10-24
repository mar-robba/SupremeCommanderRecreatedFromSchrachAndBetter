using Raylib_cs;
using Rts.Client.Audio;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>
/// ACU Overcharge toggle (<see cref="OverchargeHotkey"/>, orders panel button): arms / disarms the Overcharge mode of
/// the selected commanders (<see cref="SetOverchargeCommand"/>, armed = <see cref="UnitFlags.Overcharge"/>). While
/// armed the ACU fires an Overcharge shot whenever its army stores at least MinEnergy (it takes up to MaxEnergy;
/// damage = energy × DamagePerEnergy) until it is turned off. The button shows "not ready" while the stored energy of
/// an armed army is below MinEnergy (the reload is not in the snapshot).
/// </summary>
public static partial class UnitToggles
{
    public static KeyboardKey OverchargeHotkey => Hotkeys.Overcharge.Key;
    public static string OverchargeKeyName => Hotkeys.Overcharge.Label;

    /// <summary>Overcharge state of a selection. Count = controllable units with an Overcharge weapon, On = how many
    /// are armed, Ready = some of their armies store at least <see cref="Rule"/>.MinEnergy, Stored = the highest
    /// stored energy among those armies, Rule = the Overcharge data of the first such unit (null when Count is 0).</summary>
    public readonly record struct OverchargeSelection(int Count, int On, bool Ready, float Stored, OverchargeBlueprint? Rule)
    {
        public bool Any => Count > 0;
        public bool AllOn => Count > 0 && On == Count;
    }

    /// <summary>The Overcharge mode of a blueprint's weapons (the first weapon that has one), null = none.</summary>
    public static OverchargeBlueprint? OverchargeOf(UnitBlueprint bp)
    {
        foreach (var w in bp.Weapons)
            if (w.Overcharge != null) return w.Overcharge;
        return null;
    }

    /// <summary>Pure fold over the selection: one entry per controllable, complete unit (its Overcharge data or null,
    /// armed flag, its army's stored energy).</summary>
    public static OverchargeSelection EvaluateOvercharge(IEnumerable<(OverchargeBlueprint? Oc, bool Armed, float Stored)> units)
    {
        int n = 0, on = 0;
        bool ready = false;
        float stored = 0;
        OverchargeBlueprint? rule = null;
        foreach (var (oc, armed, st) in units)
        {
            if (oc == null) continue;
            rule ??= oc;
            n++;
            if (armed) on++;
            if (st >= oc.MinEnergy) ready = true;
            stored = MathF.Max(stored, st);
        }
        return new OverchargeSelection(n, on, ready, stored, rule);
    }

    /// <summary>Overcharge state of the selected units (see <see cref="EvaluateOvercharge"/>).</summary>
    public static OverchargeSelection OverchargeState(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var snap = ctx.Snapshots.Current;
        return EvaluateOvercharge(Entries(ctx, snap, selected));
    }

    private static IEnumerable<(OverchargeBlueprint?, bool, float)> Entries(GameContext ctx, SimSnapshot snap, IReadOnlyList<UnitHandle> selected)
    {
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !ctx.Controls(v.Army)) continue;
            if ((v.Flags & UnitFlags.UnderConstruction) != 0) continue;
            float stored = (uint)v.Army < (uint)snap.Armies.Length ? snap.Armies[v.Army].Energy : 0f;
            yield return (OverchargeOf(ctx.Db[v.Bp]), (v.Flags & UnitFlags.Overcharge) != 0, stored);
        }
    }

    /// <summary>Tooltip body of the Overcharge button: the rule, then the current state.</summary>
    public static string OverchargeTooltip(in OverchargeSelection s)
    {
        var oc = s.Rule ?? new OverchargeBlueprint();
        string rule = $"Commander shots spend stored energy: at least {oc.MinEnergy:0}, up to {oc.MaxEnergy:0} per shot; damage = "
                      + $"energy{(oc.DamagePerEnergy == 1 ? "" : $" × {oc.DamagePerEnergy:0.##}")} in a {oc.DamageRadius:0.#} radius "
                      + $"(structures × {oc.StructureDamageMult:0.##}, commanders at most {oc.CommanderMaxDamage:0}), every {oc.Reload:0.#} s. "
                      + "Stays on (fires by itself) until turned off.";
        string state = !s.Ready ? $"Not ready: needs {oc.MinEnergy:0} energy (stored {s.Stored:0})."
            : s.AllOn ? $"Armed: next shot {MathF.Min(s.Stored, oc.MaxEnergy) * oc.DamagePerEnergy:0} damage."
            : s.On > 0 ? $"Armed on {s.On} of {s.Count}." : "Ready.";
        return rule + "\n" + state;
    }

    /// <summary>Overcharge toggle: arms every selected ACU if any is disarmed, else disarms all.</summary>
    public static void ToggleOvercharge(GameContext ctx, IReadOnlyList<UnitHandle> selected)
    {
        var st = OverchargeState(ctx, selected);
        if (!st.Any) { UiSounds.Error(); return; }
        bool on = !st.AllOn;
        ArmyCommands.Send(ctx, selected, (army, units) => new SetOverchargeCommand(army, units, on));
        UiSounds.Confirm();
    }

    /// <summary>The Overcharge hotkey (called from <see cref="HandleHotkeys"/>).</summary>
    private static void OverchargeHotkeys(GameContext ctx, SelectionController sel)
    {
        if (Hotkeys.Overcharge.Pressed() && OverchargeState(ctx, sel.Selected).Any) ToggleOvercharge(ctx, sel.Selected);
    }
}
