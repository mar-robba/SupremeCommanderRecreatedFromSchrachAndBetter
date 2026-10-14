using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// Factory panel: unit build buttons (left +1, Shift +5, right-click -1 / Shift -5; hotkeys Z X C V B N),
/// counts queued in the selected factories, the build queue of the primary factory with progress, and the
/// Repeat toggle (R). T2/T3 factories build several tiers: tier tabs (T / Tab cycles) filter the buttons.
/// The upgrade tile (U, see <see cref="UpgradeUi"/>) upgrades the factory in place; production pauses meanwhile.
/// Commands: FactoryEnqueue / FactoryDequeue / FactoryRepeat / Upgrade / CancelUpgrade.
/// </summary>
public sealed class FactoryPanel
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly List<UnitHandle> _factories = new();
    private readonly List<int> _options = new();
    /// <summary>Tiers present in _options (ascending), the selected tier and the options of that tier.</summary>
    private readonly List<int> _tiers = new();
    private readonly List<int> _shown = new();
    private int _tier;
    private readonly List<UnitHandle> _upReady = new();
    private readonly List<UnitHandle> _upBusy = new();

    public FactoryPanel(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    private float ButtonSize => Ui.P(60);
    private float Gap => Ui.P(5);
    private float Pad => Ui.P(8);

    /// <summary>Selected own factories and the union of what they build. Returns true if there is content.</summary>
    public bool Refresh(SimSnapshot snap)
    {
        _factories.Clear();
        _options.Clear();
        foreach (var h in _sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !_ctx.Controls(v.Army)) continue;
            var bp = _ctx.Db[v.Bp];
            if (!bp.Has(UnitCategory.Factory) || bp.BuildIndices.Length == 0) continue; // structures and mobile factories (Fatboy)
            _factories.Add(h);
            foreach (int b in bp.BuildIndices)
                if (!_options.Contains(b)) _options.Add(b);
        }
        _tiers.Clear();
        foreach (int b in _options)
            if (!_tiers.Contains(_ctx.Db[b].Tech)) _tiers.Add(_ctx.Db[b].Tech);
        _tiers.Sort();
        if (_tiers.Count > 0 && !_tiers.Contains(_tier)) _tier = _tiers[^1]; // default: the highest tier
        _shown.Clear();
        foreach (int b in _options)
            if (_tiers.Count <= 1 || _ctx.Db[b].Tech == _tier) _shown.Add(b);
        UpgradeUi.Collect(_ctx, _factories, snap, _upReady, _upBusy, factories: true);
        return _factories.Count > 0;
    }

    private bool HasUpgrade => _upReady.Count + _upBusy.Count > 0;

    public float DesiredWidth()
    {
        int most = Math.Max(5, _tiers.Count <= 1 ? _options.Count : _tiers.Max(t => _options.Count(b => _ctx.Db[b].Tech == t)));
        float w = (most + (HasUpgrade ? 1 : 0)) * (ButtonSize + Gap) - Gap + (HasUpgrade ? Ui.P(10) : 0) + 2 * Pad;
        return MathF.Max(w, Ui.P(420));
    }

    private void CycleTier(int dir)
    {
        if (_tiers.Count <= 1) return;
        int i = Math.Max(0, _tiers.IndexOf(_tier));
        _tier = _tiers[(i + dir + _tiers.Count) % _tiers.Count];
    }

    private UnitHandle[] FactoriesFor(SimSnapshot snap, int bp)
    {
        var list = new List<UnitHandle>();
        foreach (var h in _factories)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && Array.IndexOf(_ctx.Db[v.Bp].BuildIndices, bp) >= 0) list.Add(h);
        return list.ToArray();
    }

    private void Enqueue(SimSnapshot snap, int bp, int count)
    {
        var f = FactoriesFor(snap, bp);
        if (f.Length > 0) ArmyCommands.Send(_ctx, f, (army, units) => new FactoryEnqueueCommand(army, units, bp, count));
    }

    private void Dequeue(UnitHandle[] factories, int bp, int count)
    {
        if (factories.Length > 0) ArmyCommands.Send(_ctx, factories, (army, units) => new FactoryDequeueCommand(army, units, bp, count));
    }

    private bool AllRepeat(SimSnapshot snap)
    {
        foreach (var h in _factories)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && !v.FactoryRepeat) return false;
        return _factories.Count > 0;
    }

    private void ToggleRepeat(SimSnapshot snap)
    {
        bool repeat = !AllRepeat(snap);
        ArmyCommands.Send(_ctx, _factories, (army, units) => new FactoryRepeatCommand(army, units, repeat));
    }

    public void HandleHotkeys(SimSnapshot snap)
    {
        if (Ui.Ctrl || Ui.Alt) return;
        for (int k = 0; k < BuildMenu.SlotKeys.Length && k < _shown.Count; k++)
            if (Raylib.IsKeyPressed(BuildMenu.SlotKeys[k])) Enqueue(snap, _shown[k], Ui.Shift ? 5 : 1);
        if (Raylib.IsKeyPressed(KeyboardKey.R)) ToggleRepeat(snap);
        if (Raylib.IsKeyPressed(BuildMenu.TierKey) || Raylib.IsKeyPressed(KeyboardKey.Tab)) CycleTier(Ui.Shift ? -1 : 1);
    }

    public void Draw(Rectangle r, SimSnapshot snap)
    {
        Ui.Panel(r);
        // Title + repeat toggle.
        var first = _factories[0];
        snap.TryGetUnit(first.Index, first.Generation, out var fv);
        string title = _factories.Count == 1 ? _ctx.Db[fv.Bp].Name : $"{_factories.Count} factories";
        float y = Ui.PanelTitle(r, title + "  ·  build");

        bool repeat = AllRepeat(snap);
        int fs = Ui.F(11);
        string rl = repeat ? "Repeat: ON" : "Repeat: off";
        float rw = Ui.Measure(rl, fs, true) + Ui.P(30);
        var rr = new Rectangle(MathF.Round(r.X + r.Width - Pad - rw), r.Y + Ui.P(4), MathF.Round(rw), Ui.P(20));
        var rs = Ui.Button(rr, true, repeat, repeat ? new Color(50, 100, 60, 255) : null);
        // Loop glyph.
        var lc = new Vector2(rr.X + Ui.P(11), rr.Y + rr.Height * 0.5f);
        Raylib.DrawRing(lc, Ui.P(4), Ui.P(4) + 2, 30, 330, 16, repeat ? Ui.Good : Ui.TextDim);
        Ui.Text(rl, rr.X + Ui.P(20), rr.Y + (rr.Height - fs) * 0.5f - 1, fs, repeat ? Color.White : Ui.TextDim, true);
        if (rs.Left) ToggleRepeat(snap);
        if (rs.Hover) Ui.Tooltip(rr, "Repeat build  [R]", "When on, finished units are re-added to the end of the queue (infinite production).");
        if (_tiers.Count > 1)
        {
            // Tier tabs, right-aligned before the Repeat toggle.
            float pw = Ui.P(30), px = rr.X - Ui.P(8) - _tiers.Count * (pw + Ui.P(3));
            foreach (int t in _tiers)
            {
                var pr = new Rectangle(MathF.Round(px), rr.Y, pw, rr.Height);
                var ps = Ui.Button(pr, true, t == _tier, t == _tier ? new Color(58, 84, 116, 255) : null);
                Ui.TextCentered("T" + t, pr, Ui.F(11), t == _tier ? Color.White : Ui.TextDim, true);
                if (ps.Left) _tier = t;
                if (ps.Hover) Ui.Tooltip(pr, $"Tech {t} units", "T / Tab: next tier (Shift: previous)");
                px += pw + Ui.P(3);
            }
        }

        // Unit buttons with queued counts.
        var counts = new Dictionary<int, int>();
        foreach (var h in _factories)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v)) continue;
            for (int q = 0; q < v.QueueCount; q++)
            {
                int b = snap.FactoryQueue[v.QueueStart + q];
                counts[b] = counts.GetValueOrDefault(b) + 1;
            }
        }
        y += Ui.P(4);
        float bs = ButtonSize;
        int cols = Math.Max(1, (int)((r.Width - 2 * Pad + Gap - (HasUpgrade ? bs + Gap + Ui.P(10) : 0)) / (bs + Gap)));
        if (HasUpgrade)
            UpgradeUi.DrawTile(new Rectangle(MathF.Round(r.X + r.Width - Pad - bs), MathF.Round(y), bs, bs), _ctx, snap, _upReady, _upBusy);
        for (int i = 0; i < _shown.Count && i < cols; i++)
        {
            int bpi = _shown[i];
            var bp = _ctx.Db[bpi];
            var br = new Rectangle(MathF.Round(r.X + Pad + i * (bs + Gap)), MathF.Round(y), bs, bs);
            var st = Ui.Button(br);
            UnitIcons.Draw(br, bp);
            string? key = i < BuildMenu.SlotNames.Length ? BuildMenu.SlotNames[i] : null;
            if (key != null) Ui.HotkeyBadge(br, key);
            if (counts.TryGetValue(bpi, out int n) && n > 0)
            {
                int cfs = Ui.F(13);
                string cs = n.ToString();
                float cw = Ui.Measure(cs, cfs, true) + Ui.P(8);
                var badge = new Rectangle(br.X + 2, br.Y + br.Height - cfs - Ui.P(18), cw, cfs + Ui.P(3));
                Raylib.DrawRectangleRec(badge, new Color(20, 70, 140, 230));
                Raylib.DrawRectangleLinesEx(badge, 1, Ui.Accent);
                Ui.Text(cs, badge.X + Ui.P(4), badge.Y + Ui.P(1), cfs, Color.White, true);
            }
            if (st.Left) Enqueue(snap, bpi, Ui.Shift ? 5 : 1);
            if (st.Right) Dequeue(FactoriesFor(snap, bpi), bpi, Ui.Shift ? 5 : 1);
            if (st.Hover)
            {
                float rate = 0;
                foreach (var h in FactoriesFor(snap, bpi))
                    if (snap.TryGetUnit(h.Index, h.Generation, out var v)) rate = MathF.Max(rate, _ctx.Db[v.Bp].Economy.BuildRate);
                Ui.Tooltip(br, UnitIcons.BuildTooltip(bp, key, rate, 1, "Click: +1   Shift+click: +5   Right-click: -1 (Shift: -5)"));
            }
        }
        y += bs + Ui.P(8);

        // Queue of the primary factory (first selected factory with something queued).
        UnitView primary = fv;
        UnitHandle primaryH = first;
        foreach (var h in _factories)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v) && v.QueueCount > 0) { primary = v; primaryH = h; break; }

        int lfs = Ui.F(10.5f);
        float qy = y + lfs + Ui.P(4);
        if (primary.UpgradeBp >= 0 || fv.UpgradeBp >= 0)
        {
            // The (primary) factory is upgrading: progress bar instead of the queue label; production is paused.
            var uv = primary.UpgradeBp >= 0 ? primary : fv;
            var tbp = _ctx.Db[uv.UpgradeBp];
            Ui.Bar(new Rectangle(r.X + Pad, y - Ui.P(2), r.Width - 2 * Pad, lfs + Ui.P(4)), uv.UpgradeFrac, UpgradeUi.UpgradeGreen,
                $"Upgrading to {tbp.Name}  {uv.UpgradeFrac * 100:0}%  ·  production paused");
        }
        else
        {
            string ql = _factories.Count > 1 ? "QUEUE (first busy factory)" : "QUEUE";
            Ui.Text(ql, r.X + Pad, y, lfs, Ui.TextDim, true);
        }
        float tile = MathF.Min(Ui.P(40), r.Y + r.Height - Ui.P(6) - qy);
        if (primary.QueueCount == 0)
        {
            bool mobile = _ctx.Db[primary.Bp].IsMobile; // mobile factory (Fatboy): right-click moves it
            Ui.Text(mobile ? "Empty — click a unit above to queue it. It keeps building while it moves."
                : "Empty — click a unit above to queue it. Right-click the ground to set the rally point.", r.X + Pad, qy + Ui.P(4), Ui.F(11), Ui.TextFaint);
            return;
        }

        // Group consecutive identical entries.
        var groups = new List<(int bp, int count, bool front)>();
        for (int q = 0; q < primary.QueueCount; q++)
        {
            int b = snap.FactoryQueue[primary.QueueStart + q];
            if (groups.Count > 0 && groups[^1].bp == b && !groups[^1].front)
                groups[^1] = (b, groups[^1].count + 1, groups[^1].front);
            else groups.Add((b, 1, q == 0));
        }
        float qx = r.X + Pad;
        float qmax = r.X + r.Width - Pad;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            var tr = new Rectangle(MathF.Round(qx), MathF.Round(qy), tile, tile);
            if (tr.X + tr.Width > qmax)
            {
                int rest = groups.Skip(gi).Sum(x => x.count);
                Ui.Text($"+{rest}", qx, qy + (tile - Ui.F(12)) * 0.5f, Ui.F(12), Ui.TextDim, true);
                break;
            }
            var st = Ui.Button(tr, true, g.front);
            var bp = _ctx.Db[g.bp];
            UnitIcons.Draw(tr, bp, compact: true);
            if (g.count > 1)
            {
                int cfs = Ui.F(11);
                string cs = "×" + g.count;
                Ui.TextShadow(cs, tr.X + tr.Width - Ui.Measure(cs, cfs, true) - Ui.P(3), tr.Y + tr.Height - cfs - Ui.P(3), cfs, Color.White, true);
            }
            if (g.front && primary.WorkProgress >= 0 && primary.UpgradeBp < 0)
                Ui.Bar(new Rectangle(tr.X + 2, tr.Y + tr.Height - Ui.P(7) - 2, tr.Width - 4, Ui.P(7)), primary.WorkProgress, Ui.BuildBlue);
            if (st.Right) Dequeue([primaryH], g.bp, Ui.Shift ? 5 : 1);
            if (st.Hover)
            {
                var lines = new List<TipLine> { new($"{bp.Name}  ×{g.count}", Ui.TextColor, 13, true) };
                if (g.front) lines.Add(new TipLine(primary.WorkProgress >= 0 ? $"In production: {primary.WorkProgress * 100:0}%" : "Next in production", Ui.BuildBlue, 11.5f));
                lines.Add(new TipLine("Right-click: remove one (Shift: five)", Ui.TextFaint, 11));
                Ui.Tooltip(tr, lines);
            }
            qx += tile + Ui.P(4);
            if (g.front && gi + 1 < groups.Count)
            {
                // Separator arrow between "building now" and the rest of the queue.
                var ac = new Vector2(qx + Ui.P(3), qy + tile * 0.5f);
                Raylib.DrawTriangle(ac + new Vector2(-Ui.P(3), -Ui.P(5)), ac + new Vector2(-Ui.P(3), Ui.P(5)), ac + new Vector2(Ui.P(4), 0), Ui.TextDim);
                qx += Ui.P(10);
            }
        }
    }
}
