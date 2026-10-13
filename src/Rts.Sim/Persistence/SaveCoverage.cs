using System.Reflection;
using System.Runtime.CompilerServices;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Persistence;

/// <summary>
/// Static guard for the save system (run by the test <c>SaveCoverageTests</c>): walks every type that can be reached
/// from the saved roots (the <see cref="Simulation"/>, the per-world state tables and every concrete
/// <see cref="Command"/>) and reports anything the serializer cannot store, so new sim state fails loudly in a test
/// instead of being silently lost or crashing a player's save:
/// <list type="bullet">
/// <item>field types without a codec (framework types other than the supported collections, pointers, multi-dim
///   arrays, interfaces/abstract types with no concrete game implementation, delegates);</item>
/// <item>mutable static fields in Rts.Sim (static state is never saved);</item>
/// <item>ConditionalWeakTables keyed by something other than <see cref="SimWorld"/>.</item>
/// </list>
/// Fix by storing the data in plain fields/supported collections, or mark caches/diagnostics [NoSave] (+ IAfterLoad).
/// </summary>
public static class SaveCoverage
{
    private static readonly string[] ImmutableNamespaces = ["Rts.Sim.Data", "Rts.Sim.Map"];

    public static List<string> Problems()
    {
        var w = new Walker(typeof(SimWorld).Assembly);
        w.Add(typeof(Simulation), "Simulation");
        foreach (var t in WorldState.Tables) w.Add(t.ValueType, t.Key);
        foreach (var t in w.Concrete(typeof(Command))) w.Add(t, "command " + t.Name);
        w.Run();
        CheckStatics(typeof(SimWorld).Assembly, w.Problems);
        return w.Problems;
    }

    /// <summary>The field-type walk for one root type (types of the root's assembly count as game types). For tests.</summary>
    public static List<string> ProblemsFor(Type root)
    {
        var w = new Walker(root.Assembly);
        w.Add(root, root.Name);
        w.Run();
        return w.Problems;
    }

    private sealed class Walker(Assembly game)
    {
        private readonly HashSet<Type> _seen = new();
        private readonly Queue<(Type Type, string Path)> _queue = new();
        private readonly Type[] _gameTypes = game.GetTypes();
        public readonly List<string> Problems = new();

        public void Add(Type t, string path)
        {
            if (_seen.Add(t)) _queue.Enqueue((t, path));
        }

        public void Run()
        {
            while (_queue.TryDequeue(out var item)) Visit(item.Type, item.Path);
        }

        /// <summary>Non-abstract game types assignable to <paramref name="t"/> (t itself included if concrete).</summary>
        public IEnumerable<Type> Concrete(Type t) =>
            _gameTypes.Where(c => !c.IsAbstract && !c.IsInterface && !c.ContainsGenericParameters && t.IsAssignableFrom(c))
                .OrderBy(c => c.FullName, StringComparer.Ordinal);

        private void Problem(string path, Type t, string why) =>
            Problems.Add($"{path} ({TypeNames.Of(t)}): {why}");

        private void Fields(Type t, string owner)
        {
            foreach (var f in TypeLayout.SavedFields(t))
            {
                string path = owner + "." + f.Name;
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) DelegateProblem(path, f.FieldType);
                else Add(f.FieldType, path);
            }
        }

        private void DelegateProblem(string path, Type t) =>
            Problem(path, t, "delegates/lambdas must not be stored in sim state (store plain data, e.g. an enum or an id, " +
                "and dispatch in code). A cached delegate to a method of the same object: make it " +
                "'[NoSave] Action? _f;' and use '_f ??= Method'");

        private void Visit(Type t, string path)
        {
            if (TypeLayout.IsNoSaveType(t)) return;
            if (t.IsPrimitive || t.IsEnum)
            {
                if (t == typeof(IntPtr) || t == typeof(UIntPtr)) Problem(path, t, "native-sized integers are not supported");
                return;
            }
            if (t == typeof(string)) return;
            if (t.IsPointer || t.IsByRefLike || t.IsByRef) { Problem(path, t, "pointers / ref structs are not supported"); return; }
            if (t.ContainsGenericParameters) { Problem(path, t, "open generic type"); return; }
            if (t.IsValueType)
            {
                if (!TypeLayout.IsRaw(t)) Fields(t, path);
                return;
            }
            if (typeof(Delegate).IsAssignableFrom(t)) { DelegateProblem(path, t); return; }
            if (t.IsInterface || t.IsAbstract)
            {
                var impls = Concrete(t).ToList();
                if (impls.Count == 0)
                    Problem(path, t, "interface/abstract field type without a concrete Rts.Sim implementation " +
                        "(framework interfaces like IList/IEnumerable cannot be saved: use the concrete collection type)");
                foreach (var c in impls) Add(c, path + "<" + c.Name + ">");
                return;
            }
            if (t == typeof(object))
            {
                Problem(path, t, "'object' fields cannot be checked statically (use a concrete type; a lock may use System.Threading.Lock)");
                return;
            }
            try { RefCodec.For(t); }
            catch (NotSupportedException e) { Problems.Add($"{path}: {e.Message}"); return; }
            if (t.IsArray) { Add(t.GetElementType()!, path + "[]"); return; }
            if (RefCodec.IsSystemType(t))
            {
                if (t.IsGenericType)
                    foreach (var a in t.GetGenericArguments()) Add(a, path + "<" + a.Name + ">");
                return;
            }
            Fields(t, t.Name);
            // A field of a non-sealed class may hold a subclass instance: check those too.
            if (!t.IsSealed)
                foreach (var c in Concrete(t)) Add(c, path + "<" + c.Name + ">");
        }
    }

    private static void CheckStatics(Assembly game, List<string> problems)
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var t in game.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (IsCompilerGenerated(t)) continue;
            string ns = t.Namespace ?? "";
            if (ns.StartsWith("Rts.Sim.Persistence", StringComparison.Ordinal)) continue; // serializer caches
            bool immutableNs = ImmutableNamespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));
            foreach (var f in t.GetFields(Static))
            {
                if (f.IsLiteral || f.IsDefined(typeof(NoSaveAttribute), false)) continue;
                var ft = f.FieldType;
                string name = $"static field {t.FullName}.{f.Name} ({TypeNames.Of(ft)})";
                if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(ConditionalWeakTable<,>))
                {
                    if (ft.GetGenericArguments()[0] != typeof(SimWorld))
                        problems.Add($"{name}: per-world state must be a ConditionalWeakTable keyed by SimWorld " +
                            "(those are saved automatically), or mark it [NoSave] if it is a cache");
                    continue;
                }
                if (immutableNs) continue; // blueprint/map data: immutable, shared, not saved
                if (f.IsInitOnly && (IsImmutable(ft) || ft.IsArray && IsImmutable(ft.GetElementType()!))) continue; // constants / lookup tables
                problems.Add($"{name}: static state is not saved. Keep sim state in SimWorld / per-world tables; " +
                    "mark [NoSave] only for scratch buffers, caches or constants that never carry data between ticks");
            }
        }
    }

    private static bool IsCompilerGenerated(Type t)
    {
        for (var c = t; c != null; c = c.DeclaringType)
            if (c.IsDefined(typeof(CompilerGeneratedAttribute), false) || c.Name.Contains('<')) return true;
        return false;
    }

    /// <summary>Types whose static readonly instances cannot change: strings and value types without references.</summary>
    private static bool IsImmutable(Type t) => t == typeof(string) || (t.IsValueType && !TypeLayout.ContainsReferences(t));
}
