using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Rts.Sim.Persistence;

/// <summary>
/// Stable, assembly-version independent type names: "Asm:Namespace.Type", generics as "Asm:Def`1{Arg|Arg}",
/// arrays as "Elem[]". Resolution only loads assemblies by simple name.
/// </summary>
public static class TypeNames
{
    private static readonly ConcurrentDictionary<Type, string> Names = new();
    private static readonly ConcurrentDictionary<string, Type?> Types = new();

    public static string Of(Type t) => Names.GetOrAdd(t, static t => Build(t));

    private static string Build(Type t)
    {
        if (t.IsArray)
        {
            if (!t.IsSZArray) throw new NotSupportedException($"Multi-dimensional array {t} is not supported by the save system");
            return Of(t.GetElementType()!) + "[]";
        }
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
        {
            var def = t.GetGenericTypeDefinition();
            return Asm(def) + ":" + def.FullName + "{" + string.Join("|", t.GetGenericArguments().Select(Of)) + "}";
        }
        if (t.FullName == null) throw new NotSupportedException($"Type {t} has no stable name");
        return Asm(t) + ":" + t.FullName;
    }

    private static string Asm(Type t) => t.Assembly.GetName().Name ?? "";

    /// <summary>Resolves a name written by <see cref="Of"/>; throws <see cref="IncompatibleSaveException"/> if unknown.</summary>
    public static Type Resolve(string name) =>
        Types.GetOrAdd(name, static n => TryBuild(n)) ?? throw new IncompatibleSaveException($"Type '{name}' does not exist in this build");

    private static Type? TryBuild(string n)
    {
        try
        {
            if (n.EndsWith("[]", StringComparison.Ordinal)) return TryBuild(n[..^2])?.MakeArrayType();
            int open = n.IndexOf('{');
            if (open >= 0)
            {
                if (!n.EndsWith('}')) return null;
                var def = TryBuild(n[..open]);
                if (def == null) return null;
                var args = SplitArgs(n.Substring(open + 1, n.Length - open - 2));
                var resolved = new Type[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    var a = TryBuild(args[i]);
                    if (a == null) return null;
                    resolved[i] = a;
                }
                return def.MakeGenericType(resolved);
            }
            int colon = n.IndexOf(':');
            if (colon <= 0) return null;
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == n[..colon])
                      ?? Assembly.Load(new AssemblyName(n[..colon]));
            return asm.GetType(n[(colon + 1)..], throwOnError: false);
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or ArgumentException or TypeLoadException)
        {
            return null;
        }
    }

    private static List<string> SplitArgs(string s)
    {
        var list = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
            else if (s[i] == '|' && depth == 0) { list.Add(s[start..i]); start = i + 1; }
        }
        list.Add(s[start..]);
        return list;
    }
}

/// <summary>Reflection facts about types (saved fields, raw-copy eligibility, layout signatures). Cached.</summary>
public static class TypeLayout
{
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Saved = new();
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Skipped = new();
    private static readonly ConcurrentDictionary<Type, bool> RawCache = new();
    private static readonly ConcurrentDictionary<Type, string> Sigs = new();

    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static bool IsNoSaveType(Type t) => t.IsDefined(typeof(NoSaveAttribute), false);

    public static bool IsNoSave(FieldInfo f) => f.IsDefined(typeof(NoSaveAttribute), false) || IsNoSaveType(f.FieldType);

    /// <summary>All instance fields, base class first, declaration order.</summary>
    public static IEnumerable<FieldInfo> AllFields(Type t)
    {
        var chain = new List<Type>();
        for (var c = t; c != null && c != typeof(object) && c != typeof(ValueType); c = c.BaseType) chain.Add(c);
        chain.Reverse();
        foreach (var c in chain)
            foreach (var f in c.GetFields(Inst).OrderBy(f => f.MetadataToken))
                yield return f;
    }

    /// <summary>Fields written to saves (everything not [NoSave]).</summary>
    public static FieldInfo[] SavedFields(Type t) => Saved.GetOrAdd(t, static t => AllFields(t).Where(f => !IsNoSave(f)).ToArray());

    /// <summary>[NoSave] fields (restored as default / fresh instance).</summary>
    public static FieldInfo[] SkippedFields(Type t) => Skipped.GetOrAdd(t, static t => AllFields(t).Where(IsNoSave).ToArray());

    public static bool IsPrimitiveLike(Type t) => t.IsPrimitive || t.IsEnum;

    /// <summary>
    /// True for unmanaged value types that can be copied as raw bytes with an exact, deterministic result:
    /// primitives, enums and structs without references and without padding (or with fixed/inline buffers).
    /// </summary>
    public static bool IsRaw(Type t) => RawCache.GetOrAdd(t, static t =>
    {
        if (t.IsPrimitive || t.IsEnum) return t != typeof(IntPtr) && t != typeof(UIntPtr);
        if (!t.IsValueType || ContainsReferences(t)) return false;
        if (t.IsDefined(typeof(InlineArrayAttribute), false) || t.IsExplicitLayout) return true;
        if (AllFields(t).Any(f => f.IsDefined(typeof(FixedBufferAttribute), false))) return true;
        long sum = 0;
        foreach (var f in AllFields(t))
        {
            if (!IsRaw(f.FieldType)) return false;
            sum += SizeOf(f.FieldType);
        }
        return sum == SizeOf(t);
    });

    public static bool ContainsReferences(Type t) =>
        (bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!.MakeGenericMethod(t).Invoke(null, null)!;

    public static int SizeOf(Type t) =>
        (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(t).Invoke(null, null)!;

    /// <summary>
    /// Layout signature compared on load: the names and types of every saved field, with value types expanded
    /// recursively (they are stored inline). Reference-typed fields only contribute their type name (the referenced
    /// object's own layout is checked when it is read).
    /// </summary>
    public static string Signature(Type t) => Sigs.GetOrAdd(t, static t =>
    {
        var sb = new StringBuilder();
        AppendValueSig(sb, t, 0);
        return sb.ToString();
    });

    internal static void AppendValueSig(StringBuilder sb, Type t, int depth)
    {
        if (depth > 32) throw new NotSupportedException($"Type nesting too deep at {t}");
        sb.Append(TypeNames.Of(t));
        if (t.IsEnum) { sb.Append('=').Append(TypeNames.Of(Enum.GetUnderlyingType(t))); return; }
        if (t.IsPrimitive || t == typeof(string)) return;
        if (t.IsValueType && IsRaw(t)) sb.Append("#raw").Append(SizeOf(t));
        if (!t.IsValueType) return; // classes: layout checked by their own descriptor
        sb.Append('{');
        foreach (var f in SavedFields(t))
        {
            sb.Append(f.Name).Append(':');
            AppendValueSig(sb, f.FieldType, depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }

    /// <summary>Signature of a class: its saved fields with value types expanded.</summary>
    internal static string ClassSignature(Type t)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        foreach (var f in SavedFields(t))
        {
            sb.Append(f.Name).Append(':');
            AppendValueSig(sb, f.FieldType, 1);
            sb.Append(';');
        }
        sb.Append('}');
        return sb.ToString();
    }
}
