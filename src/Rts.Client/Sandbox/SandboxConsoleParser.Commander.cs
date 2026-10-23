using System.Numerics;
using Rts.Sim.Orders;

namespace Rts.Client.Sandbox;

/// <summary>Arm / disarm the Overcharge of every commander of Army (-1 = all armies).</summary>
public sealed record OverchargeAction(int Army, bool On) : ConsoleAction;

/// <summary>Kill (with wrecks and death effects, e.g. a commander's death blast) every unit within Radius of At.</summary>
public sealed record KillAreaAction(float Radius, Vector2 At) : ConsoleAction;

/// <summary>Console commands for commander tests: <c>overcharge [army|all] [on|off]</c> and the area form of
/// <c>kill [radius] at x,z</c> (plain <c>kill</c> = the selection).</summary>
public static partial class SandboxConsoleParser
{
    public const float DefaultKillRadius = 5f;

    private static ParseResult ParseOvercharge(Cmd c, List<string> a, ConsoleContext ctx)
    {
        var r = ParseToggle(c, a, ctx, default);
        return r.Action is ToggleAction t ? ParseResult.Ok(new OverchargeAction(t.Army, t.On ?? true)) : r;
    }

    private static ParseResult ParseKill(Cmd c, List<string> a, ConsoleContext ctx)
    {
        if (a.Count == 0) return ParseResult.Ok(new UnitActionAction(SandboxUnitAction.Kill));
        if (ParseArea(c, a, out float? radius, out Vector2? at) is { Error: not null } err) return err;
        if (at is not Vector2 p) return Fail("kill: an area needs a position (kill [radius] at x,z)");
        return ParseResult.Ok(new KillAreaAction(radius ?? DefaultKillRadius, p));
    }
}
