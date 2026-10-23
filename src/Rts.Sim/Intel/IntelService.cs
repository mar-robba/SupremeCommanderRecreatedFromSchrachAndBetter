using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>A structure an army has seen and still believes is there ("ghost" in the fog, SupCom-style).</summary>
public struct GhostInfo
{
    /// <summary>Unit slot + generation of the structure when it was last seen (the unit may be dead by now).</summary>
    public int Slot, Gen;
    public int Bp;
    public int Army;
    public Vector2 Pos;
    /// <summary>World Y of the structure's base.</summary>
    public float Elevation;
    public float Heading;
}

/// <summary>
/// Fog of war, radar, sonar and omni (SupCom 1 model). Intel is per TEAM: allied armies share everything.
/// <para>
/// The map is divided into square cells of <see cref="CellSize"/> world units. For every team, <see cref="IntelSystem"/>
/// recomputes each tick which cells are in vision / radar / sonar / omni coverage of the team's complete units
/// (structures and aircraft included), and accumulates the "explored" state (ever seen). Coverage is generous:
/// every cell that can contain a point within the radius is covered, so a unit within VisionRadius of a viewer is
/// always visible.
/// </para>
/// <list type="bullet">
/// <item><see cref="IsVisible"/>: in vision (full information). Submerged units also need sonar or omni coverage.</item>
/// <item><see cref="IsDetected"/>: visible, or on radar (land/air/water surface), sonar (water surface/submerged) or omni
/// (everything). Detected-but-not-visible units are "radar blips": targetable, but their type is unknown.</item>
/// <item>Own and allied units are always visible. With <see cref="Enabled"/> = false everything is visible.</item>
/// </list>
/// Radar/sonar/omni of units with energy upkeep turn off when paused or when the army's energy stalls.
/// Queries are thread-safe reads (grids only change in IntelSystem, a sequential phase at the start of the tick).
/// </summary>
public sealed class IntelService
{
    /// <summary>Default grid cell size in world units.</summary>
    public const float DefaultCellSize = 2f;
    /// <summary>Explored-state values stored per cell (also sent to the client in the snapshot).</summary>
    public const byte FogUnexplored = 0, FogExplored = 128, FogVisible = 255;

    private readonly SimWorld _w;
    public readonly float CellSize;
    public readonly int Cols, Rows;
    /// <summary>64-bit words per row of the vision/radar/sonar/omni bitsets.</summary>
    public readonly int WordsPerRow;
    private readonly float _inv;
    /// <summary>Number of distinct teams; armies map to dense team indices (sorted by Army.Team).</summary>
    public readonly int TeamCount;
    private readonly int[] _teamOfArmy;
    internal readonly TeamIntel[] Teams;
    /// <summary>Per blueprint: may be submerged (sub / amphibious), so the layer must be checked.</summary>
    private readonly bool[] _canSubmerge;

    /// <summary>False = no fog of war: every unit and position is visible to everyone (debug, benchmarks).</summary>
    public bool Enabled = true;
    /// <summary>Incremented each time the grids are recomputed (the client re-uploads the fog texture on change).</summary>
    public int Version { get; internal set; }
    /// <summary>Duration of the last IntelSystem update (ms), for benchmarks.</summary>
    [field: Persistence.NoSave] public double LastUpdateMs { get; internal set; }
    /// <summary>Per unit slot: active stealth/cloak bits (<see cref="StealthSystem"/>); null while no unit has any.</summary>
    internal byte[]? StealthMask;

    public IntelService(SimWorld w, float cellSize = DefaultCellSize)
    {
        _w = w;
        CellSize = cellSize;
        _inv = 1f / cellSize;
        Cols = Math.Max(1, (int)MathF.Ceiling(w.Terrain.Width / cellSize));
        Rows = Math.Max(1, (int)MathF.Ceiling(w.Terrain.Height / cellSize));
        WordsPerRow = (Cols + 63) / 64;

        var teams = w.Armies.Select(a => a.Team).Distinct().Order().ToArray();
        TeamCount = Math.Max(1, teams.Length);
        _teamOfArmy = new int[w.Armies.Length];
        for (int a = 0; a < w.Armies.Length; a++) _teamOfArmy[a] = Array.IndexOf(teams, w.Armies[a].Team);
        Teams = new TeamIntel[TeamCount];
        for (int t = 0; t < TeamCount; t++) Teams[t] = new TeamIntel(Cols * Rows, WordsPerRow * Rows, w.Units.Capacity);

        _canSubmerge = new bool[w.Blueprints.Count];
        for (int b = 0; b < w.Blueprints.Count; b++)
            _canSubmerge[b] = w.Blueprints[b].Movement.Layer is MotionLayer.Sub or MotionLayer.Amphibious;
    }

    // ------------------------------------------------------------------ grid helpers

    public int TeamOf(int army) => _teamOfArmy[army];

    public int CellIndex(Vector2 p)
    {
        int cx = Math.Clamp((int)(p.X * _inv), 0, Cols - 1);
        int cz = Math.Clamp((int)(p.Y * _inv), 0, Rows - 1);
        return cz * Cols + cx;
    }

    /// <summary>Bit index of the cell containing p in the bitsets (row z starts at bit z * WordsPerRow * 64).</summary>
    internal int BitIndex(Vector2 p)
    {
        int cx = Math.Clamp((int)(p.X * _inv), 0, Cols - 1);
        int cz = Math.Clamp((int)(p.Y * _inv), 0, Rows - 1);
        return cz * WordsPerRow * 64 + cx;
    }

    internal static bool Test(ulong[] bits, int bit) => ((bits[bit >> 6] >> (bit & 63)) & 1) != 0;

    /// <summary>Explored state per cell for the army's team (row-major, Cols x Rows): 0 / 128 / 255, see Fog*.</summary>
    public ReadOnlySpan<byte> FogOf(int army) => Teams[_teamOfArmy[army]].Fog;

    // ------------------------------------------------------------------ queries (thread-safe reads)

    /// <summary>Can `army` see unit `unitIndex` (vision; full information)? Own/allied units: always.</summary>
    public bool IsVisible(int army, int unitIndex)
    {
        var u = _w.Units;
        int t = _teamOfArmy[army];
        if (!Enabled || t == _teamOfArmy[u.Army[unitIndex]]) return true;
        var ti = Teams[t];
        int c = BitIndex(u.Pos[unitIndex]);
        if (!Test(ti.Vision, c)) return false;
        if (StealthMask is { } sm && sm[unitIndex] != 0 && StealthSystem.HiddenFromVision(_w, ti, c, sm[unitIndex], unitIndex)) return false;
        if (_canSubmerge[u.Bp[unitIndex]] && OrderSystem.LayerOf(_w, unitIndex) == LayerMask.Sub)
            return (ti.SonarUsed && Test(ti.Sonar, c)) || (ti.OmniUsed && Test(ti.Omni, c));
        return true;
    }

    /// <summary>
    /// Does `army` know where the unit is: visible, or on radar (land/air/water surface), sonar (water surface,
    /// submerged) or omni? Units detected but not visible are radar blips (position only).
    /// </summary>
    public bool IsDetected(int army, int unitIndex)
    {
        if (IsVisible(army, unitIndex)) return true;
        var ti = Teams[_teamOfArmy[army]];
        if (!ti.RadarUsed && !ti.SonarUsed && !ti.OmniUsed) return false;
        int c = BitIndex(_w.Units.Pos[unitIndex]);
        if (ti.OmniUsed && Test(ti.Omni, c)) return true;
        bool radar = ti.RadarUsed && Test(ti.Radar, c);
        bool sonar = ti.SonarUsed && Test(ti.Sonar, c);
        if (StealthMask is { } sm && sm[unitIndex] != 0) StealthSystem.Jam(sm[unitIndex], ref radar, ref sonar);
        if (!radar && !sonar) return false;
        var layer = OrderSystem.LayerOf(_w, unitIndex);
        return (radar && (layer & (LayerMask.Land | LayerMask.Air | LayerMask.Water)) != 0)
            || (sonar && (layer & (LayerMask.Water | LayerMask.Sub)) != 0);
    }

    /// <summary>Is the unit a radar blip for `army` (detected but not visible)?</summary>
    public bool IsBlip(int army, int unitIndex) => !IsVisible(army, unitIndex) && IsDetected(army, unitIndex);

    /// <summary>Is the map position currently in vision of `army`?</summary>
    public bool IsPositionVisible(int army, Vector2 pos) => !Enabled || Test(Teams[_teamOfArmy[army]].Vision, BitIndex(pos));

    /// <summary>Has `army`'s team ever had vision of this position?</summary>
    public bool IsPositionExplored(int army, Vector2 pos) => !Enabled || Teams[_teamOfArmy[army]].Fog[CellIndex(pos)] != FogUnexplored;

    /// <summary>Is the position covered by `army`'s team radar?</summary>
    public bool IsPositionOnRadar(int army, Vector2 pos)
    {
        var ti = Teams[_teamOfArmy[army]];
        return !Enabled || (ti.RadarUsed && Test(ti.Radar, BitIndex(pos)));
    }

    /// <summary>
    /// Enemy structures `army`'s team has seen and not seen destroyed (last known positions, "ghosts"), including the
    /// ones currently visible. Deterministic order. Useful for AI target selection without cheating.
    /// </summary>
    public void GetKnownStructures(int army, List<GhostInfo> results)
    {
        var ti = Teams[_teamOfArmy[army]];
        foreach (int slot in ti.GhostSlots) results.Add(ti.Ghosts[slot]);
    }

    internal bool TryGetGhost(int team, int slot, out GhostInfo g)
    {
        var ti = Teams[team];
        g = ti.Ghosts[slot];
        return ti.GhostGen[slot] != 0;
    }
}

/// <summary>Intel grids and ghost memory of one team.</summary>
internal sealed class TeamIntel
{
    /// <summary>Bitsets (1 bit per cell, rows padded to whole 64-bit words): cell in vision / radar / sonar / omni.</summary>
    public readonly ulong[] Vision, Radar, Sonar, Omni;
    /// <summary>Per cell (row-major Cols x Rows): 0 unexplored, 128 explored (not in vision), 255 in vision.</summary>
    public readonly byte[] Fog;
    /// <summary>The bitset has content from the last update (cleared only when used).</summary>
    public bool RadarUsed, SonarUsed, OmniUsed;
    /// <summary>A powered sensor of this kind exists in the current update (set by the sequential gather).</summary>
    public bool RadarStamped, SonarStamped, OmniStamped;

    /// <summary>Per unit slot: generation of the ghosted structure (0 = no ghost).</summary>
    public readonly int[] GhostGen;
    public readonly GhostInfo[] Ghosts;
    /// <summary>Slots with a ghost (insertion order, compacted on removal: deterministic).</summary>
    public readonly List<int> GhostSlots = new();

    public TeamIntel(int cells, int words, int unitCapacity)
    {
        Vision = new ulong[words];
        Radar = new ulong[words];
        Sonar = new ulong[words];
        Omni = new ulong[words];
        Fog = new byte[cells];
        GhostGen = new int[unitCapacity];
        Ghosts = new GhostInfo[unitCapacity];
    }
}
