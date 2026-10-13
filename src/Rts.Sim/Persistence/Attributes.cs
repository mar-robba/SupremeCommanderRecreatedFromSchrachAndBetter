namespace Rts.Sim.Persistence;

/// <summary>
/// Excludes a field (or every field of a type) from save games and from the state-equality checks of the
/// save/load tests. Use it ONLY for data that never influences future simulation results:
/// wall-clock timings, diagnostics, caches that are rebuilt identically, immutable data derived from blueprints.
/// On load a skipped field is left at its default value, except reference types with a parameterless constructor
/// (a fresh instance is created, e.g. an empty cache). Rebuild anything else in <see cref="IAfterLoad.AfterLoad"/>.
/// Also allowed on static fields (marks static scratch as known/harmless for the coverage guard test).
/// For auto-properties use <c>[field: NoSave]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class NoSaveAttribute : Attribute
{
}

/// <summary>
/// Called on every loaded object implementing it after the whole state was restored (all objects populated,
/// dictionaries filled, per-world state tables re-attached). Rebuild [NoSave] caches here.
/// </summary>
public interface IAfterLoad
{
    void AfterLoad();
}

/// <summary>The file is damaged or not a file of the expected kind.</summary>
public class SaveFormatException : Exception
{
    public SaveFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The file is valid but was written by an incompatible build (state layout, blueprints or map differ).</summary>
public sealed class IncompatibleSaveException : SaveFormatException
{
    public IncompatibleSaveException(string message) : base(message) { }
}
