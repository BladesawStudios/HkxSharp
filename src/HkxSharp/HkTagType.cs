namespace HkxSharp;

public enum HkTagKind { Void, Opaque, Bool, String, Int, Float, Pointer, Record, Array }

public sealed record HkTagTemplate(string Name, int Value, HkTagType? Type);

public sealed record HkTagField(string Name, int Flags, int Offset, HkTagType Type, int Extra = 0);

public sealed record HkTagItem(HkTagType? Type, int Flags, int Offset, int Count)
{
    public bool IsObject => (Flags & 0x10) != 0;
}

public sealed class HkTagType
{
    public int Index { get; init; }
    public string Name { get; internal set; } = "";
    public List<HkTagTemplate> Templates { get; } = [];
    public HkTagType? Parent { get; internal set; }
    public int Presence { get; internal set; }
    public int Format { get; internal set; }
    public HkTagType? SubType { get; internal set; }
    public int Version { get; internal set; }
    public int Size { get; internal set; }
    public int AlignAndFlags { get; internal set; }
    public int AbstractValue { get; internal set; }
    public List<HkTagField> Fields { get; } = [];
    public List<(HkTagType Type, int Value)> Interfaces { get; } = [];
    public int Attribute { get; internal set; }
    public uint Hash { get; internal set; }
    public HkClass? Class { get; internal set; }

    public string FullName => Templates.Count == 0 ? Name : $"{Name}<{string.Join(", ", Templates.Select(t => t.Type?.FullName ?? t.Value.ToString()))}>";

    IEnumerable<HkTagType> Chain()
    {
        int guard = 0;
        for (var t = this; t is not null && guard++ < 64; t = t.Parent) yield return t;
    }

    int? _format, _size, _align;
    HkTagType? _sub;
    bool _subDone;
    HkTagType? _canonical;

    public int ResolvedFormat => _format ??= Chain().FirstOrDefault(t => t.Format != 0)?.Format ?? 0;
    public int ResolvedSize => _size ??= Chain().FirstOrDefault(t => t.Size != 0)?.Size ?? 0;
    public int ResolvedAlign => _align ??= (Chain().FirstOrDefault(t => t.AlignAndFlags != 0)?.AlignAndFlags ?? 0) & 0xFF;
    public HkTagType? ResolvedSubType
    {
        get
        {
            if (!_subDone) { _sub = Chain().FirstOrDefault(t => t.SubType is not null)?.SubType; _subDone = true; }
            return _sub;
        }
    }
    public HkTagType Canonical => _canonical ??= Chain().FirstOrDefault(t => t.Presence != 0 || t.Parent is null) ?? this;
    public HkTagKind Kind => (HkTagKind)(ResolvedFormat & 0xF);
    public bool Signed => (ResolvedFormat & 0x200) != 0;
    public bool IsTuple => Kind == HkTagKind.Array && (ResolvedFormat & 0x20) != 0;
    public int TupleCount => ResolvedFormat >> 8;
    public HkTagType? Template(string name) => Chain().SelectMany(t => t.Templates).FirstOrDefault(t => t.Name == name)?.Type;

    public bool IsA(string name) => Chain().Any(t => t.Name == name);

    public override string ToString() => FullName;
}
