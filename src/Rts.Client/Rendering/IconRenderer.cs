using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// SupCom strategic zoom: when zoomed out, units are drawn as screen-space icons whose shape encodes the role
/// (circle = land, triangle = air, square = structure, diamond = commander) and color the army.
/// TODO(client): proper icon atlas with role glyphs, tech-level pips, selection highlight.
/// </summary>
public static class IconRenderer
{
    /// <summary>Camera distance above which icons are shown.</summary>
    public const float IconDistance = 140f;
    /// <summary>Camera distance above which 3D meshes are hidden.</summary>
    public const float HideMeshDistance = 260f;

    public static void Draw(GameContext ctx, SimSnapshot cur, SimSnapshot? prev, float alpha, HashSet<long> selected)
    {
        if (ctx.Camera.Distance < IconDistance) return;
        float fade = Math.Clamp((ctx.Camera.Distance - IconDistance) / 40f, 0, 1);
        var cam = ctx.Camera.Camera;
        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            var bp = ctx.Db[v.Bp];
            var (pos, _) = UnitRenderer.Interpolate(v, prev, alpha);
            var sp = Raylib.GetWorldToScreen(pos, cam);
            var col = ColorUtil.WithAlpha(ctx.ArmyColors[v.Army], (byte)(255 * fade));
            var outline = selected.Contains(((long)v.Index << 32) | (uint)v.Gen) ? Color.White : ColorUtil.WithAlpha(Color.Black, (byte)(200 * fade));
            float s = bp.Has(UnitCategory.Experimental) ? 9 : bp.Tech switch { 1 => 4.5f, 2 => 5.5f, _ => 6.5f };
            if (bp.Has(UnitCategory.Commander))
            {
                Raylib.DrawPoly(sp, 4, s + 3, 0, outline);
                Raylib.DrawPoly(sp, 4, s + 1.5f, 0, col);
            }
            else if (bp.IsStructure)
            {
                Raylib.DrawRectangleV(sp - new Vector2(s + 1, s + 1), new Vector2(2 * s + 2, 2 * s + 2), outline);
                Raylib.DrawRectangleV(sp - new Vector2(s, s), new Vector2(2 * s, 2 * s), col);
            }
            else if (bp.Has(UnitCategory.Air))
            {
                Raylib.DrawPoly(sp, 3, s + 2, -90, outline);
                Raylib.DrawPoly(sp, 3, s + 0.5f, -90, col);
            }
            else
            {
                Raylib.DrawCircleV(sp, s + 1, outline);
                Raylib.DrawCircleV(sp, s, col);
            }
        }
    }
}
