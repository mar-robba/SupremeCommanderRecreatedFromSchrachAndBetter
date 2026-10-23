using System.Numerics;
using Rts.Sim.Combat;
using Rts.Sim.Commander;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Systems;
using Rts.Sim.Orders;
using Rts.Sim.Persistence;
using Rts.Sim.World;

namespace Rts.Sim.Tests;

/// <summary>
/// Commander death blast (Commander/CommanderDeathBlast): DeathBlast.Delay s after a complete commander dies, a small
/// nuke goes off where it died (inner ring InnerDamage + OuterDamage, outer ring OuterDamage, every army, inner-ring
/// wrecks vaporized one tick later). The tests kill an EXTRA commander of army 0 (killing army.Commander would defeat
/// the army and CleanupSystem would remove all its units).
/// </summary>
public class CommanderDeathBlastTests
{
    private static Simulation NewGame(string faction, int seed = 42) =>
        GameSetup.CreateSkirmish(TestUtil.Db, new SkirmishSettings
        {
            Seed = seed, MapSize = 256, Players = 2, AiPlayers = new bool[2], Factions = [faction, faction],
        });

    private static string CommanderId(SimWorld w, string faction) =>
        w.Blueprints[w.Blueprints.Factions.CommanderOf(faction)].Id;

    /// <summary>Blast centre: the map centre, far from both start commanders.</summary>
    private static Vector2 Center(SimWorld w)
    {
        var c = new Vector2(w.Terrain.Width, w.Terrain.Height) * 0.5f;
        foreach (var a in w.Armies)
            Assert.True(Vector2.Distance(w.Units.Pos[a.Commander.Index], c) > 80, "start commander too close to the test area");
        return c;
    }

    private static Vector2 At(Vector2 c, float dist, float angleDeg)
    {
        float a = angleDeg * MathF.PI / 180f;
        return c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * dist;
    }

    private static float Hp(SimWorld w, UnitHandle h) => w.Units.IsActive(h) ? w.Units.Health[h.Index] : 0;

    private static bool NukeFx(SimWorld w, out EffectEvent fx)
    {
        int k = w.Events.Effects.FindIndex(e => e.Kind == EffectKind.Nuke);
        fx = k >= 0 ? w.Events.Effects[k] : default;
        return k >= 0;
    }

    /// <summary>Every unit of both armies holds fire (only the blast may change health).</summary>
    private static void HoldFireAll(Simulation sim)
    {
        var w = sim.World;
        var cmds = new List<Command>();
        for (int a = 0; a < 2; a++)
        {
            var hs = new List<UnitHandle>();
            for (int i = 0; i < w.Units.Capacity; i++) // (AliveList is only rebuilt by the next tick)
                if (w.Units.IsActive(i) && w.Units.Army[i] == a) hs.Add(w.Units.HandleOf(i));
            cmds.Add(new SetFireStateCommand(a, hs.ToArray(), FireState.HoldFire));
        }
        sim.Step(cmds.ToArray());
    }

    /// <summary>Steps until the blast's Nuke effect is raised; returns the number of steps (-1 = none within max).</summary>
    private static int StepToBlast(Simulation sim, int max, out EffectEvent fx)
    {
        fx = default;
        for (int t = 1; t <= max; t++)
        {
            sim.World.Events.Effects.Clear();
            sim.Step();
            if (NukeFx(sim.World, out fx)) return t;
        }
        return -1;
    }

    private sealed class Setup
    {
        public required Simulation Sim;
        public required Vector2 C;
        public required UnitHandle Commander;
        public required UnitHandle[] InnerTanks;
        public required UnitHandle InnerBot, OuterBot, OuterTank, FarTank;
        public required UnitHandle OwnInnerTank, OwnOuterBot;
    }

    /// <summary>
    /// An extra commander of army 0 at the map centre, army 1 (enemy) tanks inside the inner ring, a heavy bot in each
    /// ring, a tank in the outer ring and one beyond it; army 0 has a tank in the inner ring and a heavy bot in the outer.
    /// </summary>
    private static Setup Build(string faction, int seed = 42)
    {
        var sim = NewGame(faction, seed);
        var w = sim.World;
        var c = Center(w);
        var bp = w.Blueprints[w.Blueprints.Factions.CommanderOf(faction)];
        var x = bp.DeathBlast;
        Assert.NotNull(x);
        float bot = w.Blueprints[w.Blueprints.IndexOf("uef_t3_heavy_bot")].Radius;
        float tank = w.Blueprints[w.Blueprints.IndexOf("uef_t1_tank")].Radius;
        var s = new Setup
        {
            Sim = sim, C = c,
            Commander = TestUtil.Spawn(sim, CommanderId(w, faction), 0, c),
            InnerTanks = [TestUtil.Spawn(sim, "uef_t1_tank", 1, At(c, 5, 0)), TestUtil.Spawn(sim, "uef_t1_tank", 1, At(c, 8, 90))],
            InnerBot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, At(c, 6, 180)),
            OuterBot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 1, At(c, x!.InnerRadius + bot + 5, 45)),
            OuterTank = TestUtil.Spawn(sim, "uef_t1_tank", 1, At(c, x.OuterRadius - 4, 135)),
            FarTank = TestUtil.Spawn(sim, "uef_t1_tank", 1, At(c, x.OuterRadius + tank + 8, 300)),
            OwnInnerTank = TestUtil.Spawn(sim, "uef_t1_tank", 0, At(c, 6, 270)),
            OwnOuterBot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 0, At(c, x.InnerRadius + bot + 5, 225)),
        };
        HoldFireAll(sim);
        return s;
    }

    [Theory]
    [InlineData("uef")]
    [InlineData("cybran")]
    public void BlastGoesOffAfterDelayAndHitsBothRings(string faction)
    {
        var s = Build(faction);
        var sim = s.Sim;
        var w = sim.World;
        var x = w.BpOf(s.Commander.Index).DeathBlast!;
        int cmdBp = w.Units.Bp[s.Commander.Index];
        int delayTicks = (int)MathF.Round(x.Delay / SimWorld.Dt);
        var watched = new[] { s.InnerTanks[0], s.InnerTanks[1], s.InnerBot, s.OuterBot, s.OuterTank, s.FarTank, s.OwnInnerTank, s.OwnOuterBot };
        var full = watched.Select(h => Hp(w, h)).ToArray();

        w.KillUnit(s.Commander.Index, UnitHandle.None);
        Assert.Single(CommanderDeathBlastState.Of(w).Pending);

        // No damage before the delay.
        for (int t = 1; t < delayTicks; t++)
        {
            w.Events.Effects.Clear();
            sim.Step();
            Assert.False(NukeFx(w, out _), $"blast went off early (tick {t})");
        }
        for (int k = 0; k < watched.Length; k++)
            Assert.True(MathF.Abs(Hp(w, watched[k]) - full[k]) < 1f, $"unit {k} was hurt before the blast");

        // The blast tick.
        var before = watched.Select(h => Hp(w, h)).ToArray();
        w.Events.Effects.Clear();
        sim.Step();
        Assert.True(NukeFx(w, out var fx), "no blast on the due tick");
        Assert.Equal(cmdBp, fx.BlueprintIndex);
        Assert.Equal((double)x.OuterRadius, fx.Size, 3);
        Assert.Empty(CommanderDeathBlastState.Of(w).Pending);

        foreach (var t in s.InnerTanks) Assert.False(w.Units.IsActive(t), "inner-ring enemy tank survived");
        Assert.False(w.Units.IsActive(s.OuterTank), "outer-ring tank survived 1500 damage");
        Assert.True(w.Units.IsActive(s.FarTank));
        Assert.Equal((double)before[5], Hp(w, s.FarTank), 3); // beyond OuterRadius: untouched

        // Rings add up: inner = InnerDamage + OuterDamage, outer = OuterDamage (slack for regen).
        Assert.InRange(before[2] - Hp(w, s.InnerBot), x.InnerDamage + x.OuterDamage - 30, x.InnerDamage + x.OuterDamage + 1);
        Assert.InRange(before[3] - Hp(w, s.OuterBot), x.OuterDamage - 30, x.OuterDamage + 1);

        // Own units are hurt too.
        Assert.False(w.Units.IsActive(s.OwnInnerTank), "own inner-ring tank survived");
        Assert.InRange(before[7] - Hp(w, s.OwnOuterBot), x.OuterDamage - 30, x.OuterDamage + 1);

        // Exactly one blast: nothing more happens afterwards.
        Assert.Equal(-1, StepToBlast(sim, 40, out _));
        Assert.False(w.Armies[0].Defeated);
    }

    [Fact]
    public void EveryCommanderVariantBlastsAndSupportUnitsDoNot()
    {
        var db = TestUtil.Db;
        foreach (var f in new[] { "uef", "cybran" })
        {
            int c = db.Factions.CommanderOf(f);
            Assert.NotNull(db[c].DeathBlast);
            int variants = 0;
            for (int k = 0; k < db.Count; k++)
                if (db[k].EnhancementBase == c && k != c) { variants++; Assert.NotNull(db[k].DeathBlast); }
            Assert.True(variants > 0);
            int scu = db.Factions.RoleBlueprint(f, FactionRoles.Scu);
            if (scu >= 0) Assert.Null(db[scu].DeathBlast);
            int eng = db.Factions.RoleBlueprint(f, FactionRoles.EngineerT1);
            if (eng >= 0) Assert.Null(db[eng].DeathBlast);
        }
    }

    [Fact]
    public void InnerRingWrecksVaporizeOneTickLater()
    {
        var s = Build("uef");
        var sim = s.Sim;
        var w = sim.World;
        var x = w.BpOf(s.Commander.Index).DeathBlast!;
        int tankBp = w.Blueprints.IndexOf("uef_t1_tank");
        var innerProp = w.Props.HandleOf(w.Props.Spawn(At(s.C, 9, 60), 0, tankBp, 1f, 20f, 0f));
        var farProp = w.Props.HandleOf(w.Props.Spawn(At(s.C, x.OuterRadius + 10, 30), 0, tankBp, 1f, 20f, 0f));

        w.KillUnit(s.Commander.Index, UnitHandle.None);
        Assert.True(StepToBlast(sim, 30, out _) > 0, "no blast");
        Assert.True(w.Props.IsActive(farProp));

        sim.Step();
        Assert.False(w.Props.IsActive(innerProp), "inner-ring wreck survived the blast");
        Assert.True(w.Props.IsActive(farProp), "a prop beyond the outer ring was removed");
        foreach (int p in w.Props.AliveList)
            Assert.True(Vector2.Distance(w.Props.Pos[p], s.C) > x.InnerRadius, $"wreck left inside the inner ring at {w.Props.Pos[p]}");
    }

    [Fact]
    public void UnfinishedOrSilentlyRemovedCommanderGivesNoBlast()
    {
        var sim = NewGame("uef");
        var w = sim.World;
        var c = Center(w);
        int bp = w.Blueprints.Factions.CommanderOf("uef");
        var site = w.SpawnUnit(bp, 0, c, 0, complete: false);
        Assert.False(w.Units.IsComplete(site.Index));
        var removed = TestUtil.Spawn(sim, CommanderId(w, "uef"), 0, At(c, 40, 0));
        var tank = TestUtil.Spawn(sim, "uef_t1_tank", 1, At(c, 4, 90));
        HoldFireAll(sim);

        w.KillUnit(site.Index, UnitHandle.None);
        ConstructionSystem.RemoveUnitSilently(w, removed.Index);
        Assert.False(CommanderDeathBlastState.TryGet(w, out var st) && st.Pending.Count > 0, "a blast was scheduled");
        Assert.Equal(-1, StepToBlast(sim, 40, out _));
        Assert.True(w.Units.IsActive(tank));
    }

    [Fact]
    public void KilledArmyCommanderStillBlasts()
    {
        var sim = NewGame("cybran");
        var w = sim.World;
        var acu = w.Armies[1].Commander;
        var pos = w.Units.Pos[acu.Index];
        var x = w.BpOf(acu.Index).DeathBlast!;
        var bot = TestUtil.Spawn(sim, "uef_t3_heavy_bot", 0, At(pos, x.InnerRadius + 8, 0));
        HoldFireAll(sim);
        float hp = Hp(w, bot);
        int acuBp = w.Units.Bp[acu.Index];

        w.KillUnit(acu.Index, UnitHandle.None); // army 1 is defeated: its other units go, the blast still goes off
        Assert.True(StepToBlast(sim, 30, out var fx) > 0, "the defeated army's commander did not blast");
        Assert.Equal(acuBp, fx.BlueprintIndex);
        Assert.True(w.Armies[1].Defeated);
        Assert.InRange(hp - Hp(w, bot), x.OuterDamage - 30, x.OuterDamage + 1);
    }

    [Fact]
    public void SaveLoadWithPendingBlastContinuesIdentically()
    {
        var s = Build("cybran");
        var sim = s.Sim;
        var w = sim.World;
        w.KillUnit(s.Commander.Index, UnitHandle.None);
        TestUtil.Run(sim, 6);
        Assert.Single(CommanderDeathBlastState.Of(w).Pending);

        var saved = WorldState.Capture(sim);
        var b = WorldState.Restore(saved, w.Blueprints, w.Terrain);
        Assert.True(saved.AsSpan().SequenceEqual(WorldState.Capture(b)), "reload is not byte-identical");
        Assert.Equal(sim.StateHash(), b.StateHash());
        Assert.Single(CommanderDeathBlastState.Of(b.World).Pending);
        Assert.Equal(CommanderDeathBlastState.Of(w).Pending[0].DueTick, CommanderDeathBlastState.Of(b.World).Pending[0].DueTick);

        for (int t = 0; t < 30; t += 5)
        {
            TestUtil.Run(sim, 5);
            TestUtil.Run(b, 5);
            Assert.Equal(sim.StateHash(), b.StateHash());
        }
        foreach (var t in s.InnerTanks)
        {
            Assert.False(w.Units.IsActive(t), "the blast never went off");
            Assert.False(b.World.Units.IsActive(t), "the blast never went off after the reload");
        }
    }
}
