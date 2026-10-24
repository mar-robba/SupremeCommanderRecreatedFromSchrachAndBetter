using System.Numerics;
using Rts.Sim.Orders;

namespace Rts.Client.Sandbox;

/// <summary>Arm / disarm the Overcharge of every commander of Army (-1 = all armies).</summary>
public sealed record OverchargeAction(int Army, bool On) : ConsoleAction;

/// <summary>Kill (with wrecks and death effects, e.g. a commander's death blast) every unit within Radius of At.</summary>
public sealed record KillAreaAction(float Radius, Vector2 At) : ConsoleAction;

/// <summary>Teleport order (Personal Teleporter) for every commander with a teleporter of Army (-1 = all armies) to At.</summary>
public sealed record TeleportAction(int Army, Vector2 At) : ConsoleAction;

/// <summary>Console commands for commander tests: <c>overcharge [army|all] [on|off]</c>, <c>teleport [army|all] [at] x,z</c>
/// and the area form of <c>kill [radius] at x,z</c> (plain <c>kill</c> = the selection).</summary>
public static partial class SandboxConsoleParser
{
    public const float DefaultKillRadius = 5f;

    private static ParseResult ParseOvercharge(Cmd c, List<string> a, ConsoleContext ctx)
    {
        var r = ParseToggle(c, a, ctx, default);
        return r.Action is ToggleAction t ? ParseResult.Ok(new OverchargeAction(t.Army, t.On ?? true)) : r;
    }

    /// <summary>teleport [army|all] [at] x,z (default army: me).</summary>
    private static ParseResult ParseTeleport(Cmd c, List<string> a, ConsoleContext ctx)
    {
        int army = ctx.LocalArmy;
        bool armySet = false;
        Vector2? at = null;
        for (int i = 0; i < a.Count;)
        {
            string t = a[i];
            if (at != null) return Unexpected(c, t);
            if (t == "at")
            {
                i++;
                if (!TryPos(a, ref i, true, out var p)) return Fail($"{c.Name}: 'at' needs a position (at x,z)");
                at = p;
                continue;
            }
            if (t.Contains(','))
            {
                if (!TryPos(a, ref i, false, out var p)) return Fail($"{c.Name}: bad position '{t}' (use x,z)");
                at = p;
                continue;
            }
            var k = ParseArmy(t, ctx, true, out int ar, out string? err);
            if (k == ArmyParse.Error) return Fail($"{c.Name}: {err}");
            if (k == ArmyParse.NotArmy || armySet) return Unexpected(c, t);
            army = ar;
            armySet = true;
            i++;
        }
        if (at is not Vector2 dest) return Fail($"{c.Name}: needs a destination - {c.Usage}");
        return ParseResult.Ok(new TeleportAction(army, dest));
    }

    private static ParseResult ParseKill(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count == 0) return ParseResult.Ok(new UnitActionAction(SandboxUnitAction.Kill));
        if (ParseArea(c, a, out float? radius, out Vector2? at) is { Error: not null } err) return err;
        if (at is not Vector2 p) return Fail("kill: an area needs a position (kill [radius] at x,z)");
        return ParseResult.Ok(new KillAreaAction(radius ?? DefaultKillRadius, p));
    }
}
