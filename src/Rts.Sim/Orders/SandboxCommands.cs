using System.Numerics;
using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Orders;

/// <summary>
/// Sandbox cheats. Like every other command they go through the runner's queue and are applied at the start of a
/// tick (deterministic, replay-safe), but <see cref="Systems.SandboxSystem"/> only honors them when the world was
/// started in sandbox mode (<see cref="Systems.SandboxSystem.Enable"/>); in a normal skirmish they are ignored.
/// <para><c>Army</c> is the issuing army (the local player); the army a cheat acts on is a separate field. Sandbox
/// commands are accepted even if the issuing army has been defeated (the player may nuke their own commander).</para>
/// </summary>
public abstract record SandboxCommand(int Army) : Command(Army);

/// <summary>Spawns Count units of a blueprint for TargetArmy in a grid around Pos (valid cells only; structures on
/// free footprints, extractors on free deposits unless build-anywhere is on). Complete=false spawns construction
/// sites. Large requests are spread over several ticks (<see cref="Systems.SandboxSystem.SpawnBudgetPerTick"/>).</summary>
public sealed record SandboxSpawnCommand(int Army, int TargetArmy, int BlueprintIndex, Vector2 Pos, int Count, bool Complete = true)
    : SandboxCommand(Army);

/// <summary>Per-army cheat switches.</summary>
public enum SandboxToggle : byte
{
    /// <summary>Storage always full, the economy never stalls (costs are effectively free).</summary>
    InfiniteResources,
    /// <summary>Construction sites and factory items finish as soon as a builder works on them.</summary>
    InstantBuild,
    /// <summary>Extractors/hydrocarbon plants don't need a deposit.</summary>
    BuildAnywhere,
}

/// <summary>Turns a cheat on/off for TargetArmy (-1 = every army).</summary>
public sealed record SandboxToggleCommand(int Army, int TargetArmy, SandboxToggle Toggle, bool On) : SandboxCommand(Army);

/// <summary>Turns the skirmish AI of TargetArmy on/off and sets its difficulty (null = keep the current one).</summary>
public sealed record SandboxSetAiCommand(int Army, int TargetArmy, bool On, AiDifficulty? Difficulty = null) : SandboxCommand(Army);

/// <summary>Victory/defeat end of the game in the sandbox (off by default: the game never ends).</summary>
public sealed record SandboxSetVictoryCommand(int Army, bool On) : SandboxCommand(Army);

public enum SandboxUnitAction : byte
{
    /// <summary>Destroy (explosion + wreck, like a kill).</summary>
    Kill,
    /// <summary>Remove silently: no explosion, no wreck.</summary>
    Delete,
    /// <summary>Full health (finishes nothing: construction sites stay sites).</summary>
    Heal,
    /// <summary>Lose Amount (0..1) of max health; can kill.</summary>
    Damage,
    /// <summary>Give the units to TargetArmy (commanders excluded).</summary>
    ChangeOwner,
    /// <summary>Finish construction sites instantly.</summary>
    Complete,
}

/// <summary>Acts on any units (own or enemy). Amount: damage fraction for Damage; TargetArmy: new owner for ChangeOwner.</summary>
public sealed record SandboxUnitActionCommand(int Army, UnitHandle[] Units, SandboxUnitAction Action, float Amount = 0, int TargetArmy = -1)
    : SandboxCommand(Army);

/// <summary>Removes every unit (OnlyArmy = -1: of every army) within Radius of Center, without wrecks. Commanders are
/// spared unless IncludeCommanders.</summary>
public sealed record SandboxClearAreaCommand(int Army, Vector2 Center, float Radius, int OnlyArmy = -1, bool IncludeCommanders = false)
    : SandboxCommand(Army);

/// <summary>Strategic nuke detonation at Target (see <see cref="Systems.SandboxSystem.Nuke"/>). Zero radii/damage =
/// defaults (SupCom-like: inner 30 / 70 000, outer 40 / 500).</summary>
public sealed record SandboxNukeCommand(int Army, Vector2 Target, float InnerRadius = 0, float OuterRadius = 0, float InnerDamage = 0, float OuterDamage = 0)
    : SandboxCommand(Army);
