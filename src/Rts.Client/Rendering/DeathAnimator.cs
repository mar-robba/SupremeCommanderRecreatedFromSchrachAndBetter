using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// (9b) Client-side death sequences and the ACU warp-in (<see cref="DeathAnimator"/>.WarpIn.cs).
/// <para>When a snapshot brings <c>EffectKind.UnitDeath</c> events, each one is matched to the unit of the previous
/// snapshot that is gone from the current one (same blueprint and army, nearest to the death point). Units whose
/// <see cref="DeathAnim.Choose"/> kind is not None (aircraft, surface ships, structures, land experimentals,
/// commanders) and that die inside the view get a sequence slot (at most <see cref="MaxActive"/>; fixed array, no
/// allocation per frame): <see cref="UnitRenderer"/> draws the model at <see cref="DeathAnim.Pose"/> in the same
/// instanced draws as the living units, <see cref="PropRenderer"/> hides / fades in the unit's wreck, and the
/// particles and sounds (<see cref="Audio.UnitFxSounds"/>) come from here (DeathAnimator.Fx.cs).</para>
/// Fog: deaths the player cannot see bring no event (or no matching unit), so they get no sequence. Time is game
/// time (frozen while paused, faster at higher speeds); a replay seek or any jump of the tick clears everything.
/// <see cref="Sync"/> is idempotent per snapshot and called by whichever renderer runs first in the frame.
/// </summary>
public sealed partial class DeathAnimator
{
    public const int MaxActive = 64;
    /// <summary>A tick jump larger than this (or backwards) = seek / load: all sequences are dropped.</summary>
    public const int SeekTicks = 50;
    private const float TickSeconds = 1f / SimWorld.TicksPerSecond;

    private readonly GameContext _ctx;
    private readonly ParticleSystem _ps;

    private struct Anim
    {
        public DeathPlan Plan;
        public float T, EmitAcc, NextBurst;
        public int Bp, Army, Serial, WreckId;
        public float TurretRel;
        public Vector2 DeathXZ;
        public bool ImpactDone, LandDone;
    }

    private readonly Anim[] _anims = new Anim[MaxActive];
    private int _count, _serial;
    private int _syncTick = int.MinValue;
    private long _syncStamp;
    private readonly List<int> _vanished = new();
    private readonly List<(Vector3 Pos, int Bp, DeathKind Kind)> _claimed = new();
    private readonly Dictionary<int, int> _wreckSerial = new(); // prop id -> serial of the sequence hiding it (0 = none)
    private bool[]? _may;

    /// <summary>Unit models (bounds of the meshes); set by <see cref="UnitRenderer.Fx"/>.</summary>
    public UnitRenderer? Models;

    /// <summary>Active death sequences.</summary>
    public int Count => _count;

    public DeathAnimator(GameContext ctx, ParticleSystem ps)
    {
        _ctx = ctx;
        _ps = ps;
    }

    private bool MayAnimate(int bp)
    {
        var db = _ctx.Db;
        if ((uint)bp >= (uint)db.Count) return false;
        if (_may == null)
        {
            _may = new bool[db.Count];
            for (int i = 0; i < _may.Length; i++) _may[i] = DeathAnim.MayAnimate(db[i]);
        }
        return _may[bp];
    }

    /// <summary>Drops every sequence (seek / load).</summary>
    public void Clear()
    {
        _count = 0;
        _claimed.Clear();
        _wreckSerial.Clear();
        ClearWarps();
    }

    /// <summary>Starts sequences for the deaths of the newest snapshot (once per snapshot).</summary>
    public void Sync()
    {
        var snaps = _ctx.Snapshots;
        var cur = snaps.Current;
        if (cur.Tick == _syncTick && cur.PublishedAt == _syncStamp) return;
        if (_syncTick != int.MinValue && (cur.Tick < _syncTick || cur.Tick > _syncTick + SeekTicks)) Clear();
        _syncTick = cur.Tick;
        _syncStamp = cur.PublishedAt;
        _claimed.Clear();
        CheckWarpIn(cur); // DeathAnimator.WarpIn.cs
        var prev = snaps.Prev;
        if (prev == null || prev.Tick >= cur.Tick) return;

        var fx = snaps.NewEffects;
        bool any = false;
        for (int i = 0; i < fx.Count && !any; i++) any = fx[i].Kind == EffectKind.UnitDeath && MayAnimate(fx[i].BlueprintIndex);
        if (!any) return;

        _vanished.Clear();
        for (int k = 0; k < prev.UnitCount; k++)
        {
            ref var v = ref prev.Units[k];
            if ((v.Flags & UnitFlags.UnderConstruction) != 0 || !MayAnimate(v.Bp)) continue;
            if (!cur.TryGetUnit(v.Index, v.Gen, out _)) _vanished.Add(k);
        }
        float dtTicks = (cur.Tick - prev.Tick) * TickSeconds;
        for (int i = 0; i < fx.Count; i++)
        {
            var e = fx[i];
            if (e.Kind != EffectKind.UnitDeath || !MayAnimate(e.BlueprintIndex)) continue;
            int best = -1;
            float bestD = 36f;
            for (int j = 0; j < _vanished.Count; j++)
            {
                ref var v = ref prev.Units[_vanished[j]];
                if (v.Bp != e.BlueprintIndex || v.Army != e.Army) continue;
                float dx = v.Pos.X - e.Pos.X, dz = v.Pos.Z - e.Pos.Z, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = j; }
            }
            if (best < 0) continue;
            int slot = _vanished[best];
            _vanished[best] = _vanished[^1];
            _vanished.RemoveAt(_vanished.Count - 1);
            TryStart(in e, in prev.Units[slot], dtTicks, cur.Tick);
        }
    }

    private void TryStart(in EffectEvent e, in UnitView v, float dtTicks, int tick)
    {
        var bp = _ctx.Db[e.BlueprintIndex];
        var terrain = _ctx.Terrain;
        float floor = terrain.GetHeight(e.Pos.X, e.Pos.Z), water = terrain.WaterLevel;
        float surface = MathF.Max(floor, water);
        var kind = DeathAnim.Choose(bp, v.Pos.Y - surface, water - floor, water - v.Pos.Y);
        if (kind == DeathKind.None || _count >= MaxActive) return;
        float height = MathF.Max(0.3f, bp.Visual.Size.Y), radius = MathF.Max(0.5f, MathF.Max(bp.Visual.Size.X, bp.Visual.Size.Z) * 0.5f);
        if (Models != null)
        {
            var m = Models.ModelFor(bp);
            height = MathF.Max(0.3f, m.MaxY - m.MinY);
            radius = MathF.Max(0.5f, m.Radius);
        }
        // Off-screen deaths get no sequence (their wreck shows at once): keeps big battles cheap.
        if (!FrameView.Current.SphereVisible(v.Pos, radius + 25f)) return;
        bool huge = bp.Has(UnitCategory.Experimental);
        var start = new DeathStart
        {
            Kind = kind, Pos = v.Pos, End = e.Pos, EndTime = dtTicks,
            Vel = dtTicks > 0 ? (e.Pos - v.Pos) / dtTicks : Vector3.Zero,
            Heading = v.Heading, Height = height, Radius = radius, Floor = floor, Water = water,
            Seed = Hash(v.Index * 7919 + v.Gen, tick), Huge = huge,
            BlastDelay = bp.DeathBlast?.Delay ?? (huge ? bp.DeathDelay : 0f),
        };
        ref var a = ref _anims[_count++];
        a = new Anim
        {
            Plan = DeathAnim.Plan(in start), Bp = bp.Index, Army = v.Army, Serial = ++_serial, WreckId = -1,
            TurretRel = Rts.Sim.Core.DMath.AngleDiff(v.Heading, v.TurretHeading), DeathXZ = new Vector2(e.Pos.X, e.Pos.Z),
        };
        _claimed.Add((e.Pos, e.BlueprintIndex, kind));
        OnStart(ref a); // DeathAnimator.Fx.cs
    }

    /// <summary>The sequence kind started for this death event of the current snapshot (None = not animated).</summary>
    public DeathKind ClaimedKind(in EffectEvent e)
    {
        foreach (var c in _claimed)
            if (c.Bp == e.BlueprintIndex && c.Pos == e.Pos) return c.Kind;
        return DeathKind.None;
    }

    /// <summary>Advances every sequence by dt (game seconds) and emits its particles / sounds.</summary>
    public void Update(float dt)
    {
        dt = Math.Clamp(dt, 0f, 0.1f); // a long hitch must not skip a whole sequence
        UpdateWarps(dt); // DeathAnimator.WarpIn.cs
        if (dt <= 0f) return;
        for (int i = 0; i < _count; i++)
        {
            ref var a = ref _anims[i];
            float t0 = a.T;
            a.T += dt;
            Emit(ref a, t0, dt); // DeathAnimator.Fx.cs
            if (a.T < a.Plan.Duration) continue;
            if (a.WreckId >= 0) _wreckSerial.Remove(a.WreckId);
            _anims[i--] = _anims[--_count];
        }
        if (_wreckSerial.Count > 4096) _wreckSerial.Clear(); // only resolved lazily: re-matched on the next query
    }

    /// <summary>Pose of sequence i (0..Count) for drawing.</summary>
    public bool TryGetPose(int i, out DeathPose pose, out int bp, out int army, out float turretRel)
    {
        ref var a = ref _anims[i];
        pose = DeathAnim.Pose(in a.Plan, a.T);
        bp = a.Bp;
        army = a.Army;
        turretRel = a.TurretRel;
        return pose.Visible > 0.01f;
    }

    /// <summary>0..1 visibility of a fresh wreck: 0 while its unit's sequence hides it, 1 for wrecks of other deaths.</summary>
    public float WreckVisibility(int propId, int bp, Vector3 pos)
    {
        Sync();
        if (_wreckSerial.TryGetValue(propId, out int serial))
        {
            if (serial == 0) return 1f;
            for (int i = 0; i < _count; i++)
                if (_anims[i].Serial == serial) return DeathAnim.WreckVisibility(in _anims[i].Plan, _anims[i].T);
            _wreckSerial[propId] = 0;
            return 1f;
        }
        int found = 0;
        float vis = 1f;
        for (int i = 0; i < _count; i++)
        {
            ref var a = ref _anims[i];
            if (a.Bp != bp || a.WreckId >= 0) continue;
            float dx = a.DeathXZ.X - pos.X, dz = a.DeathXZ.Y - pos.Z;
            if (dx * dx + dz * dz > 0.25f) continue;
            a.WreckId = propId;
            found = a.Serial;
            vis = DeathAnim.WreckVisibility(in a.Plan, a.T);
            break;
        }
        _wreckSerial[propId] = found;
        return vis;
    }

    private static float Hash(int a, int b)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return (h & 0xFFFFFF) / 16777216f;
    }
}
