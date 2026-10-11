using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Core;
using Rts.Sim.Data;
using Rts.Sim.Map;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// Corner minimap: terrain thumbnail baked once into a texture (heights, water, hill shading, deposits),
/// unit dots in army colors (structures as squares), the camera view trapezoid. Left click/drag moves the
/// camera, right click orders the selection to move there (rally point for factories), and in attack-move /
/// patrol / move mode a left click issues that order.
/// </summary>
public sealed class Minimap : IDisposable
{
    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private Texture2D _tex;
    private bool _baked;
    private bool _dragging;

    public Minimap(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    public bool Dragging => _dragging;

    private void Bake()
    {
        _baked = true;
        var t = _ctx.Terrain;
        int w = t.Width, h = t.Height;
        var pixels = new Color[w * h];
        var sun = Vector3.Normalize(new Vector3(-0.5f, 0.75f, -0.45f));
        float maxH = 1;
        foreach (var v in t.Heights) maxH = MathF.Max(maxH, v - t.WaterLevel);
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            float y = t.GetHeight(x + 0.5f, z + 0.5f);
            var n = t.GetNormal(x + 0.5f, z + 0.5f);
            Vector3 c;
            float rel = y - t.WaterLevel;
            if (rel < 0)
            {
                float depth = Math.Clamp(-rel / 8f, 0, 1);
                c = Vector3.Lerp(new Vector3(0.25f, 0.45f, 0.62f), new Vector3(0.08f, 0.18f, 0.36f), depth);
            }
            else
            {
                float k = Math.Clamp(rel / maxH, 0, 1);
                c = rel < 1.0f ? new Vector3(0.66f, 0.62f, 0.45f)
                    : Vector3.Lerp(new Vector3(0.30f, 0.44f, 0.22f), new Vector3(0.55f, 0.53f, 0.38f), k);
                float slope = 1f - n.Y;
                c = Vector3.Lerp(c, new Vector3(0.42f, 0.38f, 0.34f), Math.Clamp((slope - 0.12f) * 4f, 0, 1));
                c *= 0.55f + 0.6f * MathF.Max(0, Vector3.Dot(n, sun));
            }
            pixels[z * w + x] = new Color((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), (byte)255);
        }
        var img = Raylib.GenImageColor(w, h, Color.Black);
        _tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.UpdateTexture(_tex, pixels);
        Raylib.SetTextureFilter(_tex, TextureFilter.Bilinear);
    }

    /// <summary>Inner map area of the panel rectangle (keeps the map aspect ratio).</summary>
    public Rectangle MapRect(Rectangle panel)
    {
        var t = _ctx.Terrain;
        var inner = Ui.Inset(panel, Ui.P(6));
        float aspect = t.Width / (float)t.Height;
        float w = inner.Width, h = inner.Height;
        if (w / h > aspect) w = h * aspect; else h = w / aspect;
        return new Rectangle(MathF.Round(inner.X + (inner.Width - w) * 0.5f), MathF.Round(inner.Y + (inner.Height - h) * 0.5f), MathF.Round(w), MathF.Round(h));
    }

    private Vector2 ToScreen(Rectangle m, float x, float z) =>
        new(m.X + x / _ctx.Terrain.Width * m.Width, m.Y + z / _ctx.Terrain.Height * m.Height);

    private Vector2 ToWorld(Rectangle m, Vector2 p) =>
        new(Math.Clamp((p.X - m.X) / m.Width, 0, 1) * _ctx.Terrain.Width, Math.Clamp((p.Y - m.Y) / m.Height, 0, 1) * _ctx.Terrain.Height);

    /// <summary>Input + drawing. Call from Hud.Draw.</summary>
    public void Draw(Rectangle panel)
    {
        if (!_baked) Bake();
        var m = MapRect(panel);
        HandleInput(panel, m);

        Ui.Panel(panel);
        Raylib.DrawRectangleRec(Ui.Inset(m, -1), Color.Black);
        Raylib.DrawTexturePro(_tex, new Rectangle(0, 0, _tex.Width, _tex.Height), m, Vector2.Zero, 0, Color.White);
        _ctx.Fog?.DrawMinimapFog(m);

        Raylib.BeginScissorMode((int)m.X, (int)m.Y, (int)m.Width, (int)m.Height);
        float k = m.Width / _ctx.Terrain.Width;

        // Deposits.
        foreach (var d in _ctx.Terrain.Deposits)
        {
            var p = ToScreen(m, d.Pos.X, d.Pos.Y);
            var c = d.Type == DepositType.Mass ? new Color(120, 255, 120, 255) : new Color(255, 200, 70, 255);
            Raylib.DrawRectangleLinesEx(new Rectangle(p.X - 2, p.Y - 2, 4, 4), 1, c);
        }

        // Units: structures as squares, mobile units as dots; selected units white.
        var snap = _ctx.Snapshots.Current;
        var selected = new HashSet<long>();
        foreach (var h in _sel.Selected) selected.Add(((long)h.Index << 32) | (uint)h.Generation);
        float dot = MathF.Max(2f, Ui.P(2.5f));
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < snap.UnitCount; i++)
        {
            ref var v = ref snap.Units[i];
            var bp = _ctx.Db[v.Bp];
            if (bp.IsStructure != (pass == 0)) continue; // structures first, units on top
            var p = ToScreen(m, v.Pos.X, v.Pos.Z);
            bool sel = selected.Contains(((long)v.Index << 32) | (uint)v.Gen);
            var col = sel ? Color.White : _ctx.ArmyColors[v.Army];
            if (bp.IsStructure)
            {
                float s = MathF.Max(dot + 1, MathF.Max(bp.FootprintX, bp.FootprintZ) * k);
                Raylib.DrawRectangleV(p - new Vector2(s * 0.5f + 1, s * 0.5f + 1), new Vector2(s + 2, s + 2), Color.Black);
                Raylib.DrawRectangleV(p - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s), col);
            }
            else if (bp.Has(UnitCategory.Commander))
            {
                Raylib.DrawPoly(p, 4, dot * 1.6f + 1, 0, Color.Black);
                Raylib.DrawPoly(p, 4, dot * 1.6f, 0, col);
            }
            else
            {
                Raylib.DrawRectangleV(p - new Vector2(dot * 0.5f, dot * 0.5f), new Vector2(dot, dot), col);
            }
        }

        _ctx.Fog?.DrawMinimapContacts(m, snap, dot); // fog of war: ghosts + radar blips
        DrawCameraView(m);
        Raylib.EndScissorMode();
        Raylib.DrawRectangleLinesEx(Ui.Inset(m, -1), 1, Ui.PanelBorder);

        if (Ui.Hover(m) && !_dragging)
            Ui.Tooltip(m, new List<TipLine>
            {
                new("Minimap", Ui.TextColor, 13, true),
                new("Left click / drag: move camera", Ui.TextDim, 11.5f),
                new("Right click: move selection (factory: rally point)", Ui.TextDim, 11.5f),
            }, above: true);
    }

    private void HandleInput(Rectangle panel, Rectangle m)
    {
        bool over = Ui.Hover(panel);
        if (_dragging && !Raylib.IsMouseButtonDown(MouseButton.Left)) _dragging = false;

        if (over && Ui.LeftClickIn(panel))
        {
            var wp = ToWorld(m, Ui.Mouse);
            var mode = _sel.Mode;
            if (mode is SelectionController.CommandMode.AttackMove or SelectionController.CommandMode.Patrol or SelectionController.CommandMode.Move && _sel.Selected.Count > 0)
            {
                _sel.IssueAtPoint(wp, mode, Ui.Shift);
                if (!Ui.Shift) _sel.Mode = SelectionController.CommandMode.Normal;
            }
            else _dragging = true;
        }
        if (_dragging)
        {
            var wp = ToWorld(m, Ui.Mouse);
            _ctx.Camera.LookAt(wp.X, wp.Y, _ctx.Camera.Distance);
        }
        if (over && Ui.RightClickIn(panel) && _sel.Selected.Count > 0)
        {
            if (_sel.Placement.Active) _sel.Placement.Cancel();
            else if (_sel.Mode != SelectionController.CommandMode.Normal) _sel.Mode = SelectionController.CommandMode.Normal;
            else _sel.IssueAtPoint(ToWorld(m, Ui.Mouse), SelectionController.CommandMode.Normal, Ui.Shift);
        }
    }

    /// <summary>Projects the screen corners onto the ground plane through the camera focus.</summary>
    private void DrawCameraView(Rectangle m)
    {
        var cam = _ctx.Camera;
        float planeY = cam.Focus.Y;
        int sw = Ui.Sw, sh = Ui.Sh;
        Span<Vector2> corners = stackalloc Vector2[4];
        Vector2[] screen = [new(0, 0), new(sw, 0), new(sw, sh), new(0, sh)];
        for (int i = 0; i < 4; i++)
        {
            var ray = Raylib.GetScreenToWorldRay(screen[i], cam.Camera);
            Vector3 hit;
            if (ray.Direction.Y < -1e-3f)
            {
                float t = (planeY - ray.Position.Y) / ray.Direction.Y;
                hit = ray.Position + ray.Direction * MathF.Min(t, cam.MaxDistance * 4f);
            }
            else
            {
                var flat = Vector3.Normalize(new Vector3(ray.Direction.X, 0, ray.Direction.Z) + new Vector3(1e-4f, 0, 0));
                hit = ray.Position + flat * cam.MaxDistance * 4f;
            }
            corners[i] = ToScreen(m, hit.X, hit.Z);
        }
        var col = new Color(255, 255, 255, 230);
        for (int i = 0; i < 4; i++)
            Raylib.DrawLineEx(corners[i], corners[(i + 1) % 4], MathF.Max(1f, Ui.S), col);
    }

    public void Dispose()
    {
        if (_baked) Raylib.UnloadTexture(_tex);
    }
}
