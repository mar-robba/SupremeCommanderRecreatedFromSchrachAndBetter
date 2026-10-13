using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// Shared UI for SupCom-style in-place structure upgrades (blueprint <c>UpgradesTo</c>): selection helpers, the
/// upgrade tile used by <see cref="FactoryPanel"/> and <see cref="UpgradePanel"/>, the U hotkey and the tooltip.
/// Talks to the sim only through <see cref="UpgradeCommand"/> / <see cref="CancelUpgradeCommand"/>.
/// </summary>
public static class UpgradeUi
{
    public const KeyboardKey Hotkey = KeyboardKey.U;
    public const string HotkeyName = "U";
    public static readonly Color UpgradeTint = new(56, 82, 60, 255);
    public static readonly Color UpgradeGreen = new(120, 235, 130, 255);

    /// <summary>Own selected structures that can start an upgrade (complete, has UpgradesTo, not upgrading) and the ones
    /// upgrading. factories: true = only factories, false = only non-factories, null = both.</summary>
    public static void Collect(GameContext ctx, IEnumerable<UnitHandle> selected, SimSnapshot snap,
                               List<UnitHandle> ready, List<UnitHandle> upgrading, bool? factories = null)
    {
        ready.Clear();
        upgrading.Clear();
        foreach (var h in selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !ctx.Controls(v.Army)) continue;
            var bp = ctx.Db[v.Bp];
            if (!bp.IsStructure) continue;
            if (factories is bool f && bp.Has(UnitCategory.Factory) != f) continue;
            if (v.UpgradeBp >= 0) upgrading.Add(h);
            else if (bp.UpgradesToIndex >= 0 && (v.Flags & UnitFlags.UnderConstruction) == 0) ready.Add(h);
        }
    }

    public static void Start(GameContext ctx, List<UnitHandle> ready)
    {
        if (ready.Count == 0) { UiSounds.Error(); return; }
        ArmyCommands.Send(ctx, ready, (army, units) => new UpgradeCommand(army, units));
        UiSounds.Confirm();
    }

    public static void Cancel(GameContext ctx, List<UnitHandle> upgrading)
    {
        if (upgrading.Count == 0) return;
        ArmyCommands.Send(ctx, upgrading, (army, units) => new CancelUpgradeCommand(army, units));
        UiSounds.Confirm();
    }

    /// <summary>U: upgrade every selected own structure that can (any panel). Call once per frame, not in dialogs.</summary>
    public static void HandleHotkey(GameContext ctx, IEnumerable<UnitHandle> selected, SimSnapshot snap)
    {
        if (Ui.Ctrl || Ui.Alt || !Raylib.IsKeyPressed(Hotkey)) return;
        var ready = new List<UnitHandle>();
        var upgrading = new List<UnitHandle>();
        Collect(ctx, selected, snap, ready, upgrading);
        if (ready.Count > 0) Start(ctx, ready);
    }

    /// <summary>Average progress of the upgrading units (0 if none).</summary>
    public static float AverageProgress(SimSnapshot snap, List<UnitHandle> upgrading)
    {
        float sum = 0;
        int n = 0;
        foreach (var h in upgrading)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && v.UpgradeBp >= 0) { sum += v.UpgradeFrac; n++; }
        return n > 0 ? sum / n : 0;
    }

    /// <summary>(from, to) blueprints shown by the tile: the first ready structure, else the first upgrading one.</summary>
    public static (UnitBlueprint from, UnitBlueprint to)? Target(GameContext ctx, SimSnapshot snap, List<UnitHandle> ready, List<UnitHandle> upgrading)
    {
        foreach (var h in ready)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v)) { var b = ctx.Db[v.Bp]; return (b, ctx.Db[b.UpgradesToIndex]); }
        foreach (var h in upgrading)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && v.UpgradeBp >= 0) return (ctx.Db[v.Bp], ctx.Db[v.UpgradeBp]);
        return null;
    }

    /// <summary>
    /// Upgrade tile: the target blueprint's icon with a green up-arrow, U badge and the progress when upgrading.
    /// Left click (or U): upgrade the ready structures · right click: cancel the running upgrades (refund).
    /// </summary>
    public static void DrawTile(Rectangle r, GameContext ctx, SimSnapshot snap, List<UnitHandle> ready, List<UnitHandle> upgrading)
    {
        if (Target(ctx, snap, ready, upgrading) is not var (from, to)) return;
        bool busy = upgrading.Count > 0;
        var st = Ui.Button(r, true, busy && ready.Count == 0, UpgradeTint);
        UnitIcons.Draw(r, to);
        // Up-arrow badge (bottom-left of the glyph area).
        float s = Ui.P(7);
        var c = new Vector2(r.X + Ui.P(10), r.Y + r.Height * 0.5f);
        Raylib.DrawTriangle(c + new Vector2(0, -s), c + new Vector2(-s, 0), c + new Vector2(s, 0), UpgradeGreen);
        Raylib.DrawRectangleV(c + new Vector2(-s * 0.4f, 0), new Vector2(s * 0.8f, s * 0.9f), UpgradeGreen);
        Ui.HotkeyBadge(r, HotkeyName);
        float avg = AverageProgress(snap, upgrading);
        if (busy) Ui.Bar(new Rectangle(r.X + 2, r.Y + r.Height - Ui.P(7) - 2, r.Width - 4, Ui.P(7)), avg, UpgradeGreen);
        if (st.Left && ready.Count > 0) Start(ctx, ready);
        if (st.Right && busy) Cancel(ctx, upgrading);
        if (st.Hover) Ui.Tooltip(r, TooltipLines(from, to, ready.Count, upgrading.Count, avg));
    }

    /// <summary>Cost (of the target blueprint), time at the structure's own build power, what changes.</summary>
    public static List<TipLine> TooltipLines(UnitBlueprint from, UnitBlueprint to, int readyCount, int upgradingCount, float avgProgress)
    {
        var e = to.Economy;
        float rate = UpgradeSystem.SelfRate(from);
        var lines = new List<TipLine>
        {
            new($"Upgrade to {to.Name}  ·  T{to.Tech}   [{HotkeyName}]", Ui.TextColor, 14, true),
            new("Mass ", Ui.TextDim, 12.5f, true, [
                (Ui.Num(e.BuildCostMass), Ui.MassColor),
                ("    Energy ", Ui.TextDim),
                (Ui.Num(e.BuildCostEnergy), Ui.EnergyColor),
                ("    Time ", Ui.TextDim),
                (Ui.Num(e.BuildTime), Ui.TextColor),
            ]),
            new($"≈ {Ui.Seconds(e.BuildTime / rate)} with its own build power {Ui.Num(rate)} · engineers can assist (Guard)", Ui.TextDim, 11.5f),
        };
        foreach (var d in StatChanges(from, to)) lines.Add(new TipLine(d, new Color(170, 230, 175, 255), 11.5f));
        lines.Add(new TipLine(from.Has(UnitCategory.Factory)
            ? "Production pauses while upgrading (the queue is kept and resumes afterwards)."
            : "Keeps working at its current level while upgrading.", Ui.TextDim, 11.5f));
        if (upgradingCount > 0)
            lines.Add(new TipLine($"Upgrading: {avgProgress * 100:0}%{(upgradingCount > 1 ? $" (avg of {upgradingCount})" : "")}  ·  right-click: cancel (refund)", UpgradeGreen, 11.5f));
        lines.Add(new TipLine(readyCount > 0
            ? $"Click or {HotkeyName}: upgrade {(readyCount > 1 ? $"{readyCount} structures" : "")}".TrimEnd()
            : "Already upgrading", Ui.TextFaint, 11));
        return lines;
    }

    /// <summary>Human-readable stat changes between two blueprints ("Mass +2 → +6/s", "HP 600 → 1500", ...).</summary>
    public static List<string> StatChanges(UnitBlueprint a, UnitBlueprint b)
    {
        var l = new List<string>();
        void Add(string name, float x, float y, string fmt = "{0}")
        {
            if (MathF.Abs(x - y) < 1e-3f) return;
            l.Add($"{name} {string.Format(fmt, Ui.Num(x))} → {string.Format(fmt, Ui.Num(y))}");
        }
        Add("HP", a.MaxHealth, b.MaxHealth);
        Add("Mass", a.Economy.ProductionMass, b.Economy.ProductionMass, "+{0}/s");
        Add("Energy", a.Economy.ProductionEnergy, b.Economy.ProductionEnergy, "+{0}/s");
        Add("Upkeep", a.Economy.MaintenanceEnergy, b.Economy.MaintenanceEnergy, "-{0} E/s");
        Add("Build power", a.Economy.BuildRate, b.Economy.BuildRate);
        Add("Radar", a.Intel.RadarRadius, b.Intel.RadarRadius);
        Add("Omni", a.Intel.OmniRadius, b.Intel.OmniRadius);
        Add("DPS", UnitIcons.Dps(a), UnitIcons.Dps(b));
        int newUnits = b.BuildIndices.Count(x => Array.IndexOf(a.BuildIndices, x) < 0);
        if (newUnits > 0) l.Add($"+{newUnits} new unit types (T{b.Tech})");
        return l;
    }
}
