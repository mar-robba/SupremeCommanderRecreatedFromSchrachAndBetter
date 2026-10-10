namespace Rts.Sim.Core;

/// <summary>
/// Deterministic RNG (SplitMix64). Never use System.Random or Random.Shared in the simulation.
/// In parallel systems use <see cref="For(ulong,int,int,int)"/> to derive an independent stream per unit/tick,
/// so the result does not depend on thread scheduling.
/// </summary>
public struct DetRandom
{
    private ulong _state;

    public DetRandom(ulong seed) => _state = seed;

    public static DetRandom For(ulong seed, int tick, int index, int salt = 0) =>
        new(Mix(seed ^ Mix((ulong)(uint)tick | ((ulong)(uint)index << 32)) ^ Mix((ulong)(uint)salt + 0x51ED27UL)));

    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextU64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextU64() >> 40) * (1f / (1 << 24));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Uniform int in [min, maxExclusive).</summary>
    public int Range(int min, int maxExclusive) =>
        maxExclusive <= min ? min : min + (int)(NextU64() % (ulong)(maxExclusive - min));

    public bool Chance(float p) => NextFloat() < p;
}
