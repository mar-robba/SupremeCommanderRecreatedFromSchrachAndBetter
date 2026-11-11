using Rts.Sim.Snapshot;

namespace Rts.Client.Rendering;

/// <summary>(9b) A wreck stays hidden (falling aircraft, commanders, ships sinking in deep water) or fades in (collapsing
/// structures, experimentals, ships in shallow water) while its unit's death sequence plays (DeathAnimator.WreckVisibility).</summary>
public sealed partial class PropRenderer
{
    public DeathAnimator? DeathFx;

    private bool DeathFxHides(in PropView p, double age, ref float vis)
    {
        if (DeathFx == null || age > DeathAnim.MaxDuration) return false;
        vis *= DeathFx.WreckVisibility(p.Id, p.Bp, p.Pos);
        return vis <= 0.01f;
    }
}
