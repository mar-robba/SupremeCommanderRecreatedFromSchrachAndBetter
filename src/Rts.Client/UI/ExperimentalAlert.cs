using System.Numerics;
using Raylib_cs;
using Rts.Client.Rendering;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.UI;

/// <summary>
/// SupCom-style "WARNING: EXPERIMENTAL DETECTED": the first time an enemy experimental (complete or under
/// construction) appears among the viewer's units of the fog-filtered snapshot, a fading banner (top center,
/// under the strategic launch banner), a klaxon (<see cref="Audio.GameAudio.PlayExperimentalAlert"/>) and pulsing
/// rings at its position. Each unit alerts once (seen ids are remembered, also after it leaves vision).
/// </summary>
public sealed class ExperimentalAlert
{
    public const float BannerTime = 6f;

    private readonly GameContext _ctx;
    private readonly Action? _sound;
    private readonly bool[] _isExperimental;
    private readonly HashSet<long> _seen = new();
    private readonly List<(Vector3 pos, float age)> _pings = new();
    private string _name = "";
    private bool _underConstruction;
    private int _extra;
    private float _banner = -1;

    /// <param name="sound">Plays the alert sound (null = silent).</param>
    public ExperimentalAlert(GameContext ctx, Action? sound)
    {
        _ctx = ctx;
        _sound = sound;
        var db = ctx.Db;
        _isExperimental = new bool[db.Count];
        for (int b = 0; b < db.Count; b++) _isExperimental[b] = db[b].Has(UnitCategory.Experimental);
    }

    /// <summary>Banner currently shown (tests / tools).</summary>
    public bool Active => _banner >= 0;
    public string CurrentName => _name;

    /// <summary>Scans the snapshot for newly seen enemy experimentals. Returns how many were new.</summary>
    public int Poll(SimSnapshot snap)
    {
        if ((uint)_ctx.LocalArmy >= (uint)snap.Armies.Length) return 0;
        int found = 0;
        for (int k = 0; k < snap.UnitCount; k++)
        {
            ref var v = ref snap.Units[k];
            if ((uint)v.Bp >= (uint)_isExperimental.Length || !_isExperimental[v.Bp]) continue;
            if ((uint)v.Army >= (uint)snap.Armies.Length || !_ctx.IsEnemy(v.Army)) continue;
            if ((v.Flags & UnitFlags.Dead) != 0) continue;
            if (!_seen.Add(((long)v.Index << 32) | (uint)v.Gen)) continue;
            _extra = found > 0 ? _extra + 1 : 0;
            if (found == 0)
            {
                _name = _ctx.Db[v.Bp].Name;
                _underConstruction = (v.Flags & UnitFlags.UnderConstruction) != 0;
            }
            _pings.Add((v.Pos, 0));
            found++;
        }
        if (found > 0)
        {
            _banner = 0;
            _sound?.Invoke();
        }
        return found;
    }

    /// <summary>Polls and draws the banner and pings (after EndMode3D; also while the HUD is hidden).</summary>
    public void Draw2D(SimSnapshot snap, float dt)
    {
        Poll(snap);
        if (_banner >= 0) { _banner += dt; if (_banner > BannerTime) _banner = -1; }
        DrawPings(dt);
        if (_banner < 0) return;

        float sw = Raylib.GetScreenWidth();
        int fs = Ui.F(20);
        int fs2 = Ui.F(13);
        string text = "WARNING: EXPERIMENTAL DETECTED";
        string sub = (_underConstruction ? $"Enemy {_name} under construction" : $"Enemy {_name} sighted")
                     + (_extra > 0 ? $"  (+{_extra} more)" : "");
        float w = MathF.Max(Ui.Measure(text, fs, true), Ui.Measure(sub, fs2)) + Ui.P(48);
        float h = fs + fs2 + Ui.P(22);
        var r = new Rectangle(MathF.Round((sw - w) / 2), MathF.Round(Raylib.GetScreenHeight() * 0.215f), MathF.Round(w), MathF.Round(h));
        float fade = MathF.Min(1f, MathF.Min(_banner * 6f, (BannerTime - _banner) * 1.5f));
        bool blink = ((int)(_banner * 3f) & 1) == 0 || _banner > 3f;
        Raylib.DrawRectangleRec(r, new Color(58, 24, 4, (int)(225 * fade)));
        Raylib.DrawRectangleLinesEx(r, Ui.P(2), new Color(255, 150, 40, (int)(255 * fade)));
        var tc = blink ? new Color(255, 165, 50, (int)(255 * fade)) : new Color(255, 230, 190, (int)(255 * fade));
        Ui.Text(text, r.X + (r.Width - Ui.Measure(text, fs, true)) / 2, r.Y + Ui.P(8), fs, tc, true);
        Ui.Text(sub, r.X + (r.Width - Ui.Measure(sub, fs2)) / 2, r.Y + Ui.P(12) + fs, fs2, new Color(255, 215, 180, (int)(230 * fade)));
    }

    /// <summary>Three expanding orange rings at each detection point (screen space, while it is on screen).</summary>
    private void DrawPings(float dt)
    {
        var fv = FrameView.Current;
        for (int i = _pings.Count - 1; i >= 0; i--)
        {
            var (pos, age) = _pings[i];
            age += dt;
            if (age > BannerTime) { _pings.RemoveAt(i); continue; }
            _pings[i] = (pos, age);
            if (fv.ScreenW <= 0 || !fv.WorldToScreen(pos, out var s)) continue;
            for (int k = 0; k < 3; k++)
            {
                float t = (age * 0.8f + k / 3f) % 1f;
                float alpha = (1f - t) * MathF.Min(1f, (BannerTime - age) * 0.8f);
                float rad = Ui.P(18) + t * Ui.P(70);
                Raylib.DrawRing(s, rad - MathF.Max(2f, Ui.P(3)), rad, 0, 360, 48, new Color(255, 140, 40, (int)(230 * alpha)));
            }
        }
    }
}
