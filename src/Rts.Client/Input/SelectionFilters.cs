using Raylib_cs;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Client.Input;

/// <summary>Which units a selection key picks (see <see cref="SelectionFilters.Matches"/>).</summary>
public enum SelectionFilter { SameTypes, Land, Air, Naval, Engineers, Combat }

/// <summary>
/// (7b, G14) Selection keys: every unit of the selected types (Ctrl+Z on screen, Ctrl+Shift+Z on the map), all
/// combat units (Ctrl+A), all land / air / naval units / engineers (Ctrl+Shift+L / A / N / E) and the commander
/// (Ctrl+C: select + center the camera). Selection only: works in replays too. The rules are pure (unit-tested).
/// </summary>
public static class SelectionFilters
{
    /// <summary>Key of a (army, blueprint) pair for the "same types" filter.</summary>
    public static long TypeKey(int army, int bp) => ((long)army << 32) | (uint)bp;

    /// <summary>
    /// Does a unit of blueprint <paramref name="bp"/> owned by <paramref name="army"/> match the filter?
    /// SameTypes: its (army, blueprint) is in <paramref name="selectedTypes"/> (structures too, like a double-click).
    /// The others: the local army's mobile units only. Land / Air / Naval = that domain without engineers and the
    /// commander; Combat = any domain, armed; Engineers = engineers without the commander.
    /// </summary>
    public static bool Matches(SelectionFilter f, UnitBlueprint bp, int army, int localArmy, IReadOnlySet<long>? selectedTypes = null)
    {
        if (f == SelectionFilter.SameTypes) return selectedTypes != null && selectedTypes.Contains(TypeKey(army, bp.Index));
        if (army != localArmy || !bp.IsMobile || bp.Has(UnitCategory.Commander)) return false;
        bool engineer = bp.Has(UnitCategory.Engineer);
        return f switch
        {
            SelectionFilter.Engineers => engineer,
            SelectionFilter.Land => !engineer && bp.Has(UnitCategory.Land),
            SelectionFilter.Air => !engineer && bp.Has(UnitCategory.Air),
            SelectionFilter.Naval => !engineer && bp.Has(UnitCategory.Naval),
            SelectionFilter.Combat => !engineer && bp.Weapons.Length > 0,
            _ => false,
        };
    }

    /// <summary>The local army's commander(s).</summary>
    public static bool IsCommander(UnitBlueprint bp, int army, int localArmy) =>
        army == localArmy && bp.Has(UnitCategory.Commander);

    /// <summary>Called once per frame by <see cref="SelectionController"/> (not while a dialog blocks the input).</summary>
    public static void HandleHotkeys(GameContext ctx, SelectionController sel, bool shift)
    {
        // Hotkey.Pressed() leaves an undeclared Shift free, so Ctrl+Z / Ctrl+A would also fire on Ctrl+Shift+Z / A:
        // the Shift entries are checked first and the plain ones only without Shift.
        if (shift)
        {
            if (Hotkeys.SelectSameTypesMap.Pressed()) Apply(ctx, sel, SelectionFilter.SameTypes, onScreen: false);
            else
            {
                int d = Hotkeys.SelectDomain.PressedIndex();
                if (d >= 0) Apply(ctx, sel, DomainKeys[d], onScreen: false);
            }
        }
        else
        {
            if (Hotkeys.SelectSameTypes.Pressed()) Apply(ctx, sel, SelectionFilter.SameTypes, onScreen: true);
            else if (Hotkeys.SelectCombat.Pressed()) Apply(ctx, sel, SelectionFilter.Combat, onScreen: false);
        }
        if (Hotkeys.SelectCommander.Pressed()) SelectCommander(ctx, sel, add: shift);
    }

    /// <summary>Filters of <see cref="Hotkeys.SelectDomain"/>'s keys, in key order (L A N E).</summary>
    private static readonly SelectionFilter[] DomainKeys =
        [SelectionFilter.Land, SelectionFilter.Air, SelectionFilter.Naval, SelectionFilter.Engineers];

    /// <summary>
    /// Applies a filter over the snapshot. SameTypes adds to the selection (the selected units stay selected);
    /// the class filters replace it, and do nothing when no unit matches. Returns the number of units picked.
    /// </summary>
    public static int Apply(GameContext ctx, SelectionController sel, SelectionFilter f, bool onScreen)
    {
        var snap = ctx.Snapshots.Current;
        HashSet<long>? types = null;
        if (f == SelectionFilter.SameTypes)
        {
            types = new HashSet<long>();
            foreach (var h in sel.Selected)
                if (snap.TryGetUnit(h.Index, h.Generation, out var v)) types.Add(TypeKey(v.Army, v.Bp));
            if (types.Count == 0) return 0;
        }
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        var picked = new List<UnitHandle>();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var u = ref snap.Units[k];
            if (!Matches(f, ctx.Db[u.Bp], u.Army, ctx.LocalArmy, types)) continue;
            // Class filters skip units still being built (in the factory); "same types" behaves like a double-click.
            if (f != SelectionFilter.SameTypes && (u.Flags & UnitFlags.UnderConstruction) != 0) continue;
            if (onScreen)
            {
                var sp = Raylib.GetWorldToScreen(u.Pos, ctx.Camera.Camera);
                if (sp.X < 0 || sp.Y < 0 || sp.X > sw || sp.Y > sh) continue;
            }
            picked.Add(new UnitHandle(u.Index, u.Gen));
        }
        if (picked.Count == 0) return 0;
        if (f == SelectionFilter.SameTypes) AddTo(sel, picked);
        else Replace(sel, picked);
        return picked.Count;
    }

    /// <summary>Selects the local army's commander(s) (Shift: adds them) and centers the camera on them; with the
    /// commander already selected it still centers.</summary>
    public static bool SelectCommander(GameContext ctx, SelectionController sel, bool add)
    {
        var snap = ctx.Snapshots.Current;
        var picked = new List<UnitHandle>();
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var u = ref snap.Units[k];
            if (IsCommander(ctx.Db[u.Bp], u.Army, ctx.LocalArmy)) picked.Add(new UnitHandle(u.Index, u.Gen));
        }
        if (picked.Count == 0) return false;
        if (add) AddTo(sel, picked);
        else Replace(sel, picked);
        sel.CenterOn(picked);
        return true;
    }

    private static void Replace(SelectionController sel, List<UnitHandle> units)
    {
        // A new selection: leave the order mode / structure placement of the old one.
        sel.Placement.Cancel();
        sel.Mode = SelectionController.CommandMode.Normal;
        sel.Selected.Clear();
        sel.Selected.AddRange(units); // snapshot units are unique
    }

    private static void AddTo(SelectionController sel, List<UnitHandle> units)
    {
        var have = new HashSet<UnitHandle>(sel.Selected);
        foreach (var h in units)
            if (have.Add(h)) sel.Selected.Add(h);
    }
}
