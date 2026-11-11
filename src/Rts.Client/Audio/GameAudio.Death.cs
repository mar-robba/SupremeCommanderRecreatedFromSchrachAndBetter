namespace Rts.Client.Audio;

/// <summary>(9b) Plays the sounds the client-side death sequences and the ACU warp-in queue in
/// <see cref="UnitFxSounds"/> (<see cref="Rendering.DeathAnimator"/>); they arrive one frame late (the effects renderer
/// draws after the audio update), which is inaudible.</summary>
public sealed partial class GameAudio
{
    private void PlayDeathSounds()
    {
        if (UnitFxSounds.Count == 0) return;
        Span<UnitFxSounds.Request> reqs = stackalloc UnitFxSounds.Request[UnitFxSounds.Capacity];
        int n = UnitFxSounds.Drain(reqs);
        for (int i = 0; i < n; i++)
        {
            ref readonly var r = ref reqs[i];
            At(r.Id, r.Pos, r.Volume, r.Pitch, r.MinGain);
        }
    }
}
