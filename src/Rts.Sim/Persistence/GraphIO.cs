namespace Rts.Sim.Persistence;

internal static class Tag
{
    public const byte Null = 0, Ref = 1, New = 2, External = 3, String = 4;
}

/// <summary>
/// Writes an object graph: shared references and cycles are preserved (objects get ids in first-reference order),
/// object bodies are written breadth-first from a queue (no deep recursion). Type descriptors (name + layout
/// signature) are written inline at first use. Call <see cref="Flush"/> after writing the roots.
/// </summary>
public sealed class GraphWriter
{
    public readonly ByteWriter Out;
    private readonly ExternalSet? _ext;
    private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<(object obj, RefCodec codec)> _pending = new();
    private readonly Dictionary<Type, int> _types = new();
    private int _nextId;

    public GraphWriter(ByteWriter output, ExternalSet? externals = null)
    {
        Out = output;
        _ext = externals;
    }

    public int ObjectCount => _nextId;

    /// <summary>Debug aid: when set, (byte offset, object type) of every object body written, in order.</summary>
    public List<(int Offset, Type Type)>? BodyMap;

    /// <summary>(11a-2) When set, collects the external ids written (WorldState stores a stable key for each).</summary>
    public HashSet<int>? UsedExternals;

    public void WriteRef(object? o)
    {
        if (o == null) { Out.U8(Tag.Null); return; }
        if (o is string s) { Out.U8(Tag.String); Out.Str(s); return; }
        if (_ids.TryGetValue(o, out int id)) { Out.U8(Tag.Ref); Out.VarU((ulong)id); return; }
        if (_ext != null && _ext.TryGetId(o, out int ext))
        {
            Out.U8(Tag.External);
            Out.VarU((ulong)ext);
            UsedExternals?.Add(ext);
            return;
        }
        var codec = RefCodec.For(o.GetType());
        Out.U8(Tag.New);
        WriteType(codec);
        if (codec.Inline)
        {
            codec.WriteHeader(this, o);
            _ids[o] = _nextId++;
            return;
        }
        _ids[o] = _nextId++;
        codec.WriteHeader(this, o);
        _pending.Enqueue((o, codec));
    }

    /// <summary>Writes a value of static type T (struct inline, class as reference).</summary>
    public void WriteValue<T>(T value) => Val<T>.Write(this, ref value);

    private void WriteType(RefCodec c)
    {
        if (_types.TryGetValue(c.Type, out int tid)) { Out.VarU((ulong)tid); return; }
        tid = _types.Count;
        _types[c.Type] = tid;
        Out.VarU((ulong)tid);
        Out.Str(c.Name);
        Out.Str(c.Signature);
    }

    /// <summary>Writes the bodies of every object referenced so far (and of the objects they reference).</summary>
    public void Flush()
    {
        while (_pending.TryDequeue(out var p))
        {
            BodyMap?.Add((Out.Length, p.obj.GetType()));
            p.codec.WriteBody(this, p.obj);
        }
    }

    /// <summary>Forgets object identities (next writes are independent graphs); the type table is kept.</summary>
    public void ResetObjects()
    {
        Flush();
        _ids.Clear();
        _nextId = 0;
    }
}

/// <summary>Reads what <see cref="GraphWriter"/> wrote. Call <see cref="Complete"/> after reading the roots.</summary>
public sealed class GraphReader
{
    public ByteReader In { get; private set; }
    private readonly ExternalSet? _ext;
    private readonly List<object> _objs = new();
    private readonly Queue<(object obj, RefCodec codec)> _pending = new();
    private readonly List<RefCodec> _types = new();
    private readonly List<Action> _fixups = new();
    private readonly List<IAfterLoad> _afterLoad = new();

    public GraphReader(ByteReader input, ExternalSet? externals = null)
    {
        In = input;
        _ext = externals;
    }

    public object? ReadRef()
    {
        var o = ReadRefCore();
        // (11a-2) An object whose type is gone may only land where a read plan drops it (a dropped field / type, or a
        // reference field read by a plan, which turns it into null); anywhere else (e.g. inside a container whose
        // layout did not change) it would be stored in the sim as a foreign object: refuse with the type's name.
        if (o is DroppedObject d && Skipping == 0 && AcceptDropped == 0)
            throw new IncompatibleSaveException($"This game holds a {SaveLayout.Short(d.TypeName)}, a type that no longer exists in this " +
                "version, where it cannot be dropped (add a rename to SaveRenames.Types, DEV_GUIDE \"How to change a saved type\")");
        return o;
    }

    private object? ReadRefCore()
    {
        byte tag = In.U8();
        switch (tag)
        {
            case Tag.Null: return null;
            case Tag.String: return In.Str() ?? throw ByteReader.Corrupt("null string");
            case Tag.Ref:
            {
                ulong id = In.VarU();
                if (id >= (ulong)_objs.Count) throw ByteReader.Corrupt("bad object reference");
                return _objs[(int)id];
            }
            case Tag.External:
            {
                ulong id = In.VarU();
                if (ExternalMap != null)
                {
                    if (id >= (ulong)ExternalMap.Length || ExternalPaths![id] == null) throw ByteReader.Corrupt("bad external reference");
                    if (ExternalMap[id] is { } mapped) return mapped;
                    if (Skipping > 0) return new DroppedObject("external " + ExternalPaths[id]);
                    throw new IncompatibleSaveException($"This game uses the shared blueprint data '{ExternalPaths[id]}', which does not exist in this version");
                }
                if (_ext == null || id >= (ulong)_ext.Count) throw ByteReader.Corrupt("bad external reference");
                return _ext.Get((int)id);
            }
            case Tag.New:
            {
                var codec = ReadType();
                var o = codec.Create(this);
                _objs.Add(o);
                if (!codec.Inline) _pending.Enqueue((o, codec));
                return o;
            }
            default: throw ByteReader.Corrupt("bad tag");
        }
    }

    public T ReadValue<T>()
    {
        T v = default!;
        Val<T>.Read(this, ref v);
        return v;
    }

    private RefCodec ReadType()
    {
        ulong tid = In.VarU();
        if (tid < (ulong)_types.Count) return _types[(int)tid];
        if (tid != (ulong)_types.Count) throw ByteReader.Corrupt("bad type id");
        string name = In.Str() ?? throw ByteReader.Corrupt("type name");
        string sig = In.Str() ?? throw ByteReader.Corrupt("type signature");
        // (11a-2) Fast path: the layout this build has (the normal case) reads through the generated codecs. Any other
        // layout (or a type that is gone / renamed, or one holding blueprint indices to translate) gets a read plan.
        var type = _mig == null ? TypeNames.TryResolve(name) : _mig.Resolve(name);
        if (_mig == null && type != null && TypeNames.Of(type) != name) type = Migration.Resolve(name); // renamed: report it
        RefCodec? codec = null;
        if (type != null)
        {
            try { codec = RefCodec.For(type); }
            catch (NotSupportedException e) { throw new IncompatibleSaveException(e.Message); }
        }
        if (codec == null || codec.Signature != sig || (_mig != null && _mig.Forced(type!)))
            codec = Migration.CodecFor(name, sig, type);
        _types.Add(codec);
        return codec;
    }

    private MigrationContext? _mig;
    internal MigrationContext Migration => _mig ??= new MigrationContext();

    /// <summary>(11a-2) What this read had to migrate (null or empty = the data matched this build exactly).</summary>
    public SaveMigrationReport? MigrationReport => _mig?.Report;

    /// <summary>(11a-2) Saved external id → the object in this build (null entry = gone; null map = same ids), and the
    /// saved stable path of each id the save uses (null entry = not used by the save).</summary>
    internal object?[]? ExternalMap;
    internal string?[]? ExternalPaths;

    /// <summary>(11a-2) &gt; 0 while bytes of a dropped field / type are skipped (a gone external then reads as dropped).</summary>
    internal int Skipping;

    /// <summary>(11a-2) &gt; 0 while a read plan reads a reference it turns into null when its type is gone.</summary>
    internal int AcceptDropped;

    internal void AddFixup(Action a) => _fixups.Add(a);
    internal void AddAfterLoad(IAfterLoad o) => _afterLoad.Add(o);

    /// <summary>Reads every pending object body, then runs the fixups (dictionary inserts).</summary>
    public void Complete()
    {
        while (_pending.TryDequeue(out var p)) p.codec.ReadBody(this, p.obj);
        foreach (var f in _fixups) f();
        _fixups.Clear();
        if (_mig != null)
        {
            foreach (var f in _mig.PostFixups) f();
            _mig.PostFixups.Clear();
        }
    }

    /// <summary>Calls <see cref="IAfterLoad.AfterLoad"/> on the loaded objects (in load order).</summary>
    public void RunAfterLoad()
    {
        _mig?.RunMigratedHooks();
        foreach (var o in _afterLoad) o.AfterLoad();
        _afterLoad.Clear();
    }

    /// <summary>Mirror of <see cref="GraphWriter.ResetObjects"/>.</summary>
    public void ResetObjects()
    {
        Complete();
        _objs.Clear();
    }

    /// <summary>Continues the stream from another buffer (type table kept; call after <see cref="ResetObjects"/>).
    /// Used by multiplayer turns: one stream per player, cut into one buffer per turn.</summary>
    public void ContinueWith(ByteReader input) => In = input;
}
