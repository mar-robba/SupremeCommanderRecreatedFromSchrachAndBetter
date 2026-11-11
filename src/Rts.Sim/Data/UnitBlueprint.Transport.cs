using System.Text.Json.Serialization;

namespace Rts.Sim.Data;

/// <summary>Cargo / transport data of a blueprint (see Rts.Sim/Transport and docs/ARCHITECTURE.md "Transports &amp; cargo").</summary>
public sealed partial class UnitBlueprint
{
    /// <summary>Carrier module: this unit carries other units inside (air transports, air staging, Atlantis). null = none.</summary>
    public CargoBlueprint? Cargo;

    /// <summary>Cargo slots this unit takes when carried (SupCom small/medium/large = 1/2/4). -1 = derived from the
    /// unit (see <see cref="CargoSize"/>); 0 = never transportable.</summary>
    public int TransportSize = -1;

    /// <summary>Seconds of flight fuel (SupCom1 aircraft fuel). 0 = derived default for aircraft (see
    /// <see cref="FuelSeconds"/>), negative = unlimited. Out of fuel = <see cref="Transport.TransportSystem.OutOfFuelSpeed"/> speed.</summary>
    public float FuelTime;

    /// <summary>
    /// Slots taken inside a carrier (0 = not transportable). Derived unless <see cref="TransportSize"/> is set:
    /// structures, ships, subs and experimentals 0; land units T1 = 1 (small), T2 = 2 (medium), T3 and the commander
    /// = 4 (large); aircraft (docking at air staging / carriers) T1–T2 = 1, T3 = 2.
    /// </summary>
    [JsonIgnore]
    public int CargoSize
    {
        get
        {
            if (TransportSize >= 0) return TransportSize;
            if (!IsMobile || Has(UnitCategory.Experimental) || Has(UnitCategory.Naval)) return 0; // ships, also the walking ones
            switch (Movement.Layer)
            {
                case MotionLayer.Air: return Tech >= 3 ? 2 : 1;
                case MotionLayer.Land:
                case MotionLayer.Hover:
                case MotionLayer.Amphibious:
                    if (Has(UnitCategory.Commander) || Tech >= 3) return 4;
                    return Tech == 2 ? 2 : 1;
                default: return 0; // ships, subs
            }
        }
    }

    /// <summary>Which carrier layer this unit boards: Land units ride transports, aircraft dock at air carriers.</summary>
    [JsonIgnore]
    public CargoLayer CargoKind => Movement.Layer == MotionLayer.Air ? CargoLayer.Air : CargoLayer.Land;

    /// <summary>Flight fuel in seconds (0 = unlimited). Default for non-experimental aircraft: 400 / 600 / 800 s by tech.</summary>
    [JsonIgnore]
    public float FuelSeconds =>
        FuelTime > 0 ? FuelTime
        : FuelTime < 0 || Movement.Layer != MotionLayer.Air || !IsMobile || Has(UnitCategory.Experimental) ? 0
        : Tech >= 3 ? 800 : Tech == 2 ? 600 : 400;

    /// <summary>VTOL flight (hovers, lands to load/unload): air units that carry land units.</summary>
    [JsonIgnore]
    public bool IsVtol => Cargo != null && Cargo.Layer == CargoLayer.Land && Movement.Layer == MotionLayer.Air;
}

/// <summary>What a carrier holds: land units (transports) or aircraft (air staging, aircraft carriers).</summary>
public enum CargoLayer : byte { Land, Air }

/// <summary>
/// Carrier module. Capacity is in slots (see <see cref="UnitBlueprint.CargoSize"/>). Land carriers (air transports)
/// land to load/unload; air carriers (staging, Atlantis) dock aircraft, repair and refuel them.
/// </summary>
public sealed class CargoBlueprint
{
    public CargoLayer Layer = CargoLayer.Land;
    /// <summary>Total slots.</summary>
    public int Capacity = 6;
    /// <summary>Boarding distance beyond both radii (land cargo) / docking distance beyond the carrier radius (air).</summary>
    public float LoadRange = 3f;
    /// <summary>Docked units regain this fraction of their MaxHealth per second (free, like SupCom air staging).</summary>
    public float RepairRate;
    /// <summary>Docked aircraft regain this fraction of their fuel per second.</summary>
    public float RefuelRate = 0.2f;
    /// <summary>Aircraft stay docked after repair/refuel until an Unload ("launch") order (aircraft carriers).
    /// false = they take off as soon as they are repaired and refuelled (air staging).</summary>
    public bool KeepDocked;
    /// <summary>VTOL transports: height above the surface while landed for loading/unloading.</summary>
    public float HoverHeight = 1.5f;
}
