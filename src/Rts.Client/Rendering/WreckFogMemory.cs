using System.Numerics;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>
/// (10b-2, G11) Fog memory of wrecks (pure, no raylib; tests in <c>FogMemoryTests</c>). The sim sends every wreck in
/// every snapshot (props are not filtered by intel), so the client decides what the player knows:
/// <list type="bullet">
/// <item>a wreck whose spot is in vision (fog cell 255) is drawn live and remembered as last seen (mass left, pose);</item>
/// <item>a wreck under the fog is drawn as last seen, or not at all if it was never seen;</item>
/// <item>a remembered wreck that is gone (reclaimed, burnt) stays drawn until its spot is seen again.</item>
/// </list>
/// Without fog (no view army, F7 reveal, fog option None) every wreck passes through. <see cref="Update"/> runs once per
/// snapshot; the output buffers are reused (no allocation once grown).
/// </summary>
public sealed class WreckFogMemory
{
    private readonly Dictionary<int, PropView> _mem = new();
    private readonly HashSet<int> _present = new();
    private readonly List<int> _drop = new();
    private PropView[] _out = new PropView[64];
    private int _outCount;
    private SimSnapshot? _snap;
    private int _tick = int.MinValue;

    /// <summary>Wrecks to draw this frame (live ones in vision, last-seen copies under the fog).</summary>
    public ReadOnlySpan<PropView> Wrecks => _out.AsSpan(0, _outCount);
    /// <summary>Wrecks remembered (seen at least once and not yet seen gone).</summary>
    public int Remembered => _mem.Count;

    /// <summary>Fog value at a world point: 0 unexplored, 128 explored, 255 in vision (255 when the snapshot has no fog).</summary>
    public static byte FogAt(SimSnapshot s, Vector2 p)
    {
        if (s.ViewArmy < 0 || s.FogCols <= 0 || s.FogCellSize <= 0 || s.Fog.Length < s.FogCols * s.FogRows) return 255;
        int cx = Math.Clamp((int)(p.X / s.FogCellSize), 0, s.FogCols - 1);
        int cz = Math.Clamp((int)(p.Y / s.FogCellSize), 0, s.FogRows - 1);
        return s.Fog[cz * s.FogCols + cx];
    }

    public static bool HasFog(SimSnapshot s) => s.ViewArmy >= 0 && s.FogCols > 0 && s.Fog.Length >= s.FogCols * s.FogRows;

    /// <summary>Call every frame; works only when the snapshot (reference or tick) changed.</summary>
    public void Update(SimSnapshot cur)
    {
        if (ReferenceEquals(cur, _snap) && cur.Tick == _tick) return;
        _snap = cur;
        _tick = cur.Tick;
        _outCount = 0;
        bool fog = HasFog(cur);
        if (!fog) _mem.Clear(); // reveal-all: start over when the fog comes back (the reveal showed everything anyway)
        _present.Clear();
        for (int k = 0; k < cur.PropCount; k++)
        {
            ref var p = ref cur.Props[k];
            if (p.Kind != PropKind.Wreck) continue;
            if (!fog) { Emit(p); continue; }
            _present.Add(p.Id);
            if (FogAt(cur, new Vector2(p.Pos.X, p.Pos.Z)) == 255)
            {
                _mem[p.Id] = p;
                Emit(p);
            }
            else if (_mem.TryGetValue(p.Id, out var last)) Emit(last);
        }
        if (!fog) return;
        _drop.Clear();
        foreach (var (id, last) in _mem)
        {
            if (_present.Contains(id)) continue;
            if (FogAt(cur, new Vector2(last.Pos.X, last.Pos.Z)) == 255) _drop.Add(id); // seen gone
            else Emit(last);
        }
        foreach (int id in _drop) _mem.Remove(id);
    }

    private void Emit(in PropView p)
    {
        if (_outCount == _out.Length) Array.Resize(ref _out, _outCount * 2);
        _out[_outCount++] = p;
    }

    public void Clear()
    {
        _mem.Clear();
        _outCount = 0;
        _snap = null;
        _tick = int.MinValue;
    }
}
