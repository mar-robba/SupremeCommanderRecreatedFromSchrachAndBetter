using System.Numerics;

namespace Rts.Client.Audio;

/// <summary>
/// (9b) Positional sound requests raised by client-side sequences that have no sim event of their own (aircraft
/// crash, ship sinking, structure collapse, ACU warp-in: <see cref="Rendering.DeathAnimator"/>). Like
/// <see cref="UiSounds"/> a static queue: the renderers request, <see cref="GameAudio"/> drains it once per frame
/// (GameAudio.Death.cs). Fixed capacity, no allocation; requests beyond it are dropped.
/// </summary>
public static class UnitFxSounds
{
    public struct Request
    {
        public SoundId Id;
        public Vector3 Pos;
        public float Volume, Pitch, MinGain;
    }

    public const int Capacity = 32;
    private static readonly Request[] _queue = new Request[Capacity];
    private static int _count;
    private static readonly object _lock = new();

    public static int Count => _count;

    public static void Play(SoundId id, Vector3 pos, float volume = 1f, float pitch = 1f, float minGain = 0f)
    {
        lock (_lock)
        {
            if (_count >= Capacity) return;
            _queue[_count++] = new Request { Id = id, Pos = pos, Volume = volume, Pitch = pitch, MinGain = minGain };
        }
    }

    /// <summary>Copies the pending requests into <paramref name="into"/> (at most its length) and clears the queue.</summary>
    public static int Drain(Span<Request> into)
    {
        lock (_lock)
        {
            int n = Math.Min(_count, into.Length);
            _queue.AsSpan(0, n).CopyTo(into);
            _count = 0;
            return n;
        }
    }

    /// <summary>Drops everything pending (a new game starts).</summary>
    public static void Clear()
    {
        lock (_lock) _count = 0;
    }
}
