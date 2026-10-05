namespace HkxSharp;

public readonly record struct HkAbi(int PointerSize, bool ReusePadding)
{
    public static readonly HkAbi WiiU = new(4, false);
    public static readonly HkAbi Switch = new(8, true);
}

public enum HkKind
{
    Bool, U8, S8, U16, S16, U32, S32, U64, S64, F32, Half, Ulong,
    Vec4, Quat, Mat3, Rot, Transform, QsTransform, Mat4,
    Vptr, Ptr, String, Array, SmallArray, RelArray, Fixed, Struct
}

public sealed class HkType
{
    public HkKind Kind { get; }
    public HkType? Elem { get; }
    public int Count { get; }
    public string? ClassName { get; }
    public HkClass? Class { get; internal set; }

    HkType(HkKind kind, HkType? elem = null, int count = 0, string? cls = null) { Kind = kind; Elem = elem; Count = count; ClassName = cls; }

    static readonly Dictionary<HkKind, HkType> Prims = Enum.GetValues<HkKind>().Where(k => k < HkKind.Ptr).ToDictionary(k => k, k => new HkType(k));

    public static HkType Prim(HkKind k) => Prims[k];
    public static HkType Pointer(string? cls) => new(HkKind.Ptr, cls: cls);
    public static HkType Str => new(HkKind.String);
    public static HkType ArrayOf(HkType e) => new(HkKind.Array, e);
    public static HkType SmallArrayOf(HkType e) => new(HkKind.SmallArray, e);
    public static HkType RelArrayOf(HkType e) => new(HkKind.RelArray, e);
    public static HkType FixedOf(HkType e, int n) => new(HkKind.Fixed, e, n);
    public static HkType StructOf(string cls) => new(HkKind.Struct, cls: cls);

    public bool IsPrimitive => Kind < HkKind.Vptr;
    public bool IsVector => Kind is >= HkKind.Vec4 and <= HkKind.Mat4;

    public int FloatCount => Kind switch
    {
        HkKind.Vec4 or HkKind.Quat => 4,
        HkKind.Mat3 or HkKind.Rot => 12,
        HkKind.QsTransform => 12,
        HkKind.Transform or HkKind.Mat4 => 16,
        _ => 0
    };

    public int Size(HkAbi a) => Kind switch
    {
        HkKind.Bool or HkKind.U8 or HkKind.S8 => 1,
        HkKind.U16 or HkKind.S16 or HkKind.Half => 2,
        HkKind.U32 or HkKind.S32 or HkKind.F32 => 4,
        HkKind.U64 or HkKind.S64 => 8,
        HkKind.Ulong or HkKind.Vptr or HkKind.Ptr or HkKind.String => a.PointerSize,
        HkKind.Vec4 or HkKind.Quat => 16,
        HkKind.Mat3 or HkKind.Rot or HkKind.QsTransform => 48,
        HkKind.Transform or HkKind.Mat4 => 64,
        HkKind.Array => a.PointerSize + 8,
        HkKind.SmallArray => a.PointerSize + 4,
        HkKind.RelArray => 4,
        HkKind.Fixed => Elem!.Size(a) * Count,
        HkKind.Struct => Class!.Layout(a).Size,
        _ => throw new InvalidOperationException()
    };

    public int Align(HkAbi a) => Kind switch
    {
        HkKind.Fixed => Elem!.Align(a),
        HkKind.Struct => Class!.Layout(a).Align,
        HkKind.Array or HkKind.SmallArray => a.PointerSize,
        HkKind.RelArray => 2,
        _ when IsVector => 16,
        _ => Size(a)
    };

    public override string ToString() => Kind switch
    {
        HkKind.Ptr => $"{ClassName ?? "void"}*",
        HkKind.Array => $"hkArray<{Elem}>",
        HkKind.SmallArray => $"hkSmallArray<{Elem}>",
        HkKind.RelArray => $"hkRelArray<{Elem}>",
        HkKind.Fixed => $"{Elem}[{Count}]",
        HkKind.Struct => ClassName!,
        _ => Kind.ToString()
    };
}

public sealed record HkMember(string Name, HkType Type, bool NoSave = false);

public sealed class HkLayout
{
    public required int Size, DataSize, Align;
    public required bool HasVptr;
    public required int[] Offsets;
    public required int VptrOffset;
}

public sealed class HkClass
{
    public required string Name { get; init; }
    public string? ParentName { get; init; }
    public HkClass? Parent { get; internal set; }
    public bool Virtual { get; init; }
    public int ForceAlign { get; init; }
    public uint Signature { get; init; }
    public List<HkMember> Members { get; } = [];
    public HkTagType? TagType { get; init; }
    internal HkLayout? Explicit { get; set; }

    readonly HkLayout?[] _layouts = new HkLayout?[4];
    HkMember[]? _flat;
    Dictionary<string, int>? _index;

    public IReadOnlyList<HkMember> FlatMembers => _flat ??= (Parent?.FlatMembers ?? []).Concat(Members).ToArray();

    public int IndexOf(string name)
    {
        _index ??= FlatMembers.Select((m, i) => (m.Name, i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.Last().i);
        return _index.GetValueOrDefault(name, -1);
    }

    public int[] FlatOffsets(HkAbi a) => AllMembers(a).Select(m => m.Offset).ToArray();

    public IEnumerable<(HkMember Member, int Offset)> AllMembers(HkAbi a)
    {
        if (Parent is not null)
            foreach (var m in Parent.AllMembers(a)) yield return m;
        var l = Layout(a);
        for (int i = 0; i < Members.Count; i++) yield return (Members[i], l.Offsets[i]);
    }

    public bool IsA(string name)
    {
        for (var c = this; c is not null; c = c.Parent)
            if (c.Name == name) return true;
        return false;
    }

    public HkLayout Layout(HkAbi a)
    {
        if (Explicit is not null) return Explicit;
        int slot = (a.PointerSize == 8 ? 2 : 0) + (a.ReusePadding ? 1 : 0);
        if (_layouts[slot] is { } cached) return cached;
        int off = 0, align = 1;
        bool vptr = false;
        if (Parent is not null)
        {
            var p = Parent.Layout(a);
            off = a.ReusePadding ? p.DataSize : p.Size;
            align = p.Align;
            vptr = p.HasVptr;
        }
        int vptrOff = -1;
        if (Virtual && !vptr)
        {
            vptrOff = 0;
            off = a.PointerSize;
            align = Math.Max(align, a.PointerSize);
            vptr = true;
        }
        var offs = new int[Members.Count];
        for (int i = 0; i < Members.Count; i++)
        {
            var t = Members[i].Type;
            int al = t.Align(a);
            off = (off + al - 1) / al * al;
            offs[i] = off;
            off += t.Size(a);
            align = Math.Max(align, al);
        }
        align = Math.Max(align, ForceAlign);
        int size = (off + align - 1) / align * align;
        var l = new HkLayout { Size = size, DataSize = off, Align = align, HasVptr = vptr, Offsets = offs, VptrOffset = vptrOff };
        _layouts[slot] = l;
        return l;
    }

    public override string ToString() => Name;
}
