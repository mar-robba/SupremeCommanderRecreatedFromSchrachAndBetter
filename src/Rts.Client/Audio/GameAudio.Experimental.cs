using Raylib_cs;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Client.Audio;

/// <summary>
/// Experimental-scale sounds: the "Experimental detected" klaxon (requested by <see cref="UI.ExperimentalAlert"/>),
/// a super-heavy boom for very strong non-missile guns (Mavor, T3 heavy artillery: shot and impact) and a huge
/// blast for the big explosions (experimental death blasts, Size ≥ <see cref="HugeBlastSize"/>).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>Explosions at least this big (experimental death blasts: ≈ DeathRadius 10–12) get <see cref="SoundId.ExplosionHuge"/>.</summary>
    public const float HugeBlastSize = 8f;
    /// <summary>Shell weapons at least this strong fire with <see cref="SoundId.SuperHeavyFire"/> (Mavor 4000, heavy artillery 3000).</summary>
    public const float SuperHeavyDamage = 3000f;

    private bool[][]? _superHeavy;
    private double _lastExperimentalAlert = -100;

    /// <summary>Super-heavy gun: a plain shell (not a missile, nuke, bomb, laser...) with at least <see cref="SuperHeavyDamage"/>.</summary>
    public static bool IsSuperHeavy(WeaponBlueprint w) => w.Damage >= SuperHeavyDamage && w.ProjectileVisual == "shell";

    /// <summary>The "Experimental detected" klaxon (at most one per 4 s).</summary>
    public void PlayExperimentalAlert()
    {
        if (!_engine.Ready) return;
        double now = Raylib.GetTime();
        if (now - _lastExperimentalAlert < 4.0) return;
        _lastExperimentalAlert = now;
        PlayUi(SoundId.ExperimentalAlert);
        Say(VoiceIds.ExperimentalDetected); // (11b-1)
    }

    /// <summary>Overrides the regular mapping for super-heavy shots/impacts and huge explosions; null = not handled.</summary>
    private int? HandleExperimentalEffect(in EffectEvent e, bool validWeapon, string vis)
    {
        if (e.Kind is not (EffectKind.MuzzleFlash or EffectKind.Explosion)) return null;
        bool superHeavy = validWeapon && SuperHeavyTable()[e.BlueprintIndex][e.WeaponIndex];
        bool experimental = validWeapon && _ctx.Db[e.BlueprintIndex].Has(UnitCategory.Experimental);
        if (e.Kind == EffectKind.MuzzleFlash)
        {
            if (!superHeavy) return null;
            // Experimental guns a bit deeper; heard (faintly) from far away.
            At(SoundId.SuperHeavyFire, e.Pos, 1.2f, experimental ? 0.92f : 1.1f, minGain: 0.1f);
            return 1;
        }
        bool huge = e.Size >= HugeBlastSize && vis != "nuke";
        if (!huge && !superHeavy) return null;
        float pitch = huge ? Math.Clamp(1.2f - e.Size * 0.025f, 0.85f, 1.05f) : 1.12f;
        At(SoundId.ExplosionHuge, e.Pos, huge ? 1.3f : 1f, pitch, minGain: huge ? 0.1f : 0f);
        return 1;
    }

    private bool[][] SuperHeavyTable()
    {
        if (_superHeavy != null) return _superHeavy;
        var db = _ctx.Db;
        var t = new bool[db.Count][];
        for (int b = 0; b < db.Count; b++) t[b] = db[b].Weapons.Select(IsSuperHeavy).ToArray();
        return _superHeavy = t;
    }
}
