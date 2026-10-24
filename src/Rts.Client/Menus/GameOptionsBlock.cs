using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim;

namespace Rts.Client.Menus;

/// <summary>
/// The GAME OPTIONS panel of the skirmish setup screen and the multiplayer lobby: victory, unit cap, start resources,
/// fog, game speed, cheats, prebuilt units, laid out in a grid of label + control cells. Editable: cycle buttons
/// (left click next, right click previous) and on/off toggles that change the given <see cref="GameOptions"/> in place;
/// read-only (lobby clients): the values as text. Hovering a cell shows what the choice does.
/// </summary>
public static class GameOptionsBlock
{
    public const int Items = 7;

    /// <summary>Panel height for <paramref name="columns"/> columns (base px scaled).</summary>
    public static float Height(int columns) => Ui.P(26) + Rows(columns) * (Ui.P(26) + Ui.P(5)) - Ui.P(5) + Ui.P(10);

    private static int Rows(int columns) => (Items + columns - 1) / columns;

    /// <summary>Draws the panel; returns true if an editable option changed this frame.</summary>
    public static bool Draw(Rectangle p, GameOptions o, bool editable, int columns, string? note = null)
    {
        Ui.Panel(p);
        Ui.PanelTitle(p, "Game options");
        if (note != null)
            Ui.TextRight(note, p.X + p.Width - Ui.P(10), p.Y + Ui.P(5), Ui.F(10.5f), Ui.TextFaint);
        float pad = Ui.P(12), colGap = Ui.P(14), rowH = Ui.P(26), rowGap = Ui.P(5);
        float colW = (p.Width - 2 * pad - (columns - 1) * colGap) / columns;
        float labelW = MathF.Min(Ui.P(84), colW * 0.42f);
        bool changed = false;
        for (int i = 0; i < Items; i++)
        {
            int c = i % columns, r = i / columns;
            float x = p.X + pad + c * (colW + colGap), y = p.Y + Ui.P(26) + r * (rowH + rowGap);
            var cell = Ui.R(x + labelW, y, colW - labelW, rowH);
            changed |= DrawItem(i, x, cell, o, editable);
        }
        int last = Items % columns;
        if (last != 0)
        {
            // Spare cells of the last row: what the chosen victory condition means.
            float x = p.X + pad + last * (colW + colGap), w = p.X + p.Width - pad - x;
            float y = p.Y + Ui.P(26) + (Rows(columns) - 1) * (rowH + rowGap);
            if (w >= Ui.P(220))
                Ui.Text(Ui.Fit(Describe(o.Victory).Replace('\n', ' '), w, Ui.F(10.5f)), x, y + (rowH - Ui.F(10.5f)) * 0.5f - 1, Ui.F(10.5f), Ui.TextDim);
        }
        return changed;
    }

    private static bool DrawItem(int i, float lx, Rectangle cell, GameOptions o, bool editable)
    {
        switch (i)
        {
            case 0:
            {
                int d = Item("Victory", lx, cell, GameOptions.Name(o.Victory), editable, VictoryColor(o.Victory),
                    "Victory condition", Describe(o.Victory));
                if (d != 0) o.Victory = Step(o.Victory, d);
                return d != 0;
            }
            case 1:
            {
                int d = Item("Unit cap", lx, cell, o.UnitCap.ToString(), editable, null,
                    "Unit cap", "Max units per army, structures included.\nFactories and engineers wait at the cap.");
                if (d != 0) o.UnitCap = NextCap(o.UnitCap, d);
                return d != 0;
            }
            case 2:
            {
                int d = Item("Resources", lx, cell, GameOptions.Name(o.Resources), editable, null,
                    "Start resources", Describe(o.Resources));
                if (d != 0) o.Resources = Step(o.Resources, d);
                return d != 0;
            }
            case 3:
            {
                int d = Item("Fog", lx, cell, GameOptions.Name(o.Fog), editable, o.Fog == FogMode.None ? Ui.Warn : null,
                    "Fog of war", Describe(o.Fog));
                if (d != 0) o.Fog = Step(o.Fog, d);
                return d != 0;
            }
            case 4:
            {
                int d = Item("Speed", lx, cell, GameOptions.Name(o.Speed), editable, null,
                    "Game speed", Describe(o.Speed));
                if (d != 0) o.Speed = Step(o.Speed, d);
                return d != 0;
            }
            case 5:
            {
                bool n = Toggle("Cheats", lx, cell, o.Cheats, editable,
                    "Cheats", "Debug spawns, the sandbox panel (F9) and console (F10) in a normal game.");
                if (n == o.Cheats) return false;
                o.Cheats = n;
                return true;
            }
            default:
            {
                bool n = Toggle("Prebuilt", lx, cell, o.Prebuilt, editable,
                    "Prebuilt units", "Each army starts with a small base:\na few mass extractors, power generators and a land factory.");
                if (n == o.Prebuilt) return false;
                o.Prebuilt = n;
                return true;
            }
        }
    }

    private static int Item(string label, float lx, Rectangle cell, string value, bool editable, Color? color, string tipTitle, string tip)
    {
        MenuUi.Label(label, lx, cell.Y, cell.Height);
        int d = 0;
        if (editable) d = MenuUi.Cycle(cell, value, true, color);
        else Ui.TextCentered(value, cell, Ui.F(12.5f), color ?? Ui.TextColor, true);
        if (Ui.Hover(cell)) Ui.Tooltip(cell, tipTitle, tip);
        return d;
    }

    private static bool Toggle(string label, float lx, Rectangle cell, bool on, bool editable, string tipTitle, string tip)
    {
        MenuUi.Label(label, lx, cell.Y, cell.Height);
        var r = Ui.R(cell.X, cell.Y, MathF.Min(cell.Width, Ui.P(64)), cell.Height);
        bool n = on;
        if (editable) n = MenuUi.Toggle(r, on);
        else Ui.TextCentered(on ? "ON" : "OFF", r, Ui.F(12.5f), on ? Ui.TextColor : Ui.TextDim, true);
        if (Ui.Hover(r)) Ui.Tooltip(r, tipTitle, tip);
        return n;
    }

    /// <summary>Next/previous enum value, wrapping.</summary>
    public static T Step<T>(T v, int d) where T : struct, Enum
    {
        var all = Enum.GetValues<T>();
        int i = Array.IndexOf(all, v);
        return all[((i + d) % all.Length + all.Length) % all.Length];
    }

    /// <summary>Next/previous unit cap of <see cref="GameOptions.UnitCaps"/>, wrapping; a cap outside the list (CLI)
    /// moves to the nearest choice in that direction.</summary>
    public static int NextCap(int cap, int d)
    {
        var caps = GameOptions.UnitCaps;
        int i = Array.IndexOf(caps, cap);
        if (i < 0)
        {
            if (d > 0) { foreach (int c in caps) if (c > cap) return c; return caps[0]; }
            for (int k = caps.Length - 1; k >= 0; k--) if (caps[k] < cap) return caps[k];
            return caps[^1];
        }
        return caps[((i + d) % caps.Length + caps.Length) % caps.Length];
    }

    private static Color? VictoryColor(VictoryCondition v) => v == VictoryCondition.Sandbox ? Ui.Warn : null;

    public static string Describe(VictoryCondition v) => v switch
    {
        VictoryCondition.Supremacy => "An army loses when it has no structure,\nengineer or commander left.",
        VictoryCondition.Annihilation => "An army loses when it has no unit left.",
        VictoryCondition.Sandbox => "Nobody is defeated and nobody wins.",
        _ => "An army loses when its commander dies;\nits other units self-destruct.",
    };

    public static string Describe(StartResources r) => r switch
    {
        StartResources.Low => "Half of the commander storage.",
        StartResources.High => "Full storage plus a bank of twice the storage\nthat refills it as it is spent.",
        _ => "Full commander storage (the classic start).",
    };

    public static string Describe(FogMode f) => f switch
    {
        FogMode.Explored => "The whole map starts explored;\nenemy units are still hidden outside vision.",
        FogMode.None => "No fog: everyone sees everything.",
        _ => "Unexplored black, explored grey, vision from units.",
    };

    public static string Describe(GameSpeedMode s) => s switch
    {
        GameSpeedMode.Normal => "Locked at 1x: speed keys disabled (pause still works).",
        GameSpeedMode.Fast => "Locked at 2x: speed keys disabled (pause still works).",
        _ => "Speed keys work (multiplayer: requests to the host).",
    };
}
