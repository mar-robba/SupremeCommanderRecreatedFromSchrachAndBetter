using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// Cargo / fuel rows of the unit panel: "Cargo 4/6" bar (slots) with tiny icons of what is inside for a carrier,
/// a fuel bar for aircraft, and the summed cargo / lowest fuel for several selected units.
/// </summary>
public static class CargoPanel
{
    private static readonly Color CargoCol = new(70, 175, 200, 255);
    private static readonly Color FuelCol = new(225, 160, 60, 255);

    /// <summary>One selected unit: cargo bar (+ icons) and/or fuel bar. Returns the y below what was drawn.</summary>
    public static float DrawSingle(GameContext ctx, SimSnapshot snap, in UnitView v, UnitBlueprint bp, float x, float y, float w, float h)
    {
        bool cargo = bp.Cargo != null && TransportInput.TryGetCargo(snap, v.Index, v.Gen, out _);
        bool fuel = v.Fuel >= 0;
        if (!cargo && !fuel) return y;
        TransportInput.TryGetCargo(snap, v.Index, v.Gen, out var cv);
        float gap = Ui.P(4);
        float cw = cargo && fuel ? (w - gap) * 0.62f : w;
        if (cargo) CargoBar(new Rectangle(x, y, cw, h), cv.Used, cv.Capacity, bp.Cargo!.Layer == CargoLayer.Air ? "Docked" : "Cargo");
        if (fuel) FuelBar(new Rectangle(cargo ? x + cw + gap : x, y, cargo ? w - cw - gap : w, h), v.Fuel, false);
        y += h + Ui.P(5);
        if (cargo && cv.Count > 0) y = Icons(ctx, snap, cv, x, y, w);
        return y;
    }

    /// <summary>Several selected units: summed cargo of the carriers and the lowest fuel. Returns the new y.</summary>
    public static float DrawMulti(GameContext ctx, SimSnapshot snap, List<UnitView> views, float x, float y, float w, float h)
    {
        int used = 0, cap = 0, carriers = 0;
        float minFuel = 2f;
        foreach (var v in views)
        {
            if (TransportInput.TryGetCargo(snap, v.Index, v.Gen, out var cv)) { used += cv.Used; cap += cv.Capacity; carriers++; }
            if (v.Fuel >= 0) minFuel = MathF.Min(minFuel, v.Fuel);
        }
        bool fuel = minFuel <= 1f;
        if (carriers == 0 && !fuel) return y;
        float gap = Ui.P(4);
        float cw = carriers > 0 && fuel ? (w - gap) * 0.62f : w;
        if (carriers > 0) CargoBar(new Rectangle(x, y, cw, h), used, cap, carriers == 1 ? "Cargo" : $"Cargo ({carriers})");
        if (fuel) FuelBar(new Rectangle(carriers > 0 ? x + cw + gap : x, y, carriers > 0 ? w - cw - gap : w, h), minFuel, true);
        return y + h + Ui.P(6);
    }

    private static void CargoBar(Rectangle r, int used, int cap, string what)
    {
        float f = cap > 0 ? Math.Clamp(used / (float)cap, 0f, 1f) : 0f;
        Ui.Bar(r, f, CargoCol, $"{what} {used}/{cap}");
        // Slot ticks (when they are wide enough to read).
        if (cap <= 1 || r.Width / cap < 5) return;
        for (int k = 1; k < cap; k++)
        {
            float sx = MathF.Round(r.X + r.Width * k / cap);
            Raylib.DrawLineV(new Vector2(sx, r.Y + 1), new Vector2(sx, r.Y + Ui.P(3)), new Color(0, 0, 0, 150));
        }
    }

    private static void FuelBar(Rectangle r, float fuel, bool lowest)
    {
        var col = fuel > 0.25f ? FuelCol : Ui.Bad;
        string label = r.Width > Ui.P(90) ? $"{(lowest ? "Fuel min" : "Fuel")} {fuel * 100:0}%" : $"Fuel {fuel * 100:0}%";
        Ui.Bar(r, fuel, col, label);
    }

    /// <summary>Row of tiny icons of the carried units (health sliver each), "+N" when they don't fit.</summary>
    private static float Icons(GameContext ctx, SimSnapshot snap, in CargoView cv, float x, float y, float w)
    {
        float s = Ui.P(17), gap = Ui.P(2);
        int fit = Math.Max(1, (int)((w + gap) / (s + gap)));
        int shown = cv.Count <= fit ? cv.Count : fit - 1;
        for (int i = 0; i < shown; i++)
        {
            ref var u = ref snap.CargoUnits[cv.Start + i];
            var r = new Rectangle(x + i * (s + gap), y, s, s);
            Raylib.DrawRectangleRec(r, new Color(8, 10, 14, 255));
            if ((uint)u.Bp < (uint)ctx.Db.Count) UnitIcons.Draw(r, ctx.Db[u.Bp], compact: true);
            Raylib.DrawRectangleLinesEx(r, 1, Ui.PanelBorder);
            float hf = Math.Clamp(u.HealthFrac, 0f, 1f);
            Raylib.DrawRectangle((int)r.X + 1, (int)(r.Y + r.Height - 3), (int)((r.Width - 2) * hf), 2, hf > 0.5f ? Ui.Good : hf > 0.25f ? Ui.Warn : Ui.Bad);
            if (Raylib.CheckCollisionPointRec(Ui.Mouse, r) && (uint)u.Bp < (uint)ctx.Db.Count)
            {
                string extra = u.Fuel >= 0 ? $"  ·  fuel {u.Fuel * 100:0}%" : "";
                Ui.Tooltip(r, ctx.Db[u.Bp].Name, $"Health {hf * 100:0}%{extra}  ·  {u.Size} slot{(u.Size == 1 ? "" : "s")}");
            }
        }
        if (shown < cv.Count)
        {
            var r = new Rectangle(x + shown * (s + gap), y, s, s);
            Raylib.DrawRectangleRec(r, new Color(8, 10, 14, 255));
            Ui.TextCentered($"+{cv.Count - shown}", r, Ui.F(10), Ui.TextDim, true);
        }
        return y + s + Ui.P(5);
    }
}

/// <summary>Button glyphs of the transport orders (orders panel).</summary>
public static class TransportGlyphs
{
    public static void Draw(TransportButtons b, Vector2 c, float s, float t, Color col)
    {
        switch (b)
        {
            case TransportButtons.Unload: // box with an arrow coming out of the bottom
                Raylib.DrawLineEx(c + new Vector2(-s, -s), c + new Vector2(s, -s), t, col);
                Raylib.DrawLineEx(c + new Vector2(-s, -s), c + new Vector2(-s, s * 0.15f), t, col);
                Raylib.DrawLineEx(c + new Vector2(s, -s), c + new Vector2(s, s * 0.15f), t, col);
                Raylib.DrawLineEx(c + new Vector2(0, -s * 0.6f), c + new Vector2(0, s * 0.5f), t, col);
                Raylib.DrawTriangle(c + new Vector2(-s * 0.55f, s * 0.35f), c + new Vector2(0, s * 1.05f), c + new Vector2(s * 0.55f, s * 0.35f), col);
                break;
            case TransportButtons.TransportTo: // cargo box carried along an arrow
                Raylib.DrawRectangleLinesEx(new Rectangle(c.X - s, c.Y - s * 0.95f, s * 0.9f, s * 0.8f), MathF.Max(1, t * 0.7f), col);
                Raylib.DrawLineEx(c + new Vector2(-s, s * 0.45f), c + new Vector2(s * 0.45f, s * 0.45f), t, col);
                Raylib.DrawTriangle(c + new Vector2(s * 0.3f, s * 0.05f), c + new Vector2(s * 0.3f, s * 0.85f), c + new Vector2(s * 1.05f, s * 0.45f), col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.55f, -s * 0.15f), c + new Vector2(-s * 0.55f, s * 0.45f), MathF.Max(1, t * 0.6f), col);
                break;
            case TransportButtons.Ferry: // beacon - drop with a shuttle arrow both ways
                Raylib.DrawCircleV(c + new Vector2(-s, 0), s * 0.28f, col);
                Raylib.DrawCircleLinesV(c + new Vector2(s, 0), s * 0.3f, col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.6f, -s * 0.5f), c + new Vector2(s * 0.45f, -s * 0.5f), t * 0.8f, col);
                Raylib.DrawTriangle(c + new Vector2(s * 0.35f, -s * 0.8f), c + new Vector2(s * 0.35f, -s * 0.2f), c + new Vector2(s * 0.85f, -s * 0.5f), col);
                Raylib.DrawLineEx(c + new Vector2(s * 0.6f, s * 0.5f), c + new Vector2(-s * 0.45f, s * 0.5f), t * 0.8f, col);
                Raylib.DrawTriangle(c + new Vector2(-s * 0.35f, s * 0.8f), c + new Vector2(-s * 0.35f, s * 0.2f), c + new Vector2(-s * 0.85f, s * 0.5f), col);
                break;
            default: // dock: landing pad "H" in a ring
                Raylib.DrawRing(c, s * 0.95f, s * 0.95f + MathF.Max(1, t * 0.7f), 0, 360, 24, col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.4f, -s * 0.55f), c + new Vector2(-s * 0.4f, s * 0.55f), t, col);
                Raylib.DrawLineEx(c + new Vector2(s * 0.4f, -s * 0.55f), c + new Vector2(s * 0.4f, s * 0.55f), t, col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.4f, 0), c + new Vector2(s * 0.4f, 0), t, col);
                break;
        }
    }
}
