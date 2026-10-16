using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Combat;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Naval;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// Unit info panel. One unit: icon, name, description, health, construction / work progress, current order and
/// key stats, veterancy bonus, adjacency bonuses, pause / dive state and capture progress. Several units: the group's
/// total health, a summary line (idle units, fire states), cargo, and a grid of type buttons with counts (smaller when
/// they don't fit; click = keep only that type, Shift+click / right-click = remove it, Ctrl+click = add that type on
/// screen).
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
        else DrawMulti(r, snap, views);
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
        int vet = Veterancy.Level(bp, v.Kills);
        for (int i = 0; i < vet; i++)
            DrawStar(new Vector2(iconR.X + Ui.P(7) + i * Ui.P(10), iconR.Y + Ui.P(7)), Ui.P(5), new Color(255, 215, 80, 255));
        bool paused = (v.Flags & UnitFlags.Paused) != 0;
        if (paused) DrawPausedBadge(iconR);

        float tx = iconR.X + icon + Ui.P(8);
        float tw = r.X + r.Width - pad - tx;
        int fsName = Ui.F(14), fsDesc = Ui.F(11.5f);
        bool underConstruction = (v.Flags & UnitFlags.UnderConstruction) != 0;
        // Compact header when many rows follow (transports, aircraft, shields + silos…): health and order go next to
        // the icon instead of the description (shown as a tooltip on the icon / name), so nothing is cut off.
        bool compact = !underConstruction && ExtraRows(snap, v, bp) >= 2;
        Ui.Text(Ui.Fit(bp.Name, tw, fsName, true), tx, r.Y + pad, fsName, Ui.TextColor, true);
        float y = r.Y + pad + fsName + Ui.P(4);
        if (Ui.Hover(new Rectangle(iconR.X, iconR.Y, r.Width - 2 * pad, fsName + Ui.P(4) + (compact ? 0 : icon - fsName))))
            Ui.Tooltip(iconR, bp.Name, string.Join("\n", Ui.Wrap(bp.Description, Ui.P(320), Ui.F(12))));
        if (!compact)
        {
            var desc = Ui.Wrap(bp.Description, tw, fsDesc);
            for (int i = 0; i < Math.Min(2, desc.Count); i++)
            {
                string line = i == 1 && desc.Count > 2 ? Ui.Fit(desc[i] + " …", tw, fsDesc) : desc[i];
                Ui.Text(line, tx, y, fsDesc, Ui.TextDim);
                y += fsDesc + Ui.P(2);
            }
        }

        float bw = r.Width - 2 * pad;
        float barH = Ui.P(15);
        int fs = Ui.F(11.5f);

        // Health (effective max health: veterancy raises it).
        float maxHp = MaxHealthOf(v, bp);
        float hp = v.HealthFrac * maxHp;
        var hpCol = v.HealthFrac > 0.5f ? new Color(70, 200, 70, 255) : v.HealthFrac > 0.25f ? new Color(230, 190, 40, 255) : new Color(220, 50, 40, 255);
        if (compact)
        {
            Ui.Bar(new Rectangle(tx, y, tw, barH), v.HealthFrac, hpCol, $"{hp:0} / {maxHp:0}");
            y += barH + Ui.P(4);
            DrawOrderLine(snap, v, bp, paused, tx, y, tw, fs);
        }
        y = MathF.Max(y, iconR.Y + icon + Ui.P(10));
        if (!compact)
        {
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.HealthFrac, hpCol, $"{hp:0} / {maxHp:0}");
            y += barH + Ui.P(5);
        }
        if (v.CaptureFrac > 0)
        {
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.CaptureFrac, new Color(200, 70, 60, 255), $"Being captured  {v.CaptureFrac * 100:0}%");
            y += barH + Ui.P(5);
        }

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
        else if (v.WorkMode == BuilderMode.Capture)
        {
            string what = CaptureTargetName(snap, v);
            float f = Math.Clamp(v.WorkProgress, 0, 1);
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), f, CursorHint.CaptureColor, what.Length > 0 ? $"Capturing {what}  {f * 100:0}%" : $"Capturing  {f * 100:0}%");
            y += barH + Ui.P(5);
        }
        else if (v.WorkProgress >= 0)
        {
            string what = WorkTargetName(snap, v);
            Ui.Bar(new Rectangle(r.X + pad, y, bw, barH), v.WorkProgress, Ui.BuildBlue, $"Building {what}  {v.WorkProgress * 100:0}%");
            y += barH + Ui.P(5);
        }

        if (!underConstruction) y = StrategicHud.DrawUnitPanelBars(_ctx, snap, v, bp, r.X + pad, y, bw, barH);
        if (!underConstruction) y = CargoPanel.DrawSingle(_ctx, snap, v, bp, r.X + pad, y, bw, barH);
        if (!compact) y = DrawOrderLine(snap, v, bp, paused, r.X + pad, y, bw, fs);
        if (EnhancementPanel.InstalledSummary(bp) is string enhs)
        {
            // Installed enhancements (top of each slot's chain), e.g. "T3E · HAC · SGF".
            Ui.Text("Enh:", r.X + pad, y, fs, Ui.TextFaint, true);
            Ui.Text(Ui.Fit(enhs, bw - Ui.P(48), fs, true), r.X + pad + Ui.P(46), y, fs, EnhancementPanel.EnhanceColor, true);
            y += fs + Ui.P(4);
        }

        // Veterancy bonus and adjacency bonuses (structures next to power generators / storages): full-width lines.
        if (vet > 0) y = InfoLine($"Veteran {vet}:", VeterancyBonusText(bp, vet), new Color(255, 215, 80, 255), r.X + pad, y, bw, fs);
        if (AdjacencyText(v) is string adj) y = InfoLine("Adjacency:", adj, Ui.Good, r.X + pad, y, bw, fs);

        // Key stats (two compact columns).
        var stats = KeyStats(bp, v);
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

    /// <summary>"Order:" line with the toggle states (paused, submerged, fire state). Returns the y below it.</summary>
    private float DrawOrderLine(SimSnapshot snap, in UnitView v, UnitBlueprint bp, bool paused, float x, float y, float w, int fs)
    {
        string order = CurrentOrderText(snap, v, bp);
        if (Submarines.CanDive(bp)) order = ((v.Flags & UnitFlags.Surfaced) != 0 ? "Surfaced · " : "Submerged · ") + order;
        bool holding = bp.Weapons.Length > 0 && v.FireState != FireState.FireAtWill;
        if (holding) order = UnitToggles.FireStateName(v.FireState).ToUpperInvariant() + " · " + order;
        if (paused) order = "PAUSED · " + order;
        Ui.Text("Order:", x, y, fs, Ui.TextFaint, true);
        Ui.Text(Ui.Fit(order, w - Ui.P(48), fs), x + Ui.P(46), y, fs, paused || holding ? Ui.Warn : Ui.TextColor);
        return y + fs + Ui.P(4);
    }

    /// <summary>Rows the single-unit panel draws below the health bar besides order and stats (bars, cargo icons).</summary>
    private static int ExtraRows(SimSnapshot snap, in UnitView v, UnitBlueprint bp)
    {
        int n = 0;
        if (v.CaptureFrac > 0) n++;
        if (v.UpgradeBp >= 0 || v.WorkMode == BuilderMode.Capture || v.WorkProgress >= 0) n++;
        if (bp.Shield != null) n++;
        if (bp.Silo != null) n += 2;
        CargoView cv = default;
        bool cargo = bp.Cargo != null && TransportInput.TryGetCargo(snap, v.Index, v.Gen, out cv);
        if (cargo || v.Fuel >= 0) n++;
        if (cargo && cv.Count > 0) n++;
        if (bp.Cargo != null) n++; // carriers: their cargo row alone already pushes the stats out
        return n;
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
            OrderType.Load => bp.Cargo != null ? "Picking up" : "Boarding a transport",
            OrderType.Unload => bp.Cargo?.Layer == CargoLayer.Air ? "Launching aircraft" : "Unloading",
            OrderType.TransportTo => "Transporting",
            OrderType.Ferry => bp.Cargo != null ? "Ferrying" : "Waiting for a ferry",
            OrderType.Dock => "Docking",
            _ => "Idle",
        };
        if (v.OrderCount > 1) text += $" (+{v.OrderCount - 1} queued)";
        return text;
    }

    private static float InfoLine(string label, string value, Color col, float x, float y, float w, int fs)
    {
        float lw = Ui.Measure(label, fs, true) + Ui.P(5);
        Ui.Text(label, x, y, fs, Ui.TextFaint, true);
        Ui.Text(Ui.Fit(value, w - lw, fs), x + lw, y, fs, col);
        return y + fs + Ui.P(4);
    }

    /// <summary>Effective max health (veterancy), falling back to the blueprint value when the snapshot has none.</summary>
    public static float MaxHealthOf(in UnitView v, UnitBlueprint bp) => v.MaxHealth > 0 ? v.MaxHealth : bp.MaxHealth;

    /// <summary>"+20% HP, +2 HP/s" for a veterancy level.</summary>
    public static string VeterancyBonusText(UnitBlueprint bp, int level) =>
        $"+{Veterancy.HealthPerLevel * level * 100:0}% HP, +{Ui.Num(level * Veterancy.RegenPerLevel(bp))} HP/s";

    /// <summary>"+25% mass · -30% energy use", or null without adjacency bonuses.</summary>
    public static string? AdjacencyText(in UnitView v) =>
        AdjacencyPreview.Describe(new Rts.Sim.Systems.AdjacencyEffect(v.AdjMassProd, v.AdjEnergyProd, v.AdjEnergyUse)) is { Length: > 0 } s ? s : null;

    /// <summary>Pause glyph in the icon's bottom-right corner.</summary>
    private static void DrawPausedBadge(Rectangle iconR)
    {
        float s = Ui.P(14);
        var b = new Rectangle(iconR.X + iconR.Width - s - Ui.P(2), iconR.Y + iconR.Height - s - Ui.P(2), s, s);
        Raylib.DrawRectangleRec(b, new Color(0, 0, 0, 200));
        Raylib.DrawRectangleLinesEx(b, 1, Ui.Warn);
        float bw = s * 0.22f, bh = s * 0.6f;
        Raylib.DrawRectangleV(new Vector2(b.X + s * 0.24f, b.Y + s * 0.2f), new Vector2(bw, bh), Ui.Warn);
        Raylib.DrawRectangleV(new Vector2(b.X + s * 0.54f, b.Y + s * 0.2f), new Vector2(bw, bh), Ui.Warn);
    }

    /// <summary>Name of the unit an engineer is capturing: the unit at its capture order's target position, else the
    /// visible unit being captured whose progress matches.</summary>
    private string CaptureTargetName(SimSnapshot snap, in UnitView v)
    {
        Vector3? at = null;
        for (int o = 0; o < v.OrderCount; o++)
            if (snap.Orders[v.OrderStart + o].Type == OrderType.Capture) { at = snap.Orders[v.OrderStart + o].Pos; break; }
        int best = -1;
        float bestScore = float.MaxValue;
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var t = ref snap.Units[k];
            if (t.Army == v.Army) continue;
            float score;
            if (at is Vector3 p && Vector2.Distance(new Vector2(p.X, p.Z), new Vector2(t.Pos.X, t.Pos.Z)) is float d && d < _ctx.Db[t.Bp].Radius + 3f) score = d;
            else if (t.CaptureFrac > 0) score = 100 + MathF.Abs(t.CaptureFrac - v.WorkProgress) * 100 + Vector3.Distance(t.Pos, v.Pos) * 0.01f;
            else continue;
            if (score < bestScore) { bestScore = score; best = k; }
        }
        return best >= 0 ? _ctx.Db[snap.Units[best].Bp].Name : "";
    }

    private static List<(string label, string value, Color color)> KeyStats(UnitBlueprint bp, in UnitView v)
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
        // Production / upkeep including the adjacency bonuses.
        if (e.ProductionMass > 0) l.Add(("Mass", "+" + Ui.Num(e.ProductionMass * (1 + v.AdjMassProd)) + "/s", Ui.MassColor));
        if (e.ProductionEnergy > 0) l.Add(("Energy", "+" + Ui.Num(e.ProductionEnergy * (1 + v.AdjEnergyProd)) + "/s", Ui.EnergyColor));
        if (e.MaintenanceEnergy > 0) l.Add(("Upkeep", "-" + Ui.Num(e.MaintenanceEnergy * (1 - v.AdjEnergyUse)) + " E/s", Ui.Bad));
        if (e.StorageMass > 0) l.Add(("Mass store", Ui.Num(e.StorageMass), Ui.MassColor));
        if (e.StorageEnergy > 0) l.Add(("Energy store", Ui.Num(e.StorageEnergy), Ui.EnergyColor));
        return l;
    }

    private static readonly float[] TypeButtonSizes = [46f, 38f, 31f];

    private void DrawMulti(Rectangle r, SimSnapshot snap, List<UnitView> views)
    {
        // Group by blueprint: commander first, then engineers, then mobile, then structures; stable by bp index.
        var groups = views.GroupBy(v => v.Bp)
            .Select(g => (bp: g.Key, army: g.First().Army, count: g.Count(), hp: g.Sum(v => v.HealthFrac * MaxHealthOf(v, _ctx.Db[v.Bp])), max: g.Sum(v => MaxHealthOf(v, _ctx.Db[v.Bp]))))
            .OrderBy(g => SortKey(_ctx.Db[g.bp])).ThenBy(g => g.bp).ToList();

        float pad = Ui.P(8);
        float y = Ui.PanelTitle(r, $"{views.Count} units selected  ·  {groups.Count} types");
        float totalHp = groups.Sum(g => g.hp), totalMax = groups.Sum(g => g.max);
        float barH = Ui.P(12);
        Ui.Bar(new Rectangle(r.X + pad, y, r.Width - 2 * pad, barH), totalMax > 0 ? totalHp / totalMax : 0, new Color(70, 200, 70, 255), $"Health {totalHp / MathF.Max(1, totalMax) * 100:0}%");
        y += barH + Ui.P(5);
        y = DrawMultiSummary(r.X + pad, y, r.Width - 2 * pad, views);
        y = CargoPanel.DrawMulti(_ctx, snap, views, r.X + pad, y, r.Width - 2 * pad, barH);

        // Type buttons: full size if they fit, else smaller ones before the "+N" overflow button.
        float gap = Ui.P(4), btn = Ui.P(46);
        int cols = 1, rows = 1;
        foreach (float size in TypeButtonSizes)
        {
            btn = Ui.P(size);
            cols = Math.Max(1, (int)((r.Width - 2 * pad + gap) / (btn + gap)));
            rows = Math.Max(1, (int)((r.Y + r.Height - pad - y + gap) / (btn + gap)));
            if (groups.Count <= cols * rows) break;
        }
        int maxShown = cols * rows;
        bool small = btn < Ui.P(40);
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
            UnitIcons.Draw(br, bp, compact: small);
            // Count badge.
            string cnt = g.count.ToString();
            int fs = Ui.F(small ? 9.5f : 11);
            float cw = Ui.Measure(cnt, fs, true) + Ui.P(6);
            var badge = new Rectangle(br.X + br.Width - cw - 2, br.Y + 2, cw, fs + Ui.P(2));
            Raylib.DrawRectangleRec(badge, new Color(0, 0, 0, 190));
            Ui.Text(cnt, badge.X + Ui.P(3), badge.Y + Ui.P(1) - 1, fs, Color.White, true);
            // Health sliver.
            float hf = g.max > 0 ? g.hp / g.max : 0;
            Raylib.DrawRectangle((int)br.X + 2, (int)(br.Y + br.Height - 4), (int)((br.Width - 4) * hf), 2, hf > 0.5f ? Ui.Good : hf > 0.25f ? Ui.Warn : Ui.Bad);

            if (st.Left)
            {
                if (Ui.Ctrl) _sel.SelectTypeOnScreen(g.bp, g.army);
                else if (Ui.Shift) _sel.RemoveType(g.bp);
                else _sel.NarrowToType(g.bp);
            }
            else if (st.Right) _sel.RemoveType(g.bp);
            if (st.Hover)
                Ui.Tooltip(br, new List<TipLine>
                {
                    new($"{bp.Name}  ×{g.count}", Ui.TextColor, 13, true),
                    new($"Health {g.hp:0} / {g.max:0}", Ui.TextDim, 11.5f),
                    new("Click: select only these", Ui.TextFaint, 11),
                    new("Shift+click / right-click: deselect them", Ui.TextFaint, 11),
                    new("Ctrl+click: add every one on screen", Ui.TextFaint, 11),
                });
        }
    }

    /// <summary>One line under the group health bar: idle mobile units and the fire states of the armed ones
    /// (colored like the Fire order button). Returns the y below it.</summary>
    private float DrawMultiSummary(float x, float y, float w, List<UnitView> views)
    {
        int idle = 0, armed = 0;
        Span<int> fire = stackalloc int[3];
        foreach (var v in views)
        {
            var bp = _ctx.Db[v.Bp];
            if (bp.IsMobile && (v.Flags & UnitFlags.UnderConstruction) == 0 && v.OrderCount == 0 && v.WorkMode == BuilderMode.None) idle++;
            if (bp.Weapons.Length > 0 && (uint)v.FireState < 3u) { fire[(int)v.FireState]++; armed++; }
        }
        int fs = Ui.F(11);
        float cx = x;
        void Part(string text, Color col)
        {
            if (cx + Ui.Measure(text, fs) > x + w) return;
            Ui.Text(text, cx, y, fs, col);
            cx += Ui.Measure(text, fs) + Ui.P(10);
        }
        Part(idle > 0 ? $"Idle {idle}" : "None idle", idle > 0 ? Ui.Warn : Ui.TextFaint);
        if (armed > 0)
        {
            if (fire[(int)FireState.FireAtWill] > 0) Part($"At will {fire[(int)FireState.FireAtWill]}", Ui.TextDim);
            if (fire[(int)FireState.ReturnFire] > 0) Part($"Return fire {fire[(int)FireState.ReturnFire]}", Ui.Warn);
            if (fire[(int)FireState.HoldFire] > 0) Part($"Hold fire {fire[(int)FireState.HoldFire]}", Ui.Bad);
        }
        return y + fs + Ui.P(6);
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
