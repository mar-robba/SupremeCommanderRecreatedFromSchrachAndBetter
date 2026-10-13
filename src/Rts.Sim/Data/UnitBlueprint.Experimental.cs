using System.Text.Json.Serialization;

namespace Rts.Sim.Data;

/// <summary>Experimental-scale mechanics and commander enhancements (see Rts.Sim/Experimental, Rts.Sim/Commander).</summary>
public sealed partial class UnitBlueprint
{
    /// <summary>Crush / death-blast data of experimental units. null = none.</summary>
    public ExperimentalBlueprint? Experimental;

    /// <summary>
    /// Builder ids whose (final) build lists are merged into this unit's BuildIndices at load, after <c>BuiltBy</c>
    /// (e.g. a support commander builds what a T3 engineer builds, a mobile factory what a T3 land factory builds).
    /// Unknown ids are ignored.
    /// </summary>
    public string[] BuildListFrom = [];

    /// <summary>
    /// SupCom-style enhancements (ACU/SCU): each one sits in a <see cref="EnhancementSlot"/>, one per slot, built by
    /// the unit on itself over time. Every combination becomes a generated "variant" blueprint at load (see
    /// <see cref="Commander.EnhancementCatalog"/>); installing or removing an enhancement swaps the unit to it.
    /// </summary>
    public EnhancementBlueprint[] Enhancements = [];

    /// <summary>Enhanceable units: index of the base blueprint (the base itself and all its variants); -1 = none.</summary>
    [JsonIgnore] public int EnhancementBase = -1;
    /// <summary>Installed enhancement per slot (index into <see cref="Enhancements"/>, -1 = empty). Length
    /// <see cref="EnhancementSlots.Count"/> for enhanceable blueprints, empty otherwise.</summary>
    [JsonIgnore] public int[] Installed = [];
    /// <summary>Base blueprints only: variant blueprint index for every slot combination (mixed radix, see catalog).</summary>
    [JsonIgnore] public int[] EnhancementVariants = [];
    /// <summary>True for generated enhancement variants (hidden from spawn lists).</summary>
    [JsonIgnore] public bool IsVariant => EnhancementBase >= 0 && EnhancementBase != Index;
    [JsonIgnore] public bool IsEnhanceable => EnhancementBase >= 0;
}

/// <summary>Experimental mechanics. All optional (0 = off).</summary>
public sealed class ExperimentalBlueprint
{
    /// <summary>Mobile units: small land units whose center comes within this distance while this unit moves are crushed.</summary>
    public float CrushRadius;
    /// <summary>Crush damage per second to each unit under it (applied every tick while it moves).</summary>
    public float CrushDamage;
    /// <summary>Highest tech level that gets crushed (experimentals are never crushed).</summary>
    public int CrushMaxTech = 2;
    /// <summary>Also crush own/allied units (SupCom feet do; default off).</summary>
    public bool CrushAllies;
    /// <summary>Area damage when the (complete) unit dies, after <see cref="DeathDelay"/> seconds.</summary>
    public float DeathDamage;
    public float DeathRadius;
    public float DeathDelay = 1f;
}

/// <summary>Enhancement slots (SupCom ACU: left arm, right arm, back).</summary>
public enum EnhancementSlot : byte { LeftArm, RightArm, Back }

public static class EnhancementSlots
{
    public const int Count = 3;
    public static string Name(EnhancementSlot s) => s switch
    {
        EnhancementSlot.LeftArm => "Left arm",
        EnhancementSlot.RightArm => "Right arm",
        _ => "Back",
    };
}

/// <summary>One enhancement of a commander-type unit (data only; effects are applied to generated variant blueprints).</summary>
public sealed class EnhancementBlueprint
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public EnhancementSlot Slot;
    /// <summary>Id of the enhancement that must be installed in the same slot first (it is replaced, effects stack). "" = none.</summary>
    public string Prerequisite = "";
    public float BuildCostMass;
    public float BuildCostEnergy;
    /// <summary>Work units; seconds = BuildTime / build power (the unit's BuildRate, plus assisting engineers).</summary>
    public float BuildTime = 1;
    /// <summary>Client hint for the model attachment ("cannon", "amplifier", "engineering", "engineering3",
    /// "resource", "shield", "shield_field", "missile_pack", ...).</summary>
    public string Visual = "";
    /// <summary>Short icon label for the UI (2-4 chars).</summary>
    public string Icon = "";
    public EnhancementEffects Effects = new();
    /// <summary>Resolved <see cref="Prerequisite"/> (index into the owner's Enhancements, -1 = none).</summary>
    [JsonIgnore] public int PrerequisiteIndex = -1;
}

/// <summary>What an enhancement changes. Numbers add up along the prerequisite chain; Shield/Silo replace.</summary>
public sealed class EnhancementEffects
{
    public WeaponModifier[] Weapons = [];
    /// <summary>Extra weapons appended to the unit's weapon list (max UnitStore.MaxWeapons in total).</summary>
    public WeaponBlueprint[] AddWeapons = [];
    public float MaxHealthAdd;
    public float RegenRateAdd;
    public float BuildRateAdd;
    public float ProductionMassAdd;
    public float ProductionEnergyAdd;
    public float MaintenanceEnergyAdd;
    public float VisionRadiusAdd;
    /// <summary>Builders whose build lists are added (e.g. "uef_t2_engineer").</summary>
    public string[] BuildListFrom = [];
    /// <summary>Shield dome (replaces any shield from a prerequisite).</summary>
    public ShieldBlueprint? Shield;
    /// <summary>Missile silo; <c>WeaponIndex</c> is relative to this effect's <see cref="AddWeapons"/>.</summary>
    public SiloBlueprint? Silo;
}

/// <summary>Changes one existing weapon (by index in the base blueprint's Weapons).</summary>
public sealed class WeaponModifier
{
    public int Weapon;
    public float DamageMult = 1;
    public float DamageAdd;
    public float RangeAdd;
    public float DamageRadiusAdd;
    public float RateOfFireMult = 1;
}
