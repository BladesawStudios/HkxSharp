using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace HkxSharp;

sealed class HkDecoder
{
    readonly HkPackfile _pf;
    readonly HkSection _sec;
    readonly byte[] _d;
    readonly bool _big;
    readonly HkAbi _abi;
    readonly int _ptr;
    readonly BitArray _claimed;
    readonly HashSet<int> _usedFixups = [];
    readonly Dictionary<int, HkObject> _objects;
    readonly List<HkIssue> _issues;
    readonly int[] _starts;
    string _ctx = "";
    int _ctxBase;

    public HkDecoder(HkPackfile pf, Dictionary<int, HkObject> objects, List<HkIssue> issues)
    {
        _pf = pf;
        _sec = pf.Sections[pf.DataSection];
        _d = _sec.Data;
        _big = pf.BigEndian;
        _abi = new HkAbi(pf.PointerSize, pf.ReusePadding);
        _ptr = pf.PointerSize;
        _claimed = new BitArray(_d.Length);
        _objects = objects;
        _issues = issues;
        _starts = objects.Keys.Order().ToArray();
    }

    void Issue(string kind, int at, string detail) => _issues.Add(new HkIssue(kind, _ctx, at - _ctxBase, detail));

    public void DecodeAll(HkRegistry reg)
    {
        foreach (var o in _objects.Values.OrderBy(o => o.Offset))
        {
            _ctx = o.ClassName;
            _ctxBase = o.Offset;
            var cls = reg.Find(o.ClassName);
            o.Class = cls;
            if (cls is null)
            {
                Issue("unknown-class", o.Offset, o.ClassName);
                o.Opaque = Opaque(o.Offset);
                continue;
            }
            var size = cls.Layout(_abi).Size;
            int next = NextStart(o.Offset);
            if (o.Offset + size > next) Issue("overlap", o.Offset + size, $"size 0x{size:X} runs past next object at +0x{next - o.Offset:X}");
            o.Fields = Struct(cls, o.Offset);
        }
        foreach (var src in _sec.LocalFixups.Keys.Concat(_sec.GlobalFixups.Keys))
            if (!_usedFixups.Contains(src)) Owner(src, "unused-fixup", "pointer not covered by any member");
        int obj = -1, first = 0, total = 0;
        for (int i = 0; i <= _d.Length; i++)
        {
            int idx = -1;
            if (i < _d.Length)
            {
                if (_claimed[i] || _d[i] == 0) continue;
                idx = Array.BinarySearch(_starts, i);
                if (idx < 0) idx = ~idx - 1;
            }
            if (idx != obj && total > 0)
            {
                Owner(first, "unclaimed", $"{total} nonzero bytes not covered by any member");
                total = 0;
            }
            if (idx != obj) { obj = idx; first = i; }
            total++;
        }
    }

    void Owner(int at, string kind, string detail)
    {
        int idx = Array.BinarySearch(_starts, at);
        if (idx < 0) idx = ~idx - 1;
        if (idx < 0) { _issues.Add(new HkIssue(kind, "", at, detail)); return; }
        var o = _objects[_starts[idx]];
        _issues.Add(new HkIssue(kind, o.ClassName, at - o.Offset, detail));
    }

    int NextStart(int off)
    {
        int idx = Array.BinarySearch(_starts, off);
        idx = idx < 0 ? ~idx : idx + 1;
        return idx < _starts.Length ? _starts[idx] : _d.Length;
    }

    HkOpaque Opaque(int off)
    {
        int end = NextStart(off);
        var ptrs = new List<(int, object?)>();
        for (int at = off; at < end; at++)
            if (_sec.LocalFixups.ContainsKey(at) || _sec.GlobalFixups.ContainsKey(at))
                ptrs.Add((at - off, Pointer(at, null)));
        Claim(off, end - off);
        return new HkOpaque { Data = _d[off..end], Pointers = ptrs };
    }

    void Claim(int at, int n)
    {
        if (at < 0 || at + n > _d.Length) throw new InvalidDataException($"{_ctx}: read 0x{at:X}+{n} outside data section");
        for (int i = 0; i < n; i++) _claimed[at + i] = true;
    }

    HkStruct Struct(HkClass cls, int at)
    {
        var l = cls.Layout(_abi);
        Claim(at, 0);
        var offs = cls.FlatOffsets(_abi);
        var mem = cls.FlatMembers;
        var vals = new object?[mem.Count];
        if (l.HasVptr)
        {
            var v = Ulong(at);
            if (v != 0) Issue("vptr", at, $"nonzero vtable 0x{v:X}");
        }
        for (int i = 0; i < mem.Count; i++) vals[i] = Value(mem[i].Type, at + offs[i]);
        return new HkStruct(cls, vals);
    }

    ulong Ulong(int at)
    {
        Claim(at, _ptr);
        return _ptr == 8 ? U64(at) : U32(at);
    }

    ushort U16(int at) => _big ? BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(_d.AsSpan(at));
    uint U32(int at) => _big ? BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(_d.AsSpan(at));
    ulong U64(int at) => _big ? BinaryPrimitives.ReadUInt64BigEndian(_d.AsSpan(at)) : BinaryPrimitives.ReadUInt64LittleEndian(_d.AsSpan(at));
    float F32(int at) => BitConverter.Int32BitsToSingle((int)U32(at));

    object? Value(HkType t, int at)
    {
        switch (t.Kind)
        {
            case HkKind.Ptr: return Pointer(at, t.ClassName);
            case HkKind.String: return Pointer(at, "char");
            case HkKind.Vptr:
            {
                var v = Ulong(at);
                if (v != 0) Issue("vptr", at, $"nonzero secondary vtable 0x{v:X}");
                return null;
            }
            case HkKind.Struct: return Struct(t.Class!, at);
            case HkKind.Fixed: return Elements(t.Elem!, at, t.Count);
            case HkKind.Array:
            case HkKind.SmallArray:
            {
                int count;
                uint cap;
                if (t.Kind == HkKind.Array)
                {
                    Claim(at + _ptr, 8);
                    count = (int)U32(at + _ptr);
                    cap = U32(at + _ptr + 4);
                }
                else
                {
                    Claim(at + _ptr, 4);
                    count = U16(at + _ptr);
                    cap = U16(at + _ptr + 2);
                }
                var target = Target(at);
                if (count < 0 || count > 0x1000000) throw new InvalidDataException($"{_ctx}+0x{at - _ctxBase:X}: bad array count {count}");
                if (t.Kind == HkKind.Array && cap != ((uint)count | 0x80000000u)) Issue("array-cap", at, $"count {count} capacityAndFlags 0x{cap:X8}");
                if (count == 0)
                {
                    if (target is not null) Issue("array-ptr", at, "empty array with data pointer");
                    return Elements(t.Elem!, 0, 0);
                }
                if (target is not { } tg) throw new InvalidDataException($"{_ctx}+0x{at - _ctxBase:X}: array of {count} without data pointer");
                if (tg.Section != _pf.DataSection) throw new InvalidDataException("array data outside data section");
                if (_objects.ContainsKey(tg.Offset)) Issue("array-obj", at, "array data points at an object");
                return Elements(t.Elem!, tg.Offset, count);
            }
            case HkKind.RelArray:
            {
                Claim(at, 4);
                int count = U16(at), rel = U16(at + 2);
                return Elements(t.Elem!, count == 0 ? 0 : at + rel, count);
            }
            default: return Prim(t, at);
        }
    }

    (int Section, int Offset)? Target(int at)
    {
        Claim(at, _ptr);
        if (_sec.LocalFixups.TryGetValue(at, out var dst)) { _usedFixups.Add(at); return (_pf.DataSection, dst); }
        if (_sec.GlobalFixups.TryGetValue(at, out var g)) { _usedFixups.Add(at); return g; }
        var raw = _ptr == 8 ? U64(at) : U32(at);
        if (raw != 0) Issue("raw-ptr", at, $"pointer 0x{raw:X} without fixup");
        return null;
    }

    object? Pointer(int at, string? cls)
    {
        if (Target(at) is not { } tg) return null;
        if (cls == "char")
        {
            if (tg.Section != _pf.DataSection) return new HkDataPtr(tg.Section, tg.Offset);
            int z = Array.IndexOf(_d, (byte)0, tg.Offset);
            if (z < 0) throw new InvalidDataException("unterminated string");
            Claim(tg.Offset, z - tg.Offset + 1);
            return Encoding.UTF8.GetString(_d, tg.Offset, z - tg.Offset);
        }
        if (tg.Section == _pf.DataSection && _objects.TryGetValue(tg.Offset, out var o))
        {
            if (cls is not null && o.Class is { } oc && !oc.IsA(cls)) Issue("ptr-class", at, $"expected {cls}, got {o.ClassName}");
            return o;
        }
        Issue("ptr-data", at, $"pointer to non-object 0x{tg.Offset:X} (expected {cls ?? "void"})");
        return new HkDataPtr(tg.Section, tg.Offset);
    }

    object Elements(HkType e, int at, int n)
    {
        if (n == 0)
            return e.Kind switch
            {
                HkKind.Bool => Array.Empty<bool>(), HkKind.U8 => Array.Empty<byte>(), HkKind.S8 => Array.Empty<sbyte>(),
                HkKind.U16 => Array.Empty<ushort>(), HkKind.S16 => Array.Empty<short>(), HkKind.U32 => Array.Empty<uint>(),
                HkKind.S32 => Array.Empty<int>(), HkKind.U64 or HkKind.Ulong => Array.Empty<ulong>(), HkKind.S64 => Array.Empty<long>(),
                HkKind.F32 or HkKind.Half => Array.Empty<float>(),
                _ when e.IsVector => Array.Empty<float>(),
                _ => Array.Empty<object?>()
            };
        int stride = e.Size(_abi);
        if (at < 0 || at + (long)stride * n > _d.Length) throw new InvalidDataException($"{_ctx}: array 0x{at:X} x{n} runs outside data section");
        switch (e.Kind)
        {
            case HkKind.Bool: { var r = new bool[n]; Claim(at, n); for (int i = 0; i < n; i++) r[i] = _d[at + i] != 0; return r; }
            case HkKind.U8: Claim(at, n); return _d[at..(at + n)];
            case HkKind.S8: { var r = new sbyte[n]; Claim(at, n); for (int i = 0; i < n; i++) r[i] = (sbyte)_d[at + i]; return r; }
            case HkKind.U16: { var r = new ushort[n]; Claim(at, n * 2); for (int i = 0; i < n; i++) r[i] = U16(at + i * 2); return r; }
            case HkKind.S16: { var r = new short[n]; Claim(at, n * 2); for (int i = 0; i < n; i++) r[i] = (short)U16(at + i * 2); return r; }
            case HkKind.Half: { var r = new float[n]; Claim(at, n * 2); for (int i = 0; i < n; i++) r[i] = HalfToFloat(U16(at + i * 2)); return r; }
            case HkKind.U32: { var r = new uint[n]; Claim(at, n * 4); for (int i = 0; i < n; i++) r[i] = U32(at + i * 4); return r; }
            case HkKind.S32: { var r = new int[n]; Claim(at, n * 4); for (int i = 0; i < n; i++) r[i] = (int)U32(at + i * 4); return r; }
            case HkKind.F32: { var r = new float[n]; Claim(at, n * 4); for (int i = 0; i < n; i++) r[i] = F32(at + i * 4); return r; }
            case HkKind.U64: { var r = new ulong[n]; Claim(at, n * 8); for (int i = 0; i < n; i++) r[i] = U64(at + i * 8); return r; }
            case HkKind.S64: { var r = new long[n]; Claim(at, n * 8); for (int i = 0; i < n; i++) r[i] = (long)U64(at + i * 8); return r; }
            case HkKind.Ulong: { var r = new ulong[n]; for (int i = 0; i < n; i++) r[i] = Ulong(at + i * _ptr); return r; }
            case var _ when e.IsVector:
            {
                int fc = e.FloatCount;
                var r = new float[n * fc];
                Claim(at, n * stride);
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < fc; k++) r[i * fc + k] = F32(at + i * stride + k * 4);
                return r;
            }
            default:
            {
                var r = new object?[n];
                for (int i = 0; i < n; i++) r[i] = Value(e, at + i * stride);
                return r;
            }
        }
    }

    object Prim(HkType t, int at)
    {
        int size = t.Size(_abi);
        Claim(at, size);
        return t.Kind switch
        {
            HkKind.Bool => _d[at] != 0,
            HkKind.U8 => _d[at],
            HkKind.S8 => (sbyte)_d[at],
            HkKind.U16 => U16(at),
            HkKind.S16 => (short)U16(at),
            HkKind.Half => HalfToFloat(U16(at)),
            HkKind.U32 => U32(at),
            HkKind.S32 => (int)U32(at),
            HkKind.U64 => U64(at),
            HkKind.S64 => (long)U64(at),
            HkKind.F32 => F32(at),
            HkKind.Ulong => _ptr == 8 ? U64(at) : U32(at),
            _ when t.IsVector => Floats(at, t.FloatCount),
            _ => throw new InvalidOperationException(t.ToString())
        };
    }

    public static float HalfToFloat(ushort bits) => BitConverter.Int32BitsToSingle(bits << 16);

    float[] Floats(int at, int n)
    {
        var r = new float[n];
        for (int i = 0; i < n; i++) r[i] = F32(at + i * 4);
        return r;
    }
}
