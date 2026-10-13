using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rts.Sim.Persistence;

internal delegate void ValWriter<T>(GraphWriter w, ref T v);
internal delegate void ValReader<T>(GraphReader r, ref T v);
internal delegate void ObjWriter(GraphWriter w, object o);
internal delegate void ObjReader(GraphReader r, object o);

/// <summary>
/// Writes/reads one value of static type T in place: primitives directly, reference types as graph references,
/// raw structs as bytes, other structs field by field through generated IL (<see cref="FieldCodegen"/>).
/// </summary>
internal static class Val<T>
{
    private static ValWriter<T>? s_w;
    private static ValReader<T>? s_r;

    public static readonly bool IsRaw = TypeLayout.IsRaw(typeof(T));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write(GraphWriter w, ref T v)
    {
        if (typeof(T) == typeof(int)) { w.Out.Raw(Unsafe.As<T, int>(ref v)); return; }
        if (typeof(T) == typeof(float)) { w.Out.Raw(Unsafe.As<T, float>(ref v)); return; }
        if (typeof(T) == typeof(bool)) { w.Out.Raw(Unsafe.As<T, bool>(ref v)); return; }
        if (typeof(T) == typeof(byte)) { w.Out.Raw(Unsafe.As<T, byte>(ref v)); return; }
        if (typeof(T) == typeof(long)) { w.Out.Raw(Unsafe.As<T, long>(ref v)); return; }
        if (typeof(T) == typeof(ulong)) { w.Out.Raw(Unsafe.As<T, ulong>(ref v)); return; }
        if (typeof(T) == typeof(double)) { w.Out.Raw(Unsafe.As<T, double>(ref v)); return; }
        if (!typeof(T).IsValueType) { w.WriteRef(Unsafe.As<T, object?>(ref v)); return; }
        if (IsRaw) { w.Out.Bytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref v), Unsafe.SizeOf<T>())); return; }
        (s_w ??= FieldCodegen.StructWriter<T>())(w, ref v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(GraphReader r, ref T v)
    {
        if (typeof(T) == typeof(int)) { Unsafe.As<T, int>(ref v) = r.In.Raw<int>(); return; }
        if (typeof(T) == typeof(float)) { Unsafe.As<T, float>(ref v) = r.In.Raw<float>(); return; }
        if (typeof(T) == typeof(bool)) { Unsafe.As<T, bool>(ref v) = r.In.U8() != 0; return; }
        if (typeof(T) == typeof(byte)) { Unsafe.As<T, byte>(ref v) = r.In.U8(); return; }
        if (typeof(T) == typeof(long)) { Unsafe.As<T, long>(ref v) = r.In.Raw<long>(); return; }
        if (typeof(T) == typeof(ulong)) { Unsafe.As<T, ulong>(ref v) = r.In.Raw<ulong>(); return; }
        if (typeof(T) == typeof(double)) { Unsafe.As<T, double>(ref v) = r.In.Raw<double>(); return; }
        if (!typeof(T).IsValueType)
        {
            object? o = r.ReadRef();
            if (o != null && o is not T) throw new SaveFormatException($"Damaged file (a {o.GetType().Name} where a {typeof(T).Name} was expected)");
            v = (T)o!;
            return;
        }
        if (IsRaw) { r.In.Bytes(MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref v), Unsafe.SizeOf<T>())); return; }
        (s_r ??= FieldCodegen.StructReader<T>())(r, ref v);
    }

    /// <summary>Whole span (arrays, list contents): one block copy for raw element types.</summary>
    public static void WriteSpan(GraphWriter w, Span<T> span)
    {
        if (IsRaw) { w.Out.Bytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)), span.Length * Unsafe.SizeOf<T>()))); return; }
        for (int i = 0; i < span.Length; i++) Write(w, ref span[i]);
    }

    public static void ReadSpan(GraphReader r, Span<T> span)
    {
        if (IsRaw) { r.In.Bytes(MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)), span.Length * Unsafe.SizeOf<T>())); return; }
        for (int i = 0; i < span.Length; i++) Read(r, ref span[i]);
    }

    /// <summary>Lower bound of the encoded size of one value (bounds array allocations from damaged files).</summary>
    public static int MinEncodedSize => IsRaw ? Unsafe.SizeOf<T>() : typeof(T).IsValueType ? 0 : 1;
}

/// <summary>Generates per-type field-by-field writers/readers (DynamicMethod; also reaches private/readonly fields).</summary>
internal static class FieldCodegen
{
    private static MethodInfo ValMethod(Type fieldType, string name) =>
        typeof(Val<>).MakeGenericType(fieldType).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    public static ValWriter<T> StructWriter<T>()
    {
        var t = typeof(T);
        var dm = new DynamicMethod("SaveW_" + t.Name, typeof(void), [typeof(GraphWriter), t.MakeByRefType()], typeof(FieldCodegen).Module, true);
        var il = dm.GetILGenerator();
        foreach (var f in TypeLayout.SavedFields(t))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldflda, f);
            il.Emit(OpCodes.Call, ValMethod(f.FieldType, nameof(Val<int>.Write)));
        }
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<ValWriter<T>>();
    }

    public static ValReader<T> StructReader<T>()
    {
        var t = typeof(T);
        var dm = new DynamicMethod("SaveR_" + t.Name, typeof(void), [typeof(GraphReader), t.MakeByRefType()], typeof(FieldCodegen).Module, true);
        var il = dm.GetILGenerator();
        foreach (var f in TypeLayout.SavedFields(t))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldflda, f);
            il.Emit(OpCodes.Call, ValMethod(f.FieldType, nameof(Val<int>.Read)));
        }
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<ValReader<T>>();
    }

    public static ObjWriter ClassWriter(Type t) => Class<ObjWriter>(t, typeof(GraphWriter), nameof(Val<int>.Write));
    public static ObjReader ClassReader(Type t) => Class<ObjReader>(t, typeof(GraphReader), nameof(Val<int>.Read));

    private static D Class<D>(Type t, Type ioType, string method) where D : Delegate
    {
        var dm = new DynamicMethod("Save" + method[0] + "_" + t.Name, typeof(void), [ioType, typeof(object)], typeof(FieldCodegen).Module, true);
        var il = dm.GetILGenerator();
        var self = il.DeclareLocal(t);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, t);
        il.Emit(OpCodes.Stloc, self);
        foreach (var f in TypeLayout.SavedFields(t))
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, self);
            il.Emit(OpCodes.Ldflda, f);
            il.Emit(OpCodes.Call, ValMethod(f.FieldType, method));
        }
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<D>();
    }
}
