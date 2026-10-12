using System.Numerics;
using Raylib_cs;
using Rts.Client.UI;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Audio;

/// <summary>Volume settings and audio command-line options.</summary>
public sealed class AudioSettings
{
    /// <summary>False with --nosound: the audio device is never opened.</summary>
    public bool Enabled = true;
    /// <summary>--sound: keep audio on in automated screenshot runs (off by default there).</summary>
    public bool ForceOn;
    public float Master = 0.8f;
    public float Sfx = 1f;
    public float Music = 0.45f;
    public float Ambient = 0.55f;
    public bool MusicOn = true;
    public bool Muted;

    /// <summary>Parses one audio CLI option (--nosound, --sound, --volume F, --sfx-volume F, --music-volume F,
    /// --nomusic). Returns false if the argument is not an audio option.</summary>
    public static bool ParseArg(string arg, Func<string> next, AudioSettings s)
    {
        static float F(string v) => Math.Clamp(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture), 0f, 1f);
        switch (arg)
        {
            case "--nosound": s.Enabled = false; return true;
            case "--sound": s.ForceOn = true; return true;
            case "--volume": s.Master = F(next()); return true;
            case "--sfx-volume": s.Sfx = F(next()); return true;
            case "--music-volume": s.Music = F(next()); return true;
            case "--nomusic": s.MusicOn = false; return true;
            default: return false;
        }
    }
}

/// <summary>
/// Game sound director. Each frame it turns new snapshot effects into positional one-shots (weapon fire per
/// ProjectileVisual / weapon kind, impacts, explosions, deaths, building collapses, construction), raises
/// alerts (unit / commander under attack, mass / energy stall, unit ready), drives the continuous layers of
/// <see cref="AmbientSynth"/> (wind, generative music, reclaim and build hums) and plays UI sounds.
/// <para>Mixing: the listener is the camera focus. Gain falls off with ground distance over a radius that grows
/// with zoom; the strategic view ducks the whole SFX mix; pan follows the camera's right axis. Requests of one
/// sound in a frame are merged (the loudest few play, slightly boosted) and each sound has a voice limit.</para>
/// Degrades to a silent no-op without an audio device or with --nosound. Keys: M (nothing selected) or
/// Ctrl+M mute, Ctrl+Shift+M music on/off.
/// <para>Device, sound bank and ambient stream belong to the app-wide <see cref="AudioSystem"/> (shared by every
/// game and the menus); this class is per game.</para>
/// </summary>
public sealed class GameAudio : IDisposable
{
    private readonly GameContext _ctx;
    public readonly AudioSettings Settings;
    private readonly AudioSystem _sys;
    private readonly bool _ownsSystem;
    private readonly AudioEngine _engine;
    private readonly AmbientSynth _ambient;
    private readonly SoundId[][] _fire;
    private readonly string[][] _visual;
    private readonly float[][] _damageGain;
    private readonly Random _rand = new();

    private struct Req
    {
        public SoundId Id;
        public float Vol, Pan, Pitch;
    }

    private readonly List<Req> _reqs = new();
    private readonly int[] _reqTotal = new int[SoundBank.Count];
    private readonly int[] _reqPlayed = new int[SoundBank.Count];
    private int _lastTick = -1;
    private HashSet<int> _bombs = new(), _bombsNext = new();
    private double _lastAlert = -100, _lastCmdAlert = -100, _lastMassWarn = -100, _lastEnergyWarn = -100, _lastReady = -100;
    private int _massLowTicks, _energyLowTicks;
    private float _combatRate, _startFade;
    private readonly List<(string text, Color color, double until)> _toasts = new();

    /// <summary>True when the audio device is open (sounds may still be loading for a moment).</summary>
    public bool Active => _engine.Ready;

    /// <summary>One-shots started since startup (stats / tests).</summary>
    public int PlayedCount { get; private set; }

    /// <summary>Standalone: creates (and disposes) its own <see cref="AudioSystem"/>.</summary>
    public GameAudio(GameContext ctx, AudioSettings settings, bool automated = false)
        : this(ctx, new AudioSystem(settings, automated), ownsSystem: true) { }

    /// <summary>Game audio on the shared app-wide <see cref="AudioSystem"/>.</summary>
    public GameAudio(GameContext ctx, AudioSystem system, bool ownsSystem = false)
    {
        _ctx = ctx;
        _sys = system;
        _ownsSystem = ownsSystem;
        _engine = system.Engine;
        _ambient = system.Ambient;
        Settings = system.Settings;
        var db = ctx.Db;
        _fire = new SoundId[db.Count][];
        _visual = new string[db.Count][];
        _damageGain = new float[db.Count][];
        for (int b = 0; b < db.Count; b++)
        {
            var ws = db[b].Weapons;
            _fire[b] = ws.Select(FireSound).ToArray();
            _visual[b] = ws.Select(w => w.ProjectileVisual).ToArray();
            _damageGain[b] = ws.Select(w => Math.Clamp(0.75f + MathF.Sqrt(MathF.Max(0, w.Damage)) / 25f, 0.75f, 1.15f)).ToArray();
        }
    }

    /// <summary>Fire sound of a weapon: visual first, then kind (ballistic = artillery) and damage class.</summary>
    public static SoundId FireSound(WeaponBlueprint w) => w.ProjectileVisual switch
    {
        "laser" => SoundId.Laser,
        "missile" => SoundId.MissileLaunch,
        "flak" => SoundId.FlakFire,
        "bomb" => SoundId.BombWhistle,
        _ => w.Ballistic ? SoundId.Artillery : w.Damage >= 60 ? SoundId.CannonHeavy : w.Damage >= 8 ? SoundId.Cannon : SoundId.GunLight,
    };

    // ------------------------------------------------------------------ per frame

    /// <summary>Call once per frame after the snapshot interpolator update.</summary>
    public void Update(float dt, SnapshotInterpolator interp, int selectedCount, bool inputBlocked)
    {
        HandleKeys(selectedCount, inputBlocked);
        var ui = UiSounds.Drain();
        _sys.Update(dt);
        if (!_engine.Ready) return;
        _startFade = MathF.Min(1f, _startFade + dt / 2.5f);

        var cur = interp.Current;
        bool paused = _ctx.Runner.Paused;
        int combat = 0;
        foreach (var e in interp.NewEffects) combat += HandleEffect(e);
        if (cur.Tick != _lastTick)
        {
            ScanBombs(cur);
            CheckUnderAttack(interp.Prev, cur);
            CheckResources(cur);
            _lastTick = cur.Tick;
        }
        // Battle intensity (audible combat events per second) brightens the music.
        _combatRate += (combat / MathF.Max(dt, 1e-3f) - _combatRate) * MathF.Min(1f, dt / 3f);

        if (ui.click > 0) PlayUi(SoundId.UiClick);
        if (ui.confirm > 0) PlayUi(SoundId.UiConfirm);
        if (ui.error > 0) PlayUi(SoundId.UiError);
        Flush();

        UpdateLoops(cur, paused);
        float zoomOut = SmoothStep(40f, 420f, _ctx.Camera.Distance);
        _ambient.WindGain = Settings.Ambient * (0.12f + 0.3f * zoomOut) * _startFade;
        _ambient.MusicGain = Settings.MusicOn ? Settings.Music * _sys.MusicFade : 0f;
        _ambient.Tension = Math.Clamp(_combatRate / 25f, 0f, 1f);
        _engine.PumpStream(_ambient);
    }

    private void HandleKeys(int selectedCount, bool inputBlocked)
    {
        if (!_engine.Ready || inputBlocked || !Raylib.IsKeyPressed(KeyboardKey.M)) return;
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (ctrl && shift)
        {
            Settings.MusicOn = !Settings.MusicOn;
            Toast(Settings.MusicOn ? "Music on" : "Music off", Ui.TextColor, 1.5);
        }
        else if (ctrl || selectedCount == 0)
        {
            Settings.Muted = !Settings.Muted;
            Toast(Settings.Muted ? "Sound muted (M)" : "Sound on", Ui.TextColor, 1.5);
        }
    }

    // ------------------------------------------------------------------ effects

    /// <summary>Queues the sound for one sim effect. Returns 1 if it counts as audible combat.</summary>
    private int HandleEffect(in EffectEvent e)
    {
        var db = _ctx.Db;
        bool validBp = (uint)e.BlueprintIndex < (uint)db.Count;
        bool validWeapon = validBp && (uint)e.WeaponIndex < (uint)_fire[e.BlueprintIndex].Length;
        string vis = validWeapon ? _visual[e.BlueprintIndex][e.WeaponIndex] : "shell";
        switch (e.Kind)
        {
            case EffectKind.MuzzleFlash:
            {
                if (!validWeapon) return 0;
                var id = _fire[e.BlueprintIndex][e.WeaponIndex];
                if (id == SoundId.BombWhistle) return 0; // bombs: see ScanBombs
                return At(id, e.Pos, _damageGain[e.BlueprintIndex][e.WeaponIndex]) ? 1 : 0;
            }
            case EffectKind.Impact:
                return At(vis == "laser" ? SoundId.ImpactLaser : SoundId.Impact, e.Pos, 1f) ? 1 : 0;
            case EffectKind.Explosion:
            {
                var id = vis switch { "flak" => SoundId.FlakBurst, "bomb" => SoundId.ExplosionLarge, _ => e.Size >= 2.5f ? SoundId.ExplosionLarge : SoundId.ExplosionSmall };
                float pitch = id == SoundId.ExplosionSmall ? Math.Clamp(1.25f - e.Size * 0.15f, 0.8f, 1.2f) : 1f;
                return At(id, e.Pos, 1f, pitch) ? 1 : 0;
            }
            case EffectKind.UnitDeath:
            {
                var bp = validBp ? db[e.BlueprintIndex] : null;
                SoundId id = bp == null ? SoundId.ExplosionLarge
                    : bp.IsStructure ? SoundId.BuildingCollapse
                    : bp.Movement.Layer == MotionLayer.Air ? SoundId.AirDeath
                    : e.Size >= 3f ? SoundId.ExplosionLarge : SoundId.UnitDeath;
                float pitch = bp != null && !bp.IsStructure ? Math.Clamp(1.2f - e.Size * 0.12f, 0.75f, 1.15f) : 1f;
                return At(id, e.Pos, 1f, pitch) ? 1 : 0;
            }
            case EffectKind.BuildStarted:
                At(SoundId.BuildStart, e.Pos, e.Army == _ctx.LocalArmy ? 1f : 0.6f);
                return 0;
            case EffectKind.BuildComplete:
            {
                bool own = e.Army == _ctx.LocalArmy;
                bool mobile = validBp && db[e.BlueprintIndex].IsMobile;
                if (own && mobile)
                {
                    double now = Raylib.GetTime();
                    if (now - _lastReady > 1.2) { PlayUi(SoundId.UnitReady); _lastReady = now; }
                }
                else At(SoundId.BuildComplete, e.Pos, own ? 1f : 0.6f, 1f, minGain: own ? 0.35f : 0f);
                return 0;
            }
            case EffectKind.Reclaim:
                At(SoundId.ReclaimDone, e.Pos, 1f);
                return 0;
            case EffectKind.Nuke:
                // Deep boom heard map-wide: a slowed-down large explosion layered with a building collapse.
                At(SoundId.ExplosionLarge, e.Pos, 1f, 0.45f, minGain: 0.8f);
                At(SoundId.BuildingCollapse, e.Pos, 1f, 0.55f, minGain: 0.6f);
                At(SoundId.ExplosionSmall, e.Pos, 0.8f, 0.6f, minGain: 0.5f);
                return 1;
        }
        return 0;
    }

    /// <summary>New bomb projectiles whistle as they fall (bombs have no muzzle flash).</summary>
    private void ScanBombs(SimSnapshot cur)
    {
        _bombsNext.Clear();
        for (int i = 0; i < cur.ProjectileCount; i++)
        {
            ref var p = ref cur.Projectiles[i];
            if ((uint)p.SourceBp >= (uint)_fire.Length || (uint)p.WeaponIndex >= (uint)_fire[p.SourceBp].Length) continue;
            if (_fire[p.SourceBp][p.WeaponIndex] != SoundId.BombWhistle) continue;
            _bombsNext.Add(p.Id);
            if (!_bombs.Contains(p.Id)) At(SoundId.BombWhistle, p.Pos, 1f);
        }
        (_bombs, _bombsNext) = (_bombsNext, _bombs);
    }

    // ------------------------------------------------------------------ alerts

    private void CheckUnderAttack(SimSnapshot? prev, SimSnapshot cur)
    {
        if (prev == null) return;
        double now = Raylib.GetTime();
        bool any = now - _lastAlert > 25, cmd = now - _lastCmdAlert > 20;
        if (!any && !cmd) return;
        for (int k = 0; k < cur.UnitCount; k++)
        {
            ref var v = ref cur.Units[k];
            if (v.Army != _ctx.LocalArmy) continue;
            if (!prev.TryGetUnit(v.Index, v.Gen, out var pv) || pv.HealthFrac - v.HealthFrac < 0.002f) continue;
            if (_ctx.Db[v.Bp].Has(UnitCategory.Commander))
            {
                if (!cmd) continue;
                PlayUi(SoundId.CommanderAlert);
                Toast("Commander under attack!", new Color(255, 110, 90, 255), 3);
                _lastCmdAlert = _lastAlert = now;
                return;
            }
            if (any && !OnScreen(v.Pos))
            {
                PlayUi(SoundId.UnderAttack);
                Toast("Units under attack", new Color(255, 170, 90, 255), 2.5);
                _lastAlert = now;
                return;
            }
        }
    }

    private void CheckResources(SimSnapshot cur)
    {
        if ((uint)_ctx.LocalArmy >= (uint)cur.Armies.Length) return;
        var a = cur.Armies[_ctx.LocalArmy];
        _massLowTicks = a.MassEfficiency < 0.95f && a.MassDemand > 0.01f ? _massLowTicks + 1 : 0;
        _energyLowTicks = a.EnergyEfficiency < 0.95f && a.EnergyDemand > 0.01f ? _energyLowTicks + 1 : 0;
        double now = Raylib.GetTime();
        if (_massLowTicks >= 15 && now - _lastMassWarn > 30)
        {
            PlayUi(SoundId.WarnMass);
            Toast("Low mass", Ui.MassColor, 2.5);
            _lastMassWarn = now;
        }
        else if (_energyLowTicks >= 15 && now - _lastEnergyWarn > 30)
        {
            PlayUi(SoundId.WarnEnergy);
            Toast("Low energy", Ui.EnergyColor, 2.5);
            _lastEnergyWarn = now;
        }
    }

    private bool OnScreen(Vector3 p)
    {
        var fv = Rendering.FrameView.Current;
        if (fv.ScreenW <= 0 || !fv.WorldToScreen(p, out var s)) return false;
        return s.X >= 0 && s.Y >= 0 && s.X <= fv.ScreenW && s.Y <= fv.ScreenH;
    }

    // ------------------------------------------------------------------ loops

    private void UpdateLoops(SimSnapshot cur, bool paused)
    {
        float rg = 0, rp = 0, bg = 0, bpan = 0;
        if (!paused)
        {
            for (int k = 0; k < cur.UnitCount; k++)
            {
                ref var v = ref cur.Units[k];
                if ((v.Flags & UnitFlags.Building) == 0) continue;
                if (!Spatial(v.Pos, 0.7f, out float g, out float p)) continue;
                bool reclaim = v.OrderCount > 0 && cur.Orders[v.OrderStart].Type == OrderType.Reclaim;
                if (reclaim) { rg += g; rp += p * g; }
                else
                {
                    float w = _ctx.Db[v.Bp].IsStructure ? 0.3f : 1f;
                    bg += g * w;
                    bpan += p * g * w;
                }
            }
        }
        float sfx = Settings.Sfx;
        _ambient.ReclaimGain = MathF.Min(1f, rg) * 0.55f * sfx;
        _ambient.ReclaimPan = rg > 1e-4f ? rp / rg : 0;
        _ambient.BuildGain = MathF.Min(1f, bg * 0.7f) * 0.4f * sfx;
        _ambient.BuildPan = bg > 1e-4f ? bpan / bg : 0;
    }

    // ------------------------------------------------------------------ mixing

    private static float SmoothStep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Ground-distance attenuation around the camera focus, zoom ducking and stereo pan.</summary>
    private bool Spatial(Vector3 pos, float range, out float gain, out float pan)
    {
        var cam = _ctx.Camera;
        float camDist = cam.Distance;
        float dx = pos.X - cam.Focus.X, dz = pos.Z - cam.Focus.Z;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float radius = (45f + camDist * 1.4f) * range;
        float near = radius * 0.3f;
        float g = dist <= near ? 1f : MathF.Max(0f, 1f - (dist - near) / (radius - near));
        g *= g;
        float zoom = 1f - 0.7f * SmoothStep(70f, 450f, camDist);
        gain = g * zoom;

        var fwd = cam.Camera.Target - cam.Camera.Position;
        var right = new Vector2(-fwd.Z, fwd.X);
        float rl = right.Length();
        right = rl > 1e-4f ? right / rl : Vector2.UnitX;
        float aspect = Raylib.GetScreenWidth() / (float)Math.Max(1, Raylib.GetScreenHeight());
        float halfW = camDist * MathF.Tan(cam.Camera.FovY * MathF.PI / 360f) * aspect;
        pan = Math.Clamp((dx * right.X + dz * right.Y) / MathF.Max(8f, halfW * 1.1f), -1f, 1f) * 0.8f;
        return gain > 0.008f;
    }

    /// <summary>Queues a positional sound. Returns true if it is audible.</summary>
    private bool At(SoundId id, Vector3 pos, float vol, float pitch = 1f, float minGain = 0f)
    {
        var info = SoundBank.Info(id);
        Spatial(pos, info.Range, out float g, out float pan);
        g = MathF.Max(g, minGain);
        if (g <= 0.008f) return false;
        float p = pitch * (0.94f + 0.12f * (float)_rand.NextDouble()) * (1f - 0.05f * (1f - g));
        _reqs.Add(new Req { Id = id, Vol = vol * g, Pan = pan, Pitch = p });
        return true;
    }

    /// <summary>Queues a non-positional (interface) sound.</summary>
    private void PlayUi(SoundId id) => _reqs.Add(new Req { Id = id, Vol = 1f, Pan = 0, Pitch = 1f });

    /// <summary>Plays this frame's requests: loudest first, at most 2 per sound per frame (1 for UI), the
    /// loudest of a sound boosted a little when many were merged, at most 12 new voices per frame.</summary>
    private void Flush()
    {
        if (_reqs.Count == 0) return;
        _reqs.Sort((a, b) => b.Vol.CompareTo(a.Vol));
        Array.Clear(_reqTotal);
        Array.Clear(_reqPlayed);
        foreach (var r in _reqs) _reqTotal[(int)r.Id]++;
        int played = 0;
        foreach (var r in _reqs)
        {
            int id = (int)r.Id;
            var info = SoundBank.Info(r.Id);
            if (_reqPlayed[id] >= (info.Ui ? 1 : 2) || played >= 12) continue;
            float boost = _reqPlayed[id] == 0 ? MathF.Min(1.5f, 1f + 0.1f * (_reqTotal[id] - 1)) : 1f;
            float vol = r.Vol * boost * info.Gain * (info.Ui ? 1f : Settings.Sfx);
            if (_engine.Play(r.Id, vol, r.Pan, r.Pitch)) { _reqPlayed[id]++; played++; PlayedCount++; }
        }
        _reqs.Clear();
    }

    // ------------------------------------------------------------------ overlay

    private void Toast(string text, Color color, double seconds)
    {
        double now = Raylib.GetTime();
        _toasts.RemoveAll(t => t.text == text);
        _toasts.Add((text, color, now + seconds));
        if (_toasts.Count > 3) _toasts.RemoveAt(0);
    }

    /// <summary>Short notifications (mute state, alerts) under the top bar. Call after the HUD.</summary>
    public void DrawOverlay()
    {
        if (_toasts.Count == 0) return;
        double now = Raylib.GetTime();
        _toasts.RemoveAll(t => t.until < now);
        int fs = Ui.F(14);
        float y = Ui.P(30) + Ui.P(44);
        foreach (var (text, color, until) in _toasts)
        {
            float a = (float)Math.Clamp((until - now) / 0.4, 0, 1);
            float w = Ui.Measure(text, fs, true) + Ui.P(24);
            var r = new Rectangle(MathF.Round((Ui.Sw - w) * 0.5f), y, MathF.Round(w), fs + Ui.P(10));
            Raylib.DrawRectangleRec(r, new Color((byte)10, (byte)14, (byte)20, (byte)(200 * a)));
            Raylib.DrawRectangleLinesEx(r, 1, ColorUtil.WithAlpha(color, (byte)(170 * a)));
            Ui.Text(text, r.X + Ui.P(12), r.Y + Ui.P(5) - 1, fs, ColorUtil.WithAlpha(color, (byte)(255 * a)), true);
            y += r.Height + Ui.P(4);
        }
    }

    public void Dispose()
    {
        if (_engine.Ready) Console.WriteLine($"Audio: {PlayedCount} one-shots played");
        // Shared system: silence this game's hums (the menus keep the stream going with music only).
        _ambient.ReclaimGain = _ambient.BuildGain = 0;
        if (_ownsSystem) _sys.Dispose();
    }
}
