namespace Rts.Sim.Snapshot;

/// <summary>
/// Live intel/combat status of a unit for the client (<see cref="UnitView.Status"/>): which stealth/cloak effects are
/// active right now (Intel/Stealth*, own and allied units only; enemies with active cloak/stealth are simply not in the
/// snapshot or not blips) and whether an EMP stun is running (Combat/EmpStun).
/// </summary>
[Flags]
public enum UnitStatus : byte
{
    None = 0,
    /// <summary>Hidden from enemy radar (own stealth or inside an allied stealth field). Omni still sees it.</summary>
    RadarStealth = 1,
    /// <summary>Hidden from enemy sonar.</summary>
    SonarStealth = 2,
    /// <summary>Cloaked: hidden from enemy vision unless omni covers it.</summary>
    Cloaked = 4,
    /// <summary>EMP stunned: can't move, fire or build.</summary>
    Stunned = 8,
    /// <summary>Projects an active stealth field (the field radius is the blueprint's Intel.StealthFieldRadius).</summary>
    StealthField = 16,
}
