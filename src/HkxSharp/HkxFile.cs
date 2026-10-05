namespace HkxSharp;

public sealed class HkxFile
{
    public List<HkPackfile> Packfiles { get; } = [];
    public List<HkTagfile> Tagfiles { get; } = [];

    public HkPackfile Main => Packfiles[^1];

    public static HkxFile FromBinary(ReadOnlySpan<byte> data) => FromBinary(data, HkRegistry.Default);

    public static HkxFile FromBinary(ReadOnlySpan<byte> data, HkRegistry? registry)
    {
        var f = new HkxFile();
        if (HkTagfile.IsTagfile(data)) f.Tagfiles.Add(HkTagfile.FromBinary(data, registry is not null));
        else f.Packfiles.AddRange(HkPackfile.ReadAll(data, registry));
        return f;
    }

    public IEnumerable<HkObject> Objects => Packfiles.SelectMany(p => p.Objects).Concat(Tagfiles.SelectMany(t => t.Objects));

    public IEnumerable<HkObject> OfClass(string name) => Objects.Where(o => o.Class?.IsA(name) ?? o.ClassName == name);
}
