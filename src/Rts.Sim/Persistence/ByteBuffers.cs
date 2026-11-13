using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Rts.Sim.Persistence;

/// <summary>Growable little-endian byte buffer (fixed-size primitives, LEB128 varints, UTF-8 strings).</summary>
public sealed class ByteWriter
{
    private byte[] _buf;
    private int _len;

    public ByteWriter(int capacity = 1 << 16) => _buf = new byte[Math.Max(16, capacity)];

    public int Length => _len;
    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, _len);
    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();
    public void Clear() => _len = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<byte> Grow(int n)
    {
        if (_len + n > _buf.Length) Resize(n);
        var s = _buf.AsSpan(_len, n);
        _len += n;
        return s;
    }

    private void Resize(int n)
    {
        long want = Math.Max((long)_buf.Length * 2, (long)_len + n);
        if (want > Array.MaxLength) want = Math.Max((long)_len + n, Array.MaxLength);
        Array.Resize(ref _buf, (int)want);
    }

    public void U8(byte v) => Grow(1)[0] = v;

    public void VarU(ulong v)
    {
        while (v >= 0x80) { U8((byte)(v | 0x80)); v >>= 7; }
        U8((byte)v);
    }

    public void VarI(long v) => VarU((ulong)((v << 1) ^ (v >> 63)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Raw<T>(T v) where T : unmanaged => MemoryMarshal.Write(Grow(Unsafe.SizeOf<T>()), in v);

    public void Bytes(ReadOnlySpan<byte> s) => s.CopyTo(Grow(s.Length));

    /// <summary>(11a-2) Overwrites a value written earlier at byte <paramref name="pos"/> (a reserved slot).</summary>
    public void PatchRaw<T>(int pos, T v) where T : unmanaged => MemoryMarshal.Write(_buf.AsSpan(pos, Unsafe.SizeOf<T>()), in v);

    /// <summary>Length-prefixed byte block.</summary>
    public void Block(ReadOnlySpan<byte> s) { VarU((ulong)s.Length); Bytes(s); }

    public void Str(string? s)
    {
        if (s == null) { VarU(0); return; }
        int n = Encoding.UTF8.GetByteCount(s);
        VarU((ulong)n + 1);
        Encoding.UTF8.GetBytes(s, Grow(n));
    }
}

/// <summary>Bounds-checked reader over a byte array; every malformed input throws <see cref="SaveFormatException"/>.</summary>
public sealed class ByteReader
{
    private readonly byte[] _buf;
    private int _pos;
    private readonly int _end;

    public ByteReader(byte[] buf) : this(buf, 0, buf.Length) { }

    public ByteReader(byte[] buf, int start, int length)
    {
        if (start < 0 || length < 0 || start + length > buf.Length) throw new ArgumentOutOfRangeException(nameof(length));
        _buf = buf;
        _pos = start;
        _end = start + length;
    }

    public int Position => _pos;

    /// <summary>(11a-2) A second reader over the same data from absolute position <paramref name="pos"/> to the end.</summary>
    public ByteReader At(int pos) => pos >= _pos && pos <= _end ? new ByteReader(_buf, pos, _end - pos) : throw Corrupt("bad offset");
    public int Remaining => _end - _pos;
    public bool AtEnd => _pos >= _end;

    public static SaveFormatException Corrupt(string what) => new($"Damaged file ({what})");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> Take(int n)
    {
        if ((uint)n > (uint)(_end - _pos)) throw Corrupt("unexpected end of data");
        var s = _buf.AsSpan(_pos, n);
        _pos += n;
        return s;
    }

    public byte U8()
    {
        if (_pos >= _end) throw Corrupt("unexpected end of data");
        return _buf[_pos++];
    }

    public ulong VarU()
    {
        ulong v = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte b = U8();
            v |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return v;
        }
        throw Corrupt("bad varint");
    }

    public long VarI()
    {
        ulong u = VarU();
        return (long)(u >> 1) ^ -(long)(u & 1);
    }

    /// <summary>A count/length that must be &lt;= max (guards against absurd allocations from damaged data).</summary>
    public int Count(long max)
    {
        ulong v = VarU();
        if (v > (ulong)Math.Min(max, int.MaxValue)) throw Corrupt("count out of range");
        return (int)v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Raw<T>() where T : unmanaged => MemoryMarshal.Read<T>(Take(Unsafe.SizeOf<T>()));

    public void Bytes(Span<byte> dst) => Take(dst.Length).CopyTo(dst);

    public byte[] Block() => Take(Count(Remaining)).ToArray();

    public string? Str()
    {
        int n = Count((long)Remaining + 1);
        if (n == 0) return null;
        try { return Encoding.UTF8.GetString(Take(n - 1)); }
        catch (ArgumentException e) { throw new SaveFormatException("Damaged file (bad string)", e); }
    }
}

/// <summary>Fast non-cryptographic 64-bit hash (corruption detection and content fingerprints).</summary>
public static class Hash64
{
    public static ulong Of(ReadOnlySpan<byte> data, ulong seed = 0x9E3779B97F4A7C15UL)
    {
        const ulong P1 = 0x9E3779B185EBCA87UL, P2 = 0xC2B2AE3D27D4EB4FUL;
        ulong h = seed ^ ((ulong)data.Length * P1);
        var words = MemoryMarshal.Cast<byte, ulong>(data);
        foreach (ulong w in words) h = BitOperations.RotateLeft(h ^ (w * P2), 31) * P1;
        for (int i = words.Length * 8; i < data.Length; i++) h = BitOperations.RotateLeft(h ^ (data[i] * P2), 23) * P1;
        h ^= h >> 33; h *= P2; h ^= h >> 29; h *= P1; h ^= h >> 32;
        return h;
    }

    public static ulong Of(string s) => Of(Encoding.UTF8.GetBytes(s));
}
