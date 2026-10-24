namespace Rts.Client;

/// <summary>User camera options (Settings → Gameplay, applied by <see cref="Menus.GraphicsSettings"/>), read by
/// <see cref="RtsCamera"/> every frame. Defaults = the behavior before these settings existed.</summary>
public static class CameraSettings
{
    /// <summary>Pan when the mouse touches the screen edge.</summary>
    public static bool EdgeScroll = true;
    /// <summary>Keyboard / edge pan speed multiplier (0.5..2).</summary>
    public static float PanSpeed = 1f;
    /// <summary>Mouse-wheel zoom step multiplier (0.5..2).</summary>
    public static float ZoomSpeed = 1f;
}
