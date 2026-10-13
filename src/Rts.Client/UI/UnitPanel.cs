using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// Unit info panel. One unit: icon, name, description, health, construction / work progress, current order and
/// key stats. Several units: a grid of type buttons with counts (click = keep only that type, Shift+click =
/// remove that type) and the group's total health.
/// </summary>
public sealed class UnitPanel
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;

    public UnitPanel(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    public void Draw(Rectangle r, SimSnapshot snap)
    {
        Ui.Panel(r);
        var views = new List<UnitView>(_sel.Selected.Count);
        foreach (var h in _sel.Selected)
            if (snap.TryGetUnit(h.Index, h.Generation, out var v)) views.Add(v);
        if (views.Count == 0) return;
        if (views.Count == 1) DrawSingle(r, snap, views[0]);
        else DrawMulti(r, views);
    }

    private void DrawSingle(Rectangle r, SimSnapshot snap, UnitView v)
    {
        var bp = _ctx.Db[v.Bp];
        float pad = Ui.P(8);
        float icon = Ui.P(52);
        var iconR = new Rectangle(r.X + pad, r.Y + pad, icon, icon);
        Raylib.DrawRectangleRec(iconR, new Color(8, 10, 14, 255));
        UnitIcons.Draw(iconR, bp);
        Raylib.DrawRectangleLinesEx(iconR, 1, Ui.PanelBorder);
        // Army color strip under the icon.
        Raylib.DrawRectangleRec(new Rectangle(iconR.X, iconR.Y + icon + Ui.P(3), icon, Ui.P(3)), _ctx.ArmyColors[v.Army]);
        // Veterancy stars on the icon.
        int vet = VeterancyLevel(bp, v.Kills);
        for (int i = 0; i < vet; i++)
            DrawStar(new Vector2(iconR.X + Ui.P(7) + i * Ui.P(10), iconR.Y + Ui.P(7)), Ui.P(5), new Color(255, 215, 80, 255));

        float tx = iconR.X + icon + Ui.P(8);
        float tw = r.X + r.Width - pad - tx;
        int fsName = Ui.F(14), fsDesc = Ui.F(11.5f);
        Ui.Text(Ui.Fit(bp.Name, tw, fsName, true), tx, r.Y + pad, fsName, Ui.TextColor, true);
        float y = r.Y + pad + fsName + Ui.P(4);
        var desc = Ui.Wrap(bp.Description, tw, fsDesc);
        for (int i = 0; i < Math.Min(2, desc.Count); i++)
        {
            string line = i == 1 && desc.Count > 2 ? Ui.Fit(desc[i] + " …", tw, fsDesc) : desc[i];
            Ui.Text(line, tx, y, fsDesc, Ui.TextDim);
            y += fsDesc + Ui.P(2);
        }

        y = MathF.Max(y, iconR.Y + icon + Ui.P(10));
        float bw = r.Width - 2 * pad;
        float barH = Ui.P(15);

        // Health.
        float hp = v.HealthFrac * bp.MaxHealth;
        var hpCol = v.HealthFrac > 0.5f ? new Color(70, 200, 70, 255) : v.HealthFrac > 0.25f ? new Color(230, 190, 40, 255) : new Color(220, 50, 40, 255);
        Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.HealthFrac, hpCol, $"{hp:0} / {bp.MaxHealth:0}");
        y += barH + Ui.P(5);

        bool underConstruction = (v.Flags & UnitFlags.UnderConstruction) != 0;
        if (underConstruction)
        {
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.BuildFrac, Ui.BuildBlue, $"Under construction  {v.BuildFrac * 100:0}%");
            y += barH + Ui.P(5);
        }
        else if (v.UpgradeBp >= 0 && (uint)v.UpgradeBp < (uint)_ctx.Db.Count)
        {
            int enh = EnhancementCatalog.Added(_ctx.Db, v.Bp, v.UpgradeBp);
            string label = enh >= 0 ? $"Enhancing: {bp.Enhancements[enh].Name}  {v.UpgradeFrac * 100:0}%"
                : $"Upgrading to {_ctx.Db[v.UpgradeBp].Name}  {v.UpgradeFrac * 100:0}%";
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.UpgradeFrac, enh >= 0 ? EnhancementPanel.EnhanceColor : UpgradeUi.UpgradeGreen, label);
            y += barH + Ui.P(5);
        }
        else if (v.WorkProgress >= 0)
        {
            string what = WorkTargetName(snap, v);
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.WorkProgress, Ui.BuildBlue, $"Building {what}  {v.WorkProgress * 100:0}%");
            y += barH + Ui.P(5);
        }

        if (!underConstruction) y = StrategicHud.DrawUnitPanelBars(_ctx, snap, v, bp, r.X + pad, y, bw, barH);
        int fs = Ui.F(11.5f);
        string order = CurrentOrderText(snap, v, bp);
        Ui.Text("Order:", r.X + pad, y, fs, Ui.TextFaint, true);
        Ui.Text(Ui.Fit(order, bw - Ui.P(48), fs), r.X + pad + Ui.P(46), y, fs, Ui.TextColor);
        y += fs + Ui.P(4);
        if (EnhancementPanel.InstalledSummary(bp) is string enhs)
        {
            // Installed enhancements (top of each slot's chain), e.g. "T3E · HAC · SGF".
            Ui.Text("Enh:", r.X + pad, y, fs, Ui.TextFaint, true);
            Ui.Text(Ui.Fit(enhs, bw - Ui.P(48), fs, true), r.X + pad + Ui.P(46), y, fs, EnhancementPanel.EnhanceColor, true);
            y += fs + Ui.P(4);
        }

        // Key stats (two compact columns).
        var stats = KeyStats(bp);
        if (bp.Weapons.Length > 0 || v.Kills > 0)
            stats.Insert(0, ("Kills", vet > 0 ? $"{v.Kills} (vet {vet})" : v.Kills.ToString(), vet > 0 ? new Color(255, 215, 80, 255) : Ui.TextColor));
        float colW = bw * 0.5f;
        for (int i = 0; i < stats.Count; i++)
        {
            float sx = r.X + pad + (i % 2) * colW;
            float sy = y + (i / 2) * (fs + Ui.P(3));
            if (sy + fs > r.Y + r.Height - Ui.P(4)) break;
            Ui.Text(stats[i].label, sx, sy, fs, Ui.TextFaint);
            Ui.Text(stats[i].value, sx + Ui.Measure(stats[i].label, fs) + Ui.P(4), sy, fs, stats[i].color, true);
        }
    }

    private string WorkTargetName(SimSnapshot snap, in UnitView v)
    {
        var bp = _ctx.Db[v.Bp];
        if (bp.Has(UnitCategory.Factory) && v.QueueCount > 0)
            return _ctx.Db[snap.FactoryQueue[v.QueueStart]].Name;
        if (v.OrderCount > 0)
        {
            var o = snap.Orders[v.OrderStart];
            if (o.Type == OrderType.Build && (uint)o.BlueprintIndex < (uint)_ctx.Db.Count) return _ctx.Db[o.BlueprintIndex].Name;
            if (o.Type == OrderType.Guard) return "(assisting)";
            if (o.Type == OrderType.Repair) return "(repairing)";
        }
        return "";
    }

    private string CurrentOrderText(SimSnapshot snap, in UnitView v, UnitBlueprint bp)
    {
        if (v.UpgradeBp >= 0 && EnhancementCatalog.Added(_ctx.Db, v.Bp, v.UpgradeBp) >= 0) return "Enhancing";
        if (v.UpgradeBp >= 0)
            return bp.Has(UnitCategory.Factory) ? "Upgrading (production paused" + (v.QueueCount > 0 ? $", {v.QueueCount} queued)" : ")") : "Upgrading";
        if (bp.Has(UnitCategory.Factory))
        {
            string s = v.QueueCount > 0 ? $"Producing {_ctx.Db[snap.FactoryQueue[v.QueueStart]].Name}" + (v.QueueCount > 1 ? $" (+{v.QueueCount - 1} queued)" : "") : "Idle";
            if (v.FactoryRepeat) s += " · repeat";
            if (v.OrderCount > 0) s += " · rally set";
            return s;
        }
        if (v.OrderCount == 0) return (v.Flags & UnitFlags.UnderConstruction) != 0 ? "Being built" : bp.IsStructure ? "Active" : "Idle";
        var o = snap.Orders[v.OrderStart];
        string text = o.Type switch
        {
            OrderType.Move => "Moving",
            OrderType.AttackMove => "Attack-moving",
            OrderType.Attack => "Attacking",
            OrderType.Patrol => "Patrolling",
            OrderType.Guard => bp.Has(UnitCategory.Engineer) ? "Assisting" : "Guarding",
            OrderType.Build => (uint)o.BlueprintIndex < (uint)_ctx.Db.Count ? $"Building {_ctx.Db[o.BlueprintIndex].Name}" : "Building",
            OrderType.Repair => "Repairing",
            OrderType.Reclaim => "Reclaiming",
            OrderType.Capture => "Capturing",
            OrderType.Launch => "Launching",
            _ => "Idle",
        };
        if (v.OrderCount > 1) text += $" (+{v.OrderCount - 1} queued)";
        return text;
    }

    private static List<(string label, string value, Color color)> KeyStats(UnitBlueprint bp)
    {
        var l = new List<(string, string, Color)>();
        var e = bp.Economy;
        if (bp.Weapons.Length > 0)
        {
            l.Add(("DPS", Ui.Num(UnitIcons.Dps(bp)), new Color(240, 150, 130, 255)));
            l.Add(("Range", Ui.Num(UnitIcons.MaxRange(bp)), new Color(240, 150, 130, 255)));
        }
        if (bp.IsMobile && bp.Movement.MaxSpeed > 0) l.Add(("Speed", bp.Movement.MaxSpeed.ToString("0.#"), Ui.TextColor));
        l.Add(("Vision", Ui.Num(bp.Intel.VisionRadius), Ui.TextColor));
        if (bp.Intel.RadarRadius > 0) l.Add(("Radar", Ui.Num(bp.Intel.RadarRadius), new Color(120, 210, 230, 255)));
        if (e.BuildRate > 0) l.Add(("Build power", Ui.Num(e.BuildRate), Ui.BuildBlue));
        if (e.ProductionMass > 0) l.Add(("Mass", "+" + Ui.Num(e.ProductionMass) + "/s", Ui.MassColor));
        if (e.ProductionEnergy > 0) l.Add(("Energy", "+" + Ui.Num(e.ProductionEnergy) + "/s", Ui.EnergyColor));
        if (e.MaintenanceEnergy > 0) l.Add(("Upkeep", "-" + Ui.Num(e.MaintenanceEnergy) + " E/s", Ui.Bad));
        if (e.StorageMass > 0) l.Add(("Mass store", Ui.Num(e.StorageMass), Ui.MassColor));
        if (e.StorageEnergy > 0) l.Add(("Energy store", Ui.Num(e.StorageEnergy), Ui.EnergyColor));
        return l;
    }

    private void DrawMulti(Rectangle r, List<UnitView> views)
    {
        // Group by blueprint: commander first, then engineers, then mobile, then structures; stable by bp index.
        var groups = views.GroupBy(v => v.Bp)
            .Select(g => (bp: g.Key, count: g.Count(), hp: g.Sum(v => v.HealthFrac * _ctx.Db[v.Bp].MaxHealth), max: g.Sum(v => _ctx.Db[v.Bp].MaxHealth)))
            .OrderBy(g => SortKey(_ctx.Db[g.bp])).ThenBy(g => g.bp).ToList();

        float pad = Ui.P(8);
        float y = Ui.PanelTitle(r, $"{views.Count} units selected  ·  {groups.Count} types");
        float totalHp = groups.Sum(g => g.hp), totalMax = groups.Sum(g => g.max);
        float barH = Ui.P(12);
        Ui.Bar(new Rectangle(r.X + pad, y, r.Width - 2 * pad, barH), totalMax > 0 ? totalHp / totalMax : 0, new Color(70, 200, 70, 255), $"Health {totalHp / MathF.Max(1, totalMax) * 100:0}%");
        y += barH + Ui.P(6);

        float btn = Ui.P(46), gap = Ui.P(4);
        int cols = Math.Max(1, (int)((r.Width - 2 * pad + gap) / (btn + gap)));
        int rows = Math.Max(1, (int)((r.Y + r.Height - pad - y + gap) / (btn + gap)));
        int maxShown = cols * rows;
        for (int i = 0; i < groups.Count && i < maxShown; i++)
        {
            var g = groups[i];
            var bp = _ctx.Db[g.bp];
            var br = new Rectangle(r.X + pad + (i % cols) * (btn + gap), y + (i / cols) * (btn + gap), btn, btn);
            if (i == maxShown - 1 && groups.Count > maxShown)
            {
                Ui.Button(br, enabled: false);
                Ui.TextCentered($"+{groups.Count - i}", br, Ui.F(13), Ui.TextDim, true);
                break;
            }
            var st = Ui.Button(br);
            UnitIcons.Draw(br, bp, compact: false);
            // Count badge.
            string cnt = g.count.ToString();
            int fs = Ui.F(11);
            float cw = Ui.Measure(cnt, fs, true) + Ui.P(6);
            var badge = new Rectangle(br.X + br.Width - cw - 2, br.Y + 2, cw, fs + Ui.P(2));
            Raylib.DrawRectangleRec(badge, new Color(0, 0, 0, 190));
            Ui.Text(cnt, badge.X + Ui.P(3), badge.Y + Ui.P(1) - 1, fs, Color.White, true);
            // Health sliver.
            float hf = g.max > 0 ? g.hp / g.max : 0;
            Raylib.DrawRectangle((int)br.X + 2, (int)(br.Y + br.Height - 4), (int)((br.Width - 4) * hf), 2, hf > 0.5f ? Ui.Good : hf > 0.25f ? Ui.Warn : Ui.Bad);

            if (st.Left)
            {
                if (Ui.Shift) _sel.RemoveType(g.bp);
                else _sel.NarrowToType(g.bp);
            }
            else if (st.Right) _sel.RemoveType(g.bp);
            if (st.Hover)
                Ui.Tooltip(br, new List<TipLine>
                {
                    new($"{bp.Name}  ×{g.count}", Ui.TextColor, 13, true),
                    new($"Health {g.hp:0} / {g.max:0}", Ui.TextDim, 11.5f),
                    new("Click: select only these   Shift+click / right-click: deselect", Ui.TextFaint, 11),
                });
        }
    }

    /// <summary>Veterancy stars (0..5): a level every 3 kills per tech level (commander / experimentals: 20).
    /// Cosmetic for now — the sim does not apply veterancy bonuses yet.</summary>
    public static int VeterancyLevel(UnitBlueprint bp, int kills)
    {
        int per = bp.Has(UnitCategory.Commander) || bp.Has(UnitCategory.Experimental) ? 20 : 3 * Math.Max(1, bp.Tech);
        return Math.Clamp(kills / per, 0, 5);
    }

    /// <summary>Filled five-pointed star (dark outline) centered at c.</summary>
    public static void DrawStar(Vector2 c, float r, Color col)
    {
        Span<Vector2> pts = stackalloc Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
        }
        Raylib.DrawCircleV(c, r * 0.55f, new Color(0, 0, 0, 120));
        for (int i = 0; i < 10; i++)
        {
            // Screen space (y down): (c, p[i+1], p[i]) is counter-clockwise on screen.
            Raylib.DrawTriangle(c, pts[(i + 1) % 10], pts[i], col);
        }
    }

    private static int SortKey(UnitBlueprint bp) =>
        bp.Has(UnitCategory.Commander) ? 0 : bp.Has(UnitCategory.Engineer) ? 1 : bp.IsMobile ? 2 : bp.Has(UnitCategory.Factory) ? 3 : 4;
}
