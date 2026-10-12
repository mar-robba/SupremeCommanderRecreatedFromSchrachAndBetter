using System.Numerics;
using System.Text.Json.Serialization;

namespace Rts.Sim.Data;

/// <summary>How a unit moves. Structures use None.</summary>
public enum MotionLayer { None, Land, Hover, Amphibious, Water, Sub, Air }

/// <summary>Layers a unit can currently occupy / a weapon can target.</summary>
[Flags]
public enum LayerMask : byte { None = 0, Land = 1, Water = 2, Sub = 4, Air = 8, All = 15 }

/// <summary>
/// Gameplay categories, used for targeting priorities, build lists, AI and UI.
/// In JSON, list them as strings: "Categories": ["Mobile", "Land", "Tech1", "DirectFire"].
/// </summary>
[Flags]
public enum UnitCategory : ulong
{
    None = 0,
    Structure = 1UL << 0,
    Mobile = 1UL << 1,
    Land = 1UL << 2,
    Air = 1UL << 3,
    Naval = 1UL << 4,
    Commander = 1UL << 5,
    Engineer = 1UL << 6,
    Factory = 1UL << 7,
    MassExtractor = 1UL << 8,
    MassFabricator = 1UL << 9,
    PowerGenerator = 1UL << 10,
    MassStorage = 1UL << 11,
    EnergyStorage = 1UL << 12,
    Defense = 1UL << 13,
    DirectFire = 1UL << 14,
    IndirectFire = 1UL << 15,
    AntiAir = 1UL << 16,
    AntiNaval = 1UL << 17,
    Scout = 1UL << 18,
    Radar = 1UL << 19,
    Sonar = 1UL << 20,
    Shield = 1UL << 21,
    Wall = 1UL << 22,
    Bomber = 1UL << 23,
    Fighter = 1UL << 24,
    Gunship = 1UL << 25,
    Transport = 1UL << 26,
    Tech1 = 1UL << 27,
    Tech2 = 1UL << 28,
    Tech3 = 1UL << 29,
    Experimental = 1UL << 30,
    Economy = 1UL << 31,
    Intel = 1UL << 32,
    Artillery = 1UL << 33,
    Strategic = 1UL << 34,
    HydroCarbon = 1UL << 35,
}

/// <summary>
/// Static definition of a unit type, loaded from data/blueprints/units/*.json (like SupCom .bp files).
/// Blueprints are immutable after loading and may be read from any thread.
/// Units: distances in world units (1 unit ~ 1 SupCom "ogrid"), time in seconds, rates per second.
/// </summary>
public sealed partial class UnitBlueprint
{
    /// <summary>Unique string id, e.g. "uef_t1_tank". Also the file name.</summary>
    public string Id = "";
    /// <summary>Runtime numeric index assigned by BlueprintDb (position in BlueprintDb.All).</summary>
    [JsonIgnore] public int Index;

    public string Name = "";
    public string Description = "";
    public string Faction = "uef";
    public int Tech = 1;
    public string[] Categories = [];
    [JsonIgnore] public UnitCategory CategoryFlags;

    public float MaxHealth = 100;
    public float RegenRate;

    /// <summary>Collision/selection radius of mobile units (and structures, for targeting).</summary>
    public float Radius = 0.5f;
    /// <summary>Structure footprint in grid cells (blocks pathing). Ignored for mobile units.</summary>
    public int FootprintX = 1;
    public int FootprintZ = 1;

    public EconomyBlueprint Economy = new();
    public MovementBlueprint Movement = new();
    public IntelBlueprint Intel = new();
    public WeaponBlueprint[] Weapons = [];
    public VisualBlueprint Visual = new();

    /// <summary>Blueprint ids this unit can build (engineers: structures, factories: units).</summary>
    public string[] Builds = [];
    [JsonIgnore] public int[] BuildIndices = [];
    /// <summary>
    /// Builder ids that can also build this unit: at load it is appended to each builder's BuildIndices (after the
    /// builder's own Builds, in blueprint load order). Lets a new unit add itself to existing build lists without
    /// editing the builder's file. Unknown builder ids are ignored, so a unit may name builders added by other modules.
    /// </summary>
    public string[] BuiltBy = [];

    /// <summary>Id of the blueprint this structure upgrades into in place (SupCom T1→T2→T3 factories/mexes). "" = none.</summary>
    public string UpgradesTo = "";
    /// <summary>Resolved UpgradesTo (-1 = none) and its inverse (the blueprint that upgrades into this one, -1 = none).</summary>
    [JsonIgnore] public int UpgradesToIndex = -1;
    [JsonIgnore] public int UpgradesFromIndex = -1;

    /// <summary>Fraction of mass cost left as reclaimable wreck on death (SupCom ~0.81 for most units).</summary>
    public float WreckMassFraction = 0.81f;

    [JsonIgnore] public bool IsStructure => (CategoryFlags & UnitCategory.Structure) != 0;
    [JsonIgnore] public bool IsMobile => !IsStructure;
    public bool Has(UnitCategory c) => (CategoryFlags & c) == c;
    public bool HasAny(UnitCategory c) => (CategoryFlags & c) != 0;
}

public sealed class EconomyBlueprint
{
    public float BuildCostMass;
    public float BuildCostEnergy;
    /// <summary>Work units needed. Seconds to build = BuildTime / builder BuildRate.</summary>
    public float BuildTime = 1;
    /// <summary>Build power of this unit as a builder (engineer/factory/commander).</summary>
    public float BuildRate;
    public float ProductionMass;
    public float ProductionEnergy;
    /// <summary>Continuous upkeep while the unit is active (e.g. mass extractor energy, shields).</summary>
    public float MaintenanceEnergy;
    public float StorageMass;
    public float StorageEnergy;
    /// <summary>True for mass extractors: must be placed on a mass deposit.</summary>
    public bool RequiresMassDeposit;
    public bool RequiresHydroDeposit;
}

public sealed class MovementBlueprint
{
    public MotionLayer Layer = MotionLayer.None;
    public float MaxSpeed;
    public float Acceleration = 3;
    /// <summary>Degrees per second.</summary>
    public float TurnRate = 90;
    /// <summary>Flight altitude for air units.</summary>
    public float Altitude;
    /// <summary>Max terrain slope (rise/run) this unit can climb.</summary>
    public float MaxSlope = 0.75f;
}

public sealed class IntelBlueprint
{
    public float VisionRadius = 10;
    public float RadarRadius;
    public float SonarRadius;
    public float OmniRadius;
}

public sealed class WeaponBlueprint
{
    public string Name = "Weapon";
    public float Range = 10;
    public float MinRange;
    public float Damage = 10;
    /// <summary>Area damage radius; 0 = single target.</summary>
    public float DamageRadius;
    /// <summary>Shots per second.</summary>
    public float RateOfFire = 1;
    public int ProjectilesPerShot = 1;
    /// <summary>Projectile speed (units/s). 0 = instant hit (beam).</summary>
    public float MuzzleVelocity = 25;
    /// <summary>Ballistic arc (artillery) instead of straight line.</summary>
    public bool Ballistic;
    /// <summary>Random spread radius at target, in world units.</summary>
    public float Spread;
    public string[] TargetLayers = ["Land", "Water"];
    [JsonIgnore] public LayerMask TargetMask;
    /// <summary>Degrees per second the turret can rotate.</summary>
    public float TurretTurnRate = 180;
    /// <summary>Max angle (degrees) between turret and target to fire.</summary>
    public float FiringTolerance = 5;
    /// <summary>Visual hint for the client: "shell", "laser", "missile", "flak", "bomb".</summary>
    public string ProjectileVisual = "shell";
    /// <summary>Energy needed per shot (e.g. some T2+ weapons). 0 = free.</summary>
    public float EnergyPerShot;
}

public sealed class VisualBlueprint
{
    /// <summary>Procedural mesh family: "commander","engineer","tank","bot","artillery","aa","scout","factory",
    /// "mex","pgen","storage","defense","radar","wall","fighter","bomber","gunship","transport","frigate",
    /// "destroyer","cruiser","sub","experimental". Unknown shapes fall back to a box.</summary>
    public string Shape = "box";
    /// <summary>Size in world units: [width (x), height (y), length (z)].</summary>
    public Vector3 Size = new(1, 1, 1);
    /// <summary>Strategic icon for zoomed-out view: "land_direct","land_artillery","land_aa","land_engineer",
    /// "commander","air_fighter","air_bomber","air_gunship","air_transport","naval","factory","eco_mass",
    /// "eco_energy","defense","intel","shield","experimental".</summary>
    public string Icon = "land_direct";
}
