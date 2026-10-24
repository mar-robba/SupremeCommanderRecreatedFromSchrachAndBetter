using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client;

/// <summary>
/// SupCom-style camera: looks at a ground focus point; the mouse wheel zooms smoothly towards the cursor
/// (the ground point under the cursor stays under it) from an oblique close-up to a top-down strategic view.
/// Pan: arrow keys, screen edges, middle-mouse drag. Rotate: Alt + middle-mouse drag or PageUp/PageDown,
/// Home resets the rotation.
/// </summary>
public sealed class RtsCamera
{
    public Vector3 Focus;
    /// <summary>Current (smoothed) distance from the focus point.</summary>
    public float Distance = 80f;
    /// <summary>Distance the zoom is easing towards.</summary>
    public float TargetDistance = 80f;
    public float Yaw = MathF.PI * 0.5f;
    private float _targetYaw = MathF.PI * 0.5f;
    public float MinDistance = 8f;
    public float MaxDistance;
    public Camera3D Camera;
    /// <summary>False in automated screenshot runs so stray desktop input cannot move the camera.</summary>
    public bool InputEnabled = true;
    /// <summary>User settings (Settings → Gameplay): screen-edge panning, keyboard/edge pan and wheel zoom multipliers.</summary>
    public static bool EdgeScroll = true;
    public static float PanSpeedScale = 1f, ZoomSpeedScale = 1f;
    private readonly Terrain _terrain;
    private Vector3? _zoomAnchor;
    private Vector2 _anchorMouse;

    public RtsCamera(Terrain terrain)
    {
        _terrain = terrain;
        MaxDistance = MathF.Max(terrain.Width, terrain.Height) * 1.25f;
        Focus = new Vector3(terrain.Width * 0.5f, 0, terrain.Height * 0.5f);
        Camera = new Camera3D { Up = Vector3.UnitY, FovY = 45f, Projection = CameraProjection.Perspective };
        UpdateCamera();
    }

    /// <summary>0 = fully zoomed in, 1 = fully zoomed out.</summary>
    public float ZoomT => (Distance - MinDistance) / (MaxDistance - MinDistance);

    /// <summary>Elevation angle: about 40 degrees when close, nearly top-down in the strategic view.</summary>
    public float Pitch
    {
        get
        {
            float t = Math.Clamp((MathF.Log(Distance) - MathF.Log(10f)) / (MathF.Log(260f) - MathF.Log(10f)), 0f, 1f);
            return float.Lerp(0.70f, 1.40f, MathF.Pow(t, 1.3f));
        }
    }

    public void LookAt(float x, float z, float distance)
    {
        Focus = new Vector3(x, _terrain.GetHeight(x, z), z);
        Distance = TargetDistance = Math.Clamp(distance, MinDistance, MaxDistance);
        _zoomAnchor = null;
        UpdateCamera();
    }

    /// <summary>Sets the camera rotation (radians, default PI/2 = looking towards -Z).</summary>
    public void SetYaw(float yaw)
    {
        Yaw = _targetYaw = yaw;
        UpdateCamera();
    }

    public void Update(float dt, bool mouseOverUi, Func<Vector2, Vector3?> pickGround)
    {
        dt = MathF.Min(dt, 0.1f);
        if (!InputEnabled)
        {
            ClampFocus(dt);
            UpdateCamera();
            return;
        }
        var mouse = Raylib.GetMousePosition();
        bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);

        // Rotation.
        if (Raylib.IsKeyDown(KeyboardKey.PageUp)) _targetYaw += 1.6f * dt;
        if (Raylib.IsKeyDown(KeyboardKey.PageDown)) _targetYaw -= 1.6f * dt;
        if (Raylib.IsKeyPressed(KeyboardKey.Home)) _targetYaw = MathF.PI * 0.5f;
        if (alt && Raylib.IsMouseButtonDown(MouseButton.Middle)) _targetYaw += Raylib.GetMouseDelta().X * 0.006f;
        Yaw += (_targetYaw - Yaw) * (1f - MathF.Exp(-dt * 14f));

        // Keyboard / edge pan, speed proportional to zoom. (WASD is left to unit hotkeys: A = attack-move, S = stop.)
        float pan = Distance * 1.2f * dt * PanSpeedScale;
        var fwd = new Vector3(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        var move = Vector3.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.Up)) move -= fwd;
        if (Raylib.IsKeyDown(KeyboardKey.Down)) move += fwd;
        if (Raylib.IsKeyDown(KeyboardKey.Left)) move += right;
        if (Raylib.IsKeyDown(KeyboardKey.Right)) move -= right;
        if (EdgeScroll && Raylib.IsWindowFocused() && Raylib.IsCursorOnScreen() && !mouseOverUi)
        {
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            const int edge = 3;
            if (mouse.X <= edge) move += right;
            if (mouse.X >= sw - edge - 1) move -= right;
            if (mouse.Y <= edge) move -= fwd;
            if (mouse.Y >= sh - edge - 1) move += fwd;
        }
        if (move != Vector3.Zero) { Focus += Vector3.Normalize(move) * pan; _zoomAnchor = null; }

        // Middle mouse drag pans the map.
        if (!alt && Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            var d = Raylib.GetMouseDelta();
            float k = Distance / Raylib.GetScreenHeight() * 0.9f;
            Focus += right * d.X * k + fwd * d.Y * k;
            _zoomAnchor = null;
        }

        // Zoom to cursor: remember the ground point under the cursor and keep it there while easing.
        float wheel = mouseOverUi ? 0 : Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            TargetDistance = Math.Clamp(TargetDistance * MathF.Pow(0.82f, wheel * ZoomSpeedScale), MinDistance, MaxDistance);
            if (_zoomAnchor == null || Vector2.Distance(mouse, _anchorMouse) > 3f)
            {
                _zoomAnchor = pickGround(mouse);
                _anchorMouse = mouse;
            }
        }
        if (_zoomAnchor != null && Vector2.Distance(mouse, _anchorMouse) > 3f) _zoomAnchor = null;

        float before = Distance;
        Distance += (TargetDistance - Distance) * (1f - MathF.Exp(-dt * 11f));
        if (MathF.Abs(TargetDistance - Distance) < 0.01f) Distance = TargetDistance;

        ClampFocus(dt);
        UpdateCamera();
        if (_zoomAnchor is Vector3 anchor && MathF.Abs(before - Distance) > 1e-4f)
        {
            if (pickGround(mouse) is Vector3 now)
            {
                Focus.X += anchor.X - now.X;
                Focus.Z += anchor.Z - now.Z;
                ClampFocus(0);
                UpdateCamera();
            }
        }
        if (MathF.Abs(TargetDistance - Distance) < 0.01f) _zoomAnchor = null;
    }

    private void ClampFocus(float dt)
    {
        Focus.X = Math.Clamp(Focus.X, 0, _terrain.Width);
        Focus.Z = Math.Clamp(Focus.Z, 0, _terrain.Height);
        float ground = MathF.Max(_terrain.GetHeight(Focus.X, Focus.Z), _terrain.WaterLevel);
        Focus.Y = dt > 0 ? float.Lerp(Focus.Y, ground, MathF.Min(1, dt * 6)) : Focus.Y;
    }

    private void UpdateCamera()
    {
        float p = Pitch;
        var back = new Vector3(MathF.Cos(Yaw) * MathF.Cos(p), MathF.Sin(p), MathF.Sin(Yaw) * MathF.Cos(p));
        Camera.Target = Focus;
        Camera.Position = Focus + back * Distance;
        // Never go below the terrain when very close on hills.
        float minY = MathF.Max(_terrain.GetHeight(Camera.Position.X, Camera.Position.Z), _terrain.WaterLevel) + 2f;
        if (Camera.Position.Y < minY) Camera.Position.Y = minY;
    }
}
