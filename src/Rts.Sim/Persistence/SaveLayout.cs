using System.Reflection;

namespace Rts.Sim.Persistence;

/// <summary>
/// (11a-2) Renames since older saves were written (ARCHITECTURE "Save migration"): a type or a field that was renamed
/// in code is found here when a save written before the rename is loaded. Keys use the stable type names of
/// <see cref="TypeNames"/> ("Rts.Sim:Rts.Sim.World.UnitStore"). Never remove an entry while saves with the old name
/// may exist. Example: <c>Types["Rts.Sim:Rts.Sim.Old.Name"] = "Rts.Sim:Rts.Sim.New.Name"</c>,
/// <c>Fields[("Rts.Sim:Rts.Sim.World.UnitStore", "OldField")] = "NewField"</c> (key = the CURRENT type name).
/// </summary>
public static class SaveRenames
{
    /// <summary>Old stable type name → current stable type name.</summary>
    public static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
    };

    /// <summary>(current stable type name, old field name) → current field name.</summary>
    public static readonly Dictionary<(string Type, string Field), string> Fields = new()
    {
    };
}

/// <summary>(11a-2) Optional hook of a saved class: called once per object read from a save whose layout of this class
/// differs from this build (fields added / dropped / converted), after every object is loaded and before
/// <see cref="IAfterLoad.AfterLoad"/>. New fields already hold the value of the parameterless constructor (if any).</summary>
public interface ISaveMigrated
{
    void OnMigrated(SaveMigrationInfo info);
}

/// <summary>What changed for one migrated type (given to <see cref="ISaveMigrated.OnMigrated"/>).</summary>
public sealed class SaveMigrationInfo
{
    public required Type Type;
    /// <summary>Fields of this build that the save did not have (they keep their constructor / default value).</summary>
    public required IReadOnlyList<string> Defaulted;
    /// <summary>Fields of the save that this build no longer has (their values were skipped).</summary>
    public required IReadOnlyList<string> Dropped;
    public bool Has(string field) => !Defaulted.Contains(field);
}

/// <summary>(11a-2) What a load had to change (<see cref="LoadedGame.Migration"/>). Empty = an exact load.</summary>
public sealed class SaveMigrationReport
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    public readonly List<string> Lines = new();
    public bool Migrated => Lines.Count > 0;
    public void Add(string line) { if (_seen.Add(line)) Lines.Add(line); }
    public override string ToString() => Migrated ? string.Join("\n", Lines) : "(no migration)";
}

internal enum NodeKind : byte { Prim, Enum, Struct, Ref }

/// <summary>One value as the save describes it (parsed from a type descriptor's layout signature).</summary>
internal sealed class SavedNode
{
    public string Type = "";
    public NodeKind Kind;
    public string? Underlying;          // enums: the underlying primitive's type name
    public int RawSize = -1;            // "#rawN": stored as N raw bytes
    public List<(string Name, SavedNode Node)>? Fields; // structs (and class bodies)
    public string Text = "";            // the exact signature text (compared with this build's)

    /// <summary>True when the raw bytes are exactly the fields back to back (they can be read one by one).</summary>
    public bool Sequential => RawSize < 0 || Fields != null && Fields.Sum(f => f.Node.StoredSize) == RawSize;
    public int StoredSize => Kind switch
    {
        NodeKind.Prim => SaveLayout.PrimSize(Type),
        NodeKind.Enum => SaveLayout.PrimSize(Underlying!),
        NodeKind.Struct => RawSize >= 0 ? RawSize : -1_000_000,
        _ => -1_000_000,
    };
}

/// <summary>(11a-2) Parser of the layout signatures written by <see cref="TypeLayout"/> / <see cref="RefCodec"/>.</summary>
internal static class SaveLayout
{
    private const string Core = "System.Private.CoreLib:System.";

    private static readonly Dictionary<string, (int Size, bool Signed, Type T)> Prims = new(StringComparer.Ordinal)
    {
        [Core + "Boolean"] = (1, false, typeof(bool)), [Core + "Byte"] = (1, false, typeof(byte)),
        [Core + "SByte"] = (1, true, typeof(sbyte)), [Core + "Int16"] = (2, true, typeof(short)),
        [Core + "UInt16"] = (2, false, typeof(ushort)), [Core + "Char"] = (2, false, typeof(char)),
        [Core + "Int32"] = (4, true, typeof(int)), [Core + "UInt32"] = (4, false, typeof(uint)),
        [Core + "Single"] = (4, true, typeof(float)), [Core + "Int64"] = (8, true, typeof(long)),
        [Core + "UInt64"] = (8, false, typeof(ulong)), [Core + "Double"] = (8, true, typeof(double)),
    };

    public static int PrimSize(string name) => Prims.TryGetValue(name, out var p) ? p.Size : throw ByteReader.Corrupt("primitive " + name);
    public static Type? PrimType(string name) => Prims.TryGetValue(name, out var p) ? p.T : null;

    /// <summary>Lossless conversions (saved primitive/enum → current primitive/enum): integer widening (enums count as
    /// their underlying integer, so enum ↔ integer works) and float → double.</summary>
    public static bool CanConvert(Type from, Type to)
    {
        if (from.IsEnum) from = Enum.GetUnderlyingType(from);
        if (to.IsEnum) to = Enum.GetUnderlyingType(to);
        if (from == to) return true;
        if (from == typeof(float)) return to == typeof(double);
        if (!IsInteger(from) || !IsInteger(to)) return false;
        var (fs, fsg, _) = Prims[TypeNames.Of(from)];
        var (ts, tsg, _) = Prims[TypeNames.Of(to)];
        return fsg ? tsg && ts >= fs : tsg ? ts > fs : ts >= fs;
    }

    private static bool IsInteger(Type t) => t.IsPrimitive && t != typeof(bool) && t != typeof(char) && t != typeof(float) && t != typeof(double);

    public static object Convert(object saved, Type to)
    {
        var target = to.IsEnum ? Enum.GetUnderlyingType(to) : to;
        object v = saved is float f && target == typeof(double) ? (double)f
            : System.Convert.ChangeType(saved.GetType().IsEnum ? System.Convert.ChangeType(saved, Enum.GetUnderlyingType(saved.GetType())) : saved, target);
        return to.IsEnum ? Enum.ToObject(to, v) : v;
    }

    /// <summary>Parses a class body "{f:SIG;...}".</summary>
    public static List<(string, SavedNode)> ParseFields(string s)
    {
        int i = 0;
        var list = Fields(s, ref i);
        if (i != s.Length) throw ByteReader.Corrupt("type layout");
        return list;
    }

    /// <summary>Parses one or more comma-separated value signatures (dictionaries: "K,V").</summary>
    public static List<SavedNode> ParseValues(string s)
    {
        var list = new List<SavedNode>();
        int i = 0;
        while (true)
        {
            list.Add(Value(s, ref i));
            if (i >= s.Length) break;
            if (s[i++] != ',') throw ByteReader.Corrupt("type layout");
        }
        return list;
    }

    private static List<(string, SavedNode)> Fields(string s, ref int i)
    {
        Expect(s, ref i, '{');
        var list = new List<(string, SavedNode)>();
        while (i < s.Length && s[i] != '}')
        {
            int c = s.IndexOf(':', i);
            if (c < 0) throw ByteReader.Corrupt("type layout");
            string name = s[i..c];
            i = c + 1;
            list.Add((name, Value(s, ref i)));
            Expect(s, ref i, ';');
        }
        Expect(s, ref i, '}');
        return list;
    }

    private static SavedNode Value(string s, ref int i)
    {
        int start = i;
        var n = new SavedNode { Type = TypeName(s, ref i) };
        if (i < s.Length && s[i] == '=')
        {
            i++;
            n.Kind = NodeKind.Enum;
            n.Underlying = TypeName(s, ref i);
        }
        else
        {
            if (Peek(s, i, "#raw"))
            {
                i += 4;
                int d = i;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                n.RawSize = int.Parse(s.AsSpan(d, i - d));
            }
            if (i < s.Length && s[i] == '{') { n.Kind = NodeKind.Struct; n.Fields = Fields(s, ref i); }
            else n.Kind = Prims.ContainsKey(n.Type) ? NodeKind.Prim : NodeKind.Ref;
        }
        n.Text = s[start..i];
        return n;
    }

    /// <summary>A name as <see cref="TypeNames.Of"/> writes it: "Asm:Full.Name" [ "{Arg|Arg}" if generic ] [ "[]"... ].</summary>
    private static string TypeName(string s, ref int i)
    {
        int start = i;
        int c = s.IndexOf(':', i);
        if (c < 0) throw ByteReader.Corrupt("type name");
        i = c + 1;
        while (i < s.Length && s[i] is not ('=' or '#' or '{' or ';' or ',' or '|' or '}')) i++;
        if (i < s.Length && s[i] == '{' && s.AsSpan(start, i - start).Contains('`'))
        {
            i++;
            while (true)
            {
                TypeName(s, ref i);
                if (i >= s.Length) throw ByteReader.Corrupt("type name");
                if (s[i] == '|') { i++; continue; }
                Expect(s, ref i, '}');
                break;
            }
            while (Peek(s, i, "[]")) i += 2;
        }
        return s[start..i];
    }

    private static bool Peek(string s, int i, string what) => string.CompareOrdinal(s, i, what, 0, what.Length) == 0;

    private static void Expect(string s, ref int i, char ch)
    {
        if (i >= s.Length || s[i] != ch) throw ByteReader.Corrupt("type layout");
        i++;
    }

    /// <summary>Splits "Asm:Def`N{A|B}" into the definition name and the argument names (null when not generic).</summary>
    public static (string Def, List<string>? Args) SplitGeneric(string name)
    {
        int open = name.IndexOf('{');
        if (open < 0 || name.EndsWith("[]", StringComparison.Ordinal) || !name.EndsWith('}')) return (name, null);
        var args = new List<string>();
        int depth = 0, st = open + 1;
        for (int k = open + 1; k < name.Length - 1; k++)
        {
            if (name[k] == '{') depth++;
            else if (name[k] == '}') depth--;
            else if (name[k] == '|' && depth == 0) { args.Add(name[st..k]); st = k + 1; }
        }
        args.Add(name[st..^1]);
        return (name[..open], args);
    }

    public static string Short(string typeName)
    {
        var (def, args) = SplitGeneric(typeName);
        int dot = Math.Max(def.LastIndexOf('.'), def.LastIndexOf(':'));
        string b = def[(dot + 1)..];
        int tick = b.IndexOf('`');
        if (tick >= 0) b = b[..tick];
        return args == null ? b : b + "<" + string.Join(",", args.Select(Short)) + ">";
    }

    public static string Short(Type t) => Short(TypeNames.Of(t));

    /// <summary>The saved fields of a class or struct of this build, base first (as written).</summary>
    public static FieldInfo[] CurrentFields(Type t) => TypeLayout.SavedFields(t);
}
