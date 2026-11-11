using Rts.Client.Input;
using Rts.Sim.Data;

namespace Rts.Client.Tests;

/// <summary>(7b, G14) Selection filter rules (Ctrl+Z / Ctrl+A / Ctrl+Shift+L A N E / Ctrl+C) and camera bookmarks.</summary>
public class SelectionFilterTests
{
    private const int Me = 0, Enemy = 1;
    private const UnitCategory Mobile = UnitCategory.Mobile, Land = UnitCategory.Land, Air = UnitCategory.Air,
        Naval = UnitCategory.Naval, Structure = UnitCategory.Structure;

    private static UnitBlueprint Bp(int index, UnitCategory cats, bool armed = true) => new()
    {
        Index = index,
        CategoryFlags = cats,
        Weapons = armed ? [new WeaponBlueprint()] : [],
    };

    private static readonly UnitBlueprint Tank = Bp(0, Mobile | Land);
    private static readonly UnitBlueprint Fighter = Bp(1, Mobile | Air);
    private static readonly UnitBlueprint Frigate = Bp(2, Mobile | Naval);
    private static readonly UnitBlueprint Engineer = Bp(3, Mobile | Land | UnitCategory.Engineer, armed: false);
    private static readonly UnitBlueprint Commander = Bp(4, Mobile | Land | UnitCategory.Engineer | UnitCategory.Commander);
    private static readonly UnitBlueprint Transport = Bp(5, Mobile | Air | UnitCategory.Transport, armed: false);
    private static readonly UnitBlueprint Turret = Bp(6, Structure | Land | UnitCategory.Defense);
    private static readonly UnitBlueprint Factory = Bp(7, Structure | Land | UnitCategory.Factory | UnitCategory.Engineer, armed: false);
    private static readonly UnitBlueprint ArmedEngineer = Bp(8, Mobile | Naval | UnitCategory.Engineer);
    private static readonly UnitBlueprint[] AllBps = [Tank, Fighter, Frigate, Engineer, Commander, Transport, Turret, Factory, ArmedEngineer];

    private static UnitBlueprint[] Picked(SelectionFilter f, IReadOnlySet<long>? types = null) =>
        AllBps.Where(bp => SelectionFilters.Matches(f, bp, Me, Me, types)).ToArray();

    [Fact]
    public void DomainFiltersPickMobileUnitsOfThatDomainWithoutEngineersAndCommander()
    {
        Assert.Equal([Tank], Picked(SelectionFilter.Land));
        Assert.Equal([Fighter, Transport], Picked(SelectionFilter.Air)); // unarmed units of the domain are included
        Assert.Equal([Frigate], Picked(SelectionFilter.Naval));
    }

    [Fact]
    public void CombatIsEveryArmedMobileUnitWithoutEngineersAndCommander()
    {
        Assert.Equal([Tank, Fighter, Frigate], Picked(SelectionFilter.Combat));
        Assert.False(SelectionFilters.Matches(SelectionFilter.Combat, Commander, Me, Me));
        Assert.False(SelectionFilters.Matches(SelectionFilter.Combat, ArmedEngineer, Me, Me));
        Assert.False(SelectionFilters.Matches(SelectionFilter.Combat, Transport, Me, Me)); // no weapons
        Assert.False(SelectionFilters.Matches(SelectionFilter.Combat, Turret, Me, Me)); // structure
    }

    [Fact]
    public void EngineersAreMobileEngineersWithoutTheCommander()
    {
        Assert.Equal([Engineer, ArmedEngineer], Picked(SelectionFilter.Engineers));
        Assert.False(SelectionFilters.Matches(SelectionFilter.Engineers, Factory, Me, Me)); // a structure that builds
    }

    [Fact]
    public void ClassFiltersOnlyPickTheLocalArmy()
    {
        foreach (var f in new[] { SelectionFilter.Land, SelectionFilter.Air, SelectionFilter.Naval, SelectionFilter.Engineers, SelectionFilter.Combat })
        foreach (var bp in AllBps)
        {
            Assert.False(SelectionFilters.Matches(f, bp, Enemy, Me), $"{f} picked an enemy unit");
            if (bp.IsStructure) Assert.False(SelectionFilters.Matches(f, bp, Me, Me), $"{f} picked a structure");
        }
    }

    [Fact]
    public void SameTypesMatchesTheSelectedArmyAndBlueprintPairsStructuresIncluded()
    {
        var types = new HashSet<long> { SelectionFilters.TypeKey(Me, Tank.Index), SelectionFilters.TypeKey(Me, Turret.Index) };
        Assert.Equal([Tank, Turret], Picked(SelectionFilter.SameTypes, types));
        // Same blueprint of another army: not selected (sandbox / replay selections keep their army).
        Assert.False(SelectionFilters.Matches(SelectionFilter.SameTypes, Tank, Enemy, Me, types));
        var enemyTypes = new HashSet<long> { SelectionFilters.TypeKey(Enemy, Tank.Index) };
        Assert.True(SelectionFilters.Matches(SelectionFilter.SameTypes, Tank, Enemy, Me, enemyTypes));
        Assert.False(SelectionFilters.Matches(SelectionFilter.SameTypes, Tank, Me, Me, enemyTypes));
        // Nothing selected: nothing matches.
        Assert.Empty(Picked(SelectionFilter.SameTypes));
        Assert.Empty(Picked(SelectionFilter.SameTypes, new HashSet<long>()));
        Assert.NotEqual(SelectionFilters.TypeKey(0, 1), SelectionFilters.TypeKey(1, 0));
    }

    [Fact]
    public void CommanderKeyPicksOnlyTheLocalCommander()
    {
        Assert.True(SelectionFilters.IsCommander(Commander, Me, Me));
        Assert.False(SelectionFilters.IsCommander(Commander, Enemy, Me));
        Assert.False(SelectionFilters.IsCommander(Engineer, Me, Me));
        Assert.False(SelectionFilters.IsCommander(Tank, Me, Me));
    }

    /// <summary>The real unit set: every mobile unit that is not an engineer / commander is reachable with one of the
    /// domain keys, every faction has a commander the commander key finds, and no commander is in a class filter.</summary>
    [Fact]
    public void RealBlueprintsAreCoveredByTheFilters()
    {
        var db = BlueprintDb.LoadDirectory(DataPaths.UnitBlueprintDir);
        var domains = new[] { SelectionFilter.Land, SelectionFilter.Air, SelectionFilter.Naval };
        var all = (SelectionFilter[])Enum.GetValues(typeof(SelectionFilter));
        int commanders = 0, combat = 0, engineers = 0;
        foreach (var bp in db.All)
        {
            if (bp.Has(UnitCategory.Commander))
            {
                commanders++;
                Assert.True(SelectionFilters.IsCommander(bp, Me, Me));
                foreach (var f in all) Assert.False(SelectionFilters.Matches(f, bp, Me, Me), $"{bp.Id}: commander in {f}");
                continue;
            }
            if (bp.IsStructure)
            {
                foreach (var f in all) Assert.False(SelectionFilters.Matches(f, bp, Me, Me), $"{bp.Id}: structure in {f}");
                continue;
            }
            if (SelectionFilters.Matches(SelectionFilter.Engineers, bp, Me, Me)) { engineers++; continue; }
            Assert.True(domains.Count(f => SelectionFilters.Matches(f, bp, Me, Me)) == 1, $"{bp.Id}: not in exactly one of land / air / naval");
            if (SelectionFilters.Matches(SelectionFilter.Combat, bp, Me, Me)) combat++;
        }
        Assert.True(commanders > 0 && engineers > 0 && combat > 10, $"commanders {commanders}, engineers {engineers}, combat {combat}");
    }

    [Fact]
    public void BookmarksSaveRecallAndEmptySlots()
    {
        var marks = new CameraBookmarks();
        for (int i = 0; i < CameraBookmarks.Slots; i++)
        {
            Assert.False(marks.Has(i));
            Assert.Null(marks.Recall(i));
        }
        var a = new CameraView(100, 200, 80, 1.5f);
        var b = new CameraView(10, 20, 300, -0.5f);
        Assert.True(marks.Save(0, a));
        Assert.True(marks.Save(4, b));
        Assert.Equal(a, marks.Recall(0)); // position, zoom and rotation come back
        Assert.Equal(b, marks.Recall(4));
        Assert.Null(marks.Recall(1));
        // Saving again overwrites only that slot.
        Assert.True(marks.Save(0, b));
        Assert.Equal(b, marks.Recall(0));
        Assert.Equal(b, marks.Recall(4));
        // Out of range: refused, nothing changes.
        Assert.False(marks.Save(-1, a));
        Assert.False(marks.Save(CameraBookmarks.Slots, a));
        Assert.Null(marks.Recall(-1));
        Assert.Null(marks.Recall(CameraBookmarks.Slots));
        marks.Clear();
        Assert.False(marks.Has(0));
        Assert.False(marks.Has(4));
    }

    [Fact]
    public void KeysAreBoundAsDocumented()
    {
        Assert.Equal("Ctrl+Z", Hotkeys.SelectSameTypes.Label);
        Assert.Equal("Ctrl+Shift+Z", Hotkeys.SelectSameTypesMap.Label);
        Assert.Equal("Ctrl+A", Hotkeys.SelectCombat.Label);
        Assert.Equal("Ctrl+C", Hotkeys.SelectCommander.Label);
        Assert.Equal(4, Hotkeys.SelectDomain.Keys.Length); // L A N E = land, air, naval, engineers (SelectionFilters.DomainKeys)
        Assert.Equal(KeyMods.Ctrl | KeyMods.Shift, Hotkeys.SelectDomain.Mods);
        Assert.Equal(CameraBookmarks.Slots, Hotkeys.BookmarkRecall.Keys.Length);
        Assert.Equal(CameraBookmarks.Slots, Hotkeys.BookmarkSave.Keys.Length);
        Assert.Equal(Raylib_cs.KeyboardKey.One, Hotkeys.BookmarkRecall.Keys[0]);
        Assert.Equal(KeyMods.Alt, Hotkeys.BookmarkRecall.Mods);
        Assert.Equal(KeyMods.Ctrl | KeyMods.Alt, Hotkeys.BookmarkSave.Mods);
    }
}
