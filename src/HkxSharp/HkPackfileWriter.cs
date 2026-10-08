using System.Buffers.Binary;
using System.Text;

namespace HkxSharp;

/// <summary>The layout a packfile is written for: Wii U is 32-bit big endian, Switch 64-bit little endian with padding reuse.</summary>
public readonly record struct HkTarget(HkAbi Abi, bool LittleEndian)
{
    public static readonly HkTarget WiiU = new(HkAbi.WiiU, false);
    public static readonly HkTarget Switch = new(HkAbi.Switch, true);

    /// <summary>The hclClothData.targetPlatform value cloth is built with: HCL_PLATFORM_WIIU on Wii U, 0 on Switch.</summary>
    public uint ClothPlatform => LittleEndian ? 0u : 0x2000u;
}

/// <summary>
/// Writes a decoded packfile back out, laid out the way Havok's packfile writer does it: objects in the order they are
/// reached from the root, each followed by the data its members point at, every block padded to 16. Writing for the
/// other platform only changes the layout; <see cref="HkConvert.ToPlatform(HkPackfile, HkTarget)"/> changes the contents.
/// </summary>
public sealed class HkPackfileWriter
{
    static readonly (uint Signature, string Name)[] Builtin =
    [
        (0x33D42383, "hkClass"), (0xB0EFA719, "hkClassMember"), (0x8A3609CF, "hkClassEnum"), (0xCE6F8A6C, "hkClassEnumItem")
    ];

    readonly HkPackfile _pf;
    readonly HkAbi _abi;
    readonly bool _le;
    readonly int _ptr;
    readonly MemoryStream _data = new();
    readonly List<(int Src, int Dst)> _local = [];
    readonly List<(int Src, HkObject Target)> _global = [];
    readonly List<(int Offset, HkObject Object)> _virtual = [];
    readonly Dictionary<HkObject, int> _placed = [];
    readonly List<HkObject> _order = [];

    HkPackfileWriter(HkPackfile pf, HkTarget target)
    {
        _pf = pf;
        _abi = target.Abi;
        _le = target.LittleEndian;
        _ptr = target.Abi.PointerSize;
    }

    public static byte[] Write(HkPackfile pf) => Write(pf, new HkTarget(pf.Abi, pf.LittleEndian));

    public static byte[] Write(HkPackfile pf, HkTarget target) => new HkPackfileWriter(pf, target).Run();

    /// <summary>
    /// Writes several packfiles back to back, as the static compound files store them. A StaticCompoundInfo's offset is
    /// the size of its own packfile (where the next one starts), so it is set to match the layout written.
    /// </summary>
    public static byte[] Write(IEnumerable<HkPackfile> packfiles, HkTarget target)
    {
        var o = new MemoryStream();
        foreach (var p in packfiles)
        {
            var bytes = Write(p, target);
            foreach (var info in p.Objects.Where(x => x.ClassName == "StaticCompoundInfo" && x.Fields is not null))
            {
                int i = info.Fields!.Class.IndexOf("offset");
                if (i < 0 || info.Fields.Values[i] is uint v && v == (uint)bytes.Length) continue;
                info.Fields.Values[i] = (uint)bytes.Length;
                bytes = Write(p, target);
            }
            o.Write(bytes);
            while (o.Length % 16 != 0) o.WriteByte(0);
        }
        return o.ToArray();
    }

    byte[] Run()
    {
        foreach (var o in _pf.Root is { } root ? Reachable(root) : []) Place(o);
        foreach (var o in _pf.Objects) Place(o);

        var names = new MemoryStream();
        var nameOffsets = new Dictionary<string, int>();
        void Name(uint sig, string name)
        {
            if (nameOffsets.ContainsKey(name)) return;
            Span<byte> s = stackalloc byte[4];
            U32(s, sig);
            names.Write(s);
            names.WriteByte(0x09);
            nameOffsets[name] = (int)names.Position;
            names.Write(Encoding.ASCII.GetBytes(name));
            names.WriteByte(0);
        }
        foreach (var (sig, name) in Builtin) Name(sig, name);
        foreach (var o in _order) Name(o.Signature, o.ClassName);
        Pad(names, 0xFF);

        var data = _data.ToArray();
        var fixups = new MemoryStream();
        var buf = new byte[12];
        foreach (var (src, dst) in _local)
        {
            I32(buf, src); I32(buf.AsSpan(4), dst);
            fixups.Write(buf, 0, 8);
        }
        Pad(fixups, 0xFF);
        int globalStart = (int)fixups.Position;
        foreach (var (src, target) in _global)
        {
            I32(buf, src); I32(buf.AsSpan(4), _pf.DataSection); I32(buf.AsSpan(8), _placed[target]);
            fixups.Write(buf, 0, 12);
        }
        Pad(fixups, 0xFF);
        int virtualStart = (int)fixups.Position;
        foreach (var (off, o) in _virtual.OrderBy(x => x.Offset))
        {
            I32(buf, off); I32(buf.AsSpan(4), _pf.ClassNameSection); I32(buf.AsSpan(8), nameOffsets[o.ClassName]);
            fixups.Write(buf, 0, 12);
        }
        Pad(fixups, 0xFF);
        int end = (int)fixups.Position;

        int headerSize = 0x40 + _pf.Predicates.Length;
        var sections = _pf.Sections.Select(s => s.Tag).ToList();
        int start = headerSize + sections.Count * 0x40;
        var outp = new MemoryStream();
        var h = new byte[0x40];
        U32(h, HkPackfile.Magic0);
        U32(h.AsSpan(4), HkPackfile.Magic1);
        I32(h.AsSpan(8), _pf.UserTag);
        I32(h.AsSpan(0x0C), _pf.FileVersion);
        h[0x10] = (byte)_ptr;
        h[0x11] = (byte)(_le ? 1 : 0);
        h[0x12] = (byte)(_abi.ReusePadding ? 1 : 0);
        h[0x13] = (byte)(_pf.EmptyBaseClass ? 1 : 0);
        I32(h.AsSpan(0x14), sections.Count);
        I32(h.AsSpan(0x18), _pf.ContentsSection);
        I32(h.AsSpan(0x1C), _pf.Root is { } r ? _placed[r] : _pf.ContentsOffset);
        I32(h.AsSpan(0x20), _pf.ContentsClassSection);
        I32(h.AsSpan(0x24), _pf.Root is { } rc ? nameOffsets[rc.ClassName] : _pf.ContentsClassOffset);
        h.AsSpan(0x28, 16).Fill(0xFF);
        var ver = Encoding.ASCII.GetBytes(_pf.ContentsVersion);
        ver.CopyTo(h.AsSpan(0x28));
        h[0x28 + ver.Length] = 0;
        I32(h.AsSpan(0x38), _pf.Flags);
        I16(h.AsSpan(0x3C), (short)_pf.MaxPredicate);
        I16(h.AsSpan(0x3E), (short)_pf.Predicates.Length);
        outp.Write(h);
        var preds = _pf.PredicateValues;
        for (int i = 0; i < preds.Length; i++)
        {
            I16(buf, preds[i]);
            outp.Write(buf, 0, 2);
        }

        var bodies = new List<byte[]>();
        foreach (var tag in sections)
        {
            byte[] body;
            int local, global, virt, exp;
            if (tag == "__classnames__") { body = names.ToArray(); local = global = virt = exp = body.Length; }
            else if (tag == "__data__")
            {
                body = [.. data, .. fixups.ToArray()];
                local = data.Length; global = data.Length + globalStart; virt = data.Length + virtualStart; exp = data.Length + end;
            }
            else { body = []; local = global = virt = exp = 0; }
            var sh = new byte[0x40];
            var tb = Encoding.ASCII.GetBytes(tag);
            tb.CopyTo(sh, 0);
            sh[19] = 0xFF;
            I32(sh.AsSpan(0x14), start);
            I32(sh.AsSpan(0x18), local);
            I32(sh.AsSpan(0x1C), global);
            I32(sh.AsSpan(0x20), virt);
            I32(sh.AsSpan(0x24), exp);
            I32(sh.AsSpan(0x28), exp);
            I32(sh.AsSpan(0x2C), exp);
            sh.AsSpan(0x30).Fill(0xFF);
            outp.Write(sh);
            bodies.Add(body);
            start += body.Length;
        }
        foreach (var b in bodies) outp.Write(b);
        return outp.ToArray();
    }

    static void Pad(MemoryStream s, byte fill)
    {
        while (s.Position % 16 != 0) s.WriteByte(fill);
    }

    IEnumerable<HkObject> Reachable(HkObject root)
    {
        var seen = new HashSet<HkObject>();
        var result = new List<HkObject>();
        void Visit(HkObject o)
        {
            if (!seen.Add(o)) return;
            result.Add(o);
            foreach (var c in Children(o)) Visit(c);
        }
        Visit(root);
        return result;
    }

    static IEnumerable<HkObject> Children(HkObject o)
    {
        var list = new List<HkObject>();
        void Walk(object? v)
        {
            switch (v)
            {
                case HkObject p: list.Add(p); break;
                case HkStruct s: foreach (var x in s.Values) Walk(x); break;
                case object?[] a: foreach (var x in a) Walk(x); break;
            }
        }
        if (o.Fields is { } f) Walk(f);
        return list;
    }

    void Place(HkObject o)
    {
        if (_placed.ContainsKey(o)) return;
        if (o.Fields is null || o.Class is null) throw new InvalidDataException($"{o.ClassName} was not decoded and cannot be written.");
        Align(16);
        int at = (int)_data.Position;
        _placed[o] = at;
        _order.Add(o);
        _virtual.Add((at, o));
        var cls = o.Class;
        Reserve(at, cls.Layout(_abi).Size);
        var deferred = new List<Action>();
        Struct(cls, o.Fields, at, deferred);
        Align(16);
        foreach (var d in deferred) d();
        Align(16);
    }

    void Align(int a)
    {
        while (_data.Position % a != 0) _data.WriteByte(0);
    }

    void Reserve(int at, int size)
    {
        if (_data.Length < at + size) _data.SetLength(at + size);
        _data.Position = at + size;
    }

    Span<byte> At(int at, int n) => _data.GetBuffer().AsSpan(at, n);

    void Struct(HkClass cls, HkStruct s, int at, List<Action> deferred)
    {
        var offs = cls.FlatOffsets(_abi);
        var mem = cls.FlatMembers;
        for (int i = 0; i < mem.Count; i++) Value(mem[i].Type, s.Values[i], at + offs[i], deferred);
    }

    /// <param name="member">False for the elements of an array: their strings are packed together rather than each padded to 16.</param>
    void Value(HkType t, object? v, int at, List<Action> deferred, bool member = true)
    {
        switch (t.Kind)
        {
            case HkKind.Vptr: return;
            case HkKind.Ptr:
                if (v is HkObject o) deferred.Add(() => _global.Add((at, o)));
                else if (v is not null) throw new InvalidDataException($"Pointer to {v.GetType().Name} cannot be written.");
                return;
            case HkKind.String:
                if (v is string str)
                {
                    int src = at;
                    deferred.Add(() =>
                    {
                        Align(2);
                        int dst = (int)_data.Position;
                        _local.Add((src, dst));
                        var b = Encoding.UTF8.GetBytes(str);
                        _data.Write(b);
                        _data.WriteByte(0);
                        if (member) Align(16);
                    });
                }
                else if (v is not null) throw new InvalidDataException($"String member holds {v.GetType().Name}.");
                return;
            case HkKind.Struct: Struct(t.Class!, (HkStruct)v!, at, deferred); return;
            case HkKind.Fixed:
            {
                int stride = t.Elem!.Size(_abi);
                for (int i = 0; i < t.Count; i++) Value(t.Elem, Element(t.Elem, v!, i), at + i * stride, deferred);
                return;
            }
            case HkKind.Array:
            case HkKind.SmallArray:
            {
                int n = Count(t.Elem!, v!);
                if (t.Kind == HkKind.Array)
                {
                    U32(At(at + _ptr, 4), (uint)n);
                    U32(At(at + _ptr + 4, 4), (uint)n | 0x80000000u);
                }
                else
                {
                    U16(At(at + _ptr, 2), (ushort)n);
                    U16(At(at + _ptr + 2, 2), (ushort)(n == 0 ? 0 : n | 0x8000));
                }
                if (n == 0) return;
                int src = at;
                deferred.Add(() =>
                {
                    var e = t.Elem!;
                    Align(16);
                    int dst = (int)_data.Position;
                    _local.Add((src, dst));
                    int stride = e.Size(_abi);
                    Reserve(dst, stride * n);
                    var nested = new List<Action>();
                    for (int i = 0; i < n; i++) Value(e, Element(e, v!, i), dst + i * stride, nested, false);
                    _data.Position = dst + stride * n;
                    foreach (var d in nested) d();
                    Align(16);
                });
                return;
            }
            default: Prim(t, v!, at); return;
        }
    }

    static int Count(HkType e, object v) => v switch
    {
        float[] f when e.IsVector => f.Length / e.FloatCount,
        Array a => a.Length,
        _ => throw new InvalidDataException($"Array value is {v.GetType().Name}.")
    };

    static object? Element(HkType e, object arr, int i)
    {
        if (e.IsVector)
        {
            int fc = e.FloatCount;
            return ((float[])arr).AsSpan(i * fc, fc).ToArray();
        }
        return ((Array)arr).GetValue(i);
    }

    void Prim(HkType t, object v, int at)
    {
        var s = At(at, t.Size(_abi));
        switch (t.Kind)
        {
            case HkKind.Bool: s[0] = (bool)v ? (byte)1 : (byte)0; break;
            case HkKind.U8: s[0] = (byte)v; break;
            case HkKind.S8: s[0] = (byte)(sbyte)v; break;
            case HkKind.U16: U16(s, (ushort)v); break;
            case HkKind.S16: U16(s, (ushort)(short)v); break;
            case HkKind.Half: U16(s, (ushort)(BitConverter.SingleToUInt32Bits((float)v) >> 16)); break;
            case HkKind.U32: U32(s, (uint)v); break;
            case HkKind.S32: U32(s, (uint)(int)v); break;
            case HkKind.F32: U32(s, BitConverter.SingleToUInt32Bits((float)v)); break;
            case HkKind.U64: U64(s, (ulong)v); break;
            case HkKind.S64: U64(s, (ulong)(long)v); break;
            case HkKind.Ulong:
                ulong u = Convert.ToUInt64(v);
                if (_ptr == 8) U64(s, u); else U32(s, (uint)u);
                break;
            default:
                if (!t.IsVector) throw new InvalidOperationException(t.ToString());
                var f = (float[])v;
                for (int i = 0; i < f.Length; i++) U32(s[(i * 4)..], BitConverter.SingleToUInt32Bits(f[i]));
                break;
        }
    }

    void U16(Span<byte> s, ushort v) { if (_le) BinaryPrimitives.WriteUInt16LittleEndian(s, v); else BinaryPrimitives.WriteUInt16BigEndian(s, v); }
    void I16(Span<byte> s, short v) => U16(s, (ushort)v);
    void U32(Span<byte> s, uint v) { if (_le) BinaryPrimitives.WriteUInt32LittleEndian(s, v); else BinaryPrimitives.WriteUInt32BigEndian(s, v); }
    void I32(Span<byte> s, int v) => U32(s, (uint)v);
    void U64(Span<byte> s, ulong v) { if (_le) BinaryPrimitives.WriteUInt64LittleEndian(s, v); else BinaryPrimitives.WriteUInt64BigEndian(s, v); }
}
