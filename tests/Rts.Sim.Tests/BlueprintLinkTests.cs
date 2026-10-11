using Rts.Sim.Data;

namespace Rts.Sim.Tests;

public class BlueprintLinkTests
{
    private static UnitBlueprint Bp(string id, params string[] cats) => new()
    {
        Id = id,
        Categories = cats,
        Movement = cats.Contains("Structure") ? new() : new() { Layer = MotionLayer.Land },
    };

    [Fact]
    public void BuiltByAppendsToBuilderAndIgnoresUnknownBuilders()
    {
        var db = new BlueprintDb();
        var eng = Bp("eng", "Engineer");
        eng.Builds = ["pgen"];
        db.Add(eng);
        db.Add(Bp("pgen", "Structure"));
        var shield = Bp("shield", "Structure");
        shield.BuiltBy = ["eng", "not_added_yet"];
        db.Add(shield);
        var pgen2 = Bp("pgen2", "Structure");
        pgen2.BuiltBy = ["eng"];
        db.Add(pgen2);
        db.FinalizeDb();

        Assert.Equal([db.IndexOf("pgen"), db.IndexOf("shield"), db.IndexOf("pgen2")], db["eng"].BuildIndices);
    }

    [Fact]
    public void UpgradesToResolvesBothDirections()
    {
        var db = new BlueprintDb();
        var t1 = Bp("fac1", "Structure");
        t1.UpgradesTo = "fac2";
        db.Add(t1);
        var t2 = Bp("fac2", "Structure");
        t2.UpgradesTo = "fac3";
        db.Add(t2);
        db.Add(Bp("fac3", "Structure"));
        db.FinalizeDb();

        Assert.Equal(db.IndexOf("fac2"), db["fac1"].UpgradesToIndex);
        Assert.Equal(db.IndexOf("fac3"), db["fac2"].UpgradesToIndex);
        Assert.Equal(-1, db["fac3"].UpgradesToIndex);
        Assert.Equal(-1, db["fac1"].UpgradesFromIndex);
        Assert.Equal(db.IndexOf("fac1"), db["fac2"].UpgradesFromIndex);
    }

    [Fact]
    public void UpgradesToUnknownIdThrows()
    {
        var db = new BlueprintDb();
        var t1 = Bp("fac1", "Structure");
        t1.UpgradesTo = "missing";
        db.Add(t1);
        Assert.Throws<InvalidDataException>(db.FinalizeDb);
    }
}
