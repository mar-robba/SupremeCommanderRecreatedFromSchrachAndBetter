using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim.Map;

namespace Rts.Client.Menus;

/// <summary>
/// Live map thumbnail for the skirmish setup: the map is generated on a worker thread whenever the settings
/// change (latest request wins), baked into a shaded color image there, and uploaded as a texture on the main
/// thread. Draws start positions (numbered, in player colors) and deposits on top.
/// </summary>
public sealed class MapPreview : IDisposable
{
    private Task<(Terrain t, Color[] px, int res)>? _task;
    private string _taskKey = "";
    private MapGenSettings? _pending;
    private string _pendingKey = "";
    private string _shownKey = "";
    private Texture2D _tex;
    private bool _hasTex;
    private double _taskStart;

    public Terrain? Terrain { get; private set; }
    public double LastGenMs { get; private set; }
    public bool Busy => _task != null || _pending != null;

    private static string Key(MapGenSettings s) => $"{s.Size}|{s.Players}|{s.Seed}|{s.Preset}|{s.WaterAmount:F3}";

    /// <summary>Asks for a preview of these settings (no-op if already shown or being generated).</summary>
    public void Request(MapGenSettings s)
    {
        string k = Key(s);
        if (k == _shownKey && _task == null) return;
        if (k == _taskKey || k == _pendingKey) return;
        _pending = s;
        _pendingKey = k;
    }

    /// <summary>Call once per frame on the main thread.</summary>
    public void Update()
    {
        if (_task is { IsCompleted: true })
        {
            if (_task.IsCompletedSuccessfully)
            {
                var (t, px, res) = _task.Result;
                Upload(px, res);
                Terrain = t;
                _shownKey = _taskKey;
                LastGenMs = (Raylib.GetTime() - _taskStart) * 1000;
            }
            else Console.Error.WriteLine("Map preview failed: " + _task.Exception?.GetBaseException().Message);
            _task = null;
            _taskKey = "";
        }
        if (_task == null && _pending != null)
        {
            var s = _pending;
            _taskKey = _pendingKey;
            _pending = null;
            _pendingKey = "";
            _taskStart = Raylib.GetTime();
            _task = Task.Run(() =>
            {
                var t = MapGenerator.Generate(s);
                int res = Math.Min(t.Width, 512);
                return (t, Bake(t, res), res);
            });
        }
    }

    private void Upload(Color[] px, int res)
    {
        if (_hasTex && _tex.Width != res)
        {
            Raylib.UnloadTexture(_tex);
            _hasTex = false;
        }
        if (!_hasTex)
        {
            var img = Raylib.GenImageColor(res, res, Color.Black);
            _tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(_tex, TextureFilter.Bilinear);
            _hasTex = true;
        }
        Raylib.UpdateTexture(_tex, px);
    }

    /// <summary>Hill-shaded color image of the terrain (same palette as the in-game minimap).</summary>
    public static Color[] Bake(Terrain t, int res)
    {
        var px = new Color[res * res];
        var sun = Vector3.Normalize(new Vector3(-0.5f, 0.75f, -0.45f));
        float maxH = 1;
        foreach (var v in t.Heights) maxH = MathF.Max(maxH, v - t.WaterLevel);
        float step = t.Width / (float)res;
        Parallel.For(0, res, z =>
        {
            for (int x = 0; x < res; x++)
            {
                float wx = (x + 0.5f) * step, wz = (z + 0.5f) * step;
                float y = t.GetHeight(wx, wz);
                var n = t.GetNormal(wx, wz);
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
                        : Vector3.Lerp(new Vector3(0.30f, 0.44f, 0.22f), new Vector3(0.58f, 0.55f, 0.42f), k);
                    float slope = 1f - n.Y;
                    c = Vector3.Lerp(c, new Vector3(0.42f, 0.38f, 0.34f), Math.Clamp((slope - 0.12f) * 4f, 0, 1));
                    c *= 0.55f + 0.6f * MathF.Max(0, Vector3.Dot(n, sun));
                }
                px[z * res + x] = new Color((byte)Math.Clamp(c.X * 255, 0, 255), (byte)Math.Clamp(c.Y * 255, 0, 255), (byte)Math.Clamp(c.Z * 255, 0, 255), (byte)255);
            }
        });
        return px;
    }

    /// <summary>Draws the thumbnail into r (square), with start markers in the given colors.</summary>
    public void Draw(Rectangle r, IReadOnlyList<Color> startColors)
    {
        Raylib.DrawRectangleRec(Ui.Inset(r, -1), Color.Black);
        if (_hasTex) Raylib.DrawTexturePro(_tex, new Rectangle(0, 0, _tex.Width, _tex.Height), r, Vector2.Zero, 0, Color.White);
        else Raylib.DrawRectangleRec(r, new Color(20, 26, 34, 255));
        var t = Terrain;
        if (t != null && _hasTex)
        {
            float k = r.Width / t.Width;
            foreach (var d in t.Deposits)
            {
                var p = new Vector2(r.X + d.Pos.X * k, r.Y + d.Pos.Y * k);
                var c = d.Type == DepositType.Mass ? new Color(140, 255, 120, 255) : new Color(255, 205, 70, 255);
                float s = MathF.Max(2f, Ui.P(d.Type == DepositType.Mass ? 2f : 3f));
                Raylib.DrawRectangleRec(new Rectangle(p.X - s * 0.5f, p.Y - s * 0.5f, s, s), c);
            }
            for (int i = 0; i < t.StartPositions.Length; i++)
            {
                var sp = t.StartPositions[i];
                var p = new Vector2(r.X + sp.X * k, r.Y + sp.Y * k);
                float rad = Ui.P(9);
                var col = i < startColors.Count ? startColors[i] : Color.White;
                Raylib.DrawCircleV(p, rad + 2, new Color(0, 0, 0, 200));
                Raylib.DrawCircleV(p, rad, col);
                Raylib.DrawCircleLinesV(p, rad, Color.White);
                string s = (i + 1).ToString();
                int fs = Ui.F(11);
                Ui.Text(s, p.X - Ui.Measure(s, fs, true) * 0.5f, p.Y - fs * 0.5f - 1, fs, Color.White, true);
            }
        }
        if (Busy)
        {
            MenuUi.Spinner(new Vector2(r.X + r.Width - Ui.P(18), r.Y + Ui.P(18)), Ui.P(8), Color.White);
        }
        Raylib.DrawRectangleLinesEx(Ui.Inset(r, -1), 1, Ui.PanelBorder);
    }

    public void Dispose()
    {
        if (_hasTex) Raylib.UnloadTexture(_tex);
        _hasTex = false;
    }
}
