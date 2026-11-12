using System.Numerics;
using Rts.Sim.Core;

namespace Rts.Sim.Map;

/// <summary>
/// One map generation run (see <see cref="MapGenerator"/>). Stages:
/// <list type="number">
/// <item><b>Fields</b>: three symmetric noise fields (relief A, detail D, lake placement L), domain-warped, sampled
///   through <see cref="MapSymmetry.Sample"/> (half resolution on 1024 maps, parallel rows).</item>
/// <item><b>Relief</b> per preset: rolling hills (Plains/Lakes), terraces with cliffs (Highlands), mesas
///   (Canyon, then valleys are carved, see MapBuilder.Canyon.cs), islands thresholded at a sea-coverage
///   percentile (Archipelago).</item>
/// <item><b>Lakes</b>: L above the water-amount percentile, kept away from start areas.</item>
/// <item><b>Start areas</b>: flat plateaus (same height for everybody), blended into the surroundings.</item>
/// <item><b>Connectivity</b> (land presets): label land components with clearance for big units; carve corridors
///   (ramps, causeways, passes) from the main component to every disconnected start and every big component, in
///   all N rotated copies; repeat until connected.</item>
/// <item><b>Deposits</b> (MapBuilder.Deposits.cs): base clusters, expansion clusters, scattered mass,
///   hydrocarbons — validated on flat land of the main component in every rotated copy; small pads flattened.</item>
/// <item><b>Verification</b>: the final <see cref="Terrain"/> is flood-filled over CanTraverse(Land); repaired if needed.</item>
/// </list>
/// Deterministic: everything derives from the seed; parallel loops only compute independent per-vertex values.
/// </summary>
internal sealed partial class MapBuilder
{
    private readonly MapGenSettings _s;
    private readonly int _n;
    private readonly int _stride;
    private readonly float[] _h;
    private readonly float _w;
    private readonly Vector2[] _starts;
    private readonly Vector2 _center;
    private readonly MapSymmetry _sym;
    private readonly ulong _seed;
    private readonly MapPreset _preset;
    private readonly float _water;
    /// <summary>Map size relative to 256.</summary>
    private readonly float _sc;
    private readonly float _relief;
    /// <summary>Hill feature size in world units.</summary>
    private readonly float _scale;
    private readonly float _flatR;
    private readonly float _blendR;
    private readonly MapNoise _nHill, _nDetail, _nLake, _nWarpX, _nWarpY, _nEdge;
    private readonly CarveScratch _scratch;

    // Symmetric fields at vertex resolution.
    private float[] _fA = [], _fD = [], _fL = [], _fR = [];
    /// <summary>Canyon: valley floor heights (lerp target) and the valley mask.</summary>
    private float[]? _floor, _valley, _lakeOk;

    public MapBuilder(MapGenSettings s)
    {
        _s = s;
        _n = Math.Max(32, s.Size);
        _stride = _n + 1;
        _h = new float[_stride * _stride];
        _w = s.WaterLevel;
        _preset = s.Preset;
        _water = Math.Clamp(s.WaterAmount < 0 ? MapPresets.DefaultWater(s.Preset) : s.WaterAmount, 0f, 1f);
        _seed = (ulong)(uint)s.Seed * 0x2545F4914F6CDD1DUL + 17 + (ulong)s.Preset * 0x9E3779B97F4A7C15UL;
        _starts = MapGenerator.StartPositions(_n, s.Players);
        _center = new Vector2(_n * 0.5f, _n * 0.5f);
        float phi0 = DMath.HeadingOf(_starts[0] - _center);
        _sc = _n / 256f;
        _relief = s.MaxHeight / 28f;
        _scale = s.NoiseScale * MathF.Sqrt(_sc);
        _sym = new MapSymmetry(_starts.Length, _center, phi0, band: 14f * MathF.Sqrt(_sc), centerRadius: _n * 0.07f);
        _flatR = 24f + 3f * DMath.Log2(MathF.Max(1f, _sc));
        _blendR = _flatR + 20f;
        _nHill = new MapNoise(_seed ^ 0x1111);
        _nDetail = new MapNoise(_seed ^ 0x2222);
        _nLake = new MapNoise(_seed ^ 0x3333);
        _nWarpX = new MapNoise(_seed ^ 0x4444);
        _nWarpY = new MapNoise(_seed ^ 0x5555);
        _nEdge = new MapNoise(_seed ^ 0x6666);
        _scratch = new CarveScratch(_h.Length);
    }

    private bool LandPreset => _preset != MapPreset.Archipelago;

    public Terrain Build()
    {
        ComputeFields();
        ComposeRelief();
        if (_preset == MapPreset.Canyon) CarveValleys();
        if (_preset != MapPreset.Archipelago) ApplyLakes();
        FlattenStarts();
        if (LandPreset) EnsureConnectivity(connectBig: true);
        else { EnsureSeaLanes(); ShapeBeaches(); } // Archipelago: sea lanes + walkable coasts (MapBuilder.Archipelago.cs)

        var deposits = PlaceDeposits();
        FlattenPads(deposits);

        var t = new Terrain(_n, _n, _h, _w, [], _starts, _s.Seed);
        if (LandPreset && !MapGenerator.StartsConnectedByLand(t))
        {
            // Pads changed something (should not happen): repair again.
            EnsureConnectivity(connectBig: false);
            t = new Terrain(_n, _n, _h, _w, [], _starts, _s.Seed);
        }
        var valid = deposits.Where(d => DepositFits(t, d)).ToArray();
        var final = new Terrain(_n, _n, _h, _w, valid, _starts, _s.Seed);
        final.Props = MapProps.Place(final, _sym, _seed, _preset, _flatR); // trees and rocks (Map/MapProps.cs)
        return final;
    }

    private int V(int x, int z) => z * _stride + x;

    private float DistToNearestStart(Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var sp in _starts) best = MathF.Min(best, Vector2.DistanceSquared(sp, p));
        return MathF.Sqrt(best);
    }

    // ------------------------------------------------------------------ fields

    /// <summary>Template function: fields of player 0's sector at p (A relief, D detail, L lakes, R ramps).</summary>
    private void Template(Vector2 p, Span<float> o)
    {
        float S = _scale;
        float wsc = 1f / (S * 1.3f);
        float wx = _nWarpX.Fbm(p.X * wsc, p.Y * wsc, 3) * S * 0.45f;
        float wz = _nWarpY.Fbm(p.X * wsc, p.Y * wsc, 3) * S * 0.45f;
        float qx = p.X + wx, qz = p.Y + wz;
        // Highlands: smoother, larger relief so plateaus are big and coherent.
        bool hl = _preset == MapPreset.Highlands;
        float sa = hl ? S * 1.35f : S;
        o[0] = _nHill.Fbm(qx / sa, qz / sa, hl ? 3 : 5);
        o[1] = _nDetail.Fbm(p.X / (S * 0.22f), p.Y / (S * 0.22f), 3);
        o[2] = _nLake.Fbm(qx / (S * 0.75f), qz / (S * 0.75f), 4);
        o[3] = hl ? _nEdge.Fbm(p.X / (S * 0.5f), p.Y / (S * 0.5f), 2) : 0f;
    }

    private void ComputeFields()
    {
        int res = _n > 512 ? 2 : 1;
        int gw = (_n + res - 1) / res + 1;
        var a = new float[gw * gw];
        var d = new float[gw * gw];
        var l = new float[gw * gw];
        var r = new float[gw * gw];
        Parallel.For(0, gw, gz =>
        {
            Span<float> v = stackalloc float[4];
            for (int gx = 0; gx < gw; gx++)
            {
                _sym.Sample(new Vector2(Math.Min(_n, gx * res), Math.Min(_n, gz * res)), 4, v, Template);
                int i = gz * gw + gx;
                a[i] = v[0]; d[i] = v[1]; l[i] = v[2]; r[i] = v[3];
            }
        });
        if (res == 1)
        {
            _fA = a; _fD = d; _fL = l; _fR = r;
            return;
        }
        _fA = new float[_h.Length];
        _fD = new float[_h.Length];
        _fL = new float[_h.Length];
        _fR = new float[_h.Length];
        Parallel.For(0, _stride, z =>
        {
            float gzf = z / (float)res;
            int z0 = Math.Min((int)gzf, gw - 2);
            float fz = gzf - z0;
            for (int x = 0; x < _stride; x++)
            {
                float gxf = x / (float)res;
                int x0 = Math.Min((int)gxf, gw - 2);
                float fx = gxf - x0;
                int i00 = z0 * gw + x0, i10 = i00 + 1, i01 = i00 + gw, i11 = i01 + 1;
                float Bi(float[] f) => (f[i00] * (1 - fx) + f[i10] * fx) * (1 - fz) + (f[i01] * (1 - fx) + f[i11] * fx) * fz;
                int v = z * _stride + x;
                _fA[v] = Bi(a); _fD[v] = Bi(d); _fL[v] = Bi(l); _fR[v] = Bi(r);
            }
        });
    }

    // ------------------------------------------------------------------ relief

    private const int HighlandLevels = 3;

    /// <summary>Highlands terrace level height (level 0 = lowland).</summary>
    private float TerraceHeight(float level) => _w + 2.5f + _relief * 7f * level;

    private void ComposeRelief()
    {
        float W = _w, rf = _relief;
        float seaT = 0;
        if (_preset == MapPreset.Archipelago)
        {
            // Island field = relief + a bump around each start; threshold at the sea-coverage percentile.
            float seaFrac = 0.3f + 0.45f * _water;
            var samples = new List<float>();
            for (int z = 0; z <= _n; z += 4)
            for (int x = 0; x <= _n; x += 4)
                samples.Add(IslandField(x, z));
            samples.Sort();
            seaT = samples[Math.Clamp((int)(seaFrac * samples.Count), 0, samples.Count - 1)];
        }
        if (_preset == MapPreset.Canyon)
        {
            _floor = new float[_h.Length];
            _valley = new float[_h.Length];
            _lakeOk = new float[_h.Length];
        }

        Parallel.For(0, _stride, z =>
        {
            for (int x = 0; x < _stride; x++)
            {
                int v = z * _stride + x;
                float A = _fA[v], D = _fD[v];
                float h;
                switch (_preset)
                {
                    case MapPreset.Plains:
                    {
                        float raw = W + 5.5f + rf * (20f * A + 3f * D);
                        h = W + 1.5f + MapMath.SMax(raw - (W + 1.5f), 0f, 2f);
                        break;
                    }
                    case MapPreset.Lakes:
                    {
                        float raw = W + 6.5f + rf * (22f * A + 2f * D);
                        h = W + 1.5f + MapMath.SMax(raw - (W + 1.5f), 0f, 2f);
                        break;
                    }
                    case MapPreset.Highlands:
                    {
                        // Terraces: each level rises in a narrow band (a cliff); where the ramp field is high the
                        // band widens into a natural, passable slope.
                        float t = Math.Clamp(0.5f + A * 3.4f, 0f, 0.999f);
                        float xk = t * HighlandLevels;
                        float lvl = MathF.Min(MathF.Floor(xk), HighlandLevels - 1);
                        float fr = xk - lvl;
                        float band = MapMath.Lerp(0.08f, 0.75f, MapMath.SmoothStep(0.03f, 0.15f, _fR[v]));
                        float step = MapMath.SmoothStep((fr - (1f - band)) / band);
                        h = TerraceHeight(lvl + step) + 0.5f * D;
                        break;
                    }
                    case MapPreset.Canyon:
                    {
                        h = W + 20f + rf * (16f * A + 4f * D);
                        _floor![v] = W + 3f + rf * (4f * A + 1.2f * D);
                        break;
                    }
                    default: // Archipelago
                    {
                        float e = IslandField(x, z) - seaT;
                        h = e > 0
                            ? W + 0.5f + rf * (40f * MapMath.SMax(0f, MathF.Min(e, 0.22f), 0.02f) + 3f * D * MapMath.SmoothStep(0f, 0.08f, e))
                            : W + 0.5f + 34f * e;
                        h = MathF.Max(h, W - 10f);
                        break;
                    }
                }
                _h[v] = h;
            }
        });
    }

    private float IslandField(int x, int z)
    {
        float k = MathF.Sqrt(_sc);
        float ds = DistToNearestStart(new Vector2(x, z));
        float bias = 0.55f * (1f - MapMath.SmoothStep(_flatR + 10f * k, _flatR + 45f * k, ds));
        return _fA[z * _stride + x] + bias;
    }

    // ------------------------------------------------------------------ lakes

    private void ApplyLakes()
    {
        float frac = _water * 0.35f;
        if (frac <= 0.002f) return;
        float W = _w;
        float r1 = _flatR + 18f, r2 = _flatR + 46f;
        // Lake suitability (minus the start-area suppression), -1 where lakes are not allowed.
        var lf = new float[_h.Length];
        Parallel.For(0, _stride, z =>
        {
            for (int x = 0; x < _stride; x++)
            {
                int v = z * _stride + x;
                float allowed = _preset switch
                {
                    MapPreset.Highlands => MapMath.SmoothStep(W + 10f, W + 5f, _h[v]),
                    MapPreset.Canyon => _lakeOk![v],
                    _ => 1f,
                };
                float ds = DistToNearestStart(new Vector2(x, z));
                float val = _fL[v] - (1f - MapMath.SmoothStep(r1, r2, ds));
                lf[v] = allowed > 0.5f ? val : -1f;
                if (allowed < 1f) lf[v] = MathF.Min(lf[v], val - (1f - allowed) * 0.3f);
            }
        });
        var samples = new List<float>();
        for (int z = 0; z <= _n; z += 3)
        for (int x = 0; x <= _n; x += 3)
            samples.Add(lf[z * _stride + x]);
        samples.Sort();
        float T = samples[Math.Clamp((int)((1f - frac) * samples.Count), 0, samples.Count - 1)];
        T = MathF.Max(T, 0.04f); // never flood typical terrain
        Parallel.For(0, _stride, z =>
        {
            for (int x = 0; x < _stride; x++)
            {
                int v = z * _stride + x;
                float m = MapMath.SmoothStep(T - 0.035f, T + 0.05f, lf[v]);
                if (m <= 0) continue;
                float bottom = W - 2f - 4f * MapMath.SmoothStep(T + 0.05f, T + 0.2f, lf[v]);
                if (_h[v] > bottom) _h[v] += (bottom - _h[v]) * m;
            }
        });
    }

    // ------------------------------------------------------------------ start areas

    private float StartHeight()
    {
        var s0 = _starts[0];
        float sum = 0;
        int cnt = 0;
        for (int dz = -10; dz <= 10; dz += 2)
        for (int dx = -10; dx <= 10; dx += 2)
        {
            sum += MapConnectivity.HeightAt(_h, _n, s0.X + dx, s0.Y + dz);
            cnt++;
        }
        float avg = sum / cnt;
        return _preset switch
        {
            MapPreset.Highlands => TerraceHeight(Math.Clamp(MathF.Round((avg - TerraceHeight(0)) / (7f * _relief)), 0f, HighlandLevels - 1)),
            MapPreset.Canyon => _w + 3f,
            MapPreset.Archipelago => Math.Clamp(avg, _w + 2.5f, _w + 9f),
            _ => Math.Clamp(avg, _w + 3f, _w + 14f),
        };
    }

    /// <summary>Height of the flat start plateaus (the same for every start), set by <see cref="FlattenStarts"/>.</summary>
    private float _plateauH;

    private void FlattenStarts()
    {
        float hs = StartHeight();
        _plateauH = hs;
        Parallel.For(0, _stride, z =>
        {
            for (int x = 0; x < _stride; x++)
            {
                float d = DistToNearestStart(new Vector2(x, z));
                if (d >= _blendR) continue;
                float t = d <= _flatR ? 1f : 1f - MapMath.SmoothStep((d - _flatR) / (_blendR - _flatR));
                int v = z * _stride + x;
                _h[v] += (hs - _h[v]) * t;
            }
        });
    }

    // ------------------------------------------------------------------ connectivity

    /// <summary>Carves a corridor in all N rotated copies.</summary>
    private void CarveSymmetric(List<Vector2> poly, float coreR, float blendR)
    {
        var lo = new Vector2(1.5f);
        var hi = new Vector2(_n - 1.5f);
        if (!_sym.QuarterTurns)
        {
            // 3, 5–8 players: one ground profile (computed along this copy) for every rotated copy, and all copies
            // rasterized together (nearest sample wins where they overlap). A profile per copy, carved one copy
            // after the other, depends on the resampled terrain under it and on the copies carved before it, which
            // made ramps and causeways differ between the players' sectors.
            var first = new List<Vector2>(poly.Count);
            foreach (var p in poly) first.Add(Vector2.Clamp(p, lo, hi));
            var (samples, target) = MapConnectivity.CorridorProfile(_h, _n, _w, first);
            // Start plateaus stay flat: the profile is kept at the plateau height inside it and may leave that height
            // only at the corridor slope from its edge, so ramps are built outwards instead of being cut into a base
            // (on small maps with many players a corridor often runs right past or through a start).
            float hs = _plateauH;
            for (int j = 0; j < samples.Count; j++)
            {
                float slack = MapConnectivity.CorridorSlope * MathF.Max(0f, DistToNearestStart(samples[j]) - _flatR);
                target[j] = Math.Clamp(target[j], hs - slack, hs + slack);
            }
            int m = samples.Count;
            var all = new List<Vector2>(m * _sym.N);
            var allTarget = new float[m * _sym.N];
            for (int k = 0; k < _sym.N; k++)
            {
                foreach (var p in samples) all.Add(Vector2.Clamp(_sym.Rotate(p, k), lo, hi));
                Array.Copy(target, 0, allTarget, k * m, m);
            }
            MapConnectivity.RasterizeCorridor(_h, _n, all, allTarget, coreR, blendR, _scratch);
            return;
        }
        for (int k = 0; k < _sym.N; k++)
        {
            var rp = new List<Vector2>(poly.Count);
            foreach (var p in poly) rp.Add(Vector2.Clamp(_sym.Rotate(p, k), new Vector2(1.5f), new Vector2(_n - 1.5f)));
            MapConnectivity.CarveCorridor(_h, _n, _w, rp, coreR, blendR, _scratch);
        }
    }

    /// <summary>Optional components worth a corridor: on Canyon only valley floors (mesa tops stay unreachable).</summary>
    private bool[] WorthConnecting(int[] labels, int count, List<int> sizes)
    {
        var worth = new bool[count];
        if (_preset != MapPreset.Canyon)
        {
            Array.Fill(worth, true);
            return worth;
        }
        var inValley = new int[count];
        for (int c = 0; c < labels.Length; c++)
        {
            int l = labels[c];
            if (l >= 0 && _valley![V(c % _n, c / _n)] > 0.5f) inValley[l]++;
        }
        for (int l = 0; l < count; l++) worth[l] = inValley[l] > sizes[l] * 0.6f;
        return worth;
    }

    /// <summary>
    /// Makes every start (and, if requested, every big land component) reachable from start 0 for units needing
    /// clearance 2 (3x3 free cells, e.g. the commander), which implies plain land connectivity.
    /// </summary>
    private void EnsureConnectivity(bool connectBig)
    {
        int cells = _n * _n;
        var land = new byte[cells];
        var ok = new byte[cells];
        var labels = new int[cells];
        var sizes = new List<int>();
        int bigMin = (int)(cells * 0.01f);
        const int maxRounds = 6;
        for (int round = 0; round <= maxRounds; round++)
        {
            MapConnectivity.ComputeLand(_h, _n, _w, land);
            MapConnectivity.Erode(land, _n, 1, ok);
            int count = MapConnectivity.Label(ok, _n, labels, sizes);
            int main = MapConnectivity.LabelNear(labels, _n, _starts[0], 8);
            var targets = new List<int>();
            bool startsOk = main >= 0;
            foreach (var sp in _starts)
            {
                int l = MapConnectivity.LabelNear(labels, _n, sp, 8);
                if (l != main) startsOk = false;
                if (l >= 0 && l != main && !targets.Contains(l)) targets.Add(l);
            }
            // Starts first; optional big components only once all starts are connected.
            if (connectBig && startsOk && round < maxRounds - 1)
            {
                // Biggest first (deterministic: size desc, then label).
                var worth = WorthConnecting(labels, count, sizes);
                var big = Enumerable.Range(0, count)
                    .Where(c => c != main && sizes[c] >= bigMin && !targets.Contains(c) && worth[c])
                    .OrderByDescending(c => sizes[c]).ThenBy(c => c);
                targets.AddRange(big);
            }
            if (startsOk && targets.Count == 0) return;
            if (round == maxRounds || main < 0)
            {
                // Fallback: straight wide corridors from every start to the center.
                var spoke = new List<Vector2> { _starts[0], DMath.Lerp(_starts[0], _center, 0.5f), _center };
                CarveSymmetric(spoke, 5.5f, 11f);
                if (round == maxRounds) return;
                continue;
            }
            if (startsOk && round >= maxRounds - 1) return; // big components are optional

            var paths = MapConnectivity.FindPaths(_h, _n, _w, labels, ok, main, targets);
            var handled = new bool[count];
            foreach (int t in targets)
            {
                if (handled[t] || !paths.TryGetValue(t, out var pts)) continue;
                CarveSymmetric(pts, 4f + round * 0.5f, 9.5f + round);
                handled[t] = true;
                for (int k = 1; k < _sym.N; k++)
                {
                    int l = MapConnectivity.LabelNear(labels, _n, _sym.Rotate(pts[^1], k), 4);
                    if (l >= 0) handled[l] = true;
                }
            }
        }
    }
}
