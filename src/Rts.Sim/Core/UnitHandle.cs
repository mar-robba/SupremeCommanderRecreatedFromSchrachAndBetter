namespace Rts.Sim.Core;

/// <summary>
/// Stable reference to a unit slot. Becomes stale (invalid) when the slot is freed and reused:
/// always check with <see cref="World.UnitStore.IsActive(UnitHandle)"/> before use.
/// </summary>
public readonly record struct UnitHandle(int Index, int Generation)
{
    public static readonly UnitHandle None = new(-1, 0);
    public bool IsNone => Index < 0;
    public override string ToString() => IsNone ? "Unit(None)" : $"Unit({Index}#{Generation})";
}
