using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace HkxSharp;

sealed class HkTagDecoder
{
    readonly HkTagfile _f;
    readonly byte[] _d;
    readonly BitArray _claimed;
    readonly HashSet<int> _patched = [];
    readonly HashSet<int> _usedPatches = [];
    readonly HashSet<int> _usedItems = [0];
    readonly Dictionary<int, HkObject> _byItem = [];
    readonly Dictionary<int, HkObject> _byOffset = [];
    readonly Dictionary<HkTagType, HkClass> _classes = [];
    readonly Dictionary<HkClass, HkTagField[]> _flat = [];
    readonly int[] _starts;
    readonly ILookup<int, int> _itemsAt;
    string _ctx = "";
    int _ctxBase;

    public HkTagDecoder(HkTagfile f)
    {
        _f = f;
        _d = f.Data;
        _claimed = new BitArray(_d.Length);
        foreach (var (_, offs) in f.Patches) _patched.UnionWith(offs);
        _starts = f.Items.Skip(1).Select(i => i.Offset).Distinct().Order().ToArray();
        _itemsAt = Enumerable.Range(1, f.Items.Count - 1).ToLookup(i => f.Items[i].Offset);
    }

    static HkTagType Canonical(HkTagType t) => t.Canonical;

    readonly Dictionary<(HkTagType, string), HkTagField?> _fields = [];
    readonly Dictionary<HkTagType, int> _floats = [];

    HkTagField? Field(HkTagType t, string name)
    {
        if (_fields.TryGetValue((t, name), out var r)) return r;
        return _fields[(t, name)] = FindField(t, name);
    }

    static HkTagField? FindField(HkTagType t, string name)
    {
        for (var c = t; c is not null; c = c.Parent)
            foreach (var f in c.Fields)
                if (f.Name == name) return f;
        return null;
    }

    static bool IsFloat32(HkTagType? t) => t is not null && Canonical(t) is { Kind: HkTagKind.Float } c && c.ResolvedSize == 4;

    int FloatCount(HkTagType t)
    {
        if (_floats.TryGetValue(t, out var n)) return n;
        return _floats[t] = FindFloatCount(t);
    }

    static int FindFloatCount(HkTagType t)
    {
        var c = Canonical(t);
        if (c.Kind == HkTagKind.Array && c.IsTuple && IsFloat32(c.ResolvedSubType)) return c.TupleCount;
        return c.Kind == HkTagKind.Record && c.IsA("hkQsTransformf") ? 12 : 0;
    }

    void Issue(string kind, int at, string detail) => _f.Issues.Add(new HkIssue(kind, _ctx, at - _ctxBase, detail));

    void Owner(int at, string kind, string detail)
    {
        int idx = Array.BinarySearch(_starts, at);
        if (idx < 0) idx = ~idx - 1;
        int start = idx < 0 ? 0 : _starts[idx];
        var name = _byOffset.TryGetValue(start, out var o) ? o.ClassName : _itemsAt[start].Select(i => _f.Items[i].Type?.FullName).FirstOrDefault(n => n is not null) ?? "";
        _f.Issues.Add(new HkIssue(kind, name, at - start, detail));
    }

    public void DecodeAll()
    {
        for (int i = 1; i < _f.Items.Count; i++)
        {
            var it = _f.Items[i];
            if (!it.IsObject || it.Type is null) continue;
            var t = Canonical(it.Type);
            var o = new HkObject { ClassName = t.FullName, Signature = t.Hash, Offset = it.Offset, Class = t.Kind == HkTagKind.Record ? ClassOf(t) : null };
            _byItem[i] = o;
            _byOffset.TryAdd(it.Offset, o);
            _f.Objects.Add(o);
        }
        _f.Root = _byItem.GetValueOrDefault(1);
        foreach (var (i, o) in _byItem.OrderBy(kv => kv.Value.Offset))
        {
            _usedItems.Add(i);
            _ctx = o.ClassName;
            _ctxBase = o.Offset;
            if (o.Class is null)
            {
                Issue("non-record-object", o.Offset, o.ClassName);
                continue;
            }
            o.Fields = Struct(o.Class, o.Offset);
        }
        _ctx = "";
        _ctxBase = 0;
        foreach (var at in _patched)
            if (!_usedPatches.Contains(at)) Owner(at, "unused-patch", "patched pointer not covered by any member");
        for (int i = 1; i < _f.Items.Count; i++)
            if (!_usedItems.Contains(i) && _f.Items[i].Count > 0) Owner(_f.Items[i].Offset, "unused-item", $"item {i} ({_f.Items[i].Type}) x{_f.Items[i].Count} never referenced");
        int first = -1, total = 0;
        for (int i = 0; i <= _d.Length; i++)
        {
            bool hit = i < _d.Length && !_claimed[i] && _d[i] != 0;
            if (hit && first >= 0 && Array.BinarySearch(_starts, i) >= 0 && total > 0)
            {
                Owner(first, "unclaimed", $"{total} nonzero bytes not covered by any member");
                first = -1;
                total = 0;
            }
            if (hit)
            {
                if (first < 0) first = i;
                total++;
            }
            else if (first >= 0 && (i == _d.Length || _claimed[i]))
            {
                Owner(first, "unclaimed", $"{total} nonzero bytes not covered by any member");
                first = -1;
                total = 0;
            }
        }
    }

    HkClass ClassOf(HkTagType t)
    {
        t = Canonical(t);
        if (_classes.TryGetValue(t, out var cls)) return cls;
        var parent = t.Parent is { } p && Canonical(p).Kind == HkTagKind.Record ? Canonical(p) : null;
        cls = new HkClass { Name = t.FullName, ParentName = parent?.FullName, Signature = t.Hash, TagType = t };
        _classes[t] = cls;
        if (parent is not null) cls.Parent = ClassOf(parent);
        foreach (var f in t.Fields) cls.Members.Add(new HkMember(f.Name, TypeOf(f.Type)));
        int size = t.ResolvedSize;
        cls.Explicit = new HkLayout
        {
            Size = size, DataSize = size, Align = Math.Max(1, t.ResolvedAlign), HasVptr = false, VptrOffset = -1,
            Offsets = t.Fields.Select(f => f.Offset).ToArray()
        };
        return cls;
    }

    HkType TypeOf(HkTagType t)
    {
        var c = Canonical(t);
        int size = c.ResolvedSize;
        switch (c.Kind)
        {
            case HkTagKind.Bool: return HkType.Prim(HkKind.Bool);
            case HkTagKind.String: return HkType.Str;
            case HkTagKind.Int:
                return (size, c.Signed) switch
                {
                    (1, false) => HkType.Prim(HkKind.U8), (1, true) => HkType.Prim(HkKind.S8),
                    (2, false) => HkType.Prim(HkKind.U16), (2, true) => HkType.Prim(HkKind.S16),
                    (4, false) => HkType.Prim(HkKind.U32), (4, true) => HkType.Prim(HkKind.S32),
                    (_, false) => HkType.Prim(HkKind.U64), (_, true) => HkType.Prim(HkKind.S64)
                };
            case HkTagKind.Float: return HkType.Prim(size == 2 ? HkKind.Half : HkKind.F32);
            case HkTagKind.Pointer:
                return HkType.Pointer(c.ResolvedSubType is { } s && Canonical(s).Kind != HkTagKind.Void ? Canonical(s).FullName : null);
            case HkTagKind.Record:
                if (c.IsA("hkQsTransformf")) return HkType.Prim(HkKind.QsTransform);
                var st = HkType.StructOf(c.FullName);
                st.Class = ClassOf(c);
                return st;
            case HkTagKind.Array:
                var elem = c.ResolvedSubType;
                if (c.IsTuple)
                {
                    if (IsFloat32(elem))
                    {
                        if (c.IsA("hkVector4f")) return HkType.Prim(HkKind.Vec4);
                        if (c.IsA("hkQuaternionf")) return HkType.Prim(HkKind.Quat);
                        if (c.IsA("hkRotationImpl")) return HkType.Prim(HkKind.Rot);
                        if (c.IsA("hkMatrix3Impl")) return HkType.Prim(HkKind.Mat3);
                        if (c.IsA("hkTransformf")) return HkType.Prim(HkKind.Transform);
                        if (c.IsA("hkMatrix4Impl")) return HkType.Prim(HkKind.Mat4);
                    }
                    return HkType.FixedOf(elem is null ? HkType.Prim(HkKind.U8) : TypeOf(elem), c.TupleCount);
                }
                var et = elem is null ? HkType.Prim(HkKind.U8) : TypeOf(elem);
                return Field(c, "offset") is not null ? HkType.RelArrayOf(et) : HkType.ArrayOf(et);
            default:
                if (c.Kind == HkTagKind.Void && size == 0 && c.Name != "void")
                {
                    var empty = HkType.StructOf(c.FullName);
                    empty.Class = ClassOf(c);
                    return empty;
                }
                return HkType.FixedOf(HkType.Prim(HkKind.U8), size);
        }
    }

    HkTagField[] FlatFields(HkClass cls)
    {
        if (_flat.TryGetValue(cls, out var r)) return r;
        r = (cls.Parent is null ? [] : FlatFields(cls.Parent)).Concat(cls.TagType!.Fields).ToArray();
        _flat[cls] = r;
        return r;
    }

    void Claim(int at, int n)
    {
        if (at < 0 || n < 0 || at + n > _d.Length) throw new InvalidDataException($"{_ctx}: read 0x{at:X}+{n} outside data");
        for (int i = 0; i < n; i++) _claimed[at + i] = true;
    }

    HkStruct Struct(HkClass cls, int at)
    {
        var fields = FlatFields(cls);
        var vals = new object?[fields.Length];
        Claim(at, 0);
        for (int i = 0; i < fields.Length; i++) vals[i] = Value(fields[i].Type, at + fields[i].Offset);
        return new HkStruct(cls, vals);
    }

    long Int(HkTagType t, int at)
    {
        var c = Canonical(t);
        int size = c.ResolvedSize;
        Claim(at, size);
        var s = _d.AsSpan(at);
        return (size, c.Signed) switch
        {
            (1, false) => s[0], (1, true) => (sbyte)s[0],
            (2, false) => BinaryPrimitives.ReadUInt16LittleEndian(s), (2, true) => BinaryPrimitives.ReadInt16LittleEndian(s),
            (4, false) => BinaryPrimitives.ReadUInt32LittleEndian(s), (4, true) => BinaryPrimitives.ReadInt32LittleEndian(s),
            (8, _) => BinaryPrimitives.ReadInt64LittleEndian(s),
            _ => throw new InvalidDataException($"{_ctx}: {size} byte integer")
        };
    }

    float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(_d.AsSpan(at));

    object? Value(HkTagType t, int at)
    {
        var c = Canonical(t);
        int size = c.ResolvedSize;
        switch (c.Kind)
        {
            case HkTagKind.Bool: return Int(c, at) != 0;
            case HkTagKind.Int:
            {
                long v = Int(c, at);
                return (size, c.Signed) switch
                {
                    (1, false) => (byte)v, (1, true) => (sbyte)v, (2, false) => (ushort)v, (2, true) => (short)v,
                    (4, false) => (uint)v, (4, true) => (int)v, (_, false) => (ulong)v, _ => (object)v
                };
            }
            case HkTagKind.Float:
                Claim(at, size);
                return size switch
                {
                    2 => (float)BinaryPrimitives.ReadHalfLittleEndian(_d.AsSpan(at)),
                    4 => F32(at),
                    8 => BinaryPrimitives.ReadDoubleLittleEndian(_d.AsSpan(at)),
                    _ => throw new InvalidDataException($"{_ctx}: {size} byte float")
                };
            case HkTagKind.String: return String(at);
            case HkTagKind.Pointer: return Pointer(c, at);
            case HkTagKind.Record:
                if (FloatCount(c) is > 0 and var n) return Floats(at, n);
                return Struct(ClassOf(c), at);
            case HkTagKind.Array: return ArrayOf(c, at);
            default:
                if (c.Kind == HkTagKind.Void && size == 0 && c.Name != "void") return Struct(ClassOf(c), at);
                Claim(at, size);
                return _d[at..(at + size)];
        }
    }

    float[] Floats(int at, int n)
    {
        Claim(at, n * 4);
        var r = new float[n];
        for (int i = 0; i < n; i++) r[i] = F32(at + i * 4);
        return r;
    }

    int ItemIndex(int at)
    {
        Claim(at, 8);
        _usedPatches.Add(at);
        long v = BinaryPrimitives.ReadInt64LittleEndian(_d.AsSpan(at));
        if (v != 0 && !_patched.Contains(at)) Issue("raw-ptr", at, $"item index {v} without patch");
        if (v < 0 || v >= _f.Items.Count) throw new InvalidDataException($"{_ctx}+0x{at - _ctxBase:X}: item index {v} out of range");
        _usedItems.Add((int)v);
        return (int)v;
    }

    string? String(int at)
    {
        int item = ItemIndex(at);
        if (item == 0) return null;
        var it = _f.Items[item];
        Claim(it.Offset, it.Count);
        var s = _d.AsSpan(it.Offset, it.Count);
        int z = s.IndexOf((byte)0);
        return Encoding.UTF8.GetString(z < 0 ? s : s[..z]);
    }

    object? Pointer(HkTagType c, int at)
    {
        if (Field(c, "offset") is { } off)
        {
            long delta = Int(off.Type, at + off.Offset);
            Claim(at, c.ResolvedSize);
            if (delta == 0) return null;
            int target = (int)(at + off.Offset + delta);
            Relative(target, 1, at);
            if (_byOffset.TryGetValue(target, out var o)) return o;
            if (c.ResolvedSubType is { } sub && Canonical(sub).ResolvedSize > 0) return Value(sub, target);
            Issue("ptr-data", at, $"relative pointer to 0x{target:X} of unknown type");
            return new HkDataPtr(0, target);
        }
        int item = ItemIndex(at);
        if (item == 0) return null;
        if (_byItem.TryGetValue(item, out var obj))
        {
            if (c.ResolvedSubType is { } s && Canonical(s) is { Kind: HkTagKind.Record } want && obj.Class?.TagType is { } got && !got.IsA(want.Name))
                Issue("ptr-class", at, $"expected {want.FullName}, got {obj.ClassName}");
            return obj;
        }
        Issue("ptr-data", at, $"pointer to non-object item {item}");
        return new HkDataPtr(0, _f.Items[item].Offset);
    }

    void Relative(int target, int n, int at)
    {
        foreach (int i in _itemsAt[target])
        {
            _usedItems.Add(i);
            if (_f.Items[i].Count != n) Issue("array-count", at, $"relative reference to {n} but item {i} holds {_f.Items[i].Count}");
        }
    }

    object ArrayOf(HkTagType c, int at)
    {
        var elem = c.ResolvedSubType;
        if (c.IsTuple) return Elements(elem, at, c.TupleCount);
        if (Field(c, "offset") is { } off)
        {
            long delta = Int(off.Type, at + off.Offset);
            var sz = Field(c, "size") ?? throw new InvalidDataException($"{_ctx}: relative array {c} without size");
            int n = (int)Int(sz.Type, at + sz.Offset);
            Claim(at, c.ResolvedSize);
            if (n < 0) throw new InvalidDataException($"{_ctx}+0x{at - _ctxBase:X}: bad array count {n}");
            if (n == 0) return Elements(elem, 0, 0);
            int target = (int)(at + off.Offset + delta);
            Relative(target, n, at);
            return Elements(elem, target, n);
        }
        int item = ItemIndex(at);
        Claim(at, c.ResolvedSize);
        int stored = BinaryPrimitives.ReadInt32LittleEndian(_d.AsSpan(at + 8));
        if (item == 0)
        {
            if (stored != 0) Issue("array-count", at, $"size {stored} without data item");
            return Elements(elem, 0, 0);
        }
        var it = _f.Items[item];
        if (stored != 0 && stored != it.Count) Issue("array-count", at, $"size {stored} but item holds {it.Count}");
        return Elements(elem, it.Offset, it.Count);
    }

    object Elements(HkTagType? e, int at, int n)
    {
        var c = e is null ? null : Canonical(e);
        int size = c?.ResolvedSize ?? 0;
        bool signed = c?.Signed ?? false;
        int fc = c is null ? 0 : FloatCount(c);
        if (n == 0)
            return c?.Kind switch
            {
                HkTagKind.Bool => Array.Empty<bool>(),
                HkTagKind.Int => (size, signed) switch
                {
                    (1, false) => Array.Empty<byte>(), (1, true) => Array.Empty<sbyte>(),
                    (2, false) => Array.Empty<ushort>(), (2, true) => Array.Empty<short>(),
                    (4, false) => Array.Empty<uint>(), (4, true) => Array.Empty<int>(),
                    (_, false) => Array.Empty<ulong>(), _ => (object)Array.Empty<long>()
                },
                HkTagKind.Float => Array.Empty<float>(),
                _ when fc > 0 => Array.Empty<float>(),
                _ => Array.Empty<object?>()
            };
        if (c is null || size <= 0) throw new InvalidDataException($"{_ctx}+0x{at - _ctxBase:X}: array of {n} with unsized element {e}");
        if (at < 0 || at + (long)size * n > _d.Length) throw new InvalidDataException($"{_ctx}: array 0x{at:X} x{n} runs outside data");
        var s = _d.AsSpan(at);
        switch (c.Kind)
        {
            case HkTagKind.Bool: { Claim(at, n * size); var r = new bool[n]; for (int i = 0; i < n; i++) r[i] = s[i * size] != 0; return r; }
            case HkTagKind.Int:
                Claim(at, n * size);
                switch (size, signed)
                {
                    case (1, false): return s[..n].ToArray();
                    case (1, true): { var r = new sbyte[n]; for (int i = 0; i < n; i++) r[i] = (sbyte)s[i]; return r; }
                    case (2, false): { var r = new ushort[n]; for (int i = 0; i < n; i++) r[i] = BinaryPrimitives.ReadUInt16LittleEndian(s[(i * 2)..]); return r; }
                    case (2, true): { var r = new short[n]; for (int i = 0; i < n; i++) r[i] = BinaryPrimitives.ReadInt16LittleEndian(s[(i * 2)..]); return r; }
                    case (4, false): { var r = new uint[n]; for (int i = 0; i < n; i++) r[i] = BinaryPrimitives.ReadUInt32LittleEndian(s[(i * 4)..]); return r; }
                    case (4, true): { var r = new int[n]; for (int i = 0; i < n; i++) r[i] = BinaryPrimitives.ReadInt32LittleEndian(s[(i * 4)..]); return r; }
                    case (8, false): { var r = new ulong[n]; for (int i = 0; i < n; i++) r[i] = BinaryPrimitives.ReadUInt64LittleEndian(s[(i * 8)..]); return r; }
                    case (8, true): { var r = new long[n]; for (int i = 0; i < n; i++) r[i] = BinaryPrimitives.ReadInt64LittleEndian(s[(i * 8)..]); return r; }
                }
                break;
            case HkTagKind.Float when size is 2 or 4:
            {
                Claim(at, n * size);
                var r = new float[n];
                for (int i = 0; i < n; i++) r[i] = size == 2 ? (float)BinaryPrimitives.ReadHalfLittleEndian(s[(i * 2)..]) : F32(at + i * 4);
                return r;
            }
            case var _ when fc > 0:
            {
                Claim(at, n * size);
                var r = new float[n * fc];
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < fc; k++) r[i * fc + k] = F32(at + i * size + k * 4);
                return r;
            }
        }
        var o = new object?[n];
        for (int i = 0; i < n; i++) o[i] = Value(c, at + i * size);
        return o;
    }
}
