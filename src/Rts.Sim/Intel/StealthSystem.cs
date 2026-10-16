using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Snapshot;
using Rts.Sim.World;

namespace Rts.Sim.Systems;

/// <summary>
/// Stealth, cloak and stealth fields (SupCom 1 Cybran), data-driven from <see cref="IntelBlueprint"/>
/// (RadarStealth, SonarStealth, Cloak, StealthFieldRadius; commander enhancements set them on their variants).
/// <list type="bullet">
/// <item>Radar stealth: enemy radar doesn't detect the unit. Sonar stealth: enemy sonar doesn't (a submerged unit
/// then needs enemy omni even in vision). Cloak: enemy vision doesn't see it (radar still shows a blip unless it is
/// radar-stealthed too). Enemy omni beats all of them.</item>
/// <item>A stealth field (radius &gt; 0) gives radar + sonar stealth to every unit of the generator's TEAM whose
/// center is within the radius, the generator included.</item>
/// <item>An effect is active while its unit is complete, on the map (not carried), not Paused (the intel toggle) and,
/// if the blueprint has energy upkeep (<c>Economy.MaintenanceEnergy</c>), while the army's energy efficiency is at
/// least <see cref="MinPowerEfficiency"/> (an army-wide energy stall; a mass stall doesn't matter).
/// Upkeep is paid by EconomySystem like any upkeep: complete, non-paused units only.</item>
/// </list>
/// <see cref="Run"/> (sequential, first thing of IntelSystem.Run, after the spatial grid rebuild) recomputes the
/// per-slot bits; <see cref="IntelService"/> reads them through <see cref="IntelService.StealthMask"/>, which is null
/// while no unit has an active effect (zero cost for the intel queries). Without any stealth blueprint in the
/// database the update is a single check.
/// </summary>
public static class StealthSystem
{
    /// <summary>Bits of <see cref="StealthState.Mask"/> (= <see cref="UnitStatus"/> values).</summary>
    public const byte RadarBit = (byte)UnitStatus.RadarStealth, SonarBit = (byte)UnitStatus.SonarStealth,
        CloakBit = (byte)UnitStatus.Cloaked, FieldBit = (byte)UnitStatus.StealthField;
    private const byte StatusBits = RadarBit | SonarBit | CloakBit | FieldBit;

    /// <summary>Army energy efficiency needed to power stealth/cloak/fields with upkeep (same rule as shields).</summary>
    public const float MinPowerEfficiency = 0.95f;

    /// <summary>Recomputes the active stealth bits of every unit (sequential, deterministic).</summary>
    public static void Run(SimWorld w)
    {
        var intel = w.Intel;
        var s = StealthState.Of(w);
        if (!s.EnsureBlueprints(w.Blueprints))
        {
            intel.StealthMask = null;
            return;
        }
        var mask = s.Mask;
        if (s.ActiveCount > 0) Array.Clear(mask);
        var self = s.SelfBits!;
        var fields = s.Fields ??= new List<int>();
        fields.Clear();
        var u = w.Units;
        int active = 0;
        for (int k = 0; k < u.AliveCount; k++)
        {
            int i = u.AliveList[k];
            byte bits = self[u.Bp[i]];
            if (bits == 0 || !IsPowered(w, i)) continue;
            if (mask[i] == 0) active++;
            mask[i] |= bits;
            if ((bits & FieldBit) != 0) fields.Add(i);
        }
        if (fields.Count > 0)
        {
            var radius = s.FieldRadius!;
            foreach (int g in fields)
            {
                float r = radius[u.Bp[g]];
                var v = new FieldVisitor
                {
                    Units = u, Intel = intel, Mask = mask, Center = u.Pos[g], R2 = r * r, Team = intel.TeamOf(u.Army[g]),
                };
                w.Grid.ForEachInRadius(u.Pos[g], r, ref v);
                active += v.Added;
            }
        }
        s.ActiveCount = active;
        intel.StealthMask = active > 0 ? mask : null;
    }

    /// <summary>Can unit <paramref name="i"/> (alive, in the AliveList) power its stealth/cloak/field right now?</summary>
    public static bool IsPowered(SimWorld w, int i)
    {
        var u = w.Units;
        if ((u.Flags[i] & (UnitFlags.Dead | UnitFlags.UnderConstruction | UnitFlags.Paused | UnitFlags.Carried)) != 0) return false;
        return w.BpOf(i).Economy.MaintenanceEnergy <= 0 || w.Armies[u.Army[i]].Eco.EnergyEfficiency >= MinPowerEfficiency;
    }

    /// <summary>Active stealth bits of a unit slot this tick (0 = none).</summary>
    public static byte BitsOf(SimWorld w, int i) => w.Intel.StealthMask is { } m ? m[i] : (byte)0;

    /// <summary>
    /// IntelService.IsVisible hook (unit already in team <paramref name="ti"/>'s vision at bitset cell
    /// <paramref name="cell"/>): cloaked, or submerged with sonar stealth, and no omni coverage → not seen.
    /// </summary>
    internal static bool HiddenFromVision(SimWorld w, TeamIntel ti, int cell, byte bits, int i)
    {
        if (ti.OmniUsed && IntelService.Test(ti.Omni, cell)) return false;
        if ((bits & CloakBit) != 0) return true;
        return (bits & SonarBit) != 0 && OrderSystem.LayerOf(w, i) == LayerMask.Sub;
    }

    /// <summary>IntelService.IsDetected hook: radar/sonar coverage doesn't count against radar/sonar stealth.</summary>
    internal static void Jam(byte bits, ref bool radar, ref bool sonar)
    {
        if ((bits & RadarBit) != 0) radar = false;
        if ((bits & SonarBit) != 0) sonar = false;
    }

    /// <summary>Ghost hook (IntelSystem.UpdateGhosts): is enemy unit <paramref name="i"/> hidden from team
    /// <paramref name="ti"/>'s vision by its cloak (no ghost recorded/refreshed; a ghost in sight is forgotten)?</summary>
    internal static bool HiddenFromTeam(SimWorld w, TeamIntel ti, int i)
    {
        var m = w.Intel.StealthMask;
        if (m == null || m[i] == 0) return false;
        return HiddenFromVision(w, ti, w.Intel.BitIndex(w.Units.Pos[i]), m[i], i);
    }

    /// <summary>
    /// <see cref="UnitView.Status"/> of unit <paramref name="i"/> for the snapshot of <paramref name="viewArmy"/>:
    /// stealth bits only for own/allied units (all units when viewArmy &lt; 0), the EMP stun bit for everyone.
    /// </summary>
    public static UnitStatus StatusOf(SimWorld w, int i, int viewArmy)
    {
        var st = Combat.EmpStun.IsStunned(w, i) ? UnitStatus.Stunned : UnitStatus.None;
        byte bits = BitsOf(w, i);
        if (bits != 0 && ((uint)viewArmy >= (uint)w.Armies.Length || !w.IsEnemy(viewArmy, w.Units.Army[i])))
            st |= (UnitStatus)(bits & StatusBits);
        return st;
    }

    /// <summary>Gives radar + sonar stealth to the generator's team units within the field radius.</summary>
    private struct FieldVisitor : IUnitVisitor
    {
        public UnitStore Units;
        public IntelService Intel;
        public byte[] Mask;
        public Vector2 Center;
        public float R2;
        public int Team;
        public int Added;

        public bool Visit(int j)
        {
            if (Vector2.DistanceSquared(Units.Pos[j], Center) > R2) return true;
            if ((Units.Flags[j] & UnitFlags.Dead) != 0 || Intel.TeamOf(Units.Army[j]) != Team) return true;
            if (Mask[j] == 0) Added++;
            Mask[j] |= RadarBit | SonarBit;
            return true;
        }
    }
}
