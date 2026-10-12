using System.Numerics;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Sandbox settings published to the client in <see cref="Snapshot.SimSnapshot.Sandbox"/>. Immutable: a new instance
/// is created whenever something changes, so the render thread can keep a reference safely. Null outside sandbox.
/// </summary>
public sealed class SandboxStatus
{
    /// <summary>Per army: storage always full / no stall.</summary>
    public required bool[] InfiniteResources { get; init; }
    /// <summary>Per army: construction and factory production finish instantly.</summary>
    public required bool[] InstantBuild { get; init; }
    /// <summary>Per army: no deposit needed for extractors.</summary>
    public required bool[] BuildAnywhere { get; init; }
    /// <summary>Per army: skirmish AI on (<see cref="Army.IsAI"/>) and its difficulty.</summary>
    public required bool[] AiOn { get; init; }
    public required AiDifficulty[] AiDifficulty { get; init; }
    /// <summary>Victory/defeat ends the game (off by default in the sandbox).</summary>
    public bool Victory { get; init; }
    /// <summary>Units still waiting to be spawned by batched spawn requests.</summary>
    public int PendingSpawns { get; init; }
    /// <summary>Incremented on every change.</summary>
    public int Version { get; init; }
}

/// <summary>Mutable sandbox state of one world (sim thread only). See <see cref="SandboxSystem"/>.</summary>
public sealed class SandboxState
{
    public readonly bool[] InfiniteResources;
    public readonly bool[] InstantBuild;
    public readonly bool[] BuildAnywhere;
    public bool Victory;
    /// <summary>Batched spawn requests, served in FIFO order (<see cref="SandboxSystem.SpawnBudgetPerTick"/> units per tick).</summary>
    public readonly List<SpawnJob> SpawnJobs = new();
    /// <summary>Nuke effects of recent ticks, re-added to fog-filtered snapshots so a nuke is always seen.</summary>
    public readonly List<(int Tick, EffectEvent Fx)> RecentNukes = new();
    /// <summary>Nuke inner rings whose wrecks/props are vaporized at the start of the next tick (after the deaths).</summary>
    public readonly List<(int Tick, Vector2 Center, float Radius)> PendingVaporize = new();
    public SandboxStatus Status { get; private set; } = null!;
    private int _version;

    public SandboxState(SimWorld w)
    {
        int n = w.Armies.Length;
        InfiniteResources = new bool[n];
        InstantBuild = new bool[n];
        BuildAnywhere = new bool[n];
    }

    public bool AnyInfiniteResources => Array.IndexOf(InfiniteResources, true) >= 0;
    public bool AnyInstantBuild => Array.IndexOf(InstantBuild, true) >= 0;

    public int PendingSpawns
    {
        get
        {
            int n = 0;
            foreach (var j in SpawnJobs) n += j.Remaining;
            return n;
        }
    }

    /// <summary>Publishes a new immutable status for the snapshots.</summary>
    public void Publish(SimWorld w) => Status = new SandboxStatus
    {
        InfiniteResources = (bool[])InfiniteResources.Clone(),
        InstantBuild = (bool[])InstantBuild.Clone(),
        BuildAnywhere = (bool[])BuildAnywhere.Clone(),
        AiOn = w.Armies.Select(a => a.IsAI).ToArray(),
        AiDifficulty = w.Armies.Select(a => a.AiDifficulty).ToArray(),
        Victory = Victory,
        PendingSpawns = PendingSpawns,
        Version = ++_version,
    };

    /// <summary>True if the published status no longer matches the world (AI flags changed, spawns progressed).</summary>
    public bool StatusStale(SimWorld w)
    {
        var s = Status;
        if (s.PendingSpawns != PendingSpawns) return true;
        for (int a = 0; a < w.Armies.Length; a++)
            if (s.AiOn[a] != w.Armies[a].IsAI || s.AiDifficulty[a] != w.Armies[a].AiDifficulty) return true;
        return false;
    }
}

/// <summary>One batched spawn request: walks a square spiral of candidate positions around Center.</summary>
public sealed class SpawnJob
{
    public required int Army;
    public required int Blueprint;
    public required Vector2 Center;
    public required bool Complete;
    public required float Heading;
    public int Remaining;
    /// <summary>Candidate positions tried so far (search gives up after <see cref="MaxCandidates"/>).</summary>
    public int Tried;
    public int MaxCandidates;
    public SquareSpiral Spiral;
    /// <summary>Extractors that need a deposit: candidate deposits sorted by distance (null = free placement).</summary>
    public int[]? Deposits;
    public int DepositIndex;
}

/// <summary>
/// Integer offsets of a square spiral around (0, 0): (0,0), (1,0), (1,1), (0,1), (-1,1), (-1,0), (-1,-1), ...
/// so the first n² points fill an n×n block. Deterministic, allocation free.
/// </summary>
public struct SquareSpiral
{
    private int _x, _y, _dx, _dy, _segLen, _segPassed, _turns;
    private bool _started;

    public (int x, int y) Next()
    {
        if (!_started)
        {
            _started = true;
            _dx = 1; _dy = 0; _segLen = 1;
            return (0, 0);
        }
        _x += _dx;
        _y += _dy;
        var result = (_x, _y);
        if (++_segPassed == _segLen)
        {
            _segPassed = 0;
            (_dx, _dy) = (-_dy, _dx); // turn 90°
            if (++_turns % 2 == 0) _segLen++;
        }
        return result;
    }
}
