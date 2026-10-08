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

    /// <summary>Writes the file back out in the layout it was read with.</summary>
    public byte[] ToBinary() => Tagfiles.Count > 0
        ? HkTagfileWriter.Write(Tagfiles[0])
        : HkPackfileWriter.Write(Packfiles, new HkTarget(Main.Abi, Main.LittleEndian));

    /// <summary>Writes a BotW packfile for <paramref name="target"/>, first changing its contents to that platform's form (in place).</summary>
    public byte[] ToBinary(HkTarget target)
    {
        if (Tagfiles.Count > 0) throw new NotSupportedException("Tagfiles have a single layout; use ToBinary().");
        HkConvert.ToPlatform(Packfiles, target);
        return HkPackfileWriter.Write(Packfiles, target);
    }

    public IEnumerable<HkObject> Objects => Packfiles.SelectMany(p => p.Objects).Concat(Tagfiles.SelectMany(t => t.Objects));

    public IEnumerable<HkObject> OfClass(string name) => Objects.Where(o => o.Class?.IsA(name) ?? o.ClassName == name);
}
