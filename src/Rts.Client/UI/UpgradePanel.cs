using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// Build-panel slot for selected upgradable structures that are not factories (mass extractors, radars): the upgrade
/// tile (click / U: upgrade, right-click: cancel), what changes, the cost, and the progress with a Cancel button.
/// Factories show the same tile inside <see cref="FactoryPanel"/>.
/// </summary>
public sealed class UpgradePanel
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private readonly List<UnitHandle> _ready = new();
    private readonly List<UnitHandle> _upgrading = new();

    public UpgradePanel(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    private float ButtonSize => Ui.P(60);
    private float Pad => Ui.P(8);

    /// <summary>True if the selection has own non-factory structures that can upgrade or are upgrading.</summary>
    public bool Refresh(SimSnapshot snap)
    {
        UpgradeUi.Collect(_ctx, _sel.Selected, snap, _ready, _upgrading, factories: false);
        return _ready.Count + _upgrading.Count > 0;
    }

    public float DesiredWidth() => Ui.P(400);

    public void Draw(Rectangle r, SimSnapshot snap)
    {
        Ui.Panel(r);
        if (UpgradeUi.Target(_ctx, snap, _ready, _upgrading) is not var (from, to)) return;
        int n = _ready.Count + _upgrading.Count;
        float y = Ui.PanelTitle(r, (n == 1 ? from.Name : $"{n} structures") + "  ·  upgrade");
        float bs = ButtonSize;
        var tile = new Rectangle(r.X + Pad, MathF.Round(y + Ui.P(4)), bs, bs);
        UpgradeUi.DrawTile(tile, _ctx, snap, _ready, _upgrading);

        float tx = tile.X + bs + Ui.P(10);
        float tw = r.X + r.Width - Pad - tx;
        float ty = tile.Y;
        int fs = Ui.F(12.5f);
        Ui.Text(Ui.Fit($"→ {to.Name}  (T{to.Tech})", tw, fs, true), tx, ty, fs, UpgradeUi.UpgradeGreen, true);
        ty += fs + Ui.P(4);
        int sfs = Ui.F(11);
        foreach (var line in UpgradeUi.StatChanges(from, to).Take(3))
        {
            Ui.Text(Ui.Fit(line, tw, sfs), tx, ty, sfs, Ui.TextDim);
            ty += sfs + Ui.P(2);
        }

        float by = MathF.Max(ty + Ui.P(4), tile.Y + bs + Ui.P(8));
        float barH = Ui.P(16);
        if (by + barH > r.Y + r.Height - Ui.P(4)) by = r.Y + r.Height - Ui.P(4) - barH;
        if (_upgrading.Count > 0)
        {
            float avg = UpgradeUi.AverageProgress(snap, _upgrading);
            float cw = Ui.P(70);
            string label = $"Upgrading {avg * 100:0}%" + (_upgrading.Count > 1 ? $"  ({_upgrading.Count})" : "");
            Ui.Bar(new Rectangle(r.X + Pad, by, r.Width - 2 * Pad - cw - Ui.P(6), barH), avg, UpgradeUi.UpgradeGreen, label);
            var cr = new Rectangle(r.X + r.Width - Pad - cw, by, cw, barH);
            var st = Ui.Button(cr, true, false, new Color(90, 40, 36, 255));
            Ui.TextCentered("Cancel", cr, Ui.F(11), Ui.TextColor, true);
            if (st.Left) UpgradeUi.Cancel(_ctx, _upgrading);
            if (st.Hover) Ui.Tooltip(cr, "Cancel upgrade", "Stops the upgrade and refunds what was spent on it.");
        }
        else
        {
            var e = to.Economy;
            float rate = Rts.Sim.Systems.UpgradeSystem.SelfRate(from);
            Ui.Text(Ui.Fit($"Mass {Ui.Num(e.BuildCostMass)}  ·  Energy {Ui.Num(e.BuildCostEnergy)}  ·  ≈ {Ui.Seconds(e.BuildTime / rate)}  ·  click or {UpgradeUi.HotkeyName}",
                r.Width - 2 * Pad, sfs), r.X + Pad, by + (barH - sfs) * 0.5f, sfs, Ui.TextFaint);
        }
    }
}
