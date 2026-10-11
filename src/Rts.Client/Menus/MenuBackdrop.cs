using System.Numerics;
using Raylib_cs;
using Rts.Client.Rendering;
using Rts.Client.UI;
using Rts.Sim.Map;

namespace Rts.Client.Menus;

/// <summary>
/// Animated 3D background of the menus: a generated Highlands map (built on a worker thread) seen from a slowly
/// orbiting camera, with a dark gradient on the left where the menu sits.
/// </summary>
public sealed class MenuBackdrop : IDisposable
{
    private readonly Task<Terrain> _gen;
    private TerrainRenderer? _renderer;
    private Terrain? _terrain;
    private float _angle = 0.6f;

    public MenuBackdrop()
    {
        _gen = Task.Run(() => MapGenerator.Generate(new MapGenSettings { Size = 256, Players = 4, Seed = 20261011, Preset = MapPreset.Highlands }));
    }

    public void Draw(float dt, float leftShade = 0.62f)
    {
        if (_renderer == null && _gen.IsCompletedSuccessfully)
        {
            _terrain = _gen.Result;
            _renderer = new TerrainRenderer(_terrain);
        }
        Raylib.ClearBackground(new Color(14, 19, 26, 255));
        if (_renderer != null && _terrain != null)
        {
            _angle += dt * 0.022f;
            var c = new Vector3(_terrain.Width * 0.5f, 10f, _terrain.Height * 0.5f);
            var target = c + new Vector3(MathF.Cos(_angle * 0.7f) * 30f, 0, MathF.Sin(_angle * 0.7f) * 30f);
            var cam = new Camera3D
            {
                Position = target + new Vector3(MathF.Cos(_angle) * 150f, 95f, MathF.Sin(_angle) * 150f),
                Target = target,
                Up = Vector3.UnitY,
                FovY = 45f,
                Projection = CameraProjection.Perspective,
            };
            Raylib.BeginMode3D(cam);
            _renderer.Draw(cam);
            _renderer.DrawWater();
            Raylib.EndMode3D();
        }
        Ui.BeginFrame();
        int sw = Ui.Sw, sh = Ui.Sh;
        int w = (int)(sw * leftShade);
        Raylib.DrawRectangleGradientH(0, 0, w / 2, sh, new Color(6, 10, 15, 235), new Color(6, 10, 15, 170));
        Raylib.DrawRectangleGradientH(w / 2, 0, w - w / 2, sh, new Color(6, 10, 15, 170), new Color(6, 10, 15, 0));
        Raylib.DrawRectangleGradientV(0, sh - (int)Ui.P(160), sw, (int)Ui.P(160), new Color(6, 10, 15, 0), new Color(6, 10, 15, 200));
    }

    public void Dispose() => _renderer?.Dispose();
}
