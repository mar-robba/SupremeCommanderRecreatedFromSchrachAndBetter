using System.Numerics;
using Rts.Sim.Data;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Client.Sandbox;

/// <summary>What the console parser needs to know about the game (read-only, filled by the client).</summary>
public sealed class ConsoleContext
{
    public required BlueprintDb Db;
    /// <summary>Army index of the local player.</summary>
    public int LocalArmy;
    public int ArmyCount;
    /// <summary>Team per army (may be null = free for all).</summary>
    public int[]? Teams;
}

/// <summary>A parsed sandbox console command. Pure data: the client turns it into sim commands / UI changes.</summary>
public abstract record ConsoleAction;

/// <summary>Spawn Count units of a blueprint for Army. At null = at the next map click. Site = construction sites.</summary>
public sealed record SpawnAction(int Blueprint, int Count, int Army, Vector2? At, bool Site) : ConsoleAction;

/// <summary>Nuke at a point; null = the next click picks the target. Default: a real strategic missile launched by the
/// local army; Instant: the blast right away, without a missile.</summary>
public sealed record NukeAction(Vector2? At, bool Instant = false) : ConsoleAction;

/// <summary>Cheat switch for Army (-1 = all armies). On null = flip.</summary>
public sealed record ToggleAction(SandboxToggle Toggle, int Army, bool? On) : ConsoleAction;

/// <summary>Fog of war on/off (On = fog enabled); null = flip.</summary>
public sealed record FogAction(bool? On) : ConsoleAction;

/// <summary>AI control of Army (-1 = every army except the local one); Difficulty null = keep.</summary>
public sealed record AiAction(int Army, bool On, AiDifficulty? Difficulty) : ConsoleAction;

/// <summary>Acts on the current selection (Amount: damage fraction; TargetArmy: new owner).</summary>
public sealed record UnitActionAction(SandboxUnitAction Action, float Amount = 0, int TargetArmy = -1) : ConsoleAction;

/// <summary>Remove every unit within Radius of At; null = the next click.</summary>
public sealed record ClearAction(float Radius, Vector2? At) : ConsoleAction;

/// <summary>Game speed multiplier (0.125..8).</summary>
public sealed record SpeedAction(float Speed) : ConsoleAction;

/// <summary>Pause on/off; null = flip.</summary>
public sealed record PauseAction(bool? On) : ConsoleAction;

/// <summary>Victory/defeat end of the game on/off.</summary>
public sealed record VictoryAction(bool On) : ConsoleAction;

/// <summary>Move the camera to a ground point, optionally setting its distance.</summary>
public sealed record CameraAction(Vector2 At, float? Distance) : ConsoleAction;

/// <summary>Scripts: wait this many frames before the next line.</summary>
public sealed record WaitAction(int Frames) : ConsoleAction;

public sealed record HelpAction : ConsoleAction;

/// <summary>Sandbox panel visible on/off; null = flip.</summary>
public sealed record PanelAction(bool? On) : ConsoleAction;
/// <summary>Show / hide the console (null = toggle). Opened from a script it stays unfocused.</summary>
public sealed record ShowConsoleAction(bool? On) : ConsoleAction;

/// <summary>Clear the console log.</summary>
public sealed record ClearLogAction : ConsoleAction;

/// <summary>Result of parsing one console line: exactly one of Action/Error is non-null, except for an empty
/// (or comment) line where both are null.</summary>
public readonly record struct ParseResult(ConsoleAction? Action, string? Error)
{
    public static ParseResult Ok(ConsoleAction action) => new(action, null);
    public static ParseResult Fail(string error) => new(null, error);
}
