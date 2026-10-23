using System.Runtime.CompilerServices;
using Rts.Sim.Data;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Per-world state of <see cref="StealthSystem"/>: which stealth/cloak effects are active on each unit slot this tick.
/// Plain arrays (saveable); the per-blueprint tables are a cache of the immutable BlueprintDb.
/// </summary>
public sealed class StealthState
{
    private static readonly ConditionalWeakTable<SimWorld, StealthState> Table = new();

    public static StealthState Of(SimWorld w) => Table.GetValue(w, static x => new StealthState(x));

    /// <summary>
    /// Per unit slot: active bits this tick (<see cref="StealthSystem.RadarBit"/>, <see cref="StealthSystem.SonarBit"/>,
    /// <see cref="StealthSystem.CloakBit"/>, <see cref="StealthSystem.FieldBit"/>; same values as
    /// <see cref="Snapshot.UnitStatus"/>). Shared with <see cref="IntelService.StealthMask"/> while any bit is set.
    /// Saved: the AI and command validation read it before the next IntelSystem run.
    /// </summary>
    public readonly byte[] Mask;
    /// <summary>Slots with a non-zero <see cref="Mask"/> after the last update.</summary>
    public int ActiveCount;

    /// <summary>Per blueprint: the bits a powered unit gives itself (field generators: FieldBit | RadarBit | SonarBit);
    /// null = not built yet (fresh world or just loaded).</summary>
    [NoSave] internal byte[]? SelfBits;
    /// <summary>Per blueprint: stealth field radius (0 = none).</summary>
    [NoSave] internal float[]? FieldRadius;
    /// <summary>Some blueprint of the database has stealth, cloak or a stealth field.</summary>
    [NoSave] internal bool AnyBlueprint;
    /// <summary>Scratch: powered field generators of this update (AliveList order).</summary>
    [NoSave] internal List<int>? Fields;

    private StealthState(SimWorld w) => Mask = new byte[w.Units.Capacity];

    /// <summary>Builds the per-blueprint tables once; returns <see cref="AnyBlueprint"/>.</summary>
    internal bool EnsureBlueprints(BlueprintDb db)
    {
        if (SelfBits != null && SelfBits.Length == db.Count) return AnyBlueprint;
        int n = db.Count;
        var self = new byte[n];
        var field = new float[n];
        bool any = false;
        for (int b = 0; b < n; b++)
        {
            var it = db[b].Intel;
            byte bits = 0;
            if (it.RadarStealth) bits |= StealthSystem.RadarBit;
            if (it.SonarStealth) bits |= StealthSystem.SonarBit;
            if (it.Cloak) bits |= StealthSystem.CloakBit | StealthSystem.RadarBit | StealthSystem.SonarBit; // cloak includes stealth
            if (it.StealthFieldRadius > 0)
            {
                bits |= StealthSystem.FieldBit | StealthSystem.RadarBit | StealthSystem.SonarBit;
                field[b] = it.StealthFieldRadius;
            }
            self[b] = bits;
            any |= bits != 0;
        }
        FieldRadius = field;
        AnyBlueprint = any;
        SelfBits = self;
        return any;
    }
}
