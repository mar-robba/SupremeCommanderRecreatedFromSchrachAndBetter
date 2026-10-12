using System.Numerics;
using Raylib_cs;
using Rts.Client.Input;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;
using Mode = Rts.Client.Input.SelectionController.CommandMode;

namespace Rts.Client.UI;

/// <summary>
/// Strategic HUD: the global "STRATEGIC LAUNCH DETECTED" banner (every player sees every nuke launch; the alarm sound is
/// played by GameAudio), own-team nuke target markers, launch-mode targeting (launcher range rings + blast radii at the
/// cursor), and the silo/shield bars of the unit panel.
/// </summary>
public sealed class StrategicHud
{
    private const float BannerTime = 7f;
    private const float MarkerTime = 25f;

    private readonly GameContext _ctx;
    private readonly SelectionController _sel;
    private int _lastSerial = -1;
    private float _banner = -1;
    private bool _bannerOwn;
    private readonly List<(Vector2 target, float age)> _markers = new();

    public StrategicHud(GameContext ctx, SelectionController sel)
    {
        _ctx = ctx;
        _sel = sel;
    }

    private void Poll(SimSnapshot snap, float dt)
    {
        if (_lastSerial < 0) _lastSerial = snap.LaunchSerial; // launches before this client started don't alarm
        if (snap.LaunchSerial > _lastSerial)
        {
            foreach (var l in snap.Launches)
            {
                if (l.Serial <= _lastSerial) continue;
                _banner = 0;
                _bannerOwn = !_ctx.IsEnemy(l.Army);
                if (!float.IsNaN(l.Target.X)) _markers.Add((l.Target, 0));
            }
            _lastSerial = snap.LaunchSerial;
        }
        if (_banner >= 0) { _banner += dt; if (_banner > BannerTime) _banner = -1; }
        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            var m = _markers[i];
            m.age += dt;
            if (m.age > MarkerTime) _markers.RemoveAt(i); else _markers[i] = m;
        }
    }

    // ------------------------------------------------------------------ 3D (inside BeginMode3D)

    public void Draw3D(SimSnapshot snap)
    {
        float t = (float)Raylib.GetTime();
        foreach (var (target, age) in _markers)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin(t * 6f);
            GroundRing(target, 6f + 2f * pulse, new Color(255, 60, 40, (int)(160 + 90 * pulse)));
            GroundRing(target, 38f, new Color(255, 120, 40, 140));
        }
        if (_sel.Mode != Mode.Launch) return;
        // Range rings of the selected launchers (tactical; strategic range covers the whole map).
        float mapDiag = MathF.Sqrt(_ctx.Terrain.Width * _ctx.Terrain.Width + _ctx.Terrain.Height * _ctx.Terrain.Height);
        UnitBlueprint? sample = null;
        foreach (var h in _sel.Selected)
        {
            if (!snap.TryGetUnit(h.Index, h.Generation, out var v) || !StrategicOrders.IsLauncher(_ctx, v)) continue;
            var bp = _ctx.Db[v.Bp];
            sample ??= bp;
            float range = bp.Weapons[bp.Silo!.WeaponIndex].Range;
            if (range < mapDiag) GroundRing(new Vector2(v.Pos.X, v.Pos.Z), range, new Color(255, 200, 80, 170), 128);
        }
        if (sample == null || _ctx.PickGround(Raylib.GetMousePosition()) is not Vector3 g) return;
        var p = new Vector2(g.X, g.Z);
        bool ok = StrategicOrders.PickLauncher(_ctx, _sel, p, out var best);
        var lbp = ok ? _ctx.Db[best.Bp] : sample;
        var wb = lbp.Weapons[lbp.Silo!.WeaponIndex];
        var inner = ok ? new Color(255, 70, 50, 230) : new Color(150, 150, 150, 200);
        var outer = ok ? new Color(255, 160, 60, 180) : new Color(120, 120, 120, 150);
        GroundRing(p, MathF.Max(1.5f, wb.DamageRadius), inner, 48);
        if (lbp.Silo.OuterRadius > wb.DamageRadius) GroundRing(p, lbp.Silo.OuterRadius, outer, 72);
        Raylib.DrawLine3D(g + new Vector3(-2, 0.4f, 0), g + new Vector3(2, 0.4f, 0), inner);
        Raylib.DrawLine3D(g + new Vector3(0, 0.4f, -2), g + new Vector3(0, 0.4f, 2), inner);
    }

    /// <summary>Terrain-following circle.</summary>
    private void GroundRing(Vector2 c, float r, Color col, int seg = 64)
    {
        var terrain = _ctx.Terrain;
        Vector3 Pt(int k)
        {
            float a = k * MathF.Tau / seg;
            var q = new Vector2(c.X + MathF.Cos(a) * r, c.Y + MathF.Sin(a) * r);
            return new Vector3(q.X, terrain.GetSurfaceHeight(terrain.Clamp(q)) + 0.35f, q.Y);
        }
        var prev = Pt(0);
        for (int k = 1; k <= seg; k++)
        {
            var cur = Pt(k);
            Raylib.DrawLine3D(prev, cur, col);
            prev = cur;
        }
    }

    // ------------------------------------------------------------------ 2D

    /// <summary>Updates launch tracking and draws the banner (after EndMode3D; works while the HUD is hidden too).</summary>
    public void Draw2D(SimSnapshot snap, float dt)
    {
        Poll(snap, dt);
        if (_banner < 0) return;
        float sw = Raylib.GetScreenWidth();
        int fs = Ui.F(22);
        string text = "STRATEGIC LAUNCH DETECTED";
        string sub = _bannerOwn ? "Friendly nuclear missile launched" : "Enemy nuclear missile inbound";
        int fs2 = Ui.F(12);
        float w = MathF.Max(Ui.Measure(text, fs, true), Ui.Measure(sub, fs2)) + Ui.P(48);
        float h = fs + fs2 + Ui.P(22);
        var r = new Rectangle(MathF.Round((sw - w) / 2), MathF.Round(Raylib.GetScreenHeight() * 0.13f), MathF.Round(w), MathF.Round(h));
        float fade = MathF.Min(1f, MathF.Min(_banner * 6f, (BannerTime - _banner) * 1.5f));
        bool blink = ((int)(_banner * 3f) & 1) == 0 || _banner > 3.5f;
        Raylib.DrawRectangleRec(r, new Color(60, 8, 6, (int)(225 * fade)));
        Raylib.DrawRectangleLinesEx(r, Ui.P(2), new Color(255, 70, 50, (int)(255 * fade)));
        var tc = blink ? new Color(255, 90, 70, (int)(255 * fade)) : new Color(255, 210, 190, (int)(255 * fade));
        Ui.Text(text, r.X + (r.Width - Ui.Measure(text, fs, true)) / 2, r.Y + Ui.P(8), fs, tc, true);
        Ui.Text(sub, r.X + (r.Width - Ui.Measure(sub, fs2)) / 2, r.Y + Ui.P(12) + fs, fs2, new Color(255, 200, 180, (int)(230 * fade)));
    }

    /// <summary>Unit panel extra bars: shield HP/recharge and missile stockpile + build progress. Returns the new y.</summary>
    public static float DrawUnitPanelBars(GameContext ctx, SimSnapshot snap, in UnitView v, UnitBlueprint bp, float x, float y, float w, float h)
    {
        if (bp.Shield != null)
        {
            for (int k = 0; k < snap.ShieldCount; k++)
            {
                ref var s = ref snap.Shields[k];
                if (s.Unit != v.Index || s.Gen != v.Gen) continue;
                if (s.Up) Ui.Bar(new Rectangle(x, y, w, h), s.HpFrac, new Color(90, 190, 255, 255), $"Shield {s.HpFrac * bp.Shield.MaxHealth:0} / {bp.Shield.MaxHealth:0}");
                else Ui.Bar(new Rectangle(x, y, w, h), s.RechargeFrac, new Color(110, 110, 150, 255), $"Shield down - recharging {s.RechargeFrac * 100:0}%");
                y += h + Ui.P(5);
                break;
            }
        }
        if (bp.Silo != null)
        {
            for (int k = 0; k < snap.SiloCount; k++)
            {
                ref var s = ref snap.Silos[k];
                if (s.Unit != v.Index || s.Gen != v.Gen) continue;
                string what = s.Kind switch { SiloKind.Strategic => "Nukes", SiloKind.AntiNuke => "Anti-nukes", _ => "Missiles" };
                bool full = s.Stock >= s.MaxStock;
                string label = full ? $"{what} {s.Stock}/{s.MaxStock}  (full)" : $"{what} {s.Stock}/{s.MaxStock}  next {s.Progress * 100:0}%";
                Ui.Bar(new Rectangle(x, y, w, h), full ? 1f : s.Progress, new Color(235, 150, 60, 255), label);
                // Stock pips under the bar.
                float pip = MathF.Min(Ui.P(10), (w - Ui.P(2) * s.MaxStock) / MathF.Max(1, s.MaxStock));
                for (int i = 0; i < s.MaxStock; i++)
                    Raylib.DrawRectangle((int)(x + i * (pip + Ui.P(2))), (int)(y + h + Ui.P(2)), (int)pip, (int)Ui.P(3),
                        i < s.Stock ? new Color(255, 190, 80, 255) : new Color(70, 60, 50, 255));
                y += h + Ui.P(9);
                break;
            }
        }
        return y;
    }

    /// <summary>Orders panel glyph of the Launch button (a missile).</summary>
    public static void DrawLaunchGlyph(Vector2 c, float s, float t, bool enabled, Color off)
    {
        var col = enabled ? new Color(255, 170, 70, 255) : off;
        Raylib.DrawLineEx(c + new Vector2(0, s), c + new Vector2(0, -s * 0.4f), t * 1.6f, col);
        Raylib.DrawTriangle(c + new Vector2(0, -s), c + new Vector2(-s * 0.35f, -s * 0.35f), c + new Vector2(s * 0.35f, -s * 0.35f), col);
        Raylib.DrawTriangle(c + new Vector2(-t * 0.8f, s * 0.4f), c + new Vector2(-s * 0.5f, s), c + new Vector2(-t * 0.8f, s), col);
        Raylib.DrawTriangle(c + new Vector2(t * 0.8f, s * 0.4f), c + new Vector2(t * 0.8f, s), c + new Vector2(s * 0.5f, s), col);
    }
}
