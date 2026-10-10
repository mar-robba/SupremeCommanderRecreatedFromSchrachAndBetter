using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// SupCom strategic zoom: when zoomed out, units are drawn as constant-size screen-space icons from the
/// procedural <see cref="IconAtlas"/> (frame shape = domain, glyph = role from Visual.Icon, pips = tech level,
/// white border = selected), tinted with the army color. Icons fade in while meshes dither out (crossfade).
/// All icons are drawn in one batched draw call, structures first and air on top.
/// </summary>
public static unsafe class IconRenderer
{
    /// <summary>Camera distance above which icons start fading in.</summary>
    public const float IconDistance = 140f;
    /// <summary>Camera distance above which 3D meshes are hidden (they dither out over the last 70 units).</summary>
    public const float HideMeshDistance = 260f;

    private static QuadBatch? _batch;
    private static int[] _kind = [];
    private static float[] _size = [];
    private static int[] _layer = [];
    private static readonly List<int>[] Buckets = [new(), new(), new(), new()];

    public static float IconFade(float camDistance) => Math.Clamp((camDistance - IconDistance) / 50f, 0, 1);

    private static void Init(BlueprintDb db)
    {
        _batch ??= new QuadBatch(Shaders.Icon.Shader);
        _batch.Material.Maps[(int)MaterialMapIndex.Albedo].Texture = IconAtlas.Texture;
        int n = db.Count;
        _kind = new int[n];
        _size = new float[n];
        _layer = new int[n];
        for (int i = 0; i < n; i++)
        {
            var bp = db[i];
            _kind[i] = IconAtlas.KindOf(bp.Visual.Icon);
            float s = bp.Has(UnitCategory.Experimental) ? 54
                : bp.Has(UnitCategory.Commander) ? 40
                : bp.IsStructure ? (bp.Has(UnitCategory.Factory) ? 34 : 28)
                : 23 + (bp.Tech - 1) * 4;
            _size[i] = s;
            _layer[i] = bp.Has(UnitCategory.Commander) || bp.Has(UnitCategory.Experimental) ? 3
                : bp.IsStructure ? 0
                : bp.Movement.Layer == MotionLayer.Air ? 2 : 1;
        }
    }

    public static void Draw(GameContext ctx, SimSnapshot cur, SimSnapshot? prev, float alpha, HashSet<long> selected)
    {
        float fade = IconFade(ctx.Camera.Distance);
        if (fade <= 0.001f) return;
        if (_batch == null || _kind.Length != ctx.Db.Count) Init(ctx.Db);
        var fv = FrameView.Current;
        foreach (var b in Buckets) b.Clear();
        for (int k = 0; k < cur.UnitCount; k++)
        {
            int bp = cur.Units[k].Bp;
            Buckets[(uint)bp < (uint)_layer.Length ? _layer[bp] : 1].Add(k);
        }

        var batch = _batch!;
        batch.Begin();
        byte a = (byte)(255 * fade);
        foreach (var bucket in Buckets)
        foreach (int k in bucket)
        {
            ref var v = ref cur.Units[k];
            var (pos, _) = UnitRenderer.Interpolate(v, prev, alpha);
            if (!fv.WorldToScreen(pos, out var sp)) continue;
            float s = _size[v.Bp];
            if (sp.X < -s || sp.Y < -s || sp.X > fv.ScreenW + s || sp.Y > fv.ScreenH + s) continue;
            bool sel = selected.Contains(((long)v.Index << 32) | (uint)v.Gen);
            var bp = ctx.Db[v.Bp];
            var col = ctx.ArmyColors[v.Army];
            if ((v.Flags & UnitFlags.UnderConstruction) != 0) col = ColorUtil.Lerp(col, new Color(150, 210, 255, 255), 0.6f);
            batch.Screen(new Vector2(MathF.Round(sp.X), MathF.Round(sp.Y)), s, ColorUtil.WithAlpha(col, a), IconAtlas.Uv(_kind[v.Bp], bp.Tech, sel));
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        batch.Draw();
        Rlgl.EnableBackfaceCulling();
    }
}
