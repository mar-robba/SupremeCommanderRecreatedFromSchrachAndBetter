using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// Engineer / commander construction menu: the union of the selected builders' buildable structures, grouped in
/// tabs (Economy, Factory, Defense, Intel). Click or hotkey (Z X C V B N) starts placement; Tab / Shift+Tab
/// switches tab. T2/T3 builders get tier pills (T1/T2/T3/EXP, T / Shift+T cycles, default = highest non-experimental
/// tier) that filter the items so the hotkeys stay on six slots. Tooltips show cost, build time and description.
/// Mobile experimentals (Fatboy) are placed like structures; mobile factories get the factory panel instead.
/// </summary>
public sealed class BuildMenu
{
    public static KeyboardKey[] SlotKeys => Hotkeys.BuildSlots.Keys;
    public static readonly string[] SlotNames = ["Z", "X", "C", "V", "B", "N"];
    /// <summary>Cycles the tech tier filter (T1/T2/T3) in the build menu and factory panel.</summary>
    public static KeyboardKey TierKey => Hotkeys.BuildTier.Key;
    private static readonly BuildTab[] MainTabs = [BuildTab.Economy, BuildTab.Factory, BuildTab.Defense, BuildTab.Intel];

    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private BuildTab _tab = BuildTab.Economy;

    // Per-frame computed options.
    private readonly Dictionary<BuildTab, List<int>> _byTab = new();
    private readonly List<UnitHandle> _builders = new();
    /// <summary>All buildable structures (every tier), the tiers present (ascending) and the selected tier.</summary>
    private readonly List<int> _all = new();
    private readonly List<int> _tiers = new();
    private int _tier, _lastMaxTier;

    public BuildMenu(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
        foreach (BuildTab t in Enum.GetValues<BuildTab>()) _byTab[t] = new List<int>();
    }

    /// <summary>Recomputes builders and options from the selection. Returns true if the menu has content.</summary>
    public bool Refresh(SimSnapshot snap)
    {
        foreach (var l in _byTab.Values) l.Clear();
        _builders.Clear();
        _all.Clear();
        _tiers.Clear();
        foreach (var h in _sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !_ctx.Controls(v.Army)) continue;
            var bp = _ctx.Db[v.Bp];
            // Mobile factories (Fatboy) use the factory panel; everything else they list is a unit, not a placement.
            if (!bp.IsMobile || bp.BuildIndices.Length == 0 || bp.Has(UnitCategory.Factory)) continue;
            _builders.Add(h);
            foreach (int b in bp.BuildIndices)
            {
                // Structures and mobile units built in place (mobile experimentals such as the Fatboy).
                if (_all.Contains(b)) continue;
                var ob = _ctx.Db[b];
                _all.Add(b);
                if (!_tiers.Contains(ob.Tech)) _tiers.Add(ob.Tech);
            }
        }
        if (_builders.Count == 0) return false;
        _tiers.Sort();
        // Default to the highest tier whenever the selection's top tier changes (e.g. a T2 engineer gets selected).
        // (Experimentals are not the default: a T3 engineer opens on T3.)
        int maxTier = _tiers.Count > 0 ? _tiers[^1] : 0;
        if (maxTier != _lastMaxTier || !_tiers.Contains(_tier)) { _tier = DefaultTier(); _lastMaxTier = maxTier; }
        foreach (int b in _all)
        {
            var ob = _ctx.Db[b];
            if (_tiers.Count > 1 && ob.Tech != _tier) continue;
            _byTab[UnitIcons.TabOf(ob)].Add(b);
        }
        if (_byTab[_tab].Count == 0)
            foreach (var t in Tabs()) if (_byTab[t].Count > 0) { _tab = t; break; }
        return Tabs().Any(t => _byTab[t].Count > 0);
    }

    private IEnumerable<BuildTab> Tabs()
    {
        foreach (var t in MainTabs) yield return t;
        if (_byTab[BuildTab.Misc].Count > 0) yield return BuildTab.Misc;
    }

    private float ButtonSize => Ui.P(60);
    private float Gap => Ui.P(5);
    private float Pad => Ui.P(8);

    private float TabWidth(BuildTab t) => Ui.Measure($"{UnitIcons.TabName(t)} {_byTab[t].Count}", Ui.F(11.5f), true) + Ui.P(18);
    private float PillWidth(int tier) => Ui.P(tier >= 4 ? 38 : 30);
    private float PillsWidth => _tiers.Count > 1 ? _tiers.Sum(t => PillWidth(t) + Ui.P(3)) + Ui.P(10) : 0;
    /// <summary>Tier pill label: T1..T3, EXP for experimentals (Tech 4).</summary>
    public static string TierLabel(int tier) => tier >= 4 ? "EXP" : $"T{tier}";

    /// <summary>Highest non-experimental tier present (the highest one if there are only experimentals).</summary>
    private int DefaultTier()
    {
        for (int i = _tiers.Count - 1; i >= 0; i--)
            if (_tiers[i] < 4) return _tiers[i];
        return _tiers.Count > 0 ? _tiers[^1] : 0;
    }

    private void CycleTier(int dir)
    {
        if (_tiers.Count <= 1) return;
        int i = Math.Max(0, _tiers.IndexOf(_tier));
        _tier = _tiers[(i + dir + _tiers.Count) % _tiers.Count];
    }

    /// <summary>Width the panel wants (content-sized, clamped by the caller).</summary>
    public float DesiredWidth()
    {
        float tabs = Tabs().Sum(TabWidth) + Gap * 4 + PillsWidth;
        int maxItems = Tabs().Max(t => _byTab[t].Count);
        float items = Math.Max(5, maxItems) * (ButtonSize + Gap) - Gap;
        return MathF.Max(tabs, items) + 2 * Pad;
    }

    public void HandleHotkeys()
    {
        if (Hotkeys.BuildCategory.Pressed())
        {
            var tabs = Tabs().Where(t => _byTab[t].Count > 0).ToList();
            if (tabs.Count > 0)
            {
                int i = tabs.IndexOf(_tab);
                _tab = tabs[((i < 0 ? 0 : i) + (Ui.Shift ? tabs.Count - 1 : 1)) % tabs.Count];
            }
        }
        if (Ui.Ctrl || Ui.Alt) return;
        if (Hotkeys.BuildTier.Pressed()) CycleTier(Ui.Shift ? -1 : 1);
        var items = _byTab[_tab];
        for (int k = 0; k < SlotKeys.Length && k < items.Count; k++)
            if (Hotkeys.BuildSlots.PressedIndex() == k) Choose(items[k]);
    }

    private void Choose(int bp)
    {
        _sel.Mode = SelectionController.CommandMode.Normal;
        _sel.Placement.Begin(bp);
    }

    public void Draw(Rectangle r, SimSnapshot snap)
    {
        Ui.Panel(r);
        // Tabs row.
        float x = r.X + Pad;
        float y = r.Y + Ui.P(6);
        float th = Ui.P(22);
        foreach (var t in Tabs())
        {
            int count = _byTab[t].Count;
            var tr = new Rectangle(MathF.Round(x), MathF.Round(y), MathF.Round(TabWidth(t)), th);
            var st = Ui.Button(tr, count > 0, t == _tab, t == _tab ? new Color(58, 84, 116, 255) : null);
            int fs = Ui.F(11.5f);
            string name = UnitIcons.TabName(t);
            float w = Ui.Measure(name + " ", fs, true);
            float total = w + Ui.Measure(count.ToString(), fs, true);
            float tx = tr.X + (tr.Width - total) * 0.5f, ty = tr.Y + (tr.Height - fs) * 0.5f - 1;
            Ui.Text(name, tx, ty, fs, count > 0 ? (t == _tab ? Color.White : Ui.TextColor) : Ui.TextFaint, true);
            Ui.Text(count.ToString(), tx + w, ty, fs, count > 0 ? Ui.Accent : Ui.TextFaint, true);
            if (st.Left) _tab = t;
            if (st.Hover) Ui.Tooltip(tr, $"{UnitIcons.TabName(t)} structures", "Tab / Shift+Tab: next / previous category");
            x += tr.Width + Gap;
        }
        float hintX = x + Ui.P(4);
        string hint = "Tab: category";
        if (_tiers.Count > 1)
        {
            // Tier pills, right-aligned.
            float px = r.X + r.Width - Pad - _tiers.Sum(t => PillWidth(t) + Ui.P(3)) + Ui.P(3);
            if (px < hintX) px = hintX;
            foreach (int t in _tiers)
            {
                var pr = new Rectangle(MathF.Round(px), MathF.Round(y), PillWidth(t), th);
                bool on = t == _tier;
                var tint = t >= 4 ? (on ? new Color(120, 62, 48, 255) : new Color(70, 44, 40, 255)) : on ? new Color(70, 96, 60, 255) : (Color?)null;
                var ps = Ui.Button(pr, true, on, tint);
                Ui.TextCentered(TierLabel(t), pr, Ui.F(11.5f), on ? Color.White : Ui.TextDim, true);
                if (ps.Left) _tier = t;
                if (ps.Hover) Ui.Tooltip(pr, t >= 4 ? "Experimental" : $"Tech {t}", "T / Shift+T: next / previous tier");
                px += PillWidth(t) + Ui.P(3);
            }
            hint = "";
        }
        if (hint.Length > 0 && hintX + Ui.Measure(hint, Ui.F(10)) < r.X + r.Width - Pad)
            Ui.Text(hint, r.X + r.Width - Pad - Ui.Measure(hint, Ui.F(10)), y + (th - Ui.F(10)) * 0.5f, Ui.F(10), Ui.TextFaint);

        // Item grid.
        y += th + Ui.P(8);
        var items = _byTab[_tab];
        float bs = ButtonSize;
        int cols = Math.Max(1, (int)((r.Width - 2 * Pad + Gap) / (bs + Gap)));
        int placing = _sel.Placement.Active ? _sel.Placement.BpIndex : -1;
        for (int i = 0; i < items.Count; i++)
        {
            int bpi = items[i];
            var bp = _ctx.Db[bpi];
            var br = new Rectangle(MathF.Round(r.X + Pad + (i % cols) * (bs + Gap)), MathF.Round(y + (i / cols) * (bs + Gap)), bs, bs);
            if (br.Y + br.Height > r.Y + r.Height - Ui.P(2)) break;
            var st = Ui.Button(br, true, bpi == placing);
            UnitIcons.Draw(br, bp);
            string? key = i < SlotNames.Length ? SlotNames[i] : null;
            if (key != null) Ui.HotkeyBadge(br, key);
            if (st.Left) Choose(bpi);
            if (st.Hover)
            {
                var (rate, n) = BuildRate(snap, bpi);
                Ui.Tooltip(br, UnitIcons.BuildTooltip(bp, key, rate, n,
                    "Click to place · Shift+click on the map queues more · Shift+drag lays a line · R rotates · right-click / Esc cancels"));
            }
        }
    }

    /// <summary>Combined build power of selected builders able to build bp.</summary>
    private (float rate, int count) BuildRate(SimSnapshot snap, int bp)
    {
        float rate = 0;
        int n = 0;
        foreach (var h in _builders)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            var b = _ctx.Db[v.Bp];
            if (Array.IndexOf(b.BuildIndices, bp) < 0) continue;
            rate += b.Economy.BuildRate;
            n++;
        }
        return (rate, n);
    }
}
