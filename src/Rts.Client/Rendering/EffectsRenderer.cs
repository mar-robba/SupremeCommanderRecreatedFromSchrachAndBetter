using System.Numerics;
using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// Projectiles (from snapshots, interpolated as pos - vel * (1 - alpha) * tick so they stay in sync with the
/// one-tick-behind unit interpolation) and particle effects for sim EffectEvents.
/// Debug: F9 toggles a client-side fake battle (fake projectiles/impacts/deaths/build effects, also via CLI
/// --fxtest), F10 spawns a big explosion under the cursor.
/// </summary>
public sealed partial class EffectsRenderer : IDisposable
{
    private const float TickSeconds = 1f / SimWorld.TicksPerSecond;
    private readonly GameContext _ctx;
    private readonly ParticleSystem _ps;
    private readonly QuadBatch _proj;
    private readonly Dictionary<int, (Vector3 pos, int frame)> _trails = new();
    private readonly List<int> _stale = new();
    private readonly List<FakeProjectile> _fake = new();
    private int _frame;
    private float _fakeTimer, _fakeEventTimer;
    private int _fakeId = -1;

    public bool FakeBattle;
    public int ParticleCount => _ps.Count;
    /// <summary>The particle pool (other renderers may emit into it, e.g. smoking wrecks).</summary>
    public ParticleSystem Particles => _ps;
    public int ProjectileCount { get; private set; }

    private struct FakeProjectile
    {
        public int Id;
        public Vector3 Pos, Vel;
        public float Time, Gravity;
        public int Army, Bp;
        public string Visual;
    }

    public EffectsRenderer(GameContext ctx, bool fakeBattle = false)
    {
        _ctx = ctx;
        _ps = new ParticleSystem(ctx.Terrain);
        _proj = new QuadBatch(Shaders.Particle.Shader);
        FakeBattle = fakeBattle;
    }

    /// <summary>Frame time scaled by the game speed (0 while paused) for game-time animations.</summary>
    public static float GameDt(GameContext ctx, float dt) => ctx.Runner.Paused ? 0f : dt * ctx.Runner.Speed;

    private Vector4 ArmyTint(int army, float boost = 1.3f)
    {
        var c = (uint)army < (uint)_ctx.ArmyColors.Length ? _ctx.ArmyColors[army] : Color.White;
        return new Vector4(MathF.Min(1, c.R / 255f * boost + 0.25f), MathF.Min(1, c.G / 255f * boost + 0.25f), MathF.Min(1, c.B / 255f * boost + 0.25f), 1);
    }

    private string VisualOf(int bp, int weapon)
    {
        if ((uint)bp >= (uint)_ctx.Db.All.Count) return "shell";
        var ws = _ctx.Db[bp].Weapons;
        return (uint)weapon < (uint)ws.Length ? ws[weapon].ProjectileVisual : "shell";
    }

    /// <summary>Spawns the particle recipe for a sim effect event.</summary>
    public void Spawn(in EffectEvent e)
    {
        if (SpawnStrategic(in e)) return; // EffectsRenderer.Strategic.cs
        float size = MathF.Max(0.3f, e.Size);
        var fv = FrameView.Current;
        if (!fv.SphereVisible(e.Pos, size * 6 + 4)) { if (e.Kind != EffectKind.UnitDeath) return; }
        switch (e.Kind)
        {
            case EffectKind.MuzzleFlash:
            {
                var vis = (uint)e.BlueprintIndex < (uint)_ctx.Db.All.Count && _ctx.Db[e.BlueprintIndex].Weapons.Length > 0 ? _ctx.Db[e.BlueprintIndex].Weapons[0].ProjectileVisual : "shell";
                _ps.MuzzleFlash(e.Pos, MathF.Min(size, 2f), vis == "laser" ? ArmyTint(e.Army) : new Vector4(1f, 0.75f, 0.35f, 1));
                break;
            }
            case EffectKind.Impact: _ps.Impact(e.Pos, MathF.Min(size, 3f), new Vector4(1f, 0.7f, 0.35f, 1)); break;
            case EffectKind.Explosion: _ps.Explosion(e.Pos, size, e.Pos.Y - _ps.GroundAt(e.Pos) < 1.5f); break;
            case EffectKind.UnitDeath:
            {
                bool air = (uint)e.BlueprintIndex < (uint)_ctx.Db.All.Count && _ctx.Db[e.BlueprintIndex].Movement.Layer == MotionLayer.Air;
                _ps.UnitDeath(e.Pos, size, air);
                break;
            }
            case EffectKind.BuildStarted: _ps.BuildStarted(e.Pos, size); break;
            case EffectKind.BuildComplete: _ps.BuildComplete(e.Pos, size); break;
            case EffectKind.Reclaim: _ps.Reclaim(e.Pos, size); break;
        }
    }

    /// <summary>Updates and draws projectiles + effects. Call inside BeginMode3D after opaque geometry/water.
    /// dt is the real frame time; particles advance in game time (frozen while paused, faster at 2x).</summary>
    public void Draw(float dt, SimSnapshot cur, SimSnapshot? prev, float alpha, List<EffectEvent> newEffects)
    {
        dt = GameDt(_ctx, dt);
        _frame++;
        var fv = FrameView.Capture();
        _ps.Lod = Math.Clamp(110f / MathF.Max(1f, Atmosphere.CameraDistance), 0.3f, 1f) * (_ps.Count > ParticleSystem.Capacity * 0.7f ? 0.4f : 1f);
        HandleDebugKeys();
        foreach (var e in newEffects) Spawn(e);
        if (FakeBattle) UpdateFakeBattle(dt, cur);

        _ps.Update(dt);
        _proj.Begin();
        ProjectileCount = 0;

        // Real projectiles from the snapshot.
        for (int i = 0; i < cur.ProjectileCount; i++)
        {
            ref var pv = ref cur.Projectiles[i];
            var pos = pv.Pos - pv.Vel * (TickSeconds * (1f - alpha));
            DrawProjectile(pv.Id, pos, pv.Vel, VisualOf(pv.SourceBp, pv.WeaponIndex), pv.Army, fv, dt);
        }
        // Client-side fake projectiles (debug).
        foreach (var f in _fake) DrawProjectile(f.Id, f.Pos, f.Vel, f.Visual, f.Army, fv, dt);

        // Forget trails of projectiles that disappeared.
        _stale.Clear();
        foreach (var (id, t) in _trails) if (t.frame != _frame) _stale.Add(id);
        foreach (var id in _stale) _trails.Remove(id);

        _ps.Additive.Begin();
        _ps.Alpha.Begin();
        _ps.Decals.Begin();
        _ps.Build(fv);

        var sp = Shaders.Particle;
        sp.Set("time", Atmosphere.Time);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Rlgl.DisableDepthMask();
        Raylib.BeginBlendMode(BlendMode.Alpha);
        _ps.Decals.Draw();
        _ps.Alpha.Draw();
        Raylib.EndBlendMode();
        Raylib.BeginBlendMode(BlendMode.Additive);
        _ps.Additive.Draw();
        _proj.Draw();
        Raylib.EndBlendMode();
        Rlgl.EnableDepthMask();
        Rlgl.EnableBackfaceCulling();
    }

    private void DrawProjectile(int id, Vector3 pos, Vector3 vel, string visual, int army, FrameView fv, float dt)
    {
        float speed = vel.Length();
        var dir = speed > 1e-3f ? vel / speed : Vector3.UnitY;
        if (!fv.SphereVisible(pos, 3f)) return;
        ProjectileCount++;
        var cam = fv.CamPos;
        // Keep projectiles visible when zoomed out.
        float zs = Math.Clamp(Atmosphere.CameraDistance / 90f, 1f, 3f);
        switch (visual)
        {
            case "laser":
            {
                var tint = ArmyTint(army, 1.5f);
                var col = new Color((byte)(tint.X * 255), (byte)(tint.Y * 255), (byte)(tint.Z * 255), (byte)255);
                _proj.Streak(pos, dir, 2.6f * zs, 0.32f * zs, cam, col, PShape.Streak, 0);
                _proj.Streak(pos, dir, 2.2f * zs, 0.1f * zs, cam, new Color(255, 255, 255, 230), PShape.Streak, 0);
                break;
            }
            case "missile":
            {
                _proj.Billboard(pos, 0.7f * zs, 0, fv.CamRight, fv.CamUp, new Color(255, 220, 150, 255), PShape.Glow, 0);
                _proj.Streak(pos, dir, 0.6f * zs, 0.12f * zs, cam, new Color(200, 200, 210, 200), PShape.Streak, 0);
                Trail(id, pos, 0.3f, new Vector4(0.72f, 0.72f, 0.72f, 0.3f), 0.2f, 0.75f);
                break;
            }
            case "flak":
                _proj.Billboard(pos, 0.45f * zs, 0, fv.CamRight, fv.CamUp, new Color(255, 170, 80, 255), PShape.Glow, 0);
                break;
            case "bomb":
                _proj.Streak(pos, dir, 0.5f * zs, 0.22f * zs, cam, new Color(255, 120, 60, 160), PShape.Streak, 0);
                _proj.Billboard(pos, 0.3f * zs, 0, fv.CamRight, fv.CamUp, new Color(255, 80, 40, 255), PShape.Glow, 0);
                break;
            case "tactical" or "nuke" or "interceptor" or "antinuke":
                DrawStrategicProjectile(id, pos, dir, visual, fv, zs); // EffectsRenderer.Strategic.cs
                break;
            default: // shell (ballistic shells get a faint trail)
            {
                _proj.Streak(pos, dir, 1.3f * zs, 0.26f * zs, cam, new Color(255, 205, 120, 255), PShape.Streak, 0);
                _proj.Billboard(pos, 0.55f * zs, 0, fv.CamRight, fv.CamUp, new Color(255, 180, 90, 200), PShape.Glow, 0);
                if (MathF.Abs(dir.Y) > 0.35f) Trail(id, pos, 0.7f, new Vector4(0.6f, 0.58f, 0.55f, 0.12f), 0.18f, 0.45f);
                break;
            }
        }
    }

    /// <summary>Emits smoke puffs along the path travelled since last frame.</summary>
    private void Trail(int id, Vector3 pos, float spacing, Vector4 color, float size, float life)
    {
        if (_trails.TryGetValue(id, out var last))
        {
            var d = pos - last.pos;
            float len = d.Length();
            if (len > 30f) { _trails[id] = (pos, _frame); return; }
            int n = Math.Min(20, (int)(len / spacing));
            if (n == 0) { _trails[id] = (last.pos, _frame); return; }
            for (int k = 1; k <= n; k++)
            {
                ref var p = ref _ps.Emit();
                p.Pos = last.pos + d * (k / (float)n);
                p.Vel = new Vector3(_ps.Range(-0.15f, 0.15f), _ps.Range(0.1f, 0.4f), _ps.Range(-0.15f, 0.15f));
                p.Life = life * _ps.Range(0.7f, 1.2f); p.Size0 = size; p.Size1 = size * 4f; p.Drag = 1f;
                p.C0 = color; p.C1 = color with { W = 0 }; p.Shape = PShape.Smoke; p.Blend = PBlend.Alpha;
            }
            _trails[id] = (pos, _frame);
        }
        else _trails[id] = (pos, _frame);
    }

    // ------------------------------------------------------------------ debug

    private void HandleDebugKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F9)) FakeBattle = !FakeBattle;
        if (Raylib.IsKeyPressed(KeyboardKey.F10) && _ctx.PickGround(Raylib.GetMousePosition()) is Vector3 g)
            Spawn(new EffectEvent { Kind = EffectKind.UnitDeath, Pos = g, Size = 4f, Army = 0, BlueprintIndex = -1 });
    }

    private void UpdateFakeBattle(float dt, SimSnapshot cur)
    {
        // Advance fake projectiles; impacts when their flight time ends.
        for (int i = 0; i < _fake.Count;)
        {
            var f = _fake[i];
            f.Time -= dt;
            f.Vel.Y -= f.Gravity * dt;
            f.Pos += f.Vel * dt;
            if (f.Time <= 0)
            {
                if (f.Visual == "flak") _ps.FlakBurst(f.Pos);
                else if (f.Visual is "bomb" || (f.Visual == "shell" && f.Gravity > 0)) _ps.Explosion(f.Pos, 1.2f, true);
                else _ps.Impact(f.Pos, 1f, new Vector4(1f, 0.7f, 0.35f, 1));
                _fake[i] = _fake[^1];
                _fake.RemoveAt(_fake.Count - 1);
                continue;
            }
            _fake[i] = f;
            i++;
        }
        if (cur.UnitCount == 0) return;

        _fakeTimer += dt;
        while (_fakeTimer > 0.03f)
        {
            _fakeTimer -= 0.03f;
            for (int n = 0; n < 4; n++) FireFake(cur);
        }
        _fakeEventTimer += dt;
        if (_fakeEventTimer > 0.7f)
        {
            _fakeEventTimer = 0;
            ref var v = ref cur.Units[(int)(_ps.Rand() * cur.UnitCount) % cur.UnitCount];
            var bp = _ctx.Db[v.Bp];
            float r = _ps.Rand();
            var kind = r < 0.5f ? EffectKind.UnitDeath : r < 0.7f ? EffectKind.BuildStarted : r < 0.9f ? EffectKind.BuildComplete : EffectKind.Reclaim;
            Spawn(new EffectEvent { Kind = kind, Pos = v.Pos, Size = MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z), Army = v.Army, BlueprintIndex = v.Bp });
        }
    }

    private void FireFake(SimSnapshot cur)
    {
        if (_fake.Count > 4000) return;
        ref var v = ref cur.Units[(int)(_ps.Rand() * cur.UnitCount) % cur.UnitCount];
        var bp = _ctx.Db[v.Bp];
        if (bp.Weapons.Length == 0) return;
        var w = bp.Weapons[0];
        // Target: a random enemy unit in range, else a point ahead.
        Vector3 target = default;
        bool found = false;
        for (int tries = 0; tries < 12 && !found; tries++)
        {
            ref var o = ref cur.Units[(int)(_ps.Rand() * cur.UnitCount) % cur.UnitCount];
            if (o.Army == v.Army) continue;
            if (Vector3.DistanceSquared(o.Pos, v.Pos) < w.Range * w.Range * 1.5f) { target = o.Pos + new Vector3(0, 0.4f, 0); found = true; }
        }
        if (!found)
        {
            float a = v.TurretHeading + _ps.Range(-0.3f, 0.3f);
            float d = w.Range * _ps.Range(0.6f, 1f);
            var p2 = new Vector2(v.Pos.X + MathF.Cos(a) * d, v.Pos.Z + MathF.Sin(a) * d);
            target = new Vector3(p2.X, _ps.GroundAt(new Vector3(p2.X, 0, p2.Y)), p2.Y);
            if (w.TargetMask == LayerMask.Air || w.ProjectileVisual == "flak") target.Y += _ps.Range(6, 12);
        }
        var muzzle = v.Pos + new Vector3(0, bp.Visual.Size.Y * 0.8f, 0);
        var delta = target - muzzle;
        float dist = delta.Length();
        float speed = MathF.Max(8f, w.MuzzleVelocity);
        float time = dist / speed;
        float gravity = 0;
        var vel = delta / MathF.Max(0.01f, time);
        if (w.Ballistic || w.ProjectileVisual == "bomb")
        {
            gravity = 9.8f;
            vel.Y += 0.5f * gravity * time;
        }
        _fake.Add(new FakeProjectile { Id = _fakeId--, Pos = muzzle, Vel = vel, Time = time, Gravity = gravity, Army = v.Army, Bp = v.Bp, Visual = w.ProjectileVisual });
        if (FrameView.Current.SphereVisible(muzzle, 2f))
            _ps.MuzzleFlash(muzzle + Vector3.Normalize(new Vector3(vel.X, 0, vel.Z) + new Vector3(1e-4f, 0, 0)) * bp.Visual.Size.Z * 0.5f, 0.8f,
                w.ProjectileVisual == "laser" ? ArmyTint(v.Army) : new Vector4(1f, 0.75f, 0.35f, 1));
    }

    public void Dispose()
    {
        _ps.Dispose();
        _proj.Dispose();
    }
}
