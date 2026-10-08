using System.Buffers.Binary;
using System.Text;

namespace HkxSharp;

/// <summary>
/// Writes a decoded TAG0 tagfile back out from its objects, the way Havok's tagfile writer lays it out. Objects are
/// numbered breadth first from the root. Each object, then each array it owns, hands out item numbers in two passes:
/// pointers and arrays first, strings second, walking arrays of records field by field across 1024 elements at a time. Equal strings
/// share one item. An object's data is followed by the arrays and strings it owns, in item order. The type table is the
/// file's own; types of objects brought in from another tagfile are added to it (and to <see cref="HkTagfile.Types"/>).
/// </summary>
public sealed class HkTagfileWriter
{
    sealed class Block
    {
        public required int Item;
        public required HkTagType Type;
        public required int Owner;
        public object? Value;
        public int Count = 1;
        public bool IsObject, IsString;
        public int Offset;
        public readonly Dictionary<int, Block> Slots = [];
    }

    readonly HkTagfile _f;
    readonly List<Block> _items = [null!];
    readonly Dictionary<HkObject, Block> _objects = [];
    readonly Dictionary<string, Block> _strings = new(StringComparer.Ordinal);
    readonly Queue<Block> _queue = [];
    readonly Dictionary<(HkTagType, string), HkTagField?> _fields = [];
    HkTagType? _char;
    byte[] _data = [];

    readonly HashSet<HkTagType> _local;
    readonly Dictionary<HkTagType, HkTagType> _imported = [];
    int _added;

    HkTagfileWriter(HkTagfile f)
    {
        _f = f;
        _local = f.Types.Where(t => t is not null).ToHashSet()!;
    }

    public static byte[] Write(HkTagfile f) => new HkTagfileWriter(f).Run();

    byte[] Run()
    {
        if (_f.Root is null) throw new InvalidDataException("The tagfile has no root object.");
        ObjectItem(_f.Root);
        while (_queue.Count > 0) Process(_queue.Dequeue());
        foreach (var o in _f.Objects.Where(o => !_objects.ContainsKey(o)))
        {
            ObjectItem(o);
            while (_queue.Count > 0) Process(_queue.Dequeue());
        }

        Place();
        foreach (var b in _items.Skip(1)) Emit(b);
        foreach (var b in _items.Skip(1)) Local(b.Type);
        foreach (var p in _patchSlots) Local(p.Type);

        var outp = new MemoryStream();
        outp.Write(new byte[8]);
        Chunk(outp, "SDKV", Encoding.ASCII.GetBytes(_f.SdkVersion), 4);
        Chunk(outp, "DATA", _data, 16);
        TypeChunk(outp);
        long indx = outp.Position;
        outp.Write(new byte[8]);
        Chunk(outp, "ITEM", Items(), 4);
        var patches = Patches();
        if (patches.Length > 0) Chunk(outp, "PTCH", patches, 4);
        Header(outp, indx, "INDX", false);
        Header(outp, 0, "TAG0", false);
        return outp.ToArray();
    }

    // Item numbering --------------------------------------------------------------------------------------------------

    Block ObjectItem(HkObject o)
    {
        if (_objects.TryGetValue(o, out var b)) return b;
        var type = o.Class?.TagType ?? throw new InvalidDataException($"{o.ClassName} has no tagfile type and cannot be written.");
        b = new Block { Item = _items.Count, Type = type, Owner = _items.Count, Value = o.Fields, IsObject = true };
        _items.Add(b);
        _objects[o] = b;
        _queue.Enqueue(b);
        return b;
    }

    Block NewItem(HkTagType type, object? value, int count, int owner, bool str = false)
    {
        var b = new Block { Item = _items.Count, Type = type, Owner = owner, Value = value, Count = count, IsString = str };
        _items.Add(b);
        return b;
    }

    /// <summary>An object, then the arrays it owns breadth first: each block numbers its pointers and arrays, then its strings.</summary>
    void Process(Block obj)
    {
        var pending = new Queue<Block>([obj]);
        var arrays = new List<Block>();
        while (pending.Count > 0)
        {
            var b = pending.Dequeue();
            var cells = Cells(b);
            arrays.Clear();
            Scan(b.Type, cells, b, false, arrays);
            Scan(b.Type, cells, b, true, arrays);
            foreach (var a in arrays) pending.Enqueue(a);
        }
    }

    List<(object? Value, int Rel)> Cells(Block b)
    {
        if (b.IsObject) return [(b.Value, 0)];
        if (b.IsString) return [];
        int size = b.Type.Canonical.ResolvedSize;
        var cells = new List<(object?, int)>(b.Count);
        for (int i = 0; i < b.Count; i++) cells.Add((ElementAt(b.Type, b.Value!, i), i * size));
        return cells;
    }

    void Scan(HkTagType t, List<(object? Value, int Rel)> cells, Block b, bool strings, List<Block> arrays)
    {
        if (cells.Count == 0) return;
        var c = t.Canonical;
        switch (c.Kind)
        {
            case HkTagKind.Record:
            {
                if (FloatCount(c) > 0) return;
                var fields = FlatFields(c);
                //Records are walked field by field across a batch of up to 1024 elements at a time.
                for (int start = 0; start < cells.Count; start += 1024)
                {
                    var batch = cells.GetRange(start, Math.Min(1024, cells.Count - start));
                    for (int i = 0; i < fields.Count; i++)
                    {
                        var f = fields[i];
                        Scan(f.Type, batch.Select(x => (((HkStruct)x.Value!).Values[i], x.Rel + f.Offset)).ToList(), b, strings, arrays);
                    }
                }
                return;
            }
            case HkTagKind.Pointer:
            {
                if (strings) return;
                bool relative = Field(c, "offset") is not null;
                foreach (var (v, rel) in cells)
                {
                    if (v is null) continue;
                    if (v is HkObject o) b.Slots[rel] = ObjectItem(o);
                    else if (relative && c.ResolvedSubType is { } sub)
                    {
                        var a = NewItem(sub, new[] { v }, 1, b.Owner);
                        b.Slots[rel] = a;
                        arrays.Add(a);
                    }
                    else throw new InvalidDataException($"Pointer value {v.GetType().Name} cannot be written.");
                    if (!relative) _patchSlots.Add((b, rel, t));
                }
                return;
            }
            case HkTagKind.String:
            {
                if (!strings) return;
                var ch = CharType(c);
                foreach (var (v, rel) in cells)
                {
                    if (v is not string s) continue;
                    if (!_strings.TryGetValue(s, out var item)) _strings[s] = item = NewItem(ch, s, Encoding.UTF8.GetByteCount(s) + 1, b.Owner, true);
                    b.Slots[rel] = item;
                    _patchSlots.Add((b, rel, t));
                }
                return;
            }
            case HkTagKind.Array:
            {
                var elem = c.ResolvedSubType;
                if (c.IsTuple)
                {
                    if (elem is null) return;
                    int size = elem.Canonical.ResolvedSize;
                    var inner = new List<(object?, int)>();
                    foreach (var (v, rel) in cells)
                        for (int k = 0; k < c.TupleCount; k++) inner.Add((ElementAt(elem, v!, k), rel + k * size));
                    Scan(elem, inner, b, strings, arrays);
                    return;
                }
                if (strings || elem is null) return;
                bool relative = Field(c, "offset") is not null;
                foreach (var (v, rel) in cells)
                {
                    int n = Count(elem, v);
                    if (n == 0) continue;
                    var a = NewItem(elem, v, n, b.Owner);
                    b.Slots[rel] = a;
                    arrays.Add(a);
                    if (!relative) _patchSlots.Add((b, rel, t));
                }
                return;
            }
        }
    }

    readonly List<(Block Block, int Rel, HkTagType Type)> _patchSlots = [];

    // Placement ------------------------------------------------------------------------------------------------------

    void Place()
    {
        int at = 0;
        var owned = _items.Skip(1).Where(b => !b.IsObject).ToLookup(b => b.Owner);
        foreach (var o in _items.Skip(1).Where(b => b.IsObject))
        {
            at = Align(at, Math.Max(1, o.Type.Canonical.ResolvedAlign));
            o.Offset = at;
            at += o.Type.Canonical.ResolvedSize;
            foreach (var b in owned[o.Item])
            {
                at = Align(at, b.IsString ? 2 : 16);
                b.Offset = at;
                at += b.IsString ? b.Count : b.Count * b.Type.Canonical.ResolvedSize;
            }
        }
        _data = new byte[Align(at, 16)];
    }

    static int Align(int v, int a) => (v + a - 1) / a * a;

    // Emission -------------------------------------------------------------------------------------------------------

    void Emit(Block b)
    {
        if (b.IsString)
        {
            Encoding.UTF8.GetBytes((string)b.Value!).CopyTo(_data, b.Offset);
            return;
        }
        if (b.IsObject)
        {
            Value(b.Type, b.Value, b, 0);
            return;
        }
        int size = b.Type.Canonical.ResolvedSize;
        for (int i = 0; i < b.Count; i++) Value(b.Type, ElementAt(b.Type, b.Value!, i), b, i * size);
    }

    void Value(HkTagType t, object? v, Block b, int rel)
    {
        var c = t.Canonical;
        int at = b.Offset + rel;
        int size = c.ResolvedSize;
        switch (c.Kind)
        {
            case HkTagKind.Bool: Int(at, size, (bool)v! ? 1 : 0); return;
            case HkTagKind.Int: Int(at, size, v is ulong u ? (long)u : Convert.ToInt64(v)); return;
            case HkTagKind.Float:
                var s = _data.AsSpan(at);
                double d = Convert.ToDouble(v);
                if (size == 2) BinaryPrimitives.WriteHalfLittleEndian(s, (Half)d);
                else if (size == 4) BinaryPrimitives.WriteSingleLittleEndian(s, (float)d);
                else BinaryPrimitives.WriteDoubleLittleEndian(s, d);
                return;
            case HkTagKind.String:
                Slot(b, rel, at);
                return;
            case HkTagKind.Pointer:
                if (Field(c, "offset") is { } po)
                {
                    if (b.Slots.TryGetValue(rel, out var target)) Int(at + po.Offset, po.Type.Canonical.ResolvedSize, target.Offset - (at + po.Offset));
                    return;
                }
                Slot(b, rel, at);
                return;
            case HkTagKind.Record:
                if (FloatCount(c) is > 0 and var n)
                {
                    var fl = (float[])v!;
                    for (int i = 0; i < n; i++) BinaryPrimitives.WriteSingleLittleEndian(_data.AsSpan(at + i * 4), fl[i]);
                    return;
                }
                var fields = FlatFields(c);
                var st = (HkStruct)v!;
                for (int i = 0; i < fields.Count; i++) Value(fields[i].Type, st.Values[i], b, rel + fields[i].Offset);
                return;
            case HkTagKind.Array:
            {
                var elem = c.ResolvedSubType;
                if (c.IsTuple)
                {
                    if (elem is null) return;
                    int es = elem.Canonical.ResolvedSize;
                    for (int k = 0; k < c.TupleCount; k++) Value(elem, ElementAt(elem, v!, k), b, rel + k * es);
                    return;
                }
                if (Field(c, "offset") is { } ao)
                {
                    var sz = Field(c, "size")!;
                    if (b.Slots.TryGetValue(rel, out var target))
                    {
                        Int(at + ao.Offset, ao.Type.Canonical.ResolvedSize, target.Offset - (at + ao.Offset));
                        Int(at + sz.Offset, sz.Type.Canonical.ResolvedSize, target.Count);
                    }
                    return;
                }
                Slot(b, rel, at);
                return;
            }
            default:
                if (v is byte[] raw) raw.CopyTo(_data, at);
                return;
        }
    }

    void Slot(Block b, int rel, int at)
    {
        if (b.Slots.TryGetValue(rel, out var target)) BinaryPrimitives.WriteInt64LittleEndian(_data.AsSpan(at), target.Item);
    }

    void Int(int at, int size, long v)
    {
        var s = _data.AsSpan(at);
        switch (size)
        {
            case 1: s[0] = (byte)v; break;
            case 2: BinaryPrimitives.WriteInt16LittleEndian(s, (short)v); break;
            case 4: BinaryPrimitives.WriteInt32LittleEndian(s, (int)v); break;
            default: BinaryPrimitives.WriteInt64LittleEndian(s, v); break;
        }
    }

    // Tables ---------------------------------------------------------------------------------------------------------

    byte[] Items()
    {
        var o = new byte[_items.Count * 12];
        for (int i = 1; i < _items.Count; i++)
        {
            var b = _items[i];
            uint flags = b.IsObject ? 0x10u : 0x20u;
            BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(i * 12), (uint)Local(b.Type).Index | flags << 24);
            BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(i * 12 + 4), b.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(o.AsSpan(i * 12 + 8), b.Count);
        }
        return o;
    }

    byte[] Patches()
    {
        var o = new MemoryStream();
        var groups = _patchSlots.Select(p => (Type: Local(p.Type), Offset: p.Block.Offset + p.Rel)).GroupBy(p => p.Type)
            .Select(g => (g.Key, Offsets: g.Select(p => p.Offset).Order().ToArray())).OrderBy(g => g.Key.Index);
        Span<byte> w = stackalloc byte[4];
        foreach (var (type, offsets) in groups)
        {
            BinaryPrimitives.WriteInt32LittleEndian(w, type.Index); o.Write(w);
            BinaryPrimitives.WriteInt32LittleEndian(w, offsets.Length); o.Write(w);
            foreach (var off in offsets) { BinaryPrimitives.WriteInt32LittleEndian(w, off); o.Write(w); }
        }
        return o.ToArray();
    }

    // Type table -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The file's own type for <paramref name="t"/>. A type from another tagfile maps to a type of this file with the same name,
    /// version, size and hash, or is added, together with every type it refers to.
    /// </summary>
    HkTagType Local(HkTagType t)
    {
        if (_local.Contains(t)) return t;
        if (_imported.TryGetValue(t, out var mapped)) return mapped;
        var match = _f.Types.Skip(1).FirstOrDefault(x => x!.FullName == t.FullName && x.Version == t.Version && x.ResolvedSize == t.ResolvedSize
                                                          && (x.Hash == t.Hash || x.Hash == 0 || t.Hash == 0));
        if (match is not null) return _imported[t] = match;

        var n = new HkTagType
        {
            Index = _f.Types.Count, Name = t.Name, Presence = t.Presence, Format = t.Format, Version = t.Version, Size = t.Size,
            AlignAndFlags = t.AlignAndFlags, AbstractValue = t.AbstractValue, Attribute = t.Attribute, Hash = t.Hash
        };
        _imported[t] = n;
        _added++;
        _f.Types.Add(n);
        _local.Add(n);
        foreach (var tp in t.Templates)
        {
            var tt = tp.Type is null ? null : Local(tp.Type);
            n.Templates.Add(new HkTagTemplate(tp.Name, tt?.Index ?? tp.Value, tt));
        }
        n.Parent = t.Parent is null ? null : Local(t.Parent);
        n.SubType = t.SubType is null ? null : Local(t.SubType);
        foreach (var f in t.Fields) n.Fields.Add(f with { Type = Local(f.Type) });
        foreach (var (it, v) in t.Interfaces) n.Interfaces.Add((Local(it), v));
        if (t.Presence != 0 || t.Parent is not null) _f.BodyOrder.Add(n);
        if (t.Hash != 0) _f.HashOrder.Add(n);
        return n;
    }

    void TypeChunk(MemoryStream o)
    {
        long start = o.Position;
        o.Write(new byte[8]);
        var typeStrings = new List<string>(_f.TypeStrings);
        var fieldStrings = new List<string>(_f.FieldStrings);
        var names = Names(typeStrings);
        var bodies = Bodies(fieldStrings);
        foreach (var (tag, body) in _f.TypeParts)
        {
            switch (tag)
            {
                case "TST1" or "TSTR": Chunk(o, tag, Strings(typeStrings), 4, 0xFF); break;
                case "TNA1" or "TNAM": Chunk(o, tag, names, 4); break;
                case "FST1" or "FSTR": Chunk(o, tag, Strings(fieldStrings), 4, 0xFF); break;
                case "TBDY" or "TBOD": Chunk(o, tag, bodies, 4); break;
                case "THSH": Chunk(o, tag, Hashes(), 4); break;
                case "TPTR": Chunk(o, tag, [.. body, .. new byte[_added * 8]], 4); break;
                default: Chunk(o, tag, body, 4); break;
            }
        }
        Header(o, start, "TYPE", false);
    }

    static int Intern(List<string> strings, string s)
    {
        int i = strings.IndexOf(s);
        if (i >= 0) return i;
        strings.Add(s);
        return strings.Count - 1;
    }

    static byte[] Strings(List<string> strings)
    {
        var o = new MemoryStream();
        foreach (var s in strings)
        {
            o.Write(Encoding.UTF8.GetBytes(s));
            o.WriteByte(0);
        }
        return o.ToArray();
    }

    byte[] Names(List<string> strings)
    {
        var o = new MemoryStream();
        Packed(o, _f.Types.Count);
        foreach (var t in _f.Types.Skip(1))
        {
            Packed(o, Intern(strings, t!.Name));
            Packed(o, t.Templates.Count);
            foreach (var tp in t.Templates)
            {
                Packed(o, Intern(strings, tp.Name));
                Packed(o, tp.Value);
            }
        }
        return o.ToArray();
    }

    byte[] Bodies(List<string> strings)
    {
        var o = new MemoryStream();
        foreach (var t in _f.BodyOrder)
        {
            Packed(o, t.Index);
            Packed(o, t.Parent?.Index ?? 0);
            int p = t.Presence;
            Packed(o, p);
            if ((p & 0x01) != 0) Packed(o, t.Format);
            if ((p & 0x02) != 0) Packed(o, t.SubType?.Index ?? 0);
            if ((p & 0x04) != 0) Packed(o, t.Version);
            if ((p & 0x08) != 0) { Packed(o, t.Size); Packed(o, t.AlignAndFlags); }
            if ((p & 0x10) != 0) Packed(o, t.AbstractValue);
            if ((p & 0x20) != 0)
            {
                Packed(o, t.Fields.Count);
                foreach (var f in t.Fields)
                {
                    Packed(o, Intern(strings, f.Name));
                    Packed(o, f.Flags);
                    if ((f.Flags & 0x80) != 0) Packed(o, f.Extra);
                    Packed(o, f.Offset);
                    Packed(o, f.Type.Index);
                }
            }
            if ((p & 0x40) != 0)
            {
                Packed(o, t.Interfaces.Count);
                foreach (var (it, v) in t.Interfaces) { Packed(o, it.Index); Packed(o, v); }
            }
            if ((p & 0x80) != 0) Packed(o, t.Attribute);
        }
        return o.ToArray();
    }

    byte[] Hashes()
    {
        var o = new MemoryStream();
        Packed(o, _f.HashOrder.Count);
        Span<byte> w = stackalloc byte[4];
        foreach (var t in _f.HashOrder)
        {
            Packed(o, t.Index);
            BinaryPrimitives.WriteUInt32LittleEndian(w, t.Hash);
            o.Write(w);
        }
        return o.ToArray();
    }

    /// <summary>The type tables' variable length integers: the top bits of the first byte give the length, the value is big endian.</summary>
    static void Packed(MemoryStream o, long v)
    {
        if (v < 0) throw new InvalidDataException($"Negative value {v} in the type table.");
        if (v < 0x80) o.WriteByte((byte)v);
        else if (v < 0x4000) { o.WriteByte((byte)(0x80 | v >> 8)); o.WriteByte((byte)v); }
        else if (v < 0x200000) { o.WriteByte((byte)(0xC0 | v >> 16)); o.WriteByte((byte)(v >> 8)); o.WriteByte((byte)v); }
        else if (v < 0x8000000) { o.WriteByte((byte)(0xE0 | v >> 24)); o.WriteByte((byte)(v >> 16)); o.WriteByte((byte)(v >> 8)); o.WriteByte((byte)v); }
        else { o.WriteByte(0xE8); for (int i = 3; i >= 0; i--) o.WriteByte((byte)(v >> (i * 8))); }
    }

    static void Chunk(MemoryStream o, string tag, byte[] body, int padding, byte fill = 0)
    {
        int padded = Align(body.Length, padding);
        Span<byte> h = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h, (uint)(8 + padded) | 0x40000000u);
        Encoding.ASCII.GetBytes(tag).CopyTo(h[4..]);
        o.Write(h);
        o.Write(body);
        for (int i = body.Length; i < padded; i++) o.WriteByte(fill);
    }

    static void Header(MemoryStream o, long start, string tag, bool leaf)
    {
        long end = o.Position;
        Span<byte> h = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(h, (uint)(end - start) | (leaf ? 0x40000000u : 0));
        Encoding.ASCII.GetBytes(tag).CopyTo(h[4..]);
        o.Position = start;
        o.Write(h);
        o.Position = end;
    }

    // Type helpers ---------------------------------------------------------------------------------------------------

    HkTagField? Field(HkTagType t, string name)
    {
        if (_fields.TryGetValue((t, name), out var r)) return r;
        for (var c = t; c is not null; c = c.Parent)
            foreach (var f in c.Fields)
                if (f.Name == name) return _fields[(t, name)] = f;
        return _fields[(t, name)] = null;
    }

    static List<HkTagField> FlatFields(HkTagType t)
    {
        var chain = new List<HkTagType>();
        for (var c = t; c is not null; c = c.Parent is { } p && p.Canonical.Kind == HkTagKind.Record ? p.Canonical : null) chain.Add(c);
        chain.Reverse();
        return chain.SelectMany(c => c.Fields).ToList();
    }

    HkTagType CharType(HkTagType stringType) =>
        _char ??= Field(stringType, "stringAndFlag")?.Type.Canonical.ResolvedSubType
                  ?? _f.FindType("char") ?? throw new InvalidDataException("The type table has no char type for strings.");

    static bool IsFloat32(HkTagType? t) => t is not null && t.Canonical is { Kind: HkTagKind.Float } c && c.ResolvedSize == 4;

    static int FloatCount(HkTagType t)
    {
        var c = t.Canonical;
        if (c.Kind == HkTagKind.Array && c.IsTuple && IsFloat32(c.ResolvedSubType)) return c.TupleCount;
        return c.Kind == HkTagKind.Record && c.IsA("hkQsTransformf") ? 12 : 0;
    }

    static int Count(HkTagType elem, object? v) => v switch
    {
        null => 0,
        float[] f when FloatCount(elem) is > 0 and var n => f.Length / n,
        Array a => a.Length,
        _ => throw new InvalidDataException($"Array value is {v.GetType().Name}.")
    };

    static object? ElementAt(HkTagType elem, object arr, int i)
    {
        if (FloatCount(elem) is > 0 and var n && arr is float[] f) return f.AsSpan(i * n, n).ToArray();
        return ((Array)arr).GetValue(i);
    }
}
