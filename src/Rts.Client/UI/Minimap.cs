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
public sealed partial class Minimap : IDisposable
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
        var pal = Rendering.BiomePalettes.Minimap(_ctx.TerrainRenderer.Biome); // (8b-3) biome colours
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
                c = Vector3.Lerp(pal.WaterShallow, pal.WaterDeep, depth);
            }
            else
            {
                float k = Math.Clamp(rel / maxH, 0, 1);
                c = rel < 1.0f ? pal.Shore : Vector3.Lerp(pal.Low, pal.High, k);
                float slope = 1f - n.Y;
                c = Vector3.Lerp(c, pal.Slope, Math.Clamp((slope - 0.12f) * 4f, 0, 1));
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

    // m here is the virtual whole-map rectangle (MinimapView.Virtual; = the visible map rectangle at zoom 1).
    private Vector2 ToScreen(Rectangle m, float x, float z) => MinimapView.ToScreen(m, _ctx.Terrain.Width, _ctx.Terrain.Height, x, z);

    private Vector2 ToWorld(Rectangle m, Vector2 p) => MinimapView.ToWorld(m, _ctx.Terrain.Width, _ctx.Terrain.Height, p);

    private static Menus.AppSettings Settings => Menus.AppSettings.Current;

    /// <summary>(9b-2) Whole-map rectangle for this frame: zoomed around the camera focus; frozen while the camera
    /// is dragged on the minimap (else the map would slide under the cursor).</summary>
    private Rectangle _view;
    private Rectangle ViewRect(Rectangle m)
    {
        if (_dragging && _view.Width > 0) return _view;
        var f = _ctx.Camera.Focus;
        _view = MinimapView.Virtual(m, Settings.MinimapZoom, new Vector2(f.X / _ctx.Terrain.Width, f.Z / _ctx.Terrain.Height));
        return _view;
    }

    /// <summary>Input + drawing. Call from Hud.Draw.</summary>
    private readonly HashSet<long> _selected = new();
    private readonly List<int> _mobile = new();
    private int[] _dotStamp = [], _dotKey = [];
    private int _dotFrame;

    public void Draw(Rectangle panel)
    {
        if (!_baked) Bake();
        var vis = MapRect(panel);
        bool buttonHover = HandleButtons(panel, vis); // (9b-2) size / zoom / layer buttons: Minimap.Options.cs
        var m = ViewRect(vis);
        HandleInput(panel, m);
        m = ViewRect(vis); // the wheel may have changed the zoom

        Ui.Panel(panel);
        Raylib.DrawRectangleRec(Ui.Inset(vis, -1), Color.Black);
        Raylib.BeginScissorMode((int)vis.X, (int)vis.Y, (int)vis.Width, (int)vis.Height);
        Raylib.DrawTexturePro(_tex, new Rectangle(0, 0, _tex.Width, _tex.Height), m, Vector2.Zero, 0, Color.White);
        _ctx.Fog?.DrawMinimapFog(m);
        float k = m.Width / _ctx.Terrain.Width;
        var opt = Settings;
        var cull = Ui.Inset(vis, -4);

        // Deposits.
        if (opt.MinimapResources)
            foreach (var d in _ctx.Terrain.Deposits)
            {
                var p = ToScreen(m, d.Pos.X, d.Pos.Y);
                var c = d.Type == DepositType.Mass ? new Color(120, 255, 120, 255) : new Color(255, 200, 70, 255);
                float ds = MathF.Max(4f, MathF.Min(10f, 2.5f * k));
                Raylib.DrawRectangleLinesEx(new Rectangle(p.X - ds * 0.5f, p.Y - ds * 0.5f, ds, ds), 1, c);
            }
        int gw = (int)vis.Width + 2, gh = (int)vis.Height + 2;
        if (_dotStamp.Length < gw * gh) { _dotStamp = new int[gw * gh]; _dotKey = new int[gw * gh]; }
        if (opt.MinimapTrees) DrawTrees(m, vis, gw, gh);

        // Units: structures as squares, mobile units as dots; selected units white.
        var snap = _ctx.Snapshots.Current;
        var selected = _selected;
        selected.Clear();
        foreach (var h in _sel.Selected) selected.Add(((long)h.Index << 32) | (uint)h.Generation);
        float dot = MathF.Max(2f, Ui.P(2.5f));
        // Mobile units overlap a lot at minimap scale (thousands of units in a few pixels): one dot per pixel and
        // color is enough. Structures first, mobile units on top (second loop over the remembered indices).
        int stamp = ++_dotFrame;
        _mobile.Clear();
        for (int i = 0; i < (opt.MinimapUnits ? snap.UnitCount : 0); i++)
        {
            ref var v = ref snap.Units[i];
            var bp = _ctx.Db[v.Bp];
            if (!bp.IsStructure) { _mobile.Add(i); continue; }
            var p = ToScreen(m, v.Pos.X, v.Pos.Z);            bool sel = selected.Contains(((long)v.Index << 32) | (uint)v.Gen);
            var col = sel ? Color.White : _ctx.ArmyColors[v.Army];
            float s = MathF.Max(dot + 1, MathF.Max(bp.FootprintX, bp.FootprintZ) * k);
            Raylib.DrawRectangleV(p - new Vector2(s * 0.5f + 1, s * 0.5f + 1), new Vector2(s + 2, s + 2), Color.Black);
            Raylib.DrawRectangleV(p - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s), col);
        }
        foreach (int i in _mobile)
        {
            ref var v = ref snap.Units[i];
            var bp = _ctx.Db[v.Bp];
            var p = ToScreen(m, v.Pos.X, v.Pos.Z);
            if (!Raylib.CheckCollisionPointRec(p, cull)) continue;
            bool sel = selected.Contains(((long)v.Index << 32) | (uint)v.Gen);
            var col = sel ? Color.White : _ctx.ArmyColors[v.Army];
            if (bp.Has(UnitCategory.Commander))
            {
                Raylib.DrawPoly(p, 4, dot * 1.6f + 1, 0, Color.Black);
                Raylib.DrawPoly(p, 4, dot * 1.6f, 0, col);
                continue;
            }
            int gx = Math.Clamp((int)(p.X - vis.X) + 1, 0, gw - 1), gz = Math.Clamp((int)(p.Y - vis.Y) + 1, 0, gh - 1);
            int cell = gz * gw + gx, key = sel ? -1 : v.Army;
            if (_dotStamp[cell] == stamp && _dotKey[cell] == key) continue;
            _dotStamp[cell] = stamp;
            _dotKey[cell] = key;
            Raylib.DrawRectangleV(p - new Vector2(dot * 0.5f, dot * 0.5f), new Vector2(dot, dot), col);
        }

        _ctx.Fog?.DrawMinimapContacts(m, snap, dot); // fog of war: ghosts + radar blips
        PingMarkers.DrawMinimap(snap, _sel.Alerts, (x, z) => ToScreen(m, x, z)); // (7b) alert flashes + map pings
        DrawCameraView(m);
        Raylib.EndScissorMode();
        Raylib.DrawRectangleLinesEx(Ui.Inset(vis, -1), 1, Ui.PanelBorder);
        DrawButtons(panel, vis);

        if (Ui.Hover(vis) && !_dragging && !buttonHover)
            Ui.Tooltip(vis, new List<TipLine>
            {
                new("Minimap", Ui.TextColor, 13, true),
                new("Left click / drag: move camera", Ui.TextDim, 11.5f),
                new("Right click: move selection (factory: rally point)", Ui.TextDim, 11.5f),
                new("Alt+left: alert ping · Alt+Shift+left: attack ping · Alt+right: move ping · Y: alert ping", Ui.TextDim, 11.5f),
                new("In an order mode (attack, patrol, unload, launch, guard, capture…): left click gives it there", Ui.TextDim, 11.5f),
                new("Mouse wheel: zoom · buttons (hover): size, zoom, resources, units, trees", Ui.TextDim, 11.5f),
            }, above: true);
    }

    private void HandleInput(Rectangle panel, Rectangle m)
    {
        bool over = Ui.Hover(panel);
        // (7b) Alt+left = alert ping, Alt+Shift+left = attack, Alt+right = move; Y = alert ping at the cursor.
        if (over && Ui.Alt && !_ctx.ReadOnly && (Ui.LeftClickIn(panel) || Ui.RightClickIn(panel)))
        {
            _sel.Pings.Send(Input.PingInput.KindFor(Raylib.IsMouseButtonPressed(MouseButton.Right), Ui.Shift), ToWorld(m, Ui.Mouse));
            return;
        }
        if (over && Input.Hotkeys.PingAlert.Pressed()) _sel.Pings.Send(Rts.Sim.Orders.PingKind.Alert, ToWorld(m, Ui.Mouse));
        if (_dragging && !Raylib.IsMouseButtonDown(MouseButton.Left)) _dragging = false;

        if (over && Ui.LeftClickIn(panel))
        {
            var wp = ToWorld(m, Ui.Mouse);
            var mode = _sel.Mode;
            if (mode != SelectionController.CommandMode.Normal && _sel.Selected.Count > 0 && !_ctx.ReadOnly)
            {
                // Every order mode works on the minimap (guard / capture: the nearest fitting unit to the click).
                float pickR = MathF.Max(3f, Ui.P(6) * _ctx.Terrain.Width / MathF.Max(1f, m.Width));
                if (_sel.IssueAtWorldPoint(wp, mode, Ui.Shift, pickR) && !Ui.Shift) _sel.Mode = SelectionController.CommandMode.Normal;
            }
            else _dragging = true;
        }
        if (_dragging)
        {
            var wp = ToWorld(m, Ui.Mouse);
            _ctx.Camera.LookAt(wp.X, wp.Y, _ctx.Camera.Distance);
        }
        if (over && !_ctx.ReadOnly && Ui.RightClickIn(panel) && _sel.Selected.Count > 0)
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
