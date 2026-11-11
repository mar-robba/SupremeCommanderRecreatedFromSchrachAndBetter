using System.Numerics;
using Rts.Sim.World;

namespace Rts.Client.Rendering;

/// <summary>(9b) Death sequences (<see cref="DeathAnimator"/>): matched each snapshot before its effects spawn; an
/// aircraft that will spiral down gets a smaller mid-air burst instead of the full death explosion.</summary>
public sealed partial class EffectsRenderer
{
    private DeathAnimator? _deaths;
    public DeathAnimator Deaths => _deaths ??= new DeathAnimator(_ctx, _ps);

    private bool SpawnAnimatedDeath(in EffectEvent e)
    {
        if (e.Kind != EffectKind.UnitDeath || _deaths == null) return false;
        if (_deaths.ClaimedKind(in e) != DeathKind.AirCrash) return false;
        float size = MathF.Max(0.3f, e.Size);
        _ps.Explosion(e.Pos, size * 0.45f, false);
        _ps.Sparks(e.Pos, 12, 7f, new Vector4(1f, 0.7f, 0.3f, 1f), 0.6f, 0.1f);
        _ps.Debris(e.Pos, 3 + (int)size, 5f, 0.1f + size * 0.04f);
        return true;
    }

    private void UpdateDeaths(float dt)
    {
        Deaths.Update(dt);
        DeathScene.Update(_ctx);
    }
}
