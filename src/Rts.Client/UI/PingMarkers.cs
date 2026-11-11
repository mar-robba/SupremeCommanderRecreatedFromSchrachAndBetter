using System.Numerics;
using Raylib_cs;
using Rts.Client.Rendering;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;

namespace Rts.Client.UI;

/// <summary>
/// (7b) Draws the snapshot's map pings (already only the viewer's team, all of them for observers / replays) on the
/// map (screen-space glyph at the ground point: "!" alert, chevron move, crosshair attack; pulsing rings for the first
/// 1.5 s, sender name under it, fades out over the last 2 s of its 10 s) and on the minimap, plus the minimap flashes
/// of <see cref="AlertLog"/> (attacked areas, lost structures...).
/// </summary>
public static class PingMarkers
{
    public static Color KindColor(PingKind k) => k switch
    {
        PingKind.Move => new Color(90, 190, 255, 255),
        PingKind.Attack => new Color(255, 80, 70, 255),
        _ => new Color(255, 210, 60, 255),
    };

    public static string KindName(PingKind k) => k switch { PingKind.Move => "move here", PingKind.Attack => "attack here", _ => "alert" };

    /// <summary>Flash color of an alert on the minimap.</summary>
    public static Color AlertColor(AlertKind k) => k switch
    {
        AlertKind.ConstructionDone or AlertKind.UpgradeDone => new Color(130, 230, 140, 255),
        AlertKind.AllyUnderAttack => new Color(255, 200, 90, 255),
        _ => new Color(255, 90, 60, 255),
    };

    /// <summary>Ping age in seconds (tick based) and its fade (1 → 0 over the last 2 s).</summary>
    private static (float age, float fade) Age(SimSnapshot s, in MapPingView p)
    {
        float age = (s.Tick - p.Tick) / 10f;
        float life = PingSystem.Lifetime / 10f;
        return (age, Math.Clamp((life - age) / 2f, 0f, 1f));
    }

    /// <summary>Map markers (after the 3D pass, under the HUD).</summary>
    public static void Draw2D(GameContext ctx, SimSnapshot s)
    {
        if (s.PingCount == 0) return;
        var fv = FrameView.Current;
        if (fv.ScreenW <= 0) return;
        float t = (float)Raylib.GetTime();
        for (int i = 0; i < s.PingCount; i++)
        {
            ref var p = ref s.Pings[i];
            if (!fv.WorldToScreen(p.Pos, out var c)) continue;
            var (age, fade) = Age(s, p);
            if (fade <= 0) continue;
            var col = KindColor(p.Kind);
            byte A(float a) => (byte)Math.Clamp(255 * a * fade, 0, 255);
            if (age < 1.5f)
                for (int k = 0; k < 2; k++)
                {
                    float u = (age / 0.75f + k * 0.5f) % 1f;
                    float r = Ui.P(14) + u * Ui.P(46);
                    Raylib.DrawRing(c, r - MathF.Max(2f, Ui.P(2.5f)), r, 0, 360, 40, ColorUtil.WithAlpha(col, A(1 - u)));
                }
            float pulse = 1f + 0.12f * MathF.Sin(t * 6f);
            float R = Ui.P(13) * pulse;
            Raylib.DrawCircleV(c, R + 2, new Color(0, 0, 0, (int)A(0.55f)));
            Raylib.DrawRing(c, R - MathF.Max(2f, Ui.P(2.5f)), R, 0, 360, 32, ColorUtil.WithAlpha(col, A(1)));
            DrawGlyph(p.Kind, c, R * 0.62f, ColorUtil.WithAlpha(col, A(1)));
            if (p.Army != ctx.LocalArmy && (uint)p.Army < (uint)s.Armies.Length)
            {
                string name = s.Armies[p.Army].Name;
                int fs = Ui.F(11);
                float w = Ui.Measure(name, fs);
                var ac = (uint)p.Army < (uint)ctx.ArmyColors.Length ? ctx.ArmyColors[p.Army] : Color.White;
                Ui.Text(name, c.X - w / 2, c.Y + R + Ui.P(3), fs, ColorUtil.WithAlpha(ac, A(1)));
            }
        }
    }

    private static void DrawGlyph(PingKind k, Vector2 c, float r, Color col)
    {
        float th = MathF.Max(2f, r * 0.28f);
        switch (k)
        {
            case PingKind.Move: // chevron pointing down
                Raylib.DrawLineEx(c + new Vector2(-r, -r * 0.45f), c + new Vector2(0, r * 0.55f), th, col);
                Raylib.DrawLineEx(c + new Vector2(r, -r * 0.45f), c + new Vector2(0, r * 0.55f), th, col);
                break;
            case PingKind.Attack: // crosshair
                Raylib.DrawLineEx(c + new Vector2(-r, 0), c + new Vector2(r, 0), th, col);
                Raylib.DrawLineEx(c + new Vector2(0, -r), c + new Vector2(0, r), th, col);
                Raylib.DrawCircleV(c, th * 0.9f, col);
                break;
            default: // "!"
                Raylib.DrawLineEx(c + new Vector2(0, -r), c + new Vector2(0, r * 0.3f), th * 1.15f, col);
                Raylib.DrawCircleV(c + new Vector2(0, r * 0.82f), th * 0.7f, col);
                break;
        }
    }

    /// <summary>Minimap: alert flashes (two expanding rings) and pings (pulsing ring + dot). Inside the minimap scissor.</summary>
    public static void DrawMinimap(SimSnapshot s, AlertLog log, Func<float, float, Vector2> toScreen)
    {
        double now = Raylib.GetTime();
        foreach (var (e, age) in log.Flashes(now))
        {
            if (e.Kind == AlertKind.Ping) continue; // drawn below from the snapshot
            var c = toScreen(e.Pos.X, e.Pos.Z);
            var col = AlertColor(e.Kind);
            for (int k = 0; k < 2; k++)
            {
                float u = (age * 4f + k * 0.5f) % 1f;
                float r = Ui.P(3) + u * Ui.P(13);
                Raylib.DrawRing(c, r - MathF.Max(1.5f, Ui.P(1.5f)), r, 0, 360, 24, ColorUtil.WithAlpha(col, (byte)(235 * (1 - u) * (1 - age * 0.5f))));
            }
        }
        float t = (float)now;
        for (int i = 0; i < s.PingCount; i++)
        {
            ref var p = ref s.Pings[i];
            var (_, fade) = Age(s, p);
            if (fade <= 0) continue;
            var c = toScreen(p.Pos.X, p.Pos.Z);
            var col = KindColor(p.Kind);
            float u = (t * 1.3f) % 1f;
            float r = Ui.P(4) + u * Ui.P(8);
            Raylib.DrawRing(c, r - MathF.Max(1.5f, Ui.P(1.5f)), r, 0, 360, 24, ColorUtil.WithAlpha(col, (byte)(240 * (1 - u) * fade)));
            Raylib.DrawCircleV(c, MathF.Max(2.5f, Ui.P(2.5f)) + 1, new Color(0, 0, 0, (int)(200 * fade)));
            Raylib.DrawCircleV(c, MathF.Max(2.5f, Ui.P(2.5f)), ColorUtil.WithAlpha(col, (byte)(255 * fade)));
        }
    }
}
