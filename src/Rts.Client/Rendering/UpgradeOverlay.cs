using System.Numerics;
using Raylib_cs;
using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>
/// Screen-space cue for structures upgrading in place (<see cref="UnitView.UpgradeBp"/> &gt;= 0): a green progress bar
/// with an up-arrow and the target tier just under the health-bar slot. The 3D cue (the target model growing as a
/// hologram around the structure) is drawn by <see cref="UnitRenderer"/>.
/// </summary>
public static class UpgradeOverlay
{
    private static readonly Color Green = new(120, 235, 130, 255);

    public static void Draw(GameContext ctx, SimSnapshot cur, SimSnapshot? prev, float alpha)
    {
        if (ctx.Camera.Distance > IconRenderer.IconDistance) return;
        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            if (v.UpgradeBp < 0 || (uint)v.UpgradeBp >= (uint)ctx.Db.Count) continue;
            var to = ctx.Db[v.UpgradeBp];
            var (pos, _) = UnitRenderer.Interpolate(v, prev, alpha);
            var sp = Raylib.GetWorldToScreen(pos + new Vector3(0, ctx.Db[v.Bp].Visual.Size.Y + 1.0f, 0), ctx.Camera.Camera);
            if (sp.X < -50 || sp.Y < -50 || sp.X > Raylib.GetScreenWidth() + 50 || sp.Y > Raylib.GetScreenHeight() + 50) continue;
            float w = Math.Clamp(to.Visual.Size.X * 2400f / ctx.Camera.Distance / 40f, 24f, 72f);
            int x = (int)(sp.X - w / 2), y = (int)sp.Y + 6;
            Raylib.DrawRectangle(x - 1, y - 1, (int)w + 2, 6, Color.Black);
            Raylib.DrawRectangle(x, y, (int)(w * Math.Clamp(v.UpgradeFrac, 0, 1)), 4, Green);
            // Up-arrow + target tier to the left of the bar.
            var c = new Vector2(x - 7, y + 2);
            Raylib.DrawTriangle(c + new Vector2(0, -5), c + new Vector2(-4, 0), c + new Vector2(4, 0), Green);
            Raylib.DrawRectangle((int)c.X - 1, (int)c.Y, 3, 4, Green);
            Raylib.DrawText($"T{to.Tech}", x + (int)w + 4, y - 3, 10, Green);
        }
    }
}
