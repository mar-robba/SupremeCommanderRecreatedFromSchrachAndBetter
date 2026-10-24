using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;
using static Rts.Sim.Tests.EconomyTestUtil;

namespace Rts.Sim.Tests;

/// <summary>ACU Overcharge (Combat/OverchargeSystem): toggle, charge from army storage, damage rule, reload, refunds.</summary>
public class OverchargeTests
{
    private static Simulation NewGame(string faction, int seed = 42) =>
        GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = seed, MapSize = 256, Players = 2, AiPlayers = new bool[2], Factions = [faction, faction],
        });

    private static UnitHandle Acu(Simulation sim, int army = 0) => sim.World.Armies[army].Commander;

    /// <summary>A point <paramref name="dist"/> in front of army 0's commander (toward the map centre).</summary>
    private static Vector2 Front(SimWorld w, float dist)
    {
        var c = CommanderPos(w);
        var center = new Vector2(w.Terrain.Width, w.Terrain.Height) * 0.5f;
        return c + Vector2.Normalize(center - c) * dist;
    }

    /// <summary>Army 0 gets <paramref name="storages"/> energy storages (behind its commander) and this much energy.</summary>
    private static void SetEnergy(Simulation sim, float stored, int storages = 0)
    {
        var w = sim.World;
        var c = CommanderPos(w);
        var back = c - (Front(w, 1) - c) * 14f;
        for (int k = 0; k < storages; k++) TestUtil.Spawn(sim, "uef_t1_energy_storage", 0, FindSite(w, "uef_t1_energy_storage", back, 0, 20));
        sim.Step(); // storage capacity is counted by EconomySystem
        w.Armies[0].Eco.Energy = MathF.Min(stored, w.Armies[0].Eco.EnergyMax);
    }

    private static bool HasEffect(SimWorld w, EffectKind kind) => w.Events.Effects.Exists(e => e.Kind == kind);

    /// <summary>Steps until an Overcharge shot hits (OverchargeHit raised); returns the target's health drop on that tick
    /// (-1 = no hit within maxTicks).</summary>
    private static float StepToHit(Simulation sim, UnitHandle target, int maxTicks)
    {
        var w = sim.World;
        for (int t = 0; t < maxTicks; t++)
        {
            w.Events.Effects.Clear();
            float before = w.Units.IsActive(target) ? w.Units.Health[target.Index] : 0;
            sim.Step();
            if (HasEffect(w, EffectKind.OverchargeHit))
                return before - (w.Units.IsActive(target) ? w.Units.Health[target.Index] : 0);
        }
        return -1;
    }

    private static int StepToCharge(Simulation sim, UnitHandle acu, int maxTicks)
    {
        for (int t = 1; t <= maxTicks; t++)
        {
            sim.Step();
            if (OverchargeSystem.ChargeOf(sim.World, acu.Index) > 0) return t;
        }
        return -1;
    }

    [Theory]
    [InlineData("uef")]
    [InlineData("cybran")]
    public void ArmedCommanderSpendsStorageAndKillsTank(string faction)
    {
        var sim = NewGame(faction);
        var w = sim.World;
        Assert.Equal(w.Blueprints.Factions.CommanderOf(faction), w.Units.Bp[Acu(sim).Index]);
        SetEnergy(sim, 12000, storages: 2);
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 1, Front(w, 12));
        sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
        Assert.True((w.Units.Flags[Acu(sim).Index] & UnitFlags.Overcharge) != 0);

        float before = w.Armies[0].Eco.Energy;
        Assert.True(StepToCharge(sim, Acu(sim), 40) > 0, "no charge");
        Assert.Equal(5000f, OverchargeSystem.ChargeOf(w, Acu(sim).Index)); // min(stored, MaxEnergy)
        Assert.InRange(w.Armies[0].Eco.Energy, before - 5000f - 1f, before - 5000f + 50f);

        float drop = StepToHit(sim, tank, 60);
        Assert.True(drop > 0, "no overcharge hit");
        Assert.False(w.Units.IsActive(tank), "tank survived the overcharge");
        Assert.Equal(0f, OverchargeSystem.ChargeOf(w, Acu(sim).Index));
        Assert.True(OverchargeSystem.CooldownOf(w, Acu(sim).Index) > 4f);
    }

    [Fact]
    public void OverchargeDamageIsTheEnergySpent()
    {
        var sim = NewGame("uef");
        var w = sim.World;
        SetEnergy(sim, 3000); // below MaxEnergy: the whole storage goes into the shot
        var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, Front(w, 14));
        w.Units.Health[bot.Index] = 100000; // survives everything, measures the hit
        sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
        Assert.True(StepToCharge(sim, Acu(sim), 40) > 0);
        float e = OverchargeSystem.ChargeOf(w, Acu(sim).Index);
        Assert.InRange(e, 3000f, 3100f);
        Assert.Equal(0f, w.Armies[0].Eco.Energy);
        float drop = StepToHit(sim, bot, 60);
        Assert.InRange(drop, e * 0.5f, e); // area falloff: 100% at the centre, 50% at the edge
        Assert.True(drop > 1000f);
        Assert.True(w.Events.Effects.Exists(x => x.Kind == EffectKind.OverchargeHit && MathF.Abs(x.Size - 2.5f) < 1e-4f));
    }

    [Fact]
    public void BelowMinEnergyOrUnarmedFiresNormally()
    {
        foreach (bool armed in new[] { true, false })
        {
            var sim = NewGame("uef");
            var w = sim.World;
            SetEnergy(sim, armed ? 300 : 3500); // armed: below MinEnergy; unarmed: plenty
            var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, Front(w, 14));
            w.Units.Health[bot.Index] = 100000;
            float hp0 = w.Units.Health[bot.Index];
            float e0 = w.Armies[0].Eco.Energy;
            w.Events.Effects.Clear();
            TestUtil.Run(sim, 60, armed ? [new SetOverchargeCommand(0, [Acu(sim)], true)] : []);
            Assert.False(HasEffect(w, EffectKind.Overcharge));
            Assert.False(HasEffect(w, EffectKind.OverchargeHit));
            Assert.Equal(0f, OverchargeSystem.ChargeOf(w, Acu(sim).Index));
            Assert.True(w.Armies[0].Eco.Energy >= e0, "energy was spent");
            float dealt = hp0 - w.Units.Health[bot.Index];
            Assert.InRange(dealt, 100f, 700f); // a few normal 100-damage shots
            Assert.True(HasEffect(w, EffectKind.MuzzleFlash));
        }
    }

    [Fact]
    public void ReloadIsRespected()
    {
        var sim = NewGame("uef");
        var w = sim.World;
        SetEnergy(sim, 14000, storages: 2);
        var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, Front(w, 14));
        w.Units.Health[bot.Index] = 1e7f;
        var shots = new List<int>();
        sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
        for (int t = 0; t < 160; t++)
        {
            w.Events.Effects.Clear();
            sim.Step();
            if (HasEffect(w, EffectKind.Overcharge)) shots.Add(w.Tick);
        }
        Assert.True(shots.Count >= 3, $"only {shots.Count} overcharge shots");
        for (int k = 1; k < shots.Count; k++) Assert.True(shots[k] - shots[k - 1] >= 50, $"OC shots at {shots[k - 1]} and {shots[k]}");
        Assert.True(shots[1] - shots[0] <= 60, "reload far longer than 5 s");
    }

    [Fact]
    public void StructuresTakeHalfDamage()
    {
        var sim = NewGame("uef");
        var w = sim.World;
        SetEnergy(sim, 14000, storages: 2);
        var pgen = TestUtil.Spawn(sim, "uef_t3_power_generator", 1, FindSite(w, "uef_t3_power_generator", Front(w, 15), 0, 4));
        w.Units.Health[pgen.Index] = 100000;
        sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
        float drop = StepToHit(sim, pgen, 80);
        Assert.InRange(drop, 5000f * 0.5f * 0.5f, 5000f * 0.5f);
    }

    [Theory]
    [InlineData("uef")]
    [InlineData("cybran")]
    public void CommandersTakeAtMost400(string faction)
    {
        var sim = NewGame(faction);
        var w = sim.World;
        SetEnergy(sim, 14000, storages: 2);
        var enemy = TestUtil.Spawn(sim, w.Blueprints[w.Blueprints.Factions.CommanderOf(faction)].Id, 1, Front(w, 15));
        sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
        float drop = StepToHit(sim, enemy, 80);
        Assert.InRange(drop, 380f, 400.01f); // capped (minus one tick of regeneration)
        Assert.True(w.Units.IsActive(enemy));
    }

    [Fact]
    public void ToggleOnlyArmsOwnOverchargeUnits()
    {
        var sim = NewGame("uef");
        var w = sim.World;
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 0, Front(w, 6));
        sim.Step([new SetOverchargeCommand(0, [tank, Acu(sim, 1)], true)]);
        Assert.True((w.Units.Flags[tank.Index] & UnitFlags.Overcharge) == 0);
        Assert.True((w.Units.Flags[Acu(sim, 1).Index] & UnitFlags.Overcharge) == 0); // not ours
        sim.Step([new SetOverchargeCommand(1, [Acu(sim, 1)], true)]);
        Assert.True((w.Units.Flags[Acu(sim, 1).Index] & UnitFlags.Overcharge) != 0);
        sim.Step([new SetOverchargeCommand(1, [Acu(sim, 1)], false)]);
        Assert.True((w.Units.Flags[Acu(sim, 1).Index] & UnitFlags.Overcharge) == 0);
    }

    [Fact]
    public void EveryCommanderVariantKeepsOverchargeAndSupportUnitsHaveNone()
    {
        var db = TestUtil.Db;
        foreach (var f in new[] { "uef", "cybran" })
        {
            int c = db.Factions.CommanderOf(f);
            Assert.Equal(0, OverchargeSystem.WeaponIndex(db[c]));
            int variants = 0;
            for (int k = 0; k < db.Count; k++)
                if (db[k].EnhancementBase == c && k != c) { variants++; Assert.Equal(0, OverchargeSystem.WeaponIndex(db[k])); }
            Assert.True(variants > 0);
            int scu = db.Factions.RoleBlueprint(f, FactionRoles.Scu);
            if (scu >= 0) Assert.Equal(-1, OverchargeSystem.WeaponIndex(db[scu]));
        }
    }

    [Fact]
    public void ChargeIsRefundedWhenDisarmedOrHeldTooLong()
    {
        foreach (bool disarm in new[] { true, false })
        {
            var sim = NewGame("uef");
            var w = sim.World;
            SetEnergy(sim, 12000, storages: 2);
            var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, Front(w, 14));
            sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
            Assert.True(StepToCharge(sim, Acu(sim), 40) > 0);
            float e = w.Armies[0].Eco.Energy;
            w.Events.Effects.Clear();
            if (disarm) sim.Step([new SetOverchargeCommand(0, [Acu(sim)], false)]);
            else
            {
                w.KillUnit(bot.Index, UnitHandle.None); // nothing left to shoot: the charge is held, then refunded
                for (int t = 0; t < 29 && OverchargeSystem.ChargeOf(w, Acu(sim).Index) > 0; t++) sim.Step();
                Assert.True(OverchargeSystem.ChargeOf(w, Acu(sim).Index) > 0, "refunded before 3 s");
                for (int t = 0; t < 5; t++) sim.Step();
            }
            Assert.Equal(0f, OverchargeSystem.ChargeOf(w, Acu(sim).Index));
            Assert.False(HasEffect(w, EffectKind.Overcharge));
            Assert.InRange(w.Armies[0].Eco.Energy, e + 5000f - 1f, e + 5000f + 200f);
        }
    }

    [Fact]
    public void SaveLoadWithHeldChargeContinuesIdentically()
    {
        var sim = NewGame("cybran");
        var w = sim.World;
        SetEnergy(sim, 12000, storages: 2);
        var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, Front(w, 14));
        sim.Step([new SetOverchargeCommand(0, [Acu(sim)], true)]);
        Assert.True(StepToCharge(sim, Acu(sim), 40) > 0);

        var saved = WorldState.Capture(sim);
        var b = WorldState.Restore(saved, w.Blueprints, w.Terrain);
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(b)), "reload is not byte-identical");
        Assert.Equal(OverchargeSystem.ChargeOf(w, Acu(sim).Index), OverchargeSystem.ChargeOf(b.World, Acu(sim).Index));
        Assert.True((b.World.Units.Flags[Acu(sim).Index] & UnitFlags.Overcharge) != 0);
        for (int t = 0; t < 120; t += 10)
        {
            TestUtil.Run(sim, 10);
            TestUtil.Run(b, 10);
            Assert.Equal(sim.StateHash(), b.StateHash());
        }
        Assert.True(w.Units.Health[bot.Index] < 6500 - 1000 || !w.Units.IsActive(bot), "the held charge was never fired");
    }
}
