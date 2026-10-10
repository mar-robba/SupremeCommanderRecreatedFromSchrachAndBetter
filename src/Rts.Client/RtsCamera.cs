using System.Numerics;
using Raylib_cs;
using Rts.Sim.Map;

namespace Rts.Client;

/// <summary>
/// SupCom-style camera: looks at a ground focus point; mouse wheel zooms towards the cursor from close-up
/// to a full-map strategic view (pitch goes from oblique to top-down as you zoom out).
/// </summary>
public sealed class RtsCamera
{
    public Vector3 Focus;
    public float Distance = 80f;
    public float Yaw = MathF.PI * 0.5f;
    public float MinDistance = 12f;
    public float MaxDistance;
    public Camera3D Camera;
    private readonly Terrain _terrain;

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

    public float Pitch => float.Lerp(0.95f, 1.45f, MathF.Pow(Math.Clamp(ZoomT, 0, 1), 0.6f));

    public void LookAt(float x, float z, float distance)
    {
        Focus = new Vector3(x, _terrain.GetHeight(x, z), z);
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
        UpdateCamera();
    }

    public void Update(float dt, bool mouseOverUi, Func<Vector2, Vector3?> pickGround)
    {
        // Keyboard / edge pan, speed proportional to zoom.
        float pan = Distance * 1.2f * dt;
        var fwd = new Vector3(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        var move = Vector3.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) move -= fwd;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) move += fwd;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) move += right;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) move -= right;
        if (Raylib.IsWindowFocused() && !mouseOverUi)
        {
            var m = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            const int edge = 3;
            if (m.X <= edge) move += right;
            if (m.X >= sw - edge - 1) move -= right;
            if (m.Y <= edge) move -= fwd;
            if (m.Y >= sh - edge - 1) move += fwd;
        }
        if (move != Vector3.Zero) Focus += Vector3.Normalize(move) * pan;

        // Middle mouse drag pans the map.
        if (Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            var d = Raylib.GetMouseDelta();
            float k = Distance / Raylib.GetScreenHeight() * 0.9f;
            Focus += right * d.X * k + fwd * d.Y * k;
        }

        // Zoom to cursor.
        float wheel = mouseOverUi ? 0 : Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            var before = pickGround(Raylib.GetMousePosition());
            float factor = MathF.Pow(0.85f, wheel);
            float newDist = Math.Clamp(Distance * factor, MinDistance, MaxDistance);
            if (before is Vector3 p && wheel > 0)
            {
                float t = 1f - newDist / Distance;
                Focus += (new Vector3(p.X, Focus.Y, p.Z) - Focus) * t;
            }
            Distance = newDist;
        }

        Focus.X = Math.Clamp(Focus.X, 0, _terrain.Width);
        Focus.Z = Math.Clamp(Focus.Z, 0, _terrain.Height);
        Focus.Y = float.Lerp(Focus.Y, MathF.Max(_terrain.GetHeight(Focus.X, Focus.Z), _terrain.WaterLevel), MathF.Min(1, dt * 6));
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        float p = Pitch;
        var back = new Vector3(MathF.Cos(Yaw) * MathF.Cos(p), MathF.Sin(p), MathF.Sin(Yaw) * MathF.Cos(p));
        Camera.Target = Focus;
        Camera.Position = Focus + back * Distance;
    }
}
