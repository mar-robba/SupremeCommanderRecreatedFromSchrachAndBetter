using System.Numerics;
using Rts.Sim.Data;

namespace Rts.Client.Rendering;

/// <summary>Death sequence of a unit that left the snapshot (<see cref="DeathAnimator"/>).</summary>
public enum DeathKind : byte
{
    /// <summary>No sequence: the regular explosion and the wreck at once (land units, subs under water, unknown).</summary>
    None,
    /// <summary>Aircraft: spirals down (a descending loop that ends over the death point) and crashes on the ground/water.</summary>
    AirCrash,
    /// <summary>Surface ship: lists to one side, then sinks (bow up) and dithers out under the water.</summary>
    ShipSink,
    /// <summary>Structure: shakes, crumbles and sinks into its wreck.</summary>
    Collapse,
    /// <summary>Land experimental: staggers with secondary explosions, then collapses into its wreck (longer).</summary>
    BigFall,
    /// <summary>Commander: staggers and kneels with an overloading core until its death blast vaporizes it.</summary>
    CommanderFall,
}

/// <summary>What the client knew of the unit when it died (last snapshot it was in + the terrain below).</summary>
public struct DeathStart
{
    public DeathKind Kind;
    /// <summary>Where the unit was drawn last (the previous snapshot's position).</summary>
    public Vector3 Pos;
    /// <summary>Where the unit died (the sim's death position = its wreck's position) and the time (s) from Pos to it.</summary>
    public Vector3 End;
    public float EndTime;
    /// <summary>Velocity (world units / s), estimated from the previous position and the death position.</summary>
    public Vector3 Vel;
    public float Heading;
    /// <summary>Model height (MaxY - MinY) and bounding radius.</summary>
    public float Height, Radius;
    /// <summary>Terrain height under the death point (sea floor on water).</summary>
    public float Floor;
    public float Water;
    /// <summary>0..1 per-death random (side of the spiral / list, tilt directions).</summary>
    public float Seed;
    /// <summary>Experimental scale: slower, longer sequences.</summary>
    public bool Huge;
    /// <summary>Seconds until the sim's death blast: commander DeathBlast.Delay, experimental DeathDelay (0 = none).</summary>
    public float BlastDelay;
}

/// <summary>Model transform of a dying unit at one moment: position, Euler angles (yaw in sim heading convention,
/// pitch &gt; 0 = nose down, roll &gt; 0 = banks towards the left side), vertical squash and the shader inputs.</summary>
public struct DeathPose
{
    public Vector3 Pos;
    public float Yaw, Pitch, Roll, Squash;
    /// <summary>0..1 burnt look (team color and lights die out).</summary>
    public float Char;
    /// <summary>0..1 model visibility (dithered out below 1).</summary>
    public float Visible;
}

/// <summary>Derived constants of one death sequence (computed once by <see cref="DeathAnim.Plan"/>).</summary>
public struct DeathPlan
{
    public DeathStart S;
    /// <summary>Total length (s, game time) of the sequence.</summary>
    public float Duration;
    /// <summary>Moment of the main event: aircraft crash, commander blast, experimental final blast, structure give.</summary>
    public float ImpactTime;
    /// <summary>Aircraft: loop radius at the start, fall height, ground (or water) height of the crash.</summary>
    public float LoopRadius, Drop, CrashY;
    /// <summary>Ships: final height (sea floor or deep enough to vanish); true over deep water (the wreck sinks too).</summary>
    public float SinkTo;
    public bool Deep;
    /// <summary>±1: spiral / list / tilt side.</summary>
    public float Side;
}

/// <summary>
/// Pure timing and path functions of the client death sequences (tested in Rts.Client.Tests/DeathAnimTests).
/// Every function depends only on its inputs and the time since the death, so the sequences play the same at any
/// frame rate and game speed (time = game time, frozen while paused).
/// </summary>
public static class DeathAnim
{
    /// <summary>Aircraft lower than this over the ground/water (landed, taking off) get no fall.</summary>
    public const float MinAirDrop = 1.5f;
    /// <summary>Water depth over which a dead ship sinks out of sight (same rule as the wreck: PropRenderer.SinkMinDepth).</summary>
    public const float DeepWater = 1f;
    /// <summary>Longest sequence (s): the wreck check skips older props.</summary>
    public const float MaxDuration = 15f;

    /// <summary>The sequence for a unit kind. <paramref name="aboveSurface"/> = unit height over max(terrain, water);
    /// <paramref name="waterDepth"/> = water level - terrain height at the unit; <paramref name="belowWater"/> = how
    /// far the unit's position is under the water surface (submerged subs: &gt; 0.6).</summary>
    public static DeathKind Choose(UnitBlueprint bp, float aboveSurface, float waterDepth, float belowWater)
    {
        if (bp.DeathBlast != null) return DeathKind.CommanderFall;
        if (bp.Movement.Layer == MotionLayer.Air) return aboveSurface >= MinAirDrop ? DeathKind.AirCrash : DeathKind.None;
        if (bp.IsStructure) return DeathKind.Collapse;
        bool naval = bp.Has(UnitCategory.Naval) || bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub;
        if (naval && waterDepth > 0.3f) return belowWater > 0.6f ? DeathKind.None : DeathKind.ShipSink;
        if (bp.Has(UnitCategory.Experimental)) return DeathKind.BigFall;
        return DeathKind.None;
    }

    /// <summary>Cheap pre-filter on the blueprint alone (false = never animated, whatever the position).</summary>
    public static bool MayAnimate(UnitBlueprint bp) =>
        bp.DeathBlast != null || bp.IsStructure || bp.Movement.Layer is MotionLayer.Air or MotionLayer.Water or MotionLayer.Sub
        || bp.Has(UnitCategory.Naval) || bp.Has(UnitCategory.Experimental);

    public static DeathPlan Plan(in DeathStart s)
    {
        var p = new DeathPlan { S = s, Side = s.Seed < 0.5f ? -1f : 1f };
        float surface = MathF.Max(s.Floor, s.Water);
        switch (s.Kind)
        {
            case DeathKind.AirCrash:
            {
                p.CrashY = surface;
                p.Drop = MathF.Max(0f, s.Pos.Y - surface);
                float t = Math.Clamp(1.3f + 0.07f * p.Drop, 1.5f, 3.2f) * (s.Huge ? 1.6f : 1f);
                float speed = new Vector2(s.Vel.X, s.Vel.Z).Length();
                if (speed < 2f) speed = s.Huge ? 4f : 6f; // hovering gunships drift off in a tight spiral
                p.LoopRadius = Math.Clamp(speed * t / MathF.Tau, 1.5f, s.Huge ? 30f : 12f);
                p.Duration = p.ImpactTime = t;
                break;
            }
            case DeathKind.ShipSink:
            {
                p.Deep = s.Water - s.Floor >= DeepWater;
                float h = MathF.Max(0.3f, s.Height);
                // Deep water: as long as the wreck's own sinking (PropRenderer.Naval), so the wreck has dithered out by then.
                p.Duration = p.Deep ? Math.Clamp(5f + h * 1.2f, 6f, 14f) : Math.Clamp(3.5f + h * 0.6f, 4f, 8f);
                p.SinkTo = MathF.Max(s.Floor - h * 0.2f, s.Pos.Y - h - 2.5f);
                p.ImpactTime = p.Duration * 0.12f; // starts going down
                break;
            }
            case DeathKind.Collapse:
                p.Duration = Math.Clamp(1.4f + s.Radius * 0.35f, 1.6f, 3.5f) * (s.Huge ? 1.6f : 1f);
                p.ImpactTime = p.Duration * 0.2f;
                break;
            case DeathKind.BigFall:
                p.ImpactTime = MathF.Max(0.3f, s.BlastDelay);
                p.Duration = p.ImpactTime + 2.6f;
                break;
            case DeathKind.CommanderFall:
                p.ImpactTime = MathF.Max(0.2f, s.BlastDelay);
                p.Duration = p.ImpactTime + 0.4f; // keeps the wreck hidden until the blast vaporizes it (one tick later)
                break;
            default:
                p.Duration = 0;
                break;
        }
        return p;
    }

    public static float Smooth(float a, float b, float x)
    {
        float k = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return k * k * (3f - 2f * k);
    }

    /// <summary>The unit's pose <paramref name="t"/> seconds (game time) after its death.</summary>
    public static DeathPose Pose(in DeathPlan p, float t)
    {
        ref readonly var s = ref p.S;
        t = MathF.Max(0f, t);
        float u = p.Duration > 0 ? Math.Clamp(t / p.Duration, 0f, 1f) : 1f;
        var pose = new DeathPose { Pos = Base(in s, t), Yaw = s.Heading, Squash = 1f, Visible = 1f };
        switch (s.Kind)
        {
            case DeathKind.AirCrash:
                pose = AirPose(in p, t);
                break;
            case DeathKind.ShipSink:
            {
                // Deep water: drifts on (the wreck is out of sight); shallow: stops at the death point, over its wreck.
                const float tau = 1.5f;
                if (p.Deep) pose.Pos += s.Vel with { Y = 0 } * (tau * (1f - MathF.Exp(-t / tau)) * Smooth(0f, 0.1f, t));
                float list = Smooth(0f, 0.35f, u), sink = MathF.Pow(Smooth(0.12f, 1f, u), 1.3f);
                float pitchSign = Frac(s.Seed * 7.31f) < 0.5f ? -1f : 1f;
                pose.Pos.Y = s.Pos.Y + (p.SinkTo - s.Pos.Y) * sink;
                pose.Roll = p.Side * (s.Huge ? 0.3f : 0.45f + 0.2f * s.Seed) * list * (1f + 0.4f * sink);
                pose.Pitch = pitchSign * (s.Huge ? 0.22f : 0.42f) * sink * sink;
                pose.Char = 0.15f + 0.6f * u;
                pose.Visible = p.Deep ? 1f - Smooth(0.72f, 1f, u) : 1f - Smooth(0.8f, 1f, u);
                break;
            }
            case DeathKind.Collapse:
            {
                float shake = (1f - Smooth(0f, 0.35f, u)) * 0.05f * MathF.Max(0.5f, s.Radius);
                float give = MathF.Pow(Smooth(0.12f, 1f, u), 1.5f);
                pose.Pos += new Vector3(MathF.Sin(t * 43f + s.Seed * 10f), 0, MathF.Sin(t * 37f + s.Seed * 20f)) * shake;
                pose.Pos.Y -= 0.3f * s.Height * give;
                pose.Squash = 1f - 0.4f * give;
                pose.Pitch = (s.Seed - 0.5f) * 0.25f * give;
                pose.Roll = (Frac(s.Seed * 7.31f) - 0.5f) * 0.25f * give;
                pose.Char = 0.2f + 0.8f * u;
                pose.Visible = 1f - Smooth(0.65f, 1f, u);
                break;
            }
            case DeathKind.BigFall:
            {
                float a = MathF.Min(1f, t / p.ImpactTime); // stagger until the death blast
                float fall = MathF.Pow(Smooth(p.ImpactTime, p.Duration, t), 1.6f);
                float wobble = (1f - fall) * (0.04f + 0.08f * a);
                pose.Pitch = wobble * MathF.Sin(t * 6.5f) + (Frac(s.Seed * 3.7f) - 0.5f) * 0.7f * fall;
                pose.Roll = wobble * MathF.Sin(t * 5.1f + s.Seed * 6f) + p.Side * 0.3f * fall;
                pose.Pos.Y -= s.Height * (0.04f * a + 0.2f * fall);
                pose.Squash = 1f - 0.25f * fall;
                pose.Char = 0.2f + 0.75f * u;
                pose.Visible = 1f - Smooth(0.86f, 1f, u);
                break;
            }
            case DeathKind.CommanderFall:
            {
                float k = Math.Clamp(t / p.ImpactTime, 0f, 1f);
                float kneel = Smooth(0f, 1f, k);
                pose.Pos.Y -= 0.15f * s.Height * kneel;
                pose.Pitch = 0.32f * kneel + 0.05f * MathF.Sin(t * 11f);
                pose.Roll = p.Side * 0.12f * kneel + 0.07f * MathF.Sin(t * 8.3f + s.Seed * 5f) * (1f - 0.5f * k);
                pose.Squash = 1f - 0.08f * kneel;
                pose.Char = 0.15f + 0.5f * k;
                pose.Visible = t < p.ImpactTime ? 1f : 0f;
                break;
            }
        }
        return pose;
    }

    /// <summary>Where a non-flying unit stands: it keeps moving from its last drawn position to the death point
    /// (one tick), then stays there (= over its wreck).</summary>
    public static Vector3 Base(in DeathStart s, float t) =>
        s.EndTime > 0 ? Vector3.Lerp(s.Pos, s.End, Math.Clamp(t / s.EndTime, 0f, 1f)) : s.End;

    /// <summary>Aircraft: one descending loop of shrinking radius that starts along the heading at the unit's speed
    /// and ends over the death point (= where the sim put the wreck), falling faster and faster, banking into the turn,
    /// nose dropping and spinning out at the end.</summary>
    public static DeathPose AirPose(in DeathPlan p, float t)
    {
        ref readonly var s = ref p.S;
        float u = p.Duration > 0 ? Math.Clamp(t / p.Duration, 0f, 1f) : 1f;
        float theta = MathF.Tau * u;
        float r = p.LoopRadius * (1f - 0.5f * u);
        var fwd = new Vector3(MathF.Cos(s.Heading), 0, MathF.Sin(s.Heading));
        var side = new Vector3(-MathF.Sin(s.Heading), 0, MathF.Cos(s.Heading));
        var pos = Vector3.Lerp(s.Pos, s.End, u) + r * (MathF.Sin(theta) * fwd + p.Side * (1f - MathF.Cos(theta)) * side);
        pos.Y = s.Pos.Y - p.Drop * (0.3f * u + 0.7f * u * u);
        return new DeathPose
        {
            Pos = pos,
            Yaw = s.Heading + p.Side * theta,
            Pitch = 0.12f + 0.75f * u * u,
            Roll = p.Side * (0.8f * MathF.Min(1f, u / 0.25f) + 3f * MathF.PI * u * u * u),
            Squash = 1f,
            Char = 0.2f + 0.5f * u,
            Visible = t < p.Duration ? 1f : 0f,
        };
    }

    /// <summary>0..1 visibility of the unit's wreck <paramref name="t"/> s after the death (hidden while the unit itself
    /// is still falling / sinking / collapsing, faded in as the model fades out).</summary>
    public static float WreckVisibility(in DeathPlan p, float t)
    {
        if (t >= p.Duration) return 1f;
        float u = p.Duration > 0 ? t / p.Duration : 1f;
        return p.S.Kind switch
        {
            DeathKind.AirCrash => 0f,
            DeathKind.ShipSink => p.Deep ? 0f : Smooth(0.8f, 1f, u),
            DeathKind.Collapse => Smooth(0.55f, 0.95f, u),
            DeathKind.BigFall => Smooth(0.8f, 1f, u),
            DeathKind.CommanderFall => 0f,
            _ => 1f,
        };
    }

    /// <summary>Packs char (0..1) and visibility (0..1) into the unit shader's instance w: integer part = 16 dither
    /// levels of fade-out, fraction = char. 0 = a normal unit.</summary>
    public static float PackShaderW(float charF, float visible)
    {
        float q = MathF.Floor(Math.Clamp(1f - visible, 0f, 1f) * 16f + 0.5f);
        return q + Math.Clamp(charF, 0f, 1f) * 0.99f;
    }

    /// <summary>System.Numerics model matrix of a pose (same axis convention as the wrecks: model forward = +Z).</summary>
    public static Matrix4x4 Matrix(in DeathPose pose) =>
        Matrix4x4.CreateScale(1f, pose.Squash, 1f) * Matrix4x4.CreateRotationX(pose.Pitch) * Matrix4x4.CreateRotationZ(pose.Roll)
        * Matrix4x4.CreateRotationY(MathF.PI * 0.5f - pose.Yaw) * Matrix4x4.CreateTranslation(pose.Pos);

    private static float Frac(float x) => x - MathF.Floor(x);
}
