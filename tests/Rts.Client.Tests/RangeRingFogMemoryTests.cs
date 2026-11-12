using System.Numerics;
using Rts.Client.Rendering;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Tests;

/// <summary>(10b-2) Selection range rings (merge, cap, union outline) and the fog memory of wrecks.</summary>
public class RangeRingFogMemoryTests
{
    private static UnitBlueprint Tank() => new()
    {
        Weapons = [new WeaponBlueprint { Range = 20, TargetMask = LayerMask.Land | LayerMask.Water }],
    };

    [Fact]
    public void EqualRingsMergeIntoOne()
    {
        var set = new RangeRingSet();
        for (int i = 0; i < 50; i++) set.AddUnit(Tank(), new Vector2(100.2f + (i % 2) * 0.1f, 100.2f));
        set.Finish();
        Assert.Equal(50, set.Added);
        Assert.Equal(1, set.Count);
        Assert.Equal(50, set.Rings[0].Count);
        Assert.Equal(20f, set.Rings[0].Radius);
    }

    [Fact]
    public void KindsAndAntiAirStayApart()
    {
        var bp = new UnitBlueprint
        {
            Weapons =
            [
                new WeaponBlueprint { Range = 30, MinRange = 8, TargetMask = LayerMask.Land },
                new WeaponBlueprint { Range = 25, TargetMask = LayerMask.Air },
            ],
            Intel = new IntelBlueprint { RadarRadius = 60, SonarRadius = 40, OmniRadius = 0 },
        };
        var set = new RangeRingSet();
        set.AddUnit(bp, new Vector2(50, 50));
        set.Finish();
        var kinds = new List<RingKind>();
        foreach (var r in set.Rings) kinds.Add(r.Kind);
        Assert.Equal([RingKind.Weapon, RingKind.MinRange, RingKind.AntiAir, RingKind.Radar, RingKind.Sonar], kinds);
    }

    [Fact]
    public void CapKeepsSelectionAndRareKindsFirst()
    {
        var set = new RangeRingSet { Cap = 10 };
        for (int i = 0; i < 40; i++) set.Add(RingKind.Weapon, 20, new Vector2(i * 50, 0));      // 40 separate blobs
        set.Add(RingKind.Radar, 60, new Vector2(500, 500));
        set.Add(RingKind.Shield, 15, new Vector2(900, 900), source: 1);                     // Shift structure
        set.Finish();
        Assert.Equal(10, set.Count);
        Assert.Equal(32, set.Dropped);
        Assert.Equal(RingKind.Radar, set.Rings[0].Kind);
        foreach (var r in set.Rings) Assert.Equal(0, r.Source); // structures (Shift) come after the selection
    }

    [Fact]
    public void OverlappingRingsDrawOnlyTheUnionOutline()
    {
        var set = new RangeRingSet();
        set.Add(RingKind.Weapon, 20, new Vector2(100, 100));
        set.Finish();
        set.BuildOutline();
        Assert.Equal(RangeRingSet.Segments, set.SegmentCount);

        set.Clear();
        set.Add(RingKind.Weapon, 20, new Vector2(100, 100));
        set.Add(RingKind.Weapon, 20, new Vector2(120, 100)); // far enough not to merge, overlapping
        set.Add(RingKind.Radar, 20, new Vector2(110, 100));   // other kind: drawn whole
        set.Finish();
        set.BuildOutline();
        Assert.Equal(3, set.Count);
        Assert.InRange(set.SegmentCount, RangeRingSet.Segments + 2, RangeRingSet.Segments * 3 - 20);
    }

    [Fact]
    public void RebuildDoesNotAllocateOnceWarm()
    {
        var set = new RangeRingSet();
        var bp = Tank();
        void Run()
        {
            set.Clear();
            for (int i = 0; i < 500; i++) set.AddUnit(bp, new Vector2(100 + i % 25 * 2.5f, 100 + i / 25 * 2.5f));
            set.Finish();
            set.BuildOutline();
        }
        for (int i = 0; i < 60; i++) Run(); // past JIT tiering (OSR allocates a little on the first tier-up)
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;
        // A per-unit or per-segment allocation would be >= 12 KB here; a constant 64 B shows up in the test host only.
        Assert.True(delta < 1024, $"allocated {delta} bytes");
        Assert.True(set.Count <= RangeRingSet.DefaultCap && set.Count < 120, $"{set.Count} rings for 500 tanks");
    }

    // ---------------------------------------------------------------- fog memory

    private static SimSnapshot Snap(int tick, byte fogLeft, byte fogRight, params PropView[] wrecks)
    {
        // 2 x 1 fog cells of 50 units: x < 50 = left cell, x >= 50 = right cell.
        var s = new SimSnapshot { Tick = tick, ViewArmy = 0, FogCols = 2, FogRows = 1, FogCellSize = 50, Fog = [fogLeft, fogRight] };
        s.Props = wrecks.Length > 0 ? wrecks : new PropView[1];
        s.PropCount = wrecks.Length;
        return s;
    }

    private static PropView Wreck(int id, float x, float mass) =>
        new() { Id = id, Kind = PropKind.Wreck, Pos = new Vector3(x, 0, 10), Mass = mass, MassMax = 100, Bp = -1, Radius = 1 };

    [Fact]
    public void WrecksAreRememberedUnderFogAndUpdatedWhenSeen()
    {
        var mem = new WreckFogMemory();
        // Never seen (right cell explored but not in vision) → not drawn; seen (left cell visible) → drawn.
        mem.Update(Snap(1, 255, 128, Wreck(1, 10, 100), Wreck(2, 80, 100)));
        Assert.Equal(1, mem.Wrecks.Length);
        Assert.Equal(1, mem.Wrecks[0].Id);

        // Left goes under the fog and wreck 1 is partly reclaimed there: still drawn as last seen (mass 100).
        mem.Update(Snap(2, 128, 128, Wreck(1, 10, 40), Wreck(2, 80, 100)));
        Assert.Equal(1, mem.Wrecks.Length);
        Assert.Equal(100f, mem.Wrecks[0].Mass);

        // Wreck 1 is gone, still unseen: the remembered copy stays.
        mem.Update(Snap(3, 128, 128, Wreck(2, 80, 100)));
        Assert.Equal(1, mem.Wrecks.Length);
        Assert.Equal(1, mem.Remembered);

        // Seen again: gone → forgotten; the right cell comes into vision → wreck 2 is seen with its live mass.
        mem.Update(Snap(4, 255, 255, Wreck(2, 80, 70)));
        Assert.Equal(1, mem.Wrecks.Length);
        Assert.Equal(2, mem.Wrecks[0].Id);
        Assert.Equal(70f, mem.Wrecks[0].Mass);
        Assert.Equal(1, mem.Remembered);
    }

    [Fact]
    public void WithoutFogEveryWreckPasses()
    {
        var mem = new WreckFogMemory();
        var s = Snap(1, 0, 0, Wreck(1, 10, 100), Wreck(2, 80, 100));
        s.ViewArmy = -1;
        mem.Update(s);
        Assert.Equal(2, mem.Wrecks.Length);
        Assert.Equal(255, WreckFogMemory.FogAt(s, new Vector2(10, 10)));
        Assert.Equal(0, WreckFogMemory.FogAt(Snap(1, 0, 255), new Vector2(10, 10)));
    }
}
