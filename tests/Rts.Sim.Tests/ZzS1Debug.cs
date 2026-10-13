using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Strategic;
using Rts.Sim.World;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

public class ZzS1Debug(ITestOutputHelper o)
{
    [Fact]
    public void Dbg()
    {
        var sim = CombatWorld.Flat();
        for (int k = 0; k < 20; k++) CombatWorld.Spawn(sim, "uef_t1_power_generator", 0, 6 + k * 2.5f, 6);
        CombatWorld.Spawn(sim, "uef_t1_energy_storage", 0, 120, 6);
        sim.World.Armies[0].Eco.Energy = 5000;
        var g = CombatWorld.Spawn(sim, "uef_t2_shield_generator", 0, 64, 64);
        var target = CombatWorld.Spawn(sim, "uef_t1_power_generator", 0, 67, 64);
        var b = CombatWorld.Spawn(sim, "uef_t1_bomber", 1, 20, 64);
        sim.Step([new IssueOrderCommand(1, [b], Order.AttackUnit(target), false)]);
        var P = sim.World.Projectiles;
        for (int t = 0; t < 400; t++)
        {
            sim.Step();
            var u = sim.World.Units;
            string fx = string.Join(",", sim.World.Events.Effects.Where(e => e.Kind != EffectKind.MuzzleFlash).Select(e => $"{e.Kind}@{e.Pos.X:F1},{e.Pos.Y:F1},{e.Pos.Z:F1}"));
            sim.World.Events.Effects.Clear();
            string pr = "";
            for (int k = 0; k < P.AliveCount; k++) { int p = P.AliveList[k]; pr += $" p{p}({P.Pos[p].X:F1},{P.Pos[p].Y:F1},{P.Pos[p].Z:F1})"; }
            if (t % 5 == 0 || fx.Length > 0 || pr.Length > 0)
                o.WriteLine($"t{t} b=({u.Pos[b.Index].X:F1},{u.Elevation[b.Index]:F1},{u.Pos[b.Index].Y:F1}) v={u.Move[b.Index].Velocity} sh={StrategicState.Of(sim.World).Shield[g.Index].Hp:F0} up={StrategicState.Of(sim.World).Shield[g.Index].Up} tgtHp={CombatWorld.Health(sim, target):F0} cd={CombatWorld.Weapon(sim, b).Cooldown:F2} fx={fx}{pr}");
        }
    }
}
