using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Commander visuals: ACU Overcharge (muzzle burst <c>EffectKind.Overcharge</c>, the big glowing shot with an energy
/// trail for projectiles flagged <see cref="ProjectileFlags.Overcharge"/>, impact burst + shockwave ring
/// <c>EffectKind.OverchargeHit</c>; both raised in addition to the regular muzzle flash / explosion) and the commander
/// death blast: an <c>EffectKind.Nuke</c> whose blueprint has a <see cref="UnitBlueprint.DeathBlast"/> (a real nuke's
/// blueprint is its silo) gets the shorter <see cref="NukeFx"/> commander variant plus a cyan energy burst.
/// Energy colour: white-yellow core, outer glow tinted by the army colour.
/// </summary>
public sealed partial class EffectsRenderer
{
    /// <summary>Is this nuke effect a commander's death blast (blueprint with a DeathBlast block)?</summary>
    public static bool IsCommanderBlast(BlueprintDb db, int bp) => (uint)bp < (uint)db.Count && db[bp].DeathBlast != null;

    private void SpawnNuke(in EffectEvent e)
    {
        bool acu = IsCommanderBlast(_ctx.Db, e.BlueprintIndex);
        _nuke.Spawn(e.Pos, e.Size, acu); // always, even off-screen (NukeFx)
        if (acu && FrameView.Current.SphereVisible(e.Pos, e.Size * 2f + 10f)) CommanderBlastFx(e.Pos, e.Size);
    }

    /// <summary>Overcharge muzzle / impact; false for every other kind.</summary>
    private bool SpawnOvercharge(in EffectEvent e)
    {
        if (e.Kind is not (EffectKind.Overcharge or EffectKind.OverchargeHit)) return false;
        float s = Math.Clamp(e.Size, 1f, 8f);
        if (!FrameView.Current.SphereVisible(e.Pos, s * 4f + 6f)) return true;
        var outer = OcColor(e.Army);
        if (e.Kind == EffectKind.Overcharge) OverchargeMuzzleFx(e.Pos, s, outer);
        else OverchargeHitFx(e.Pos, s, outer);
        return true;
    }

    /// <summary>Outer glow of Overcharge effects: energy yellow mixed with the army colour.</summary>
    private Vector4 OcColor(int army)
    {
        var t = ArmyTint(army, 1.4f);
        return new Vector4(0.5f + 0.5f * t.X, 0.42f + 0.5f * t.Y, 0.2f + 0.45f * t.Z, 1f);
    }

    private static readonly Vector4 OcCore = new(1f, 0.97f, 0.85f, 1f);

    private void OverchargeMuzzleFx(Vector3 pos, float s, Vector4 outer)
    {
        _ps.Flash(pos, s * 2.4f, outer with { W = 0.95f }, 0.22f);
        _ps.Flash(pos, s * 1.1f, OcCore, 0.12f);
        // Camera-facing energy ring bursting out of the barrel.
        ref var ring = ref _ps.Emit();
        ring.Pos = pos; ring.Life = 0.28f; ring.Size0 = s * 0.4f; ring.Size1 = s * 2.6f;
        ring.C0 = outer with { W = 0.8f }; ring.C1 = outer with { W = 0 }; ring.Shape = PShape.Ring; ring.Blend = PBlend.Additive;
        ElectricSparks(pos, Num(10 + s * 2), 6f + s * 1.5f, 0f);
    }

    private void OverchargeHitFx(Vector3 pos, float s, Vector4 outer)
    {
        float gy = _ps.GroundAt(pos);
        var g = new Vector3(pos.X, gy, pos.Z);
        // Blinding energy burst, then a softer lingering glow.
        _ps.Flash(pos + new Vector3(0, 0.3f, 0), s * 4.2f, outer with { W = 1f }, 0.3f);
        _ps.Flash(pos + new Vector3(0, 0.3f, 0), s * 1.8f, OcCore, 0.16f);
        ref var glow = ref _ps.Emit();
        glow.Pos = pos + new Vector3(0, 0.4f, 0); glow.Life = 0.9f; glow.Size0 = s * 2.2f; glow.Size1 = s * 3.2f;
        glow.C0 = outer with { W = 0.45f }; glow.C1 = outer with { W = 0 }; glow.Shape = PShape.Glow; glow.Blend = PBlend.Additive;
        // Shockwave: a flat ring racing over the ground and a camera-facing one around the core.
        if (pos.Y - gy < 2.5f) _ps.Ring(g, s * 0.4f, s * 3.2f, 0.45f, outer with { W = 0.7f });
        ref var bubble = ref _ps.Emit();
        bubble.Pos = pos; bubble.Life = 0.32f; bubble.Size0 = s * 0.6f; bubble.Size1 = s * 3f;
        bubble.C0 = OcCore with { W = 0.75f }; bubble.C1 = outer with { W = 0 }; bubble.Shape = PShape.Ring; bubble.Blend = PBlend.Additive;
        // Energy sparks, and a short crackle where it hit.
        ElectricSparks(pos, Num(14 + s * 4), 7f + s * 2f, 0f);
        _ps.Sparks(pos, Num(8 + s * 3), 6f * MathF.Sqrt(s), outer, 0.5f, 0.1f, 4f);
        for (int i = 0; i < 4; i++)
        {
            float delay = _ps.Range(0.05f, 0.6f);
            var at = pos + _ps.RandDir() * s * 0.5f;
            ref var f = ref _ps.Emit();
            f.Pos = at; f.Age = -delay; f.Life = _ps.Range(0.07f, 0.13f); f.Size0 = s * _ps.Range(0.5f, 0.9f); f.Size1 = f.Size0 * 0.5f;
            f.C0 = OcCore; f.C1 = outer with { W = 0 }; f.Shape = PShape.Glow; f.Blend = PBlend.Additive;
            if (i % 2 == 0) ElectricSparks(at, Num(4), 4f, delay);
        }
    }

    /// <summary>Overcharge shot: a big white-hot ball in an army-tinted halo with a glowing energy trail.</summary>
    private void DrawOverchargeProjectile(int id, Vector3 pos, Vector3 vel, int army, FrameView fv)
    {
        float speed = vel.Length();
        var dir = speed > 1e-3f ? vel / speed : Vector3.UnitY;
        if (!fv.SphereVisible(pos, 5f)) return;
        ProjectileCount++;
        float zs = Math.Clamp(Atmosphere.CameraDistance / 90f, 1f, 3f);
        float flicker = 0.85f + 0.15f * MathF.Sin(_frame * 2.3f + id * 0.37f);
        var o = OcColor(army);
        var outer = new Color((byte)(o.X * 255), (byte)(o.Y * 255), (byte)(o.Z * 255), (byte)150);
        _proj.Billboard(pos, 3.2f * zs * flicker, 0, fv.CamRight, fv.CamUp, outer, PShape.Glow, 0);
        _proj.Streak(pos, dir, 4.2f * zs, 0.9f * zs, fv.CamPos, outer with { A = 200 }, PShape.Streak, 0);
        _proj.Billboard(pos, 1.3f * zs, 0, fv.CamRight, fv.CamUp, new Color(255, 250, 225, 255), PShape.Glow, 0);
        _proj.Streak(pos, dir, 2.4f * zs, 0.35f * zs, fv.CamPos, new Color(255, 255, 240, 240), PShape.Streak, 0);
        EnergyTrail(id, pos, 0.35f, o, 0.55f * zs);
    }

    /// <summary>Additive glow puffs along the path travelled since last frame (shares the smoke trails' table).</summary>
    private void EnergyTrail(int id, Vector3 pos, float spacing, Vector4 color, float size)
    {
        if (_trails.TryGetValue(id, out var last))
        {
            var d = pos - last.pos;
            float len = d.Length();
            if (len > 30f) { _trails[id] = (pos, _frame); return; }
            int n = Math.Min(24, (int)(len / spacing));
            if (n == 0) { _trails[id] = (last.pos, _frame); return; }
            for (int k = 1; k <= n; k++)
            {
                ref var p = ref _ps.Emit();
                p.Pos = last.pos + d * (k / (float)n) + _ps.RandDir() * 0.08f;
                p.Vel = _ps.RandDir() * 0.3f;
                p.Life = _ps.Range(0.25f, 0.45f); p.Size0 = size; p.Size1 = size * 0.25f; p.Drag = 2f;
                p.C0 = color with { W = 0.7f }; p.C1 = color with { W = 0 }; p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
            }
            _trails[id] = (pos, _frame);
        }
        else _trails[id] = (pos, _frame);
    }

    /// <summary>Commander death blast extras on top of the <see cref="NukeFx"/> commander variant: the quantum core
    /// bursting (cyan-white flash, a fast cyan ground ring, electric sparks and a crackle around the core).</summary>
    private void CommanderBlastFx(Vector3 pos, float size)
    {
        float s = Math.Clamp(size, 8f, 60f);
        float gy = _ps.GroundAt(pos);
        var g = new Vector3(pos.X, gy, pos.Z);
        var cyan = Col(0.55f, 0.85f, 1f, 0.9f);
        _ps.Flash(g + new Vector3(0, s * 0.3f, 0), s * 1.6f, cyan, 0.45f);
        _ps.Flash(g + new Vector3(0, s * 0.15f, 0), s * 0.6f, Col(0.9f, 0.97f, 1f, 1f), 0.25f);
        _ps.Ring(g, s * 0.2f, s * 2.3f, 0.6f, Col(0.5f, 0.82f, 1f, 0.75f));
        _ps.Ring(g, s * 0.1f, s * 1.3f, 0.9f, Col(0.75f, 0.92f, 1f, 0.5f));
        ElectricSparks(g + new Vector3(0, 1f, 0), Num(40), s * 0.7f, 0f);
        for (int i = 0; i < 10; i++)
        {
            float delay = _ps.Range(0f, 1.4f);
            var at = g + new Vector3(0, _ps.Range(0.5f, s * 0.25f), 0) + RandHoriz() * _ps.Range(0.5f, s * 0.3f);
            ref var f = ref _ps.Emit();
            f.Pos = at; f.Age = -delay; f.Life = _ps.Range(0.08f, 0.16f); f.Size0 = _ps.Range(2f, 4f); f.Size1 = f.Size0 * 0.5f;
            f.C0 = Col(0.8f, 0.93f, 1f, 1f); f.C1 = Col(0.4f, 0.65f, 1f, 0); f.Shape = PShape.Glow; f.Blend = PBlend.Additive;
            if (i % 2 == 0) ElectricSparks(at, Num(8), 6f, delay);
        }
    }
}
