using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>An enemy structure the AI has seen (kept after it leaves vision, like a SupCom player's memory).</summary>
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
    /// <summary>All own structures (complete or under construction).</summary>
    public readonly List<int> Structures = new();
    /// <summary>Own structures under construction.</summary>
    public readonly List<int> Unfinished = new();
    /// <summary>Complete mobile non-builder land/amphibious units (tanks, artillery, AA, scouts).</summary>
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

    /// <summary>Known complete enemy mobile units.</summary>
    public readonly List<int> EnemyMobile = new();
    /// <summary>Known enemy structures currently in sight/radar.</summary>
    public readonly List<int> EnemyStructures = new();
    public float EnemyLandStrength;
    public float EnemyAirStrength;
    public int EnemyAirCount;
    public bool EnemyHasAirFactory;

    /// <summary>Coarse threat map: known enemy armed strength per ThreatCell x ThreatCell cell (land/structures, air).</summary>
    public const float ThreatCell = 16f;
    /// <summary>Threat below this (e.g. a lone scout) is ignored by builders.</summary>
    public const float ThreatThreshold = 25f;
    public int ThreatCols, ThreatRows;
    public float[] Threat = [];
    public float[] AirThreat = [];

    public void InitThreat(int width, int height)
    {
        ThreatCols = Math.Max(1, (int)MathF.Ceiling(width / ThreatCell));
        ThreatRows = Math.Max(1, (int)MathF.Ceiling(height / ThreatCell));
        if (Threat.Length != ThreatCols * ThreatRows)
        {
            Threat = new float[ThreatCols * ThreatRows];
            AirThreat = new float[ThreatCols * ThreatRows];
        }
        Array.Clear(Threat);
        Array.Clear(AirThreat);
    }

    public void AddThreat(Vector2 p, float v, bool air)
    {
        int cx = Math.Clamp((int)(p.X / ThreatCell), 0, ThreatCols - 1);
        int cz = Math.Clamp((int)(p.Y / ThreatCell), 0, ThreatRows - 1);
        (air ? AirThreat : Threat)[cz * ThreatCols + cx] += v;
    }

    /// <summary>Sum of enemy threat (land + air) in the cells overlapping the square of half-size r around p,
    /// ignoring trivial threats (below <see cref="ThreatThreshold"/>: returns 0).</summary>
    public float ThreatAt(Vector2 p, float r, bool includeAir = true)
    {
        int x0 = Math.Clamp((int)((p.X - r) / ThreatCell), 0, ThreatCols - 1);
        int x1 = Math.Clamp((int)((p.X + r) / ThreatCell), 0, ThreatCols - 1);
        int z0 = Math.Clamp((int)((p.Y - r) / ThreatCell), 0, ThreatRows - 1);
        int z1 = Math.Clamp((int)((p.Y + r) / ThreatCell), 0, ThreatRows - 1);
        float t = 0;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            t += Threat[z * ThreatCols + x];
            if (includeAir) t += AirThreat[z * ThreatCols + x];
        }
        return t < ThreatThreshold ? 0 : t;
    }

    public void Clear(int bpCount)
    {
        Commander = -1;
        Engineers.Clear(); LandFactories.Clear(); AirFactories.Clear(); Structures.Clear(); Unfinished.Clear();
        LandCombat.Clear(); AirUnits.Clear(); EnemyMobile.Clear(); EnemyStructures.Clear();
        if (Count.Length != bpCount) { Count = new int[bpCount]; Complete = new int[bpCount]; Queued = new int[bpCount]; }
        Array.Clear(Count); Array.Clear(Complete); Array.Clear(Queued);
        OwnLandStrength = OwnAirStrength = EnemyLandStrength = EnemyAirStrength = 0;
        EnemyAirCount = 0;
        EnemyHasAirFactory = false;
    }

    public int Total(int bp) => bp < 0 ? 0 : Count[bp] + Queued[bp];
}

/// <summary>
/// The AI's only window on enemy state. Today the AI may be omniscient (<see cref="RespectIntel"/> = false, the
/// default), but every read of enemy units, enemy positions and wrecks goes through here so that switching
/// RespectIntel on makes the AI play by the fog-of-war rules of <see cref="Systems.IntelService"/>.
/// Also keeps a memory of enemy structures seen (removed when found dead).
/// </summary>
public sealed class AiPerception
{
    private readonly SimWorld _w;
    private readonly int _army;
    /// <summary>false = omniscient AI; true = only what the army's intel (vision/radar) reports.</summary>
    public bool RespectIntel;

    public readonly List<KnownStructure> Structures = new();
    /// <summary>Per unit slot: generation+1 of the remembered structure (0 = not remembered).</summary>
    private readonly int[] _rememberedGen;
    /// <summary>Last known position of an enemy commander (per army), or null.</summary>
    public readonly Vector2?[] EnemyCommanderPos;

    public AiPerception(SimWorld w, int army)
    {
        _w = w;
        _army = army;
        _rememberedGen = new int[w.Units.Capacity];
        EnemyCommanderPos = new Vector2?[w.Armies.Length];
    }

    /// <summary>Does the AI know where this (enemy) unit is? (vision or radar)</summary>
    public bool IsKnown(int unit) => !RespectIntel || _w.Intel.IsVisible(_army, unit) || _w.Intel.IsDetected(_army, unit);

    /// <summary>Does the AI know what this unit is (blueprint)? Radar-only contacts are anonymous.</summary>
    public bool IsIdentified(int unit) => !RespectIntel || _w.Intel.IsVisible(_army, unit);

    public bool IsPositionVisible(Vector2 p) => !RespectIntel || _w.Intel.IsPositionVisible(_army, p);

    /// <summary>Strength value of a unit: mass cost of armed units, scaled by remaining health.</summary>
    public static float Strength(SimWorld w, int i, UnitBlueprint bp)
    {
        if (bp.Weapons.Length == 0) return 0;
        float m = bp.Has(UnitCategory.Commander) ? 1200 : bp.Economy.BuildCostMass;
        return m * MathF.Max(0.2f, w.Units.Health[i] / bp.MaxHealth);
    }

    /// <summary>One deterministic pass over the alive units: classifies own units and the known enemies.</summary>
    public void Scan(AiView v)
    {
        var u = _w.Units;
        var db = _w.Blueprints;
        v.Clear(db.Count);
        v.InitThreat(_w.Terrain.Width, _w.Terrain.Height);
        v.Tick = _w.Tick;
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
                    if (bp.Has(UnitCategory.Factory))
                    {
                        if (bp.Has(UnitCategory.Air)) v.AirFactories.Add(i); else v.LandFactories.Add(i);
                        var q = u.Factory[i].Queue;
                        if (q != null)
                            for (int e = u.IsActive(u.Factory[i].Current) ? 1 : 0; e < q.Count; e++)
                                if ((uint)q[e] < (uint)v.Queued.Length) v.Queued[q[e]]++;
                    }
                    continue;
                }
                if (!complete) continue;
                if (bp.Has(UnitCategory.Commander)) { v.Commander = i; continue; }
                if (bp.Has(UnitCategory.Engineer)) { v.Engineers.Add(i); continue; }
                if (bp.Movement.Layer == MotionLayer.Air) { v.AirUnits.Add(i); v.OwnAirStrength += Strength(_w, i, bp); }
                else { v.LandCombat.Add(i); v.OwnLandStrength += Strength(_w, i, bp); }
                continue;
            }
            if (!_w.IsEnemy(_army, a) || !IsKnown(i)) continue;
            if (bp.IsStructure)
            {
                v.EnemyStructures.Add(i);
                Remember(i, bp, a);
                if (complete && bp.Weapons.Length > 0 && IsIdentified(i))
                {
                    bool antiAir = (bp.Weapons[0].TargetMask & LayerMask.Air) != 0;
                    v.AddThreat(u.Pos[i], bp.Economy.BuildCostMass * 1.5f, antiAir);
                }
                if (IsIdentified(i) && bp.Has(UnitCategory.Factory) && bp.Has(UnitCategory.Air)) v.EnemyHasAirFactory = true;
                continue;
            }
            if (!complete) continue;
            v.EnemyMobile.Add(i);
            if (!IsIdentified(i)) { v.EnemyLandStrength += 50; v.AddThreat(u.Pos[i], 50, false); continue; }
            if (bp.Has(UnitCategory.Commander)) EnemyCommanderPos[a] = u.Pos[i];
            if (bp.Movement.Layer == MotionLayer.Air)
            {
                v.EnemyAirCount++;
                v.EnemyAirStrength += MathF.Max(30, bp.Economy.BuildCostMass);
                if (bp.Weapons.Length > 0) v.AddThreat(u.Pos[i], bp.Economy.BuildCostMass, true);
            }
            else
            {
                float st = Strength(_w, i, bp);
                v.EnemyLandStrength += st;
                v.AddThreat(u.Pos[i], st, false);
            }
        }
        Forget();
    }

    private void Remember(int i, UnitBlueprint bp, int army)
    {
        var u = _w.Units;
        if (_rememberedGen[i] == u.Gen[i] + 1) return; // already known
        _rememberedGen[i] = u.Gen[i] + 1;
        Structures.Add(new KnownStructure { Handle = u.HandleOf(i), Pos = u.Pos[i], Bp = bp.Index, Army = army });
    }

    /// <summary>Drops remembered structures that are gone (and whose spot we can see, if we respect intel).</summary>
    private void Forget()
    {
        var u = _w.Units;
        for (int s = Structures.Count - 1; s >= 0; s--)
        {
            var ks = Structures[s];
            if (u.IsActive(ks.Handle) && !_w.Armies[ks.Army].Defeated) continue;
            if (RespectIntel && !_w.Armies[ks.Army].Defeated && !IsPositionVisible(ks.Pos)) continue;
            if ((uint)ks.Handle.Index < (uint)_rememberedGen.Length && _rememberedGen[ks.Handle.Index] == ks.Handle.Generation + 1)
                _rememberedGen[ks.Handle.Index] = 0;
            Structures.RemoveAt(s);
        }
        for (int a = 0; a < EnemyCommanderPos.Length; a++)
            if (_w.Armies[a].Defeated) EnemyCommanderPos[a] = null;
    }

    /// <summary>Wrecks the AI may know about (props in vision, or all of them when omniscient).</summary>
    public bool CanSeeProp(int prop) => IsPositionVisible(_w.Props.Pos[prop]);
}
