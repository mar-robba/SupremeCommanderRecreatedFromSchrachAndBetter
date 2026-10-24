using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Personal Teleporter visuals (Commander enhancement, sim <c>TeleportSystem</c>).
/// Charge (every unit with <see cref="UnitView.TeleportFrac"/> &gt;= 0, enemies too since the snapshot is fog-filtered):
/// a ground ring growing with the charge, rings collapsing onto the commander, a body glow, a light column in the last
/// half and rising sparks (emitted on a game-time accumulator, so nothing is emitted while paused). Own units also get
/// a faint ring + beam at the destination (front <see cref="OrderView"/> of type Teleport).
/// Events: <c>TeleportCharge</c> = small burst when the charge starts, <c>TeleportOut</c> = implosion flash at the origin,
/// <c>TeleportIn</c> = flash + ring burst + light column at the arrival. Colour: energy cyan tinted by the army colour.
/// Per-frame quads go to the projectile batch (additive); no per-frame allocations.
/// </summary>
public sealed partial class EffectsRenderer
{
    private float _tpAcc;
    private static readonly Vector4 TpCore = new(0.9f, 0.98f, 1f, 1f);

    /// <summary>Teleport energy colour: cyan mixed with the army colour.</summary>
    private Vector4 TpColor(int army)
    {
        var t = ArmyTint(army, 1.3f);
        return new Vector4(0.25f + 0.4f * t.X, 0.5f + 0.4f * t.Y, 0.75f + 0.25f * t.Z, 1f);
    }

    private static float TpFrac(float x) => x - MathF.Floor(x);

    /// <summary>TeleportCharge / TeleportOut / TeleportIn; false for every other kind.</summary>
    private bool SpawnTeleport(in EffectEvent e)
    {
        if (e.Kind is not (EffectKind.TeleportCharge or EffectKind.TeleportOut or EffectKind.TeleportIn)) return false;
        float s = Math.Clamp(e.Size, 0.8f, 6f);
        if (!FrameView.Current.SphereVisible(e.Pos, s * 8f + 14f)) return true;
        float h = (uint)e.BlueprintIndex < (uint)_ctx.Db.Count ? MathF.Max(1f, _ctx.Db[e.BlueprintIndex].Visual.Size.Y) : 2f * s;
        var c = TpColor(e.Army);
        var g = new Vector3(e.Pos.X, _ps.GroundAt(e.Pos), e.Pos.Z);
        var mid = g + new Vector3(0, h * 0.55f, 0);
        switch (e.Kind)
        {
            case EffectKind.TeleportCharge:
                _ps.Flash(mid, s * 3f, c with { W = 0.8f }, 0.25f);
                _ps.Ring(g, s * 0.5f, s * 4.5f, 0.5f, c with { W = 0.75f });
                ElectricSparks(mid, Num(12), 5f, 0f);
                break;
            case EffectKind.TeleportOut: TeleportOutFx(g, mid, s, h, c); break;
            default: TeleportInFx(g, mid, s, h, c); break;
        }
        return true;
    }

    /// <summary>Origin: everything collapses into the commander, then a flash and a column of light.</summary>
    private void TeleportOutFx(Vector3 g, Vector3 mid, float s, float h, Vector4 c)
    {
        _ps.Flash(mid, s * 6f, c with { W = 0.95f }, 0.3f);
        _ps.Flash(mid, s * 2.5f, TpCore, 0.18f);
        ref var ring = ref _ps.Emit();
        ring.Pos = mid; ring.Life = 0.32f; ring.Size0 = s * 7f; ring.Size1 = s * 0.3f;
        ring.C0 = c with { W = 0.3f }; ring.C1 = TpCore with { W = 0.9f }; ring.Shape = PShape.Ring; ring.Blend = PBlend.Additive;
        ref var gr = ref _ps.Emit();
        gr.Pos = g + new Vector3(0, 0.12f, 0); gr.Life = 0.4f; gr.Size0 = s * 6f; gr.Size1 = s * 0.4f;
        gr.C0 = c with { W = 0.25f }; gr.C1 = c with { W = 0.85f }; gr.Shape = PShape.Ring; gr.Mode = PMode.Flat; gr.Blend = PBlend.Additive;
        // Energy sucked into the core.
        int n = Num(24);
        for (int i = 0; i < n; i++)
        {
            var dir = _ps.RandDir();
            float rad = s * _ps.Range(2.5f, 4.5f), life = _ps.Range(0.22f, 0.34f);
            ref var p = ref _ps.Emit();
            p.Pos = mid + dir * rad; p.Vel = -dir * (rad / life); p.Life = life;
            p.Size0 = 0.22f; p.Size1 = 0.08f; p.Stretch = 0.04f;
            p.C0 = c with { W = 0.3f }; p.C1 = TpCore with { W = 1f };
            p.Shape = PShape.Streak; p.Mode = PMode.Streak; p.Blend = PBlend.Additive;
        }
        LightColumn(g, s, h, c, 0.25f);
        ElectricSparks(mid, Num(10), 4f, 0.25f);
        ref var glow = ref _ps.Emit();
        glow.Pos = g + new Vector3(0, 0.3f, 0); glow.Age = -0.2f; glow.Life = 1.2f; glow.Size0 = s * 3f; glow.Size1 = s * 1.5f;
        glow.C0 = c with { W = 0.35f }; glow.C1 = c with { W = 0 }; glow.Shape = PShape.Glow; glow.Blend = PBlend.Additive;
    }

    /// <summary>Arrival: blinding flash, rings racing over the ground, a column of light and crackling sparks.</summary>
    private void TeleportInFx(Vector3 g, Vector3 mid, float s, float h, Vector4 c)
    {
        _ps.Flash(mid, s * 7f, c with { W = 1f }, 0.35f);
        _ps.Flash(mid, s * 3f, TpCore, 0.2f);
        _ps.Ring(g, s * 0.5f, s * 7f, 0.55f, c with { W = 0.85f });
        _ps.Ring(g, s * 0.3f, s * 4f, 0.8f, TpCore with { W = 0.5f });
        ref var bubble = ref _ps.Emit();
        bubble.Pos = mid; bubble.Life = 0.3f; bubble.Size0 = s; bubble.Size1 = s * 6f;
        bubble.C0 = TpCore with { W = 0.85f }; bubble.C1 = c with { W = 0 }; bubble.Shape = PShape.Ring; bubble.Blend = PBlend.Additive;
        LightColumn(g, s, h, c, 0f);
        ElectricSparks(mid, Num(26), 7f, 0f);
        _ps.Sparks(mid, Num(10), 6f, c, 0.6f, 0.1f, 4f);
        ref var glow = ref _ps.Emit();
        glow.Pos = g + new Vector3(0, 0.4f, 0); glow.Life = 1f; glow.Size0 = s * 3f; glow.Size1 = s * 4f;
        glow.C0 = c with { W = 0.4f }; glow.C1 = c with { W = 0 }; glow.Shape = PShape.Glow; glow.Blend = PBlend.Additive;
        for (int i = 0; i < 3; i++) ElectricSparks(mid + _ps.RandDir() * s, Num(5), 4f, _ps.Range(0.1f, 0.7f));
    }

    /// <summary>A short-lived column of glow puffs shooting up from the ground.</summary>
    private void LightColumn(Vector3 g, float s, float h, Vector4 c, float delay)
    {
        for (int k = 0; k < 6; k++)
        {
            ref var p = ref _ps.Emit();
            p.Pos = g + new Vector3(0, h * (0.3f + k * 0.9f), 0); p.Vel = new Vector3(0, 6f + k * 3f, 0); p.Age = -delay;
            p.Life = 0.35f + k * 0.04f; p.Size0 = s * 2.2f * (1f - k * 0.12f); p.Size1 = p.Size0 * 0.3f;
            p.C0 = Vector4.Lerp(c, TpCore, 0.4f) with { W = 0.7f }; p.C1 = c with { W = 0 }; p.Shape = PShape.Glow; p.Blend = PBlend.Additive;
        }
    }

    /// <summary>Per-frame charge visuals; call while the projectile batch is open (between _proj.Begin and Draw).</summary>
    private void TeleportFrame(float dt, SimSnapshot cur, SimSnapshot? prev, float alpha, FrameView fv)
    {
        float step = 0;
        if (dt > 0) { _tpAcc += dt; if (_tpAcc >= 0.05f) { step = MathF.Min(_tpAcc, 0.25f); _tpAcc = 0; } }
        float time = Atmosphere.Time;
        for (int i = 0; i < cur.UnitCount; i++)
        {
            ref var v = ref cur.Units[i];
            if (v.TeleportFrac < 0 || (uint)v.Bp >= (uint)_ctx.Db.Count) continue;
            var bp = _ctx.Db[v.Bp];
            float f = Math.Clamp(v.TeleportFrac, 0f, 1f);
            float s = Math.Clamp(bp.Radius, 0.8f, 6f), h = MathF.Max(1f, bp.Visual.Size.Y);
            float t = time + v.Index * 0.37f;
            var c = TpColor(v.Army);
            if (_ctx.Controls(v.Army) && v.OrderCount > 0 && cur.Orders[v.OrderStart].Type == OrderType.Teleport)
                TeleportDestFrame(cur.Orders[v.OrderStart].Pos, s, f, c, t, fv);
            var pos = UnitRenderer.Interpolate(v, prev, alpha).pos;
            if (fv.SphereVisible(pos, s * 6f + h)) TeleportChargeFrame(pos, s, h, f, c, t, step, fv);
        }
    }

    private void TeleportChargeFrame(Vector3 pos, float s, float h, float f, Vector4 c, float t, float step, FrameView fv)
    {
        var g = new Vector3(pos.X, _ps.GroundAt(pos) + 0.45f, pos.Z); // lifted: flat rings clip into slopes
        var mid = pos + new Vector3(0, h * 0.55f, 0);
        float pulse = 0.5f + 0.5f * MathF.Sin(t * (4f + 8f * f));
        // Ground ring growing with the charge, pulsing faster as it fills.
        _proj.Flat(g, s * (3f + 2.5f * f) * (0.94f + 0.06f * pulse), t * 0.8f, ToColor(c with { W = 0.35f + 0.45f * f }), PShape.Ring, 0);
        // Two phase-shifted rings collapsing onto the commander.
        for (int k = 0; k < 2; k++)
        {
            float ph = TpFrac(t * (0.6f + 0.9f * f) + k * 0.5f);
            float a = (1 - ph) * ph * 4f;
            _proj.Flat(g + new Vector3(0, 0.05f * (k + 1), 0), s * ((7f + 3f * f) * (1 - ph) + 1f), 0,
                ToColor(c with { W = a * (0.25f + 0.5f * f) }), PShape.Ring, 0);
        }
        // Body glow + white core, brighter and bigger with the charge.
        _proj.Billboard(mid, s * (2.2f + 3.5f * f) * (0.9f + 0.1f * pulse), 0, fv.CamRight, fv.CamUp, ToColor(c with { W = 0.18f + 0.45f * f }), PShape.Glow, 0);
        _proj.Billboard(mid, s * (0.8f + 1.4f * f), 0, fv.CamRight, fv.CamUp, ToColor(TpCore with { W = 0.15f + 0.6f * f * pulse }), PShape.Glow, 0);
        // Light column building up in the second half.
        if (f > 0.5f)
        {
            float k = (f - 0.5f) * 2f, len = h * (1.5f + 4f * k);
            _proj.Streak(g + new Vector3(0, len, 0), Vector3.UnitY, len, s * (0.6f + 1.2f * k), fv.CamPos, ToColor(c with { W = 0.5f * k }), PShape.Streak, 0);
        }
        if (step <= 0) return;
        // Rising sparks spiralling in around the commander.
        int n = Num(step * (14f + 50f * f));
        for (int i = 0; i < n; i++)
        {
            var off = RandHoriz() * s * _ps.Range(0.5f, 1.6f);
            ref var p = ref _ps.Emit();
            p.Pos = g + off + new Vector3(0, _ps.Range(0f, h * 0.4f), 0);
            p.Vel = new Vector3(-off.X * 0.4f, _ps.Range(2.5f, 5f) * (1f + f), -off.Z * 0.4f);
            p.Life = _ps.Range(0.4f, 0.8f); p.Size0 = _ps.Range(0.1f, 0.18f); p.Size1 = 0.03f; p.Drag = 0.6f; p.Stretch = 0.06f;
            p.C0 = Vector4.Lerp(c, TpCore, _ps.Rand()) with { W = 1f }; p.C1 = c with { W = 0 };
            p.Shape = PShape.Streak; p.Mode = PMode.Streak; p.Blend = PBlend.Additive;
        }
        if (_ps.Rand() < step * (0.5f + 4f * f)) ElectricSparks(mid + _ps.RandDir() * s * 0.6f, Num(4 + 6 * f), 3f + 3f * f, 0f);
    }

    /// <summary>Destination marker of an own charging commander: ring, inner pulse, glow and a beam growing with the charge.</summary>
    private void TeleportDestFrame(Vector3 dest, float s, float f, Vector4 c, float t, FrameView fv)
    {
        var g = new Vector3(dest.X, _ps.GroundAt(dest) + 0.45f, dest.Z);
        if (!fv.SphereVisible(g, s * 6f + 30f)) return;
        float pulse = 0.5f + 0.5f * MathF.Sin(t * 3f);
        _proj.Flat(g, s * (4f + 0.4f * pulse), -t * 0.6f, ToColor(c with { W = 0.25f + 0.35f * f }), PShape.Ring, 0);
        float ph = TpFrac(t * 0.7f);
        _proj.Flat(g + new Vector3(0, 0.05f, 0), s * (1f + 4f * ph), 0, ToColor(c with { W = (1 - ph) * (0.2f + 0.3f * f) }), PShape.Ring, 0);
        float hgt = 4f + 26f * f;
        _proj.Streak(g + new Vector3(0, hgt, 0), Vector3.UnitY, hgt, s * (0.25f + 0.9f * f), fv.CamPos, ToColor(c with { W = 0.12f + 0.4f * f }), PShape.Streak, 0);
        _proj.Billboard(g + new Vector3(0, 0.5f, 0), s * (1.5f + 2f * f), 0, fv.CamRight, fv.CamUp, ToColor(c with { W = 0.1f + 0.35f * f }), PShape.Glow, 0);
    }
}
