using System.Numerics;
using Raylib_cs;
using Rts.Client.Audio;
using Rts.Client.Input;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// Commander / SCU enhancement strip, drawn just above the build panel when the selection has own enhanceable units
/// (all of the same base blueprint as the first one). One column per slot (left arm, right arm, back) with a button
/// per enhancement: installed (highlighted), available (click: the selected units that can install it start building
/// it on themselves), swap (amber: the slot holds another enhancement or a prerequisite is missing; click: remove the
/// slot and install the whole chain, see <see cref="EnhancementChain"/>), locked (greyed: busy). Right-click an installed one: remove that slot
/// (instant, no refund); right-click the one being built: cancel (refund). The right part shows the progress and a
/// Cancel button while enhancing. Reads snapshots, writes commands only.
/// </summary>
public sealed class EnhancementPanel
{
    /// <summary>Color of enhancement progress (here and in <see cref="UnitPanel"/>).</summary>
    public static readonly Color EnhanceColor = new(90, 215, 220, 255);
    private static readonly Color InstalledTint = new(38, 92, 84, 255);
    private static readonly Color BuildingTint = new(40, 70, 104, 255);

    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly List<UnitHandle> _units = new();
    private readonly List<UnitHandle> _enhancing = new();
    private readonly List<UnitHandle> _tmp = new();
    private readonly List<int>[] _slots = [new(), new(), new()];
    private UnitBlueprint? _base;
    private readonly EnhancementChain _chain = new(); // (10b-2) queued steps of one-click swaps / chains
    private SimSnapshot? _lookupSnap;
    private readonly EnhancementChain.UnitLookup _lookup;
    private readonly Action<Command> _send;
    private static readonly Color SwapText = new(240, 200, 120, 255);
    private Rectangle _r;
    private bool _visible;

    public EnhancementPanel(GameContext ctx, SelectionController sel)
    {
        _lookup = LookupUnit;
        _send = c => _ctx.Runner.Enqueue(c);
        _ctx = ctx;
        _sel = sel;
    }

    private float ButtonSize => Ui.P(40);
    private float Gap => Ui.P(4);
    private float Pad => Ui.P(8);
    private float ColGap => Ui.P(14);
    private float StatusWidth => Ui.P(200);
    private float Height => Ui.P(5) + Ui.F(11) + Ui.P(4) + Ui.F(10) + Ui.P(3) + ButtonSize + Ui.P(8);

    /// <summary>Is the strip shown this frame, and where (valid after <see cref="Layout"/>).</summary>
    public bool Visible => _visible;
    public Rectangle Rect => _visible ? _r : default;

    /// <summary>True if the strip is shown and contains <paramref name="m"/> (the HUD blocks world input there).</summary>
    public bool Hit(Vector2 m) => _visible && Raylib.CheckCollisionPointRec(m, _r);

    /// <summary>
    /// Recomputes the selection and the strip rectangle: left edge <paramref name="x"/> (clamped so it fits before
    /// <paramref name="right"/>), bottom edge <paramref name="bottom"/> (the top of the bottom panel row minus a gap).
    /// </summary>
    public void Layout(SimSnapshot snap, bool show, float x, float bottom, float right)
    {
        if (!_ctx.ReadOnly && _chain.Count > 0) { _lookupSnap = snap; _chain.Update(_lookup, _ctx.LocalArmy, snap.Tick, _send); }
        _visible = show && Refresh(snap);
        if (!_visible) return;
        float w = DesiredWidth();
        x = MathF.Max(Ui.P(4), MathF.Min(x, right - w));
        _r = Ui.R(x, bottom - Height, MathF.Min(w, right - x), Height);
    }

    private bool Refresh(SimSnapshot snap)
    {
        _units.Clear();
        _enhancing.Clear();
        _base = null;
        int baseIdx = -1;
        foreach (var h in _sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || v.Army != _ctx.LocalArmy) continue;
            if ((v.Flags & UnitFlags.UnderConstruction) != 0) continue;
            var bp = _ctx.Db[v.Bp];
            if (!bp.IsEnhanceable || bp.Enhancements.Length == 0) continue;
            if (baseIdx < 0) baseIdx = bp.EnhancementBase;
            else if (bp.EnhancementBase != baseIdx) continue;
            _units.Add(h);
            if (EnhancingIndex(v) >= 0) _enhancing.Add(h);
        }
        if (baseIdx < 0) return false;
        _base = _ctx.Db[baseIdx];
        foreach (var l in _slots) l.Clear();
        for (int e = 0; e < _base.Enhancements.Length; e++)
        {
            int s = (int)_base.Enhancements[e].Slot;
            if ((uint)s < (uint)_slots.Length) _slots[s].Add(e);
        }
        return true;
    }

    /// <summary>Enhancement being built by this unit view, or -1 (not upgrading, or a normal structure upgrade).</summary>
    private int EnhancingIndex(in UnitView v) =>
        v.UpgradeBp >= 0 ? EnhancementCatalog.Added(_ctx.Db, v.Bp, v.UpgradeBp) : -1;

    private float ColumnWidth(int slot)
    {
        int n = _slots[slot].Count;
        return MathF.Max(n * (ButtonSize + Gap) - Gap, Ui.Measure(EnhancementSlots.Name((EnhancementSlot)slot), Ui.F(10)));
    }

    private float DesiredWidth()
    {
        float w = 2 * Pad + StatusWidth;
        for (int s = 0; s < _slots.Length; s++)
            if (_slots[s].Count > 0) w += ColumnWidth(s) + ColGap;
        return w;
    }

    /// <summary>Installed enhancements of a blueprint (the top of each slot's chain), e.g. "T3E · HAC · SGF"; null if none.</summary>
    public static string? InstalledSummary(UnitBlueprint bp)
    {
        if (!bp.IsEnhanceable || bp.Installed.Length == 0) return null;
        string? s = null;
        foreach (int e in bp.Installed)
        {
            if ((uint)e >= (uint)bp.Enhancements.Length) continue;
            var x = bp.Enhancements[e];
            string label = x.Icon.Length > 0 ? x.Icon : x.Name;
            s = s == null ? label : s + " · " + label;
        }
        return s;
    }

    // ------------------------------------------------------------------ drawing / input

    public void Draw(SimSnapshot snap)
    {
        if (!_visible || _base == null) return;
        var r = _r;
        Ui.Panel(r);
        int n = _units.Count;
        float y = Ui.PanelTitle(r, n > 1 ? $"Enhancements  ·  {n} units" : "Enhancements");
        float x = r.X + Pad;
        float bs = ButtonSize;
        int lfs = Ui.F(10);
        for (int s = 0; s < _slots.Length; s++)
        {
            if (_slots[s].Count == 0) continue;
            float cw = ColumnWidth(s);
            if (x + cw > r.X + r.Width - Pad) break;
            Ui.Text(EnhancementSlots.Name((EnhancementSlot)s), x, y, lfs, Ui.TextFaint, true);
            float by = y + lfs + Ui.P(3);
            for (int k = 0; k < _slots[s].Count; k++)
                DrawButton(new Rectangle(MathF.Round(x + k * (bs + Gap)), MathF.Round(by), bs, bs), _slots[s][k], snap);
            x += cw + ColGap;
        }
        float sx = x - ColGap * 0.5f;
        Raylib.DrawLineEx(new Vector2(sx, r.Y + Ui.P(8)), new Vector2(sx, r.Y + r.Height - Ui.P(8)), 1, new Color(60, 76, 94, 255));
        DrawStatus(new Rectangle(x, y, r.X + r.Width - Pad - x, r.Y + r.Height - Ui.P(8) - y), snap);
    }

    private bool LookupUnit(UnitHandle h, out UnitBlueprint bp, out bool busy)
    {
        bp = null!;
        busy = false;
        if (_lookupSnap == null || !_lookupSnap.TryGetUnit(h.Index, h.Generation, out var v) || v.Army != _ctx.LocalArmy) return false;
        bp = _ctx.Db[v.Bp];
        busy = v.UpgradeBp >= 0;
        return true;
    }

    private enum State { Locked, Available, Installed, Building, Swap }

    private void DrawButton(Rectangle br, int e, SimSnapshot snap)
    {
        var db = _ctx.Db;
        var x = _base!.Enhancements[e];
        int nInst = 0, nCan = 0, nBuild = 0, nPlan = 0;
        float frac = 0;
        foreach (var h in _units)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            var cur = db[v.Bp];
            int building = EnhancingIndex(v);
            if (building == e) { nBuild++; frac += v.UpgradeFrac; }
            if (EnhancementCatalog.HasInstalled(cur, e)) nInst++;
            else if (building < 0 && v.UpgradeBp < 0) { if (EnhancementCatalog.CanInstall(cur, e)) nCan++; else nPlan++; }
        }
        var state = nBuild > 0 ? State.Building : nCan > 0 ? State.Available : nPlan > 0 ? State.Swap : nInst > 0 ? State.Installed : State.Locked;
        if (nBuild > 0) frac /= nBuild;

        bool locked = state == State.Locked;
        var tint = state switch { State.Installed => InstalledTint, State.Building => BuildingTint, _ => (Color?)null };
        var st = Ui.Button(br, !locked, state is State.Installed or State.Building || (nInst > 0 && state == State.Available), tint);
        var textCol = state switch
        {
            State.Installed => new Color(150, 245, 225, 255),
            State.Building => EnhanceColor,
            State.Available => Ui.TextColor,
            State.Swap => SwapText,
            _ => Ui.TextFaint,
        };
        string icon = x.Icon.Length > 0 ? x.Icon : x.Name[..Math.Min(3, x.Name.Length)];
        Ui.TextCentered(icon, br, Ui.F(icon.Length > 3 ? 11 : 12.5f), textCol, true);
        if (state == State.Installed)
        {
            // Small "installed" corner mark.
            float c = Ui.P(7);
            Raylib.DrawTriangle(new Vector2(br.X + br.Width - c - 2, br.Y + 2), new Vector2(br.X + br.Width - 2, br.Y + c + 2),
                new Vector2(br.X + br.Width - 2, br.Y + 2), new Color(110, 230, 190, 255));
        }
        if (state == State.Building)
        {
            float bh = Ui.P(4);
            var pr = new Rectangle(br.X + 2, br.Y + br.Height - bh - 2, br.Width - 4, bh);
            Raylib.DrawRectangleRec(pr, new Color(8, 10, 14, 230));
            Raylib.DrawRectangleRec(new Rectangle(pr.X, pr.Y, MathF.Max(1, pr.Width * Math.Clamp(frac, 0, 1)), bh), EnhanceColor);
        }
        if (_units.Count > 1 && nInst > 0 && nInst < _units.Count)
        {
            string badge = $"{nInst}/{_units.Count}";
            Ui.Text(badge, br.X + Ui.P(3), br.Y + br.Height - Ui.F(9) - Ui.P(2), Ui.F(9), Ui.TextDim, true);
        }

        // Input.
        if (st.Left)
        {
            if (state is State.Available or State.Swap) Enhance(snap, e);
            else UiSounds.Error();
        }
        else if (locked && Ui.LeftClickIn(br)) UiSounds.Error();
        if (st.Right)
        {
            if (state == State.Building) CancelBuilding(snap, e);
            else if (nInst > 0) Remove(snap, e);
        }
        if (st.Hover || (locked && Ui.Hover(br))) Ui.Tooltip(br, TooltipLines(snap, e, state, nInst, nCan + nPlan, frac));
    }

    private void Enhance(SimSnapshot snap, int e)
    {
        // Units that can install it now get one shared command; the others (slot taken, prerequisite missing) get a
        // per-unit plan: remove + the chain's first step now, the rest later (EnhancementChain).
        _tmp.Clear();
        bool any = false;
        foreach (var h in _units)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || v.UpgradeBp >= 0) continue;
            var cur = _ctx.Db[v.Bp];
            if (EnhancementCatalog.CanInstall(cur, e)) { _chain.Drop(h); _tmp.Add(h); }
            else any |= _chain.Begin(h, cur, e, _ctx.LocalArmy, snap.Tick, _send);
        }
        if (_tmp.Count > 0) _ctx.Runner.Enqueue(new EnhanceCommand(_ctx.LocalArmy, _tmp.ToArray(), _base!.Enhancements[e].Id));
        if (_tmp.Count > 0 || any) UiSounds.Confirm();
    }

    private void Remove(SimSnapshot snap, int e)
    {
        _tmp.Clear();
        foreach (var h in _units)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && v.UpgradeBp < 0 && EnhancementCatalog.HasInstalled(_ctx.Db[v.Bp], e))
                _tmp.Add(h);
        if (_tmp.Count == 0) { UiSounds.Error(); return; }
        _ctx.Runner.Enqueue(new RemoveEnhancementCommand(_ctx.LocalArmy, _tmp.ToArray(), _base!.Enhancements[e].Slot));
        UiSounds.Confirm();
    }

    private void CancelBuilding(SimSnapshot snap, int e)
    {
        _tmp.Clear();
        foreach (var h in _enhancing)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && EnhancingIndex(v) == e) { _tmp.Add(h); _chain.Drop(h); }
        if (_tmp.Count == 0) return;
        _ctx.Runner.Enqueue(new CancelUpgradeCommand(_ctx.LocalArmy, _tmp.ToArray()));
        UiSounds.Confirm();
    }

    private List<TipLine> TooltipLines(SimSnapshot snap, int e, State state, int nInst, int nCan, float frac)
    {
        var db = _ctx.Db;
        var x = _base!.Enhancements[e];
        // Representative unit: the first that does not have it yet (for the lock reason), else the first.
        UnitBlueprint rep = _base;
        bool repBusy = false, found = false;
        foreach (var h in _units)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            var cur = db[v.Bp];
            if (!found || !EnhancementCatalog.HasInstalled(cur, e) && EnhancementCatalog.HasInstalled(rep, e))
            {
                rep = cur;
                repBusy = v.UpgradeBp >= 0;
                found = true;
            }
        }
        float rate = rep.Economy.BuildRate > 0 ? rep.Economy.BuildRate : 1;
        var lines = new List<TipLine>
        {
            new($"{x.Name}   [{(x.Icon.Length > 0 ? x.Icon : "-")}]", Ui.TextColor, 14, true),
            new($"{EnhancementSlots.Name(x.Slot)} slot", Ui.TextFaint, 11),
            new("Mass ", Ui.TextDim, 12.5f, true, [
                (Ui.Num(x.BuildCostMass), Ui.MassColor),
                ("    Energy ", Ui.TextDim),
                (Ui.Num(x.BuildCostEnergy), Ui.EnergyColor),
                ("    Time ", Ui.TextDim),
                ($"≈ {Ui.Seconds(x.BuildTime / rate)}", Ui.TextColor),
            ]),
            new($"Build time {Ui.Num(x.BuildTime)} at build power {Ui.Num(rate)} (engineers can assist)", Ui.TextFaint, 11),
        };
        if (!string.IsNullOrWhiteSpace(x.Description)) lines.Add(new TipLine(x.Description, new Color(200, 215, 230, 255), 12));
        if (x.PrerequisiteIndex >= 0)
        {
            var pre = _base.Enhancements[x.PrerequisiteIndex];
            bool has = EnhancementCatalog.HasInstalled(rep, x.PrerequisiteIndex);
            lines.Add(new TipLine($"Requires: {pre.Name}" + (has ? "  (installed)" : ""), has ? Ui.Good : Ui.Warn, 12));
        }
        lines.Add(new TipLine("", Ui.TextDim, 6));
        switch (state)
        {
            case State.Building:
                lines.Add(new TipLine($"Enhancing… {frac * 100:0}%", EnhanceColor, 12, true));
                lines.Add(new TipLine($"Right-click or Stop [{Hotkeys.Stop.Label}]: cancel (refunds what was spent)", Ui.TextFaint, 11));
                break;
            case State.Available:
                lines.Add(new TipLine(_units.Count > 1 ? $"Click: install on {nCan} of {_units.Count} units" : "Click: install (the unit stops and builds it on itself)", Ui.Good, 12, true));
                if (nInst > 0) lines.Add(new TipLine($"Installed on {nInst} · right-click: remove", Ui.TextFaint, 11));
                break;
            case State.Swap:
            {
                var plan = EnhancementChain.Plan(rep, e, out bool remove);
                if (plan.Length > 0)
                    lines.Add(new TipLine("Click: " + EnhancementChain.Describe(rep, plan, remove), SwapText, 12, true));
                if (_units.Count > 1) lines.Add(new TipLine($"On {nCan} of {_units.Count} units", Ui.TextFaint, 11));
                if (nInst > 0) lines.Add(new TipLine($"Installed on {nInst} · right-click: remove", Ui.TextFaint, 11));
                break;
            }
            case State.Installed:
                lines.Add(new TipLine(_units.Count > 1 && nInst < _units.Count ? $"Installed on {nInst} of {_units.Count} units" : "Installed", new Color(150, 245, 225, 255), 12, true));
                lines.Add(new TipLine($"Right-click: remove everything in the {EnhancementSlots.Name(x.Slot).ToLowerInvariant()} slot (instant, no refund)", Ui.TextFaint, 11));
                break;
            default:
                foreach (var why in LockReasons(rep, e, repBusy)) lines.Add(new TipLine(why, Ui.Bad, 12, true));
                break;
        }
        return lines;
    }

    private IEnumerable<string> LockReasons(UnitBlueprint rep, int e, bool busy)
    {
        var x = _base!.Enhancements[e];
        if (busy) { yield return "Busy: already enhancing or upgrading (cancel it first)"; yield break; }
        if (!rep.IsEnhanceable || rep.Installed.Length <= (int)x.Slot) yield break;
        int inSlot = rep.Installed[(int)x.Slot];
        if (inSlot >= 0 && inSlot != x.PrerequisiteIndex && (uint)inSlot < (uint)rep.Enhancements.Length)
            yield return $"Remove {rep.Enhancements[inSlot].Name} first (right-click it)";
        if (x.PrerequisiteIndex >= 0 && !EnhancementCatalog.HasInstalled(rep, x.PrerequisiteIndex))
            yield return $"Requires {_base.Enhancements[x.PrerequisiteIndex].Name}";
    }

    /// <summary>Right part: progress + Cancel while enhancing, else a short usage hint.</summary>
    private void DrawStatus(Rectangle r, SimSnapshot snap)
    {
        int fs = Ui.F(11.5f), sfs = Ui.F(10.5f);
        if (_enhancing.Count > 0)
        {
            float sum = 0;
            int first = -1, cnt = 0;
            bool mixed = false;
            foreach (var h in _enhancing)
            {
                if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
                int e = EnhancingIndex(v);
                if (e < 0) continue;
                if (first < 0) first = e;
                else if (e != first) mixed = true;
                sum += v.UpgradeFrac;
                cnt++;
            }
            if (cnt == 0) return;
            float avg = sum / cnt;
            string what = mixed ? $"{cnt} units" : _base!.Enhancements[first].Name;
            Ui.Text(Ui.Fit($"Enhancing: {what}", r.Width, fs, true), r.X, r.Y, fs, EnhanceColor, true);
            float barH = Ui.P(16), cw = Ui.P(62);
            float by = r.Y + r.Height - barH;
            Ui.Bar(new Rectangle(r.X, by, r.Width - cw - Ui.P(6), barH), avg, EnhanceColor,
                $"{avg * 100:0}%" + (cnt > 1 ? $"  ({cnt})" : ""));
            var cr = new Rectangle(r.X + r.Width - cw, by, cw, barH);
            var st = Ui.Button(cr, true, false, new Color(90, 40, 36, 255));
            Ui.TextCentered("Cancel", cr, Ui.F(11), Ui.TextColor, true);
            if (st.Left)
            {
                foreach (var h in _enhancing) _chain.Drop(h);
                _ctx.Runner.Enqueue(new CancelUpgradeCommand(_ctx.LocalArmy, _enhancing.ToArray()));
                UiSounds.Confirm();
            }
            if (st.Hover) Ui.Tooltip(cr, $"Cancel enhancement  [{Hotkeys.Stop.Label}]", "Stops building the enhancement and refunds what was spent on it.\nStop (or any new order) cancels it too.");
            return;
        }
        Ui.Text("Click: install (swaps the slot)", r.X, r.Y, sfs, Ui.TextDim);
        Ui.Text("Right-click installed: remove", r.X, r.Y + sfs + Ui.P(3), sfs, Ui.TextDim);
        Ui.Text(Ui.Fit("Engineers can assist (G)", r.Width, sfs), r.X, r.Y + 2 * (sfs + Ui.P(3)), sfs, Ui.TextFaint);
    }
}
