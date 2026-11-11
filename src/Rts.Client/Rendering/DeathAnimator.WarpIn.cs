using System.Numerics;
using Rts.Client.Audio;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b) ACU warp-in at the start of a game: when the first snapshot this client sees is at most
/// <see cref="WarpMaxStartTick"/>, every visible commander arrives with a light column and flash, expanding rings,
/// rising sparks and the warp sound, its model materializing bottom-up as a hologram (<see cref="WarpFrac"/>, drawn
/// by <see cref="UnitRenderer"/>) until it turns solid with a second flash. A loaded save or a replay seek starts
/// later than that, so it never plays there. Purely visual: the sim does not wait for it.
/// </summary>
public sealed partial class DeathAnimator
{
    public const int WarpMaxStartTick = 5;
    /// <summary>Flash at <see cref="WarpDelay"/>, hologram rising for <see cref="WarpRise"/>, effects end at <see cref="WarpDuration"/> (s).</summary>
    public const float WarpDelay = 0.35f, WarpRise = 1.5f, WarpDuration = 2.6f;

    private struct Warp
    {
        public int Index, Gen;
        public Vector3 Pos;
        public float T, Height, Radius, EmitAcc;
    }

    private readonly Warp[] _warps = new Warp[16];
    private int _warpCount;
    private bool _warpChecked;

    /// <summary>Warp-ins playing.</summary>
    public int WarpCount => _warpCount;

    /// <summary>0..1 materialization of a warping commander (bottom-up hologram cut); false = not warping (draw normally).</summary>
    public static bool WarpProgress(float t, out float frac)
    {
        frac = Math.Clamp((t - WarpDelay) / WarpRise, 0f, 1f);
        return t < WarpDelay + WarpRise;
    }

    public bool WarpFrac(int index, int gen, out float frac)
    {
        frac = 1f;
        for (int i = 0; i < _warpCount; i++)
            if (_warps[i].Index == index && _warps[i].Gen == gen) return WarpProgress(_warps[i].T, out frac);
        return false;
    }

    private void ClearWarps() => _warpCount = 0;

    private void CheckWarpIn(SimSnapshot cur)
    {
        if (_warpChecked) return;
        _warpChecked = true;
        if (cur.Tick > WarpMaxStartTick) return; // loaded save, replay seek: the commanders are already on the field
        var db = _ctx.Db;
        for (int k = 0; k < cur.UnitCount && _warpCount < _warps.Length; k++)
        {
            ref var v = ref cur.Units[k];
            if ((uint)v.Bp >= (uint)db.Count || (v.Flags & UnitFlags.UnderConstruction) != 0) continue;
            var bp = db[v.Bp];
            if (!bp.Has(UnitCategory.Commander)) continue;
            float h = MathF.Max(1f, bp.Visual.Size.Y), r = MathF.Max(1f, MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) * 0.5f);
            if (Models != null) { var m = Models.ModelFor(bp); h = MathF.Max(1f, m.MaxY - m.MinY); r = MathF.Max(1f, m.Radius); }
            _warps[_warpCount++] = new Warp { Index = v.Index, Gen = v.Gen, Pos = v.Pos, Height = h, Radius = r };
            UnitFxSounds.Play(SoundId.WarpIn, v.Pos, 1f, 1f, minGain: v.Army == _ctx.LocalArmy ? 0.3f : 0f);
        }
    }

    private void UpdateWarps(float dt)
    {
        if (dt <= 0f) return;
        for (int i = 0; i < _warpCount; i++)
        {
            ref var w = ref _warps[i];
            float t0 = w.T;
            w.T += dt;
            var p = w.Pos;
            var cyan = new Vector4(0.45f, 0.82f, 1f, 1f);
            if (t0 < WarpDelay && w.T >= WarpDelay)
            {
                // Arrival: light column, flash, two rings.
                for (int k = 0; k < 10; k++)
                    _ps.Flash(p + new Vector3(0, k * w.Height * 0.55f, 0), w.Radius * (2.2f - k * 0.12f), cyan with { W = 0.75f }, 0.7f);
                _ps.Flash(p + new Vector3(0, w.Height * 0.5f, 0), w.Radius * 7f, new Vector4(0.85f, 0.95f, 1f, 1f), 0.3f);
                _ps.Ring(p, w.Radius * 0.6f, w.Radius * 9f, 0.9f, cyan with { W = 0.7f });
                _ps.Ring(p, w.Radius * 0.3f, w.Radius * 5f, 1.4f, new Vector4(0.8f, 0.95f, 1f, 0.5f));
            }
            if (w.T > WarpDelay && w.T < WarpDelay + WarpRise)
            {
                w.EmitAcc += dt;
                if (w.EmitAcc >= 0.04f)
                {
                    w.EmitAcc = 0;
                    WarpProgress(w.T, out float frac);
                    float y = p.Y + w.Height * frac;
                    for (int k = 0; k < 3; k++)
                    {
                        ref var f = ref _ps.Emit();
                        float ang = _ps.Rand() * MathF.Tau;
                        f.Pos = new Vector3(p.X + MathF.Cos(ang) * w.Radius * 1.1f, y, p.Z + MathF.Sin(ang) * w.Radius * 1.1f);
                        f.Vel = new Vector3(-MathF.Sin(ang) * 2f, _ps.Range(1.5f, 3.5f), MathF.Cos(ang) * 2f);
                        f.Life = _ps.Range(0.4f, 0.7f); f.Size0 = 0.35f; f.Size1 = 0.05f; f.Drag = 1f;
                        f.C0 = cyan; f.C1 = cyan with { W = 0 }; f.Shape = PShape.Glow; f.Blend = PBlend.Additive;
                    }
                    _ps.Flash(p + new Vector3(0, w.Height * frac, 0), w.Radius * 1.6f, cyan with { W = 0.5f }, 0.08f);
                }
            }
            if (t0 < WarpDelay + WarpRise && w.T >= WarpDelay + WarpRise)
            {
                // Solid: second flash, a burst of sparks and a fast ring.
                _ps.Flash(p + new Vector3(0, w.Height * 0.5f, 0), w.Radius * 5f, new Vector4(0.8f, 0.95f, 1f, 1f), 0.22f);
                _ps.Sparks(p + new Vector3(0, w.Height * 0.5f, 0), 30, 10f, cyan, 0.6f, 0.1f, 4f);
                _ps.Ring(p, w.Radius, w.Radius * 6f, 0.6f, cyan with { W = 0.6f });
            }
            if (w.T >= WarpDuration) _warps[i--] = _warps[--_warpCount];
        }
    }
}
