namespace HkxSharp;

public sealed class HkObject
{
    public required string ClassName { get; init; }
    public required uint Signature { get; init; }
    public required int Offset { get; init; }
    public HkClass? Class { get; internal set; }
    public HkStruct? Fields { get; internal set; }
    public HkOpaque? Opaque { get; internal set; }

    public object? this[string name] => Fields?[name];

    public override string ToString() => $"{ClassName}@0x{Offset:X}";
}

public sealed class HkStruct(HkClass cls, object?[] values)
{
    public HkClass Class => cls;
    public IReadOnlyList<HkMember> Members => cls.FlatMembers;
    public object?[] Values => values;

    public object? this[string name]
    {
        get
        {
            var i = cls.IndexOf(name);
            return i < 0 ? throw new KeyNotFoundException($"{cls.Name}.{name}") : values[i];
        }
    }

    public T Get<T>(string name) => (T)this[name]!;
}

public sealed record HkDataPtr(int Section, int Offset);

public sealed class HkOpaque
{
    public required byte[] Data { get; init; }
    public required List<(int Offset, object? Target)> Pointers { get; init; }
}

public sealed record HkIssue(string Kind, string Class, int Offset, string Detail)
{
    public override string ToString() => $"{Kind} {Class}+0x{Offset:X}: {Detail}";
}
