using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>EMP stun (stub, filled in by the Cybran sim-features work): stunned units can't move, fire or build.</summary>
public static class EmpStun
{
    /// <summary>Is unit slot <paramref name="unit"/> stunned right now? Thread-safe read.</summary>
    public static bool IsStunned(SimWorld w, int unit) => false;
}
