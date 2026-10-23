using Rts.Client.Rendering;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Audio;

/// <summary>
/// Commander sounds: the Overcharge shot and impact (layered on the regular gun / explosion sounds, which the sim
/// raises too) and the commander death blast: an <c>EffectKind.Nuke</c> from a commander's blueprint is positional
/// (<see cref="SoundId.CommanderBlast"/>, heard faintly from afar) instead of the map-wide strategic nuke sound.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>Handles Overcharge effects and commander blasts; null = not a commander effect.</summary>
    private int? HandleCommanderEffect(in EffectEvent e)
    {
        switch (e.Kind)
        {
            case EffectKind.Overcharge:
                return At(SoundId.OverchargeFire, e.Pos, 1.1f, 1f, minGain: e.Army == _ctx.LocalArmy ? 0.08f : 0f) ? 1 : 0;
            case EffectKind.OverchargeHit:
                return At(SoundId.OverchargeHit, e.Pos, 1.1f, Math.Clamp(1.15f - e.Size * 0.06f, 0.85f, 1.1f)) ? 1 : 0;
            case EffectKind.Nuke when EffectsRenderer.IsCommanderBlast(_ctx.Db, e.BlueprintIndex):
                At(SoundId.CommanderBlast, e.Pos, 1.35f, 1f, minGain: 0.12f);
                return 1;
        }
        return null;
    }
}
