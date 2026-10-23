using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;

namespace Rts.Client.UI;

/// <summary>Build-menu category tabs for structures (SupCom-like grouping).</summary>
public enum BuildTab { Economy, Factory, Defense, Intel, Misc }

/// <summary>
/// Procedural "art" for blueprints: role color, short label, strategic glyph, build tab, and stat helpers
/// used by tooltips. Everything is derived from blueprint categories so new units get sensible icons.
/// </summary>
public static class UnitIcons
{
    public static BuildTab TabOf(UnitBlueprint bp)
    {
        if (bp.Has(UnitCategory.Factory)) return BuildTab.Factory;
        if (bp.HasAny(UnitCategory.Defense | UnitCategory.Shield | UnitCategory.Wall | UnitCategory.Strategic)) return BuildTab.Defense;
        if (bp.HasAny(UnitCategory.Radar | UnitCategory.Sonar) || (bp.Has(UnitCategory.Intel) && bp.IsStructure)) return BuildTab.Intel;
        if (bp.HasAny(UnitCategory.Economy | UnitCategory.MassExtractor | UnitCategory.MassFabricator | UnitCategory.PowerGenerator
                      | UnitCategory.MassStorage | UnitCategory.EnergyStorage | UnitCategory.HydroCarbon)) return BuildTab.Economy;
        if (bp.IsStructure && bp.HasAny(UnitCategory.Artillery)) return BuildTab.Defense;
        return BuildTab.Misc;
    }

    public static string TabName(BuildTab t) => t switch
    {
        BuildTab.Economy => "Economy",
        BuildTab.Factory => "Factory",
        BuildTab.Defense => "Defense",
        BuildTab.Intel => "Intel",
        _ => "Other",
    };

    public static Color RoleColor(UnitBlueprint bp)
    {
        if (bp.Has(UnitCategory.Commander)) return new Color(230, 190, 70, 255);
        if (bp.HasAny(UnitCategory.MassExtractor | UnitCategory.MassStorage | UnitCategory.MassFabricator)) return new Color(80, 190, 80, 255);
        if (bp.HasAny(UnitCategory.PowerGenerator | UnitCategory.EnergyStorage | UnitCategory.HydroCarbon)) return new Color(215, 180, 50, 255);
        if (bp.Has(UnitCategory.Factory)) return new Color(105, 135, 175, 255);
        if (bp.IsStructure && bp.HasAny(UnitCategory.Defense | UnitCategory.Shield | UnitCategory.Wall)) return new Color(200, 80, 70, 255);
        if (bp.HasAny(UnitCategory.Radar | UnitCategory.Sonar) || (bp.IsStructure && bp.Has(UnitCategory.Intel))) return new Color(70, 180, 200, 255);
        if (bp.Has(UnitCategory.Engineer)) return new Color(215, 140, 60, 255);
        if (bp.Has(UnitCategory.Air)) return new Color(110, 150, 220, 255);
        if (bp.Has(UnitCategory.Naval)) return new Color(60, 110, 190, 255);
        return new Color(140, 160, 110, 255);
    }

    /// <summary>Short label printed on icons (e.g. MEX, PGEN, TANK).</summary>
    private static string StrategicLabel(UnitBlueprint bp) => bp.Silo?.Kind switch
    {
        SiloKind.Tactical => "TML",
        SiloKind.Strategic => "NUKE",
        SiloKind.AntiNuke => "A-NUKE",
        _ => "TMD",
    };

    public static string ShortLabel(UnitBlueprint bp)
    {
        if (bp.Has(UnitCategory.Commander)) return "ACU";
        if (bp.Has(UnitCategory.MassExtractor)) return "MEX";
        if (bp.Has(UnitCategory.MassFabricator)) return "FAB";
        if (bp.Has(UnitCategory.HydroCarbon)) return "HYDRO";
        if (bp.Has(UnitCategory.PowerGenerator)) return "PGEN";
        if (bp.Has(UnitCategory.MassStorage)) return "M-STOR";
        if (bp.Has(UnitCategory.EnergyStorage)) return "E-STOR";
        if (bp.Has(UnitCategory.Factory))
            return bp.Has(UnitCategory.Air) ? "AIR FAC" : bp.Has(UnitCategory.Naval) ? "NAV FAC" : "LAND FAC";
        if (bp.IsStructure)
        {
            if (bp.Silo != null || bp.MissileDefense != null) return StrategicLabel(bp);
            if (bp.Has(UnitCategory.Shield)) return "SHIELD";
            if (bp.Has(UnitCategory.Wall)) return "WALL";
            if (bp.Has(UnitCategory.AntiAir)) return "AA";
            if (bp.HasAny(UnitCategory.Artillery | UnitCategory.IndirectFire)) return "ARTY";
            if (bp.Has(UnitCategory.AntiNaval)) return "TORP";
            if (bp.Has(UnitCategory.Defense)) return "PD";
            if (bp.Intel.OmniRadius > 0) return "OMNI";
            if (bp.Has(UnitCategory.Radar)) return "RADAR";
            if (bp.Has(UnitCategory.Sonar)) return "SONAR";
        }
        else
        {
            if (bp.Shield != null) return "M-SHLD";
            if (bp.Has(UnitCategory.Engineer)) return "ENG";
            if (bp.Has(UnitCategory.Air))
            {
                if (bp.Has(UnitCategory.Scout)) return "SCOUT";
                if (bp.Has(UnitCategory.Bomber)) return bp.Has(UnitCategory.AntiNaval) ? "T-BOMB" : "BOMB";
                if (bp.Has(UnitCategory.Fighter)) return "INT";
                if (bp.Has(UnitCategory.Gunship)) return "GUN";
                if (bp.Has(UnitCategory.Transport)) return "TRANS";
                return "AIR";
            }
            if (bp.Has(UnitCategory.Naval)) return NavalLabel(bp);
            if (bp.Has(UnitCategory.Scout)) return "SCOUT";
            if (bp.HasAny(UnitCategory.Artillery | UnitCategory.IndirectFire)) return "ARTY";
            if (bp.Has(UnitCategory.AntiAir)) return "AA";
            if (bp.Has(UnitCategory.DirectFire)) return "TANK";
        }
        var words = bp.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "?" : words[^1].Length <= 6 ? words[^1].ToUpperInvariant() : words[^1][..5].ToUpperInvariant();
    }

    /// <summary>Ship class label: by hull shape, else by layer/tech (sub, frigate, destroyer/cruiser, battleship).</summary>
    private static string NavalLabel(UnitBlueprint bp) => Rendering.UnitModels.BaseShape(bp.Visual.Shape) switch
    {
        "frigate" => "FRIG",
        "sub" => "SUB",
        "destroyer" => "DEST",
        "cruiser" => "CRUIS",
        "battleship" => "BSHIP",
        _ when bp.Movement.Layer == MotionLayer.Sub => "SUB",
        _ when bp.Has(UnitCategory.Tech3) => "BSHIP",
        _ when bp.Has(UnitCategory.Tech2) => bp.Has(UnitCategory.AntiAir) ? "CRUIS" : "DEST",
        _ => "FRIG",
    };

    /// <summary>Draws a square build/selection icon for the blueprint into r (background, glyph, label, tech).</summary>
    public static void Draw(Rectangle r, UnitBlueprint bp, bool enabled = true, bool compact = false)
    {
        var role = RoleColor(bp);
        if (!enabled) role = ColorUtil.Lerp(role, new Color(60, 60, 60, 255), 0.7f);
        var inner = Ui.Inset(r, 2);
        Raylib.DrawRectangleGradientV((int)inner.X, (int)inner.Y, (int)inner.Width, (int)inner.Height,
            ColorUtil.WithAlpha(ColorUtil.Scale(role, 0.55f), 230), ColorUtil.WithAlpha(ColorUtil.Scale(role, 0.22f), 230));

        // Glyph (strategic-icon shape) in the upper part.
        float labelH = compact ? 0 : Ui.F(10) + Ui.P(3);
        var c = new Vector2(r.X + r.Width * 0.5f, r.Y + (r.Height - labelH) * 0.5f + (compact ? 0 : Ui.P(1)));
        float s = MathF.Min(r.Width, r.Height - labelH) * 0.26f;
        var fill = ColorUtil.Scale(role, 1.25f);
        var dark = new Color(10, 12, 16, 255);
        DrawGlyph(c, s, bp, fill, dark);

        if (!compact)
        {
            string label = ShortLabel(bp);
            int fs = Ui.F(10);
            while (fs > 7 && Ui.Measure(label, fs, true) > r.Width - 4) fs--;
            float w = Ui.Measure(label, fs, true);
            Ui.Text(label, r.X + (r.Width - w) * 0.5f, r.Y + r.Height - fs - Ui.P(4), fs, enabled ? Color.White : Ui.TextFaint, true);
        }
        // Tech level in the top-left.
        if (!bp.Has(UnitCategory.Commander) && !compact)
        {
            string tl = bp.Has(UnitCategory.Experimental) ? "EXP" : "T" + bp.Tech;
            Ui.Text(tl, r.X + Ui.P(4), r.Y + Ui.P(3), Ui.F(9), new Color(230, 230, 230, enabled ? 200 : 100), true);
        }
    }

    /// <summary>Strategic glyph: square = structure, circle = land, triangle = air, diamond = commander.</summary>
    public static void DrawGlyph(Vector2 c, float s, UnitBlueprint bp, Color fill, Color outline)
    {
        if (bp.Has(UnitCategory.Commander))
        {
            Raylib.DrawPoly(c, 4, s * 1.25f + 2, 0, outline);
            Raylib.DrawPoly(c, 4, s * 1.25f, 0, fill);
        }
        else if (bp.IsStructure)
        {
            Raylib.DrawRectangleV(c - new Vector2(s + 2, s + 2), new Vector2(2 * s + 4, 2 * s + 4), outline);
            Raylib.DrawRectangleV(c - new Vector2(s, s), new Vector2(2 * s, 2 * s), fill);
            DrawInnerMark(c, s, bp, outline);
        }
        else if (bp.Has(UnitCategory.Air))
        {
            Raylib.DrawPoly(c, 3, s * 1.3f + 2.5f, -90, outline);
            Raylib.DrawPoly(c, 3, s * 1.3f, -90, fill);
        }
        else
        {
            Raylib.DrawCircleV(c, s + 2, outline);
            Raylib.DrawCircleV(c, s, fill);
            DrawInnerMark(c, s, bp, outline);
        }
    }

    private static void DrawInnerMark(Vector2 c, float s, UnitBlueprint bp, Color col)
    {
        float k = s * 0.45f;
        if (bp.Has(UnitCategory.MassExtractor) || bp.Has(UnitCategory.MassStorage))
            Raylib.DrawPoly(c, 4, k, 45, col);
        else if (bp.HasAny(UnitCategory.PowerGenerator | UnitCategory.EnergyStorage))
        {
            // Lightning bolt-ish zigzag.
            Raylib.DrawLineEx(c + new Vector2(k * 0.4f, -k * 1.2f), c + new Vector2(-k * 0.5f, k * 0.1f), MathF.Max(1.5f, s * 0.18f), col);
            Raylib.DrawLineEx(c + new Vector2(-k * 0.5f, k * 0.1f), c + new Vector2(k * 0.5f, -k * 0.1f), MathF.Max(1.5f, s * 0.18f), col);
            Raylib.DrawLineEx(c + new Vector2(k * 0.5f, -k * 0.1f), c + new Vector2(-k * 0.4f, k * 1.2f), MathF.Max(1.5f, s * 0.18f), col);
        }
        else if (bp.Has(UnitCategory.Factory))
        {
            Raylib.DrawRectangleV(c - new Vector2(k, k * 0.2f), new Vector2(2 * k, k * 1.1f), col);
            Raylib.DrawRectangleV(c - new Vector2(k, k), new Vector2(k * 0.6f, k), col);
        }
        else if (bp.HasAny(UnitCategory.Radar | UnitCategory.Sonar))
        {
            Raylib.DrawRing(c, k * 0.6f, k * 0.9f, 0, 360, 16, col);
            Raylib.DrawCircleV(c, k * 0.3f, col);
        }
        else if (bp.Has(UnitCategory.AntiAir))
            Raylib.DrawPoly(c, 3, k, -90, col);
        else if (bp.HasAny(UnitCategory.Artillery | UnitCategory.IndirectFire))
        {
            // Mortar arc over a dot.
            Raylib.DrawRing(c + new Vector2(0, k * 0.35f), k * 0.75f, k * 0.75f + MathF.Max(1.5f, s * 0.14f), 180, 360, 12, col);
            Raylib.DrawCircleV(c + new Vector2(0, k * 0.35f), k * 0.3f, col);
        }
        else if (bp.Has(UnitCategory.Engineer))
        {
            // Hammer.
            Raylib.DrawRectangleV(c - new Vector2(k * 0.9f, k), new Vector2(1.8f * k, k * 0.55f), col);
            Raylib.DrawRectangleV(c - new Vector2(k * 0.2f, k), new Vector2(k * 0.4f, 2 * k), col);
        }
        else if (bp.Has(UnitCategory.Scout))
            Raylib.DrawRing(c, k * 0.4f, k * 0.4f + MathF.Max(1.5f, s * 0.14f), 0, 360, 16, col);
        else if (bp.HasAny(UnitCategory.DirectFire | UnitCategory.Defense))
        {
            // Turret with a barrel.
            Raylib.DrawCircleV(c + new Vector2(0, k * 0.25f), k * 0.5f, col);
            Raylib.DrawLineEx(c + new Vector2(0, k * 0.25f), c + new Vector2(0, -k * 1.15f), MathF.Max(2f, s * 0.2f), col);
        }
    }

    // ------------------------------------------------------------------ stats for tooltips

    public static float Dps(UnitBlueprint bp)
    {
        float d = 0;
        foreach (var w in bp.Weapons) d += w.Damage * w.RateOfFire * Math.Max(1, w.ProjectilesPerShot);
        return d;
    }

    public static float MaxRange(UnitBlueprint bp)
    {
        float r = 0;
        foreach (var w in bp.Weapons) r = MathF.Max(r, w.Range);
        return r;
    }

    /// <summary>Tooltip lines describing a blueprint as a build option.</summary>
    public static List<TipLine> BuildTooltip(UnitBlueprint bp, string? hotkey, float builderRate, int builderCount, string? extraHint = null)
    {
        var lines = new List<TipLine>
        {
            new(bp.Name + (bp.Has(UnitCategory.Commander) ? "" : bp.Has(UnitCategory.Experimental) ? "  · EXP"
                : bp.Name.StartsWith($"T{bp.Tech} ") ? "" : $"  · T{bp.Tech}")
                + (hotkey != null ? $"   [{hotkey}]" : ""), Ui.TextColor, 14, true),
            new("Mass ", Ui.TextDim, 12.5f, true, [
                (Ui.Num(bp.Economy.BuildCostMass), Ui.MassColor),
                ("    Energy ", Ui.TextDim),
                (Ui.Num(bp.Economy.BuildCostEnergy), Ui.EnergyColor),
                ("    Time ", Ui.TextDim),
                (Ui.Num(bp.Economy.BuildTime), Ui.TextColor),
            ]),
        };
        if (builderRate > 0)
            lines.Add(new TipLine($"≈ {Ui.Seconds(bp.Economy.BuildTime / builderRate)} with {(builderCount > 1 ? $"{builderCount} builders" : "1 builder")} (build power {Ui.Num(builderRate)})", Ui.TextDim, 11.5f));
        if (!string.IsNullOrWhiteSpace(bp.Description)) lines.Add(new TipLine(bp.Description, new Color(200, 215, 230, 255), 12));
        var stats = StatSummary(bp);
        if (stats.Length > 0) lines.Add(new TipLine(stats, Ui.TextDim, 11.5f));
        if (extraHint != null) { lines.Add(new TipLine("", Ui.TextDim, 6)); lines.Add(new TipLine(extraHint, Ui.TextFaint, 11)); }
        return lines;
    }

    /// <summary>Compact one-line stats: HP, production, storage, weapons, speed.</summary>
    public static string StatSummary(UnitBlueprint bp)
    {
        var parts = new List<string> { $"HP {Ui.Num(bp.MaxHealth)}" };
        var e = bp.Economy;
        if (e.ProductionMass > 0) parts.Add($"+{Ui.Num(e.ProductionMass)} mass/s");
        if (e.ProductionEnergy > 0) parts.Add($"+{Ui.Num(e.ProductionEnergy)} energy/s");
        if (e.MaintenanceEnergy > 0) parts.Add($"-{Ui.Num(e.MaintenanceEnergy)} energy/s");
        if (e.StorageMass > 0 && !bp.Has(UnitCategory.Commander)) parts.Add($"stores {Ui.Num(e.StorageMass)} mass");
        if (e.StorageEnergy > 0 && !bp.Has(UnitCategory.Commander)) parts.Add($"stores {Ui.Num(e.StorageEnergy)} energy");
        if (e.BuildRate > 0) parts.Add($"build power {Ui.Num(e.BuildRate)}");
        if (bp.Weapons.Length > 0) parts.Add($"DPS {Ui.Num(Dps(bp))}, range {Ui.Num(MaxRange(bp))}");
        if (bp.IsMobile && bp.Movement.MaxSpeed > 0) parts.Add($"speed {bp.Movement.MaxSpeed:0.#}");
        if (bp.Intel.RadarRadius > 0) parts.Add($"radar {Ui.Num(bp.Intel.RadarRadius)}");
        if (bp.IsStructure) parts.Add($"{bp.FootprintX}x{bp.FootprintZ}");
        if (e.RequiresMassDeposit) parts.Add("needs mass deposit");
        if (e.RequiresHydroDeposit) parts.Add("needs hydrocarbon deposit");
        return string.Join("  ·  ", parts);
    }
}
