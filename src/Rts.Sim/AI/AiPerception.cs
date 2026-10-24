using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.Systems;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>An enemy structure the AI knows about (a ghost of one it has seen, or one in sight now).</summary>
public struct KnownStructure
{
    public UnitHandle Handle;
    public Vector2 Pos;
    public int Bp;
    public int Army;
}

/// <summary>
/// Everything an AI brain knows about the world this think, rebuilt by <see cref="AiPerception.Scan"/> from one pass
/// over Units.AliveList (ascending index order = deterministic). Own units are always fully known; enemy data is
/// filtered by the perception layer.
/// </summary>
public sealed class AiView
{
    public int Tick = -1;
    public int Commander = -1;
    /// <summary>Complete mobile engineers (not the commander).</summary>
    public readonly List<int> Engineers = new();
    public readonly List<int> LandFactories = new();
    public readonly List<int> AirFactories = new();
    /// <summary>Complete naval factories, and other complete factory structures (quantum gateways).</summary>
    public readonly List<int> NavalFactories = new();
    public readonly List<int> Gateways = new();
    /// <summary>Complete own missile silos (tactical/strategic launchers, anti-nukes).</summary>
    public readonly List<int> Silos = new();
    /// <summary>Complete mobile naval units (surface/sub, not engineers).</summary>
    public readonly List<int> NavalUnits = new();
    /// <summary>Highest tech of the complete land/air/naval factories and of the engineers (commander excluded); 0 = none.</summary>
    public int LandTier, AirTier, NavalTier, EngineerTier;
    /// <summary>Own structures upgrading right now.</summary>
    public int Upgrading;
    public float OwnNavalStrength;
    /// <summary>All own structures (complete or under construction).</summary>
    public readonly List<int> Structures = new();
    /// <summary>Own structures under construction.</summary>
    public readonly List<int> Unfinished = new();
    /// <summary>Complete mobile non-builder land/amphibious/hover units (tanks, artillery, AA, scouts).</summary>
    public readonly List<int> LandCombat = new();
    public readonly List<int> AirUnits = new();
    /// <summary>Own units per blueprint (complete + under construction).</summary>
    public int[] Count = [];
    /// <summary>Own complete units per blueprint.</summary>
    public int[] Complete = [];
    /// <summary>Factory queue entries per blueprint that have not started yet.</summary>
    public int[] Queued = [];
    public float OwnLandStrength;
    public float OwnAirStrength;

    /// <summary>Enemy mobile units the army detects right now (vision or radar; radar contacts are anonymous).</summary>
    public readonly List<int> EnemyMobile = new();
    /// <summary>Enemy structures detected right now (vision or radar).</summary>
    public readonly List<int> EnemyStructures = new();
    /// <summary>Strength of the enemy land/air units detected right now.</summary>
    public float EnemyLandStrength;
    public float EnemyAirStrength;
    public int EnemyAirCount;
    /// <summary>Strength of the enemy naval units detected right now.</summary>
    public float EnemyNavalStrength;
    /// <summary>Known enemy strategic launchers (ghosts or in sight): nuke silos / tactical missile launchers, and the
    /// position of the known TML closest to our base (valid when EnemyTml &gt; 0).</summary>
    public int EnemyNukes, EnemyTml;
    public Vector2 EnemyTmlPos;
    /// <summary>Known enemy naval factories.</summary>
    public int EnemyNavalFactories;
    /// <summary>An enemy air factory is known (seen and not seen destroyed).</summary>
    public bool EnemyHasAirFactory;

    /// <summary>Coarse threat maps per ThreatCell x ThreatCell cell: <see cref="Threat"/> = estimated enemy firepower
    /// against surface units (tanks, point defense, bombers...), <see cref="AirThreat"/> = enemy anti-air (AA,
    /// interceptors). Both combine what is detected now, the ghosts of known defenses and a decaying memory of enemy
    /// units seen recently (see <see cref="AiPerception"/>).</summary>
    public const float ThreatCell = 16f;
    /// <summary>Threat below this (e.g. a lone scout) is ignored by builders.</summary>
    public const float ThreatThreshold = 25f;
    public int ThreatCols, ThreatRows;
    public float[] Threat = [];
    public float[] AirThreat = [];
    /// <summary>Threat from enemies detected right now only (no memory): for reacting to what is really there.</summary>
    public float[] ThreatNow = [];

    public void InitThreat(int width, int height)
    {
        ThreatCols = Math.Max(1, (int)MathF.Ceiling(width / ThreatCell));
        ThreatRows = Math.Max(1, (int)MathF.Ceiling(height / ThreatCell));
        if (Threat.Length != ThreatCols * ThreatRows)
        {
            Threat = new float[ThreatCols * ThreatRows];
            AirThreat = new float[ThreatCols * ThreatRows];
            ThreatNow = new float[ThreatCols * ThreatRows];
        }
        Array.Clear(Threat);
        Array.Clear(AirThreat);
        Array.Clear(ThreatNow);
    }

    public int CellOf(Vector2 p)
    {
        int cx = Math.Clamp((int)(p.X / ThreatCell), 0, ThreatCols - 1);
        int cz = Math.Clamp((int)(p.Y / ThreatCell), 0, ThreatRows - 1);
        return cz * ThreatCols + cx;
    }

    public Vector2 CellCenter(int c) => new((c % ThreatCols + 0.5f) * ThreatCell, (c / ThreatCols + 0.5f) * ThreatCell);

    /// <summary>Adds a unit's firepower to the maps according to the layers its weapons can hit.</summary>
    public void AddThreat(Vector2 p, float v, LayerMask targets)
    {
        int c = CellOf(p);
        if ((targets & (LayerMask.Land | LayerMask.Water | LayerMask.Sub)) != 0) Threat[c] += v;
        if ((targets & LayerMask.Air) != 0) AirThreat[c] += v;
    }

    private static float Sum(float[] map, int cols, int rows, Vector2 p, float r)
    {
        int x0 = Math.Clamp((int)((p.X - r) / ThreatCell), 0, cols - 1);
        int x1 = Math.Clamp((int)((p.X + r) / ThreatCell), 0, cols - 1);
        int z0 = Math.Clamp((int)((p.Y - r) / ThreatCell), 0, rows - 1);
        int z1 = Math.Clamp((int)((p.Y + r) / ThreatCell), 0, rows - 1);
        float t = 0;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++) t += map[z * cols + x];
        return t;
    }

    /// <summary>Estimated enemy firepower against surface units in the cells overlapping the square of half-size r
    /// around p (known + remembered); trivial threats (below <see cref="ThreatThreshold"/>) count as 0.</summary>
    public float ThreatAt(Vector2 p, float r)
    {
        float t = Sum(Threat, ThreatCols, ThreatRows, p, r);
        return t < ThreatThreshold ? 0 : t;
    }

    /// <summary>Like <see cref="ThreatAt"/> but only from enemies detected right now (and known defenses).</summary>
    public float ThreatNowAt(Vector2 p, float r)
    {
        float t = Sum(ThreatNow, ThreatCols, ThreatRows, p, r);
        return t < ThreatThreshold ? 0 : t;
    }

    /// <summary>Enemy anti-air around p (for bomber targeting).</summary>
    public float AirThreatAt(Vector2 p, float r) => Sum(AirThreat, ThreatCols, ThreatRows, p, r);

    public void Clear(int bpCount)
    {
        Commander = -1;
        Engineers.Clear(); LandFactories.Clear(); AirFactories.Clear(); Structures.Clear(); Unfinished.Clear();
        LandCombat.Clear(); AirUnits.Clear(); EnemyMobile.Clear(); EnemyStructures.Clear();
        NavalFactories.Clear(); Gateways.Clear(); Silos.Clear(); NavalUnits.Clear();
        LandTier = AirTier = NavalTier = EngineerTier = 0;
        Upgrading = 0;
        OwnNavalStrength = EnemyNavalStrength = 0;
        EnemyNukes = EnemyTml = EnemyNavalFactories = 0;
        if (Count.Length != bpCount) { Count = new int[bpCount]; Complete = new int[bpCount]; Queued = new int[bpCount]; }
        Array.Clear(Count); Array.Clear(Complete); Array.Clear(Queued);
        OwnLandStrength = OwnAirStrength = EnemyLandStrength = EnemyAirStrength = 0;
        EnemyAirCount = 0;
        EnemyHasAirFactory = false;
    }

    public int Total(int bp) => bp < 0 ? 0 : Count[bp] + Queued[bp];
}

/// <summary>
/// The AI's only window on enemy state. With <see cref="RespectIntel"/> (the default) the AI plays by the fog-of-war
/// rules of <see cref="IntelService"/>, like a human: it sees enemy units only in its team's vision, radar contacts
/// only as anonymous blips (position, no type), enemy structures as the ghosts its team has seen (forgotten when
/// re-scouted dead), wrecks in explored areas. On top of that it keeps a decaying memory of where enemy firepower was
/// seen (threat maps), the last known position of each enemy commander and when each part of the map was last seen
/// (for re-scouting). Start positions are known (SupCom AIs know them too), the actual base must be scouted.
/// <para>RespectIntel = false makes the AI omniscient (debug/tests only).</para>
/// </summary>
public sealed class AiPerception
{
    private readonly SimWorld _w;
    private readonly int _army;
    /// <summary>true = only what the army's intel (vision/radar/ghosts) reports; false = omniscient (debug).</summary>
    public bool RespectIntel = true;

    /// <summary>Enemy structures known this think (ghosts + currently seen, incl. construction sites in sight).</summary>
    public readonly List<KnownStructure> Structures = new();
    /// <summary>Omniscient mode: per unit slot generation+1 of a remembered structure (0 = not remembered).</summary>
    private readonly int[] _rememberedGen;
    private readonly List<KnownStructure> _omniMemory = new();
    private readonly List<GhostInfo> _ghosts = new();
    /// <summary>Last known position of an enemy commander (per army), or null; tick it was last seen.</summary>
    public readonly Vector2?[] EnemyCommanderPos;
    public readonly int[] EnemyCommanderTick;
    public readonly UnitHandle[] EnemyCommanderHandle;

    /// <summary>Half-life (ticks) of the remembered enemy unit threat.</summary>
    public int MemoryHalfLife = 900;
    /// <summary>Remembered enemy mobile threat per threat cell (surface / anti-air), decaying when not refreshed.</summary>
    private float[] _memThreat = [], _memAir = [];
    private float[] _nowMob = [], _nowAir = [];
    /// <summary>Per threat cell: last tick its center was in vision (int.MinValue/2 = never).</summary>
    public int[] CellSeenTick = [];
    private int _lastScanTick = -1;
    /// <summary>Remembered enemy air: count and strength (decaying max of what was seen).</summary>
    public float EnemyAirCountMem, EnemyAirStrengthMem;
    /// <summary>Remembered enemy naval strength (decaying max of what was seen).</summary>
    public float EnemyNavalMem;
    /// <summary>Estimated enemy land army strength (decaying memory of what was seen, summed over the map).</summary>
    public float EnemyLandEstimate;

    public AiPerception(SimWorld w, int army)
    {
        _w = w;
        _army = army;
        _rememberedGen = new int[w.Units.Capacity];
        EnemyCommanderPos = new Vector2?[w.Armies.Length];
        EnemyCommanderTick = new int[w.Armies.Length];
        EnemyCommanderHandle = new UnitHandle[w.Armies.Length];
        for (int a = 0; a < EnemyCommanderHandle.Length; a++) EnemyCommanderHandle[a] = UnitHandle.None;
    }

    private bool IntelOn => RespectIntel && _w.Intel.Enabled;

    /// <summary>Does the AI know where this (enemy) unit is? (vision or radar)</summary>
    public bool IsKnown(int unit) => !IntelOn || _w.Intel.IsDetected(_army, unit);

    /// <summary>Does the AI know what this unit is (blueprint)? Radar-only contacts are anonymous.</summary>
    public bool IsIdentified(int unit) => !IntelOn || _w.Intel.IsVisible(_army, unit);

    public bool IsPositionVisible(Vector2 p) => !IntelOn || _w.Intel.IsPositionVisible(_army, p);

    public bool IsPositionExplored(Vector2 p) => !IntelOn || _w.Intel.IsPositionExplored(_army, p);

    /// <summary>Ticks since the threat cell containing p was last in vision (large if never).</summary>
    public int Staleness(AiView v, Vector2 p)
    {
        if (CellSeenTick.Length == 0) return int.MaxValue / 4;
        return _w.Tick - CellSeenTick[v.CellOf(p)];
    }

    /// <summary>Strength value of a unit: mass cost of armed units, scaled by remaining health.</summary>
    public static float Strength(SimWorld w, int i, UnitBlueprint bp)
    {
        if (bp.Weapons.Length == 0) return 0;
        float m = bp.Has(UnitCategory.Commander) ? 1200 : bp.Economy.BuildCostMass;
        return m * MathF.Max(0.2f, w.Units.Health[i] / MathF.Max(1f, bp.MaxHealth));
    }

    /// <summary>Union of the layers a unit's weapons can hit.</summary>
    public static LayerMask TargetsOf(UnitBlueprint bp)
    {
        var m = LayerMask.None;
        foreach (var wb in bp.Weapons) m |= wb.TargetMask;
        return m;
    }

    private static bool IsAirUnit(UnitBlueprint bp) => bp.Movement.Layer == MotionLayer.Air;

    /// <summary>One deterministic pass over the alive units: classifies own units and the known enemies, then
    /// updates the structure list, the threat memory and the scouting staleness grid.</summary>
    public void Scan(AiView v)
    {
        var u = _w.Units;
        var db = _w.Blueprints;
        v.Clear(db.Count);
        v.InitThreat(_w.Terrain.Width, _w.Terrain.Height);
        int cells = v.Threat.Length;
        if (_memThreat.Length != cells)
        {
            _memThreat = new float[cells]; _memAir = new float[cells];
            _nowMob = new float[cells]; _nowAir = new float[cells];
            CellSeenTick = new int[cells];
            Array.Fill(CellSeenTick, int.MinValue / 4);
        }
        Array.Clear(_nowMob);
        Array.Clear(_nowAir);
        v.Tick = _w.Tick;
        bool intel = IntelOn;
        if (intel) Structures.Clear();
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            if (!u.IsActive(i)) continue;
            int a = u.Army[i];
            var bp = db[u.Bp[i]];
            bool complete = u.IsComplete(i);
            if (a == _army)
            {
                v.Count[bp.Index]++;
                if (complete) v.Complete[bp.Index]++;
                if (bp.IsStructure)
                {
                    v.Structures.Add(i);
                    if (!complete) { v.Unfinished.Add(i); continue; }
                    if (u.Upgrade[i].Active) v.Upgrading++;
                    if (bp.Silo != null) v.Silos.Add(i);
                    if (bp.Has(UnitCategory.Factory))
                    {
                        if (bp.Has(UnitCategory.Air)) { v.AirFactories.Add(i); v.AirTier = Math.Max(v.AirTier, bp.Tech); }
                        else if (bp.Has(UnitCategory.Naval)) { v.NavalFactories.Add(i); v.NavalTier = Math.Max(v.NavalTier, bp.Tech); }
                        else if (bp.Has(UnitCategory.Land)) { v.LandFactories.Add(i); v.LandTier = Math.Max(v.LandTier, bp.Tech); }
                        else v.Gateways.Add(i);
                        var q = u.Factory[i].Queue;
                        if (q != null)
                            for (int e = u.IsActive(u.Factory[i].Current) ? 1 : 0; e < q.Count; e++)
                                if ((uint)q[e] < (uint)v.Queued.Length) v.Queued[q[e]]++;
                    }
                    continue;
                }
                if (!complete) continue;
                if (bp.Has(UnitCategory.Commander)) { v.Commander = i; continue; }
                if (bp.Has(UnitCategory.Engineer))
                {
                    if (bp.Movement.Layer is not (MotionLayer.Water or MotionLayer.Sub)) { v.Engineers.Add(i); v.EngineerTier = Math.Max(v.EngineerTier, bp.Tech); }
                    continue;
                }
                if (IsAirUnit(bp)) { v.AirUnits.Add(i); v.OwnAirStrength += Strength(_w, i, bp); }
                else if (bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub)
                {
                    v.NavalUnits.Add(i);
                    v.OwnNavalStrength += Strength(_w, i, bp);
                }
                else if (bp.Movement.Layer is MotionLayer.Land or MotionLayer.Amphibious or MotionLayer.Hover)
                {
                    v.LandCombat.Add(i);
                    v.OwnLandStrength += Strength(_w, i, bp);
                }
                continue;
            }
            if (!_w.IsEnemy(_army, a)) continue;
            if (bp.IsStructure)
            {
                if (!IsKnown(i)) continue;
                v.EnemyStructures.Add(i);
                if (!intel) Remember(i, bp, a);
                else if (!complete && IsIdentified(i))
                    Structures.Add(new KnownStructure { Handle = u.HandleOf(i), Pos = u.Pos[i], Bp = bp.Index, Army = a }); // site in sight
                continue;
            }
            if (!complete || !IsKnown(i)) continue;
            v.EnemyMobile.Add(i);
            var p = u.Pos[i];
            int c = v.CellOf(p);
            if (!IsIdentified(i))
            {
                // Radar blip: position only. Assume a T1 tank's worth of land firepower.
                v.EnemyLandStrength += 50;
                _nowMob[c] += 50;
                continue;
            }
            if (bp.Has(UnitCategory.Commander))
            {
                EnemyCommanderPos[a] = p;
                EnemyCommanderTick[a] = _w.Tick;
                EnemyCommanderHandle[a] = u.HandleOf(i);
            }
            float st = Strength(_w, i, bp);
            if (IsAirUnit(bp))
            {
                v.EnemyAirCount++;
                v.EnemyAirStrength += MathF.Max(30, bp.Economy.BuildCostMass);
            }
            else if (bp.Movement.Layer is MotionLayer.Water or MotionLayer.Sub) v.EnemyNavalStrength += st;
            else v.EnemyLandStrength += st;
            if (st <= 0) continue;
            var targets = TargetsOf(bp);
            if ((targets & (LayerMask.Land | LayerMask.Water | LayerMask.Sub)) != 0) _nowMob[c] += st;
            if ((targets & LayerMask.Air) != 0) _nowAir[c] += st;
        }

        if (intel)
        {
            // Ghosts: structures the team has seen and not seen destroyed (complete ones; sites in sight added above).
            _ghosts.Clear();
            _w.Intel.GetKnownStructures(_army, _ghosts);
            foreach (var g in _ghosts)
            {
                if (!_w.IsEnemy(_army, g.Army) || _w.Armies[g.Army].Defeated) continue;
                Structures.Add(new KnownStructure { Handle = new UnitHandle(g.Slot, g.Gen), Pos = g.Pos, Bp = g.Bp, Army = g.Army });
            }
        }
        else
        {
            Forget();
            Structures.Clear();
            Structures.AddRange(_omniMemory);
        }

        // Known enemy structures: defenses add threat (they don't move: no decay), air factories are noted.
        float tmlBest = float.MaxValue;
        var home = _w.Units.IsActive(_w.Armies[_army].Commander) ? _w.Units.Pos[_w.Armies[_army].Commander.Index] : _w.Terrain.StartPositions[_army % _w.Terrain.StartPositions.Length];
        foreach (var ks in Structures)
        {
            var bp = db[ks.Bp];
            if (bp.Has(UnitCategory.Factory) && bp.Has(UnitCategory.Air)) v.EnemyHasAirFactory = true;
            if (bp.Has(UnitCategory.Factory) && bp.Has(UnitCategory.Naval)) v.EnemyNavalFactories++;
            if (bp.Silo is { } silo)
            {
                if (silo.Kind == SiloKind.Strategic) v.EnemyNukes++;
                else if (silo.Kind == SiloKind.Tactical)
                {
                    v.EnemyTml++;
                    float d = Vector2.DistanceSquared(ks.Pos, home);
                    if (d < tmlBest) { tmlBest = d; v.EnemyTmlPos = ks.Pos; }
                }
            }
            if (bp.Weapons.Length == 0) continue;
            if (u.IsActive(ks.Handle) && !u.IsComplete(ks.Handle.Index)) continue;
            float st = bp.Economy.BuildCostMass * 1.5f;
            v.AddThreat(ks.Pos, st, TargetsOf(bp));
        }
        Array.Copy(v.Threat, v.ThreatNow, cells);

        // Threat memory: cells in vision show the truth, cells in the fog keep a decaying memory of what was there.
        int dt = _lastScanTick < 0 ? 0 : _w.Tick - _lastScanTick;
        _lastScanTick = _w.Tick;
        float decay = dt <= 0 ? 1f : MathF.Pow(0.5f, dt / (float)Math.Max(1, MemoryHalfLife));
        float landEst = 0;
        for (int c = 0; c < cells; c++)
        {
            bool seen = !intel || _w.Intel.IsPositionVisible(_army, v.CellCenter(c));
            if (seen) CellSeenTick[c] = _w.Tick;
            float m = _memThreat[c] * decay, ma = _memAir[c] * decay;
            if (seen) { m = 0; ma = 0; }
            m = MathF.Max(m, _nowMob[c]);
            ma = MathF.Max(ma, _nowAir[c]);
            if (m < 1f) m = 0;
            if (ma < 1f) ma = 0;
            _memThreat[c] = m;
            _memAir[c] = ma;
            v.Threat[c] += m;
            v.AirThreat[c] += ma;
            v.ThreatNow[c] += _nowMob[c];
            landEst += m;
        }
        EnemyLandEstimate = landEst;
        EnemyAirCountMem = MathF.Max(v.EnemyAirCount, EnemyAirCountMem * decay);
        EnemyNavalMem = MathF.Max(v.EnemyNavalStrength, EnemyNavalMem * decay);
        EnemyAirStrengthMem = MathF.Max(v.EnemyAirStrength, EnemyAirStrengthMem * decay);
        for (int a = 0; a < EnemyCommanderPos.Length; a++)
            if (_w.Armies[a].Defeated) { EnemyCommanderPos[a] = null; EnemyCommanderHandle[a] = UnitHandle.None; }
    }

    /// <summary>Is the structure still believed to exist (ghost / in sight)?</summary>
    public bool IsStructureKnown(UnitHandle h)
    {
        foreach (var ks in Structures)
            if (ks.Handle == h) return true;
        return false;
    }

    /// <summary>Ticks since the enemy commander of an army was last seen (large if never).</summary>
    public int CommanderAge(int army) => EnemyCommanderPos[army] == null ? int.MaxValue / 4 : _w.Tick - EnemyCommanderTick[army];

    private void Remember(int i, UnitBlueprint bp, int army)
    {
        var u = _w.Units;
        if (_rememberedGen[i] == u.Gen[i] + 1) return; // already known
        _rememberedGen[i] = u.Gen[i] + 1;
        _omniMemory.Add(new KnownStructure { Handle = u.HandleOf(i), Pos = u.Pos[i], Bp = bp.Index, Army = army });
    }

    /// <summary>Omniscient mode: drops remembered structures that are gone.</summary>
    private void Forget()
    {
        var u = _w.Units;
        for (int s = _omniMemory.Count - 1; s >= 0; s--)
        {
            var ks = _omniMemory[s];
            if (u.IsActive(ks.Handle) && !_w.Armies[ks.Army].Defeated) continue;
            if ((uint)ks.Handle.Index < (uint)_rememberedGen.Length && _rememberedGen[ks.Handle.Index] == ks.Handle.Generation + 1)
                _rememberedGen[ks.Handle.Index] = 0;
            _omniMemory.RemoveAt(s);
        }
    }

    /// <summary>Wrecks the AI may know about (props in explored areas, or all of them when omniscient).</summary>
    public bool CanSeeProp(int prop) => IsPositionExplored(_w.Props.Pos[prop]);

    /// <summary>
    /// Is the deposit's extractor site free as far as this army knows? Own/allied structures and known enemy
    /// structures block it; an enemy structure nobody on our team has seen does not (the engineer will find out).
    /// </summary>
    public bool DepositLooksFree(Vector2 site, UnitBlueprint sbp)
    {
        var w = _w;
        if (w.Occupancy.CanPlace(sbp, site, w.Terrain)) return true;
        if (!IntelOn) return false;
        var (x0, z0, x1, z1) = OccupancyGrid.Footprint(sbp, site);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (!w.Terrain.CellInBounds(x, z) || (w.Terrain.PassAt(x, z) & PassFlags.Land) == 0) return false;
            int occ = w.Occupancy.At(x, z);
            if (occ < 0) continue;
            int a = w.Units.Army[occ];
            if (!w.IsEnemy(_army, a)) return false; // ours or allied
            if (w.Intel.IsVisible(_army, occ)) return false;
            if (IsStructureKnown(w.Units.HandleOf(occ))) return false;
        }
        // Blocked only by enemy structures nobody on our team has seen.
        return true;
    }
}
