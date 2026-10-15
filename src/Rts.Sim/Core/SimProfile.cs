using System.Diagnostics;

namespace Rts.Sim.Core;

/// <summary>
/// Wall-clock milliseconds of each stage of the last <see cref="Simulation.Step"/> (diagnostics only, never read by
/// the simulation, not saved). A lap costs one timestamp read. Used by Rts.Bench and the client's perf log.
/// </summary>
[Persistence.NoSave]
public sealed class SimProfile
{
    public enum Stage
    {
        Commands, Ai, Transport, Rebuild, Intel, Economy, Orders, Construction, Factory, Path, Movement,
        Special, Targeting, Weapons, Projectiles, Damage, Cleanup,
    }

    public static readonly string[] Names = Enum.GetNames<Stage>();
    public const int Count = 17;

    /// <summary>Last tick, per <see cref="Stage"/>.</summary>
    public readonly double[] LastMs = new double[Count];
    private long _t;

    public void Begin() => _t = Stopwatch.GetTimestamp();

    public void Lap(Stage s)
    {
        long now = Stopwatch.GetTimestamp();
        LastMs[(int)s] = (now - _t) * 1000.0 / Stopwatch.Frequency;
        _t = now;
    }
}
