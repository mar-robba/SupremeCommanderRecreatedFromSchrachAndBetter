using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Recomputes the intel grids of every team (see <see cref="IntelService"/>). Runs at the start of the tick, after the
/// spatial grid rebuild. Grids are bitsets (1 bit per cell), so a covered row interval costs one or two word ORs.
/// <list type="number">
/// <item>Sequential gather: every complete unit becomes a vision record (position, radius, in cell units),
/// counting-sorted by (team, grid row); powered radar/sonar/omni units become sensor records.</item>
/// <item>PARALLEL over (team, band of <see cref="BandRows"/> rows), heaviest first: each work item clears its rows of
/// the team's bitsets, ORs in the circles of the team's records that reach the band and updates the explored state
/// (fog = max(min(fog, 128), vision)). A work item only writes its own rows of its own team: no shared writes, no
/// atomics, identical results for any thread count.</item>
/// <item>PARALLEL over teams: ghosts (last-seen enemy structures) are recorded / removed when re-scouted.</item>
/// </list>
/// </summary>
public static class IntelSystem
{
    /// <summary>Grid rows per parallel work item.</summary>
    public const int BandRows = 16;

    public static void Run(SimWorld w)
    {
        StealthSystem.Run(w); // active stealth/cloak/field bits per unit (Intel/StealthSystem)
        var intel = w.Intel;
        if (!intel.Enabled) return;
        long sw = Stopwatch.GetTimestamp(); // (no Stopwatch object per tick)
        var state = IntelUpdate.Of(w);
        state.Gather();
        Par.For(state.Items, 1, state.BandBody);
        foreach (var ti in intel.Teams)
        {
            ti.RadarUsed = ti.RadarStamped;
            ti.SonarUsed = ti.SonarStamped;
            ti.OmniUsed = ti.OmniStamped;
        }
        Par.ForEach(intel.TeamCount, state.GhostBody);
        intel.Version++;
        intel.LastUpdateMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
    }

    /// <summary>
    /// Sets the bits of rows [rowMin, rowMax) whose cell center is within the circle. Coordinates are in cells, shifted
    /// so that cell centers are integers: px = x / cellSize - 0.5, pz = z / cellSize - 0.5, rc = radius / cellSize.
    /// Bitset layout: row z starts at word z * wordsPerRow; bit x of the row = cell column x.
    /// </summary>
    internal static void StampCells(ulong[] bits, int wordsPerRow, int cols, int rowMin, int rowMax, float px, float pz, float rc)
    {
        int z0 = Math.Max(rowMin, (int)MathF.Ceiling(pz - rc));
        int z1 = Math.Min(rowMax - 1, (int)MathF.Floor(pz + rc));
        if (z1 < z0) return;
        if ((uint)rowMin > (uint)rowMax || (long)rowMax * wordsPerRow > bits.Length) throw new ArgumentOutOfRangeException(nameof(rowMax));
        float r2 = rc * rc;
        ref ulong b0 = ref MemoryMarshal.GetArrayDataReference(bits);
        for (int z = z0; z <= z1; z++)
        {
            float dz = z - pz;
            float hw = MathF.Sqrt(MathF.Max(0f, r2 - dz * dz));
            int x0 = (int)MathF.Ceiling(px - hw);
            int x1 = (int)MathF.Floor(px + hw);
            if (x0 < 0) x0 = 0;
            if (x1 >= cols) x1 = cols - 1;
            if (x1 < x0) continue;
            // In bounds (checked above): rowMin <= z < rowMax and 0 <= x0 <= x1 < cols <= 64 * wordsPerRow.
            ref ulong row = ref Unsafe.Add(ref b0, z * wordsPerRow);
            int w0 = x0 >> 6, w1 = x1 >> 6;
            ulong m0 = ~0UL << (x0 & 63);
            ulong m1 = ~0UL >> (63 - (x1 & 63));
            if (w0 == w1) Unsafe.Add(ref row, w0) |= m0 & m1;
            else
            {
                Unsafe.Add(ref row, w0) |= m0;
                for (int k = w0 + 1; k < w1; k++) Unsafe.Add(ref row, k) = ~0UL;
                Unsafe.Add(ref row, w1) |= m1;
            }
        }
    }

    private enum SensorKind : byte { Radar, Sonar, Omni }

    /// <summary>A powered radar/sonar/omni circle, in cell units (see <see cref="StampCells"/>).</summary>
    private struct Sensor
    {
        public int Team;
        public SensorKind Kind;
        public float X, Z, R;
    }

    /// <summary>Per-world scratch: per-blueprint intel radii, sorted records, cached delegates (no per-tick allocations).</summary>
    private sealed class IntelUpdate
    {
        private static readonly ConditionalWeakTable<SimWorld, IntelUpdate> Table = new();
        public static IntelUpdate Of(SimWorld w) => Table.GetValue(w, static x => new IntelUpdate(x));

        private readonly SimWorld _w;
        /// <summary>Per blueprint, in cells (0 = none).</summary>
        private readonly float[] _vision, _radar, _sonar, _omni;
        private readonly bool[] _upkeep, _structure;
        // Cached delegates (not saved: recreated lazily after a load).
        [Persistence.NoSave] private Action<int, int, int>? _bandBody;
        [Persistence.NoSave] private Action<int>? _ghostBody;
        public Action<int, int, int> BandBody => _bandBody ??= Band;
        public Action<int> GhostBody => _ghostBody ??= UpdateGhosts;
        public readonly int Bands;
        /// <summary>Work items (team, band), dispatched by descending estimated cost: Parallel.For hands out
        /// contiguous index ranges, and the heavy bands of a dense army blob are adjacent.</summary>
        public readonly int Items;
        private readonly int[] _order;
        private readonly int[] _cost;

        // Vision records of this update (cell units) sorted by (team, row), SoA: bucket key = team * rows + row,
        // records of bucket b = [_start[b], _start[b + 1]).
        private readonly float[] _recX, _recZ, _recR;
        private readonly float[] _tmpX, _tmpZ, _tmpR;
        private readonly int[] _tmpKey;
        private readonly int[] _start, _cursor;
        /// <summary>Largest vision radius of this update, in whole cells (rounded up).</summary>
        private int _maxRc;
        private readonly List<Sensor> _sensors = new();

        private IntelUpdate(SimWorld w)
        {
            _w = w;
            var db = w.Blueprints;
            int n = db.Count;
            _vision = new float[n]; _radar = new float[n]; _sonar = new float[n]; _omni = new float[n];
            _upkeep = new bool[n]; _structure = new bool[n];
            float inv = 1f / w.Intel.CellSize;
            // Generous coverage: any cell that can contain a point within the radius (half cell diagonal of padding),
            // so a unit within the radius of a viewer is always in a covered cell (no false negatives).
            const float pad = 0.7072f;
            for (int b = 0; b < n; b++)
            {
                var it = db[b].Intel;
                _vision[b] = MathF.Max(it.VisionRadius, 0) * inv + pad; // radius 0: the unit still sees its own cell
                _radar[b] = it.RadarRadius > 0 ? it.RadarRadius * inv + pad : 0;
                _sonar[b] = it.SonarRadius > 0 ? it.SonarRadius * inv + pad : 0;
                _omni[b] = it.OmniRadius > 0 ? it.OmniRadius * inv + pad : 0;
                _upkeep[b] = db[b].Economy.MaintenanceEnergy > 0;
                _structure[b] = db[b].IsStructure;
            }
            int cap = w.Units.Capacity;
            _recX = new float[cap]; _recZ = new float[cap]; _recR = new float[cap];
            _tmpX = new float[cap]; _tmpZ = new float[cap]; _tmpR = new float[cap]; _tmpKey = new int[cap];
            int buckets = w.Intel.TeamCount * w.Intel.Rows;
            _start = new int[buckets + 1];
            _cursor = new int[buckets + 1];
            Bands = (w.Intel.Rows + BandRows - 1) / BandRows;
            Items = Bands * w.Intel.TeamCount;
            _order = new int[Items];
            _cost = new int[Items];
        }

        /// <summary>Sequential: vision records (stable counting sort by team and row) and powered sensors.</summary>
        public void Gather()
        {
            var w = _w;
            var u = w.Units;
            var intel = w.Intel;
            float inv = 1f / intel.CellSize;
            int rows = intel.Rows;
            Array.Clear(_start);
            _sensors.Clear();
            foreach (var ti in intel.Teams) ti.RadarStamped = ti.SonarStamped = ti.OmniStamped = false;
            int n = 0;
            float maxR = 0;
            for (int k = 0; k < u.AliveCount; k++)
            {
                int i = u.AliveList[k];
                var flags = u.Flags[i];
                if ((flags & (UnitFlags.Dead | UnitFlags.UnderConstruction)) != 0) continue;
                int b = u.Bp[i];
                int team = intel.TeamOf(u.Army[i]);
                float px = u.Pos[i].X * inv - 0.5f, pz = u.Pos[i].Y * inv - 0.5f;
                int key = team * rows + Math.Clamp((int)(pz + 0.5f), 0, rows - 1);
                _tmpX[n] = px; _tmpZ[n] = pz; _tmpR[n] = _vision[b]; _tmpKey[n] = key;
                _start[key + 1]++;
                maxR = MathF.Max(maxR, _vision[b]);
                n++;

                if (_radar[b] <= 0 && _sonar[b] <= 0 && _omni[b] <= 0) continue;
                // Active sensors need power: off when paused or when the army's energy stalls.
                if (_upkeep[b] && ((flags & UnitFlags.Paused) != 0 || u.Econ[i].Efficiency < 0.5f)) continue;
                var tt = intel.Teams[team];
                if (_radar[b] > 0) { _sensors.Add(new Sensor { Team = team, Kind = SensorKind.Radar, X = px, Z = pz, R = _radar[b] }); tt.RadarStamped = true; }
                if (_sonar[b] > 0) { _sensors.Add(new Sensor { Team = team, Kind = SensorKind.Sonar, X = px, Z = pz, R = _sonar[b] }); tt.SonarStamped = true; }
                if (_omni[b] > 0) { _sensors.Add(new Sensor { Team = team, Kind = SensorKind.Omni, X = px, Z = pz, R = _omni[b] }); tt.OmniStamped = true; }
            }
            int buckets = _start.Length - 1;
            for (int c = 0; c < buckets; c++) _start[c + 1] += _start[c];
            Array.Copy(_start, _cursor, buckets + 1);
            for (int k = 0; k < n; k++)
            {
                int d = _cursor[_tmpKey[k]]++;
                _recX[d] = _tmpX[k]; _recZ[d] = _tmpZ[k]; _recR[d] = _tmpR[k];
            }
            _maxRc = (int)MathF.Ceiling(maxR);

            // Estimated cost of each work item = records that can reach its band; heaviest first.
            for (int it = 0; it < Items; it++)
            {
                int team = it / Bands;
                var (ra, rb) = RecordRows(it % Bands);
                _order[it] = it;
                _cost[it] = -(_start[team * rows + rb + 1] - _start[team * rows + ra]);
            }
            Array.Sort(_cost, _order);
        }

        /// <summary>Grid rows whose records may reach the band (records are keyed by the row containing them).</summary>
        private (int ra, int rb) RecordRows(int band)
        {
            int rows = _w.Intel.Rows;
            int z0 = band * BandRows, z1 = Math.Min(rows, z0 + BandRows);
            return (Math.Max(0, z0 - _maxRc - 1), Math.Min(rows - 1, z1 + _maxRc));
        }

        /// <summary>Work item _order[orderIndex] = team * Bands + band: rows [band*BandRows, +BandRows) of one team.</summary>
        private void Band(int _, int orderIndex, int orderEnd)
        {
            var intel = _w.Intel;
            int item = _order[orderIndex];
            int team = item / Bands, band = item % Bands;
            var ti = intel.Teams[team];
            int cols = intel.Cols, rows = intel.Rows, wpr = intel.WordsPerRow;
            int z0 = band * BandRows, z1 = Math.Min(rows, z0 + BandRows);
            int wFrom = z0 * wpr, wLen = (z1 - z0) * wpr;

            ti.Vision.AsSpan(wFrom, wLen).Clear();
            if (ti.RadarUsed) ti.Radar.AsSpan(wFrom, wLen).Clear();
            if (ti.SonarUsed) ti.Sonar.AsSpan(wFrom, wLen).Clear();
            if (ti.OmniUsed) ti.Omni.AsSpan(wFrom, wLen).Clear();

            // Vision: the team's records whose row is near the band (one contiguous range of the sorted records).
            var (ra, rb) = RecordRows(band);
            float lo = z0, hi = z1 - 1; // cell-center rows of the band
            var vision = ti.Vision;
            for (int k = _start[team * rows + ra], end = _start[team * rows + rb + 1]; k < end; k++)
            {
                float z = _recZ[k], r = _recR[k];
                if (z + r < lo || z - r > hi) continue;
                StampCells(vision, wpr, cols, z0, z1, _recX[k], z, r);
            }
            foreach (var sn in _sensors)
            {
                if (sn.Team != team || sn.Z + sn.R < lo || sn.Z - sn.R > hi) continue;
                var grid = sn.Kind switch { SensorKind.Radar => ti.Radar, SensorKind.Sonar => ti.Sonar, _ => ti.Omni };
                StampCells(grid, wpr, cols, z0, z1, sn.X, sn.Z, sn.R);
            }

            // Explored state: fog = max(min(fog, Explored), vision ? Visible : 0), 64 cells per word.
            var fogAll = ti.Fog;
            var explored = new Vector<byte>(IntelService.FogExplored);
            int vn = Vector<byte>.Count;
            for (int z = z0; z < z1; z++)
            for (int wi = 0; wi < wpr; wi++)
            {
                ulong bits = vision[z * wpr + wi];
                int c0 = wi * 64;
                int cnt = Math.Min(64, cols - c0);
                var fog = fogAll.AsSpan(z * cols + c0, cnt);
                ulong full = cnt == 64 ? ~0UL : (1UL << cnt) - 1;
                if ((bits & full) == full) { fog.Fill(IntelService.FogVisible); continue; }
                if (bits == 0)
                {
                    int c = 0;
                    for (; c + vn <= cnt; c += vn) Vector.Min(new Vector<byte>(fog.Slice(c, vn)), explored).CopyTo(fog.Slice(c, vn));
                    for (; c < cnt; c++) fog[c] = Math.Min(fog[c], IntelService.FogExplored);
                    continue;
                }
                for (int c = 0; c < cnt; c++)
                    fog[c] = ((bits >> c) & 1) != 0 ? IntelService.FogVisible : Math.Min(fog[c], IntelService.FogExplored);
            }
        }

        /// <summary>Records visible enemy structures as ghosts of team t, drops ghosts seen to be gone.</summary>
        private void UpdateGhosts(int t)
        {
            var w = _w;
            var u = w.Units;
            var intel = w.Intel;
            var ti = intel.Teams[t];
            for (int k = 0; k < u.AliveCount; k++)
            {
                int i = u.AliveList[k];
                int b = u.Bp[i];
                if (!_structure[b] || (u.Flags[i] & (UnitFlags.Dead | UnitFlags.UnderConstruction)) != 0) continue;
                if (intel.TeamOf(u.Army[i]) == t || !IntelService.Test(ti.Vision, intel.BitIndex(u.Pos[i]))) continue;
                if (StealthSystem.HiddenFromTeam(w, ti, i)) continue; // cloaked: not seen, no ghost
                if (ti.GhostGen[i] == 0) ti.GhostSlots.Add(i);
                ti.GhostGen[i] = u.Gen[i];
                ti.Ghosts[i] = new GhostInfo { Slot = i, Gen = u.Gen[i], Bp = b, Army = u.Army[i], Pos = u.Pos[i], Elevation = u.Elevation[i], Heading = u.Heading[i] };
            }
            // Ghosts whose position is in vision but whose structure is gone are forgotten.
            var list = ti.GhostSlots;
            int n = 0;
            for (int k = 0; k < list.Count; k++)
            {
                int s = list[k];
                ref var g = ref ti.Ghosts[s];
                bool gone = IntelService.Test(ti.Vision, intel.BitIndex(g.Pos)) && !(u.Alive[s] && u.Gen[s] == g.Gen && u.IsActive(s)
                    && !StealthSystem.HiddenFromTeam(w, ti, s)); // a cloaked structure in sight looks gone
                if (gone) { ti.GhostGen[s] = 0; continue; }
                list[n++] = s;
            }
            if (n < list.Count) list.RemoveRange(n, list.Count - n);
        }
    }
}
