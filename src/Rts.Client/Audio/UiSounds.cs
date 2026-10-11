namespace Rts.Client.Audio;

/// <summary>
/// Fire-and-forget UI sound requests from widgets and input code (main thread). <see cref="GameAudio"/> drains
/// them once per frame; without audio they are simply discarded.
/// </summary>
public static class UiSounds
{
    private static int _click, _confirm, _error;

    /// <summary>A button was pressed.</summary>
    public static void Click() => _click++;

    /// <summary>An order was issued (acknowledgement blip).</summary>
    public static void Confirm() => _confirm++;

    /// <summary>An action was rejected (invalid placement, no valid target...).</summary>
    public static void Error() => _error++;

    public static (int click, int confirm, int error) Drain()
    {
        var r = (_click, _confirm, _error);
        _click = _confirm = _error = 0;
        return r;
    }
}
