using System.Buffers.Binary;
using System.Text;

namespace HkxSharp;

public sealed class HkSection
{
    public required string Tag { get; init; }
    public required int Start { get; init; }
    public required byte[] Data { get; init; }
    public Dictionary<int, int> LocalFixups { get; } = [];
    public Dictionary<int, (int Section, int Offset)> GlobalFixups { get; } = [];
    public List<(int Offset, int Section, int NameOffset)> VirtualFixups { get; } = [];
    public int ExportsSize { get; init; }
    public int ImportsSize { get; init; }
}

public sealed class HkPackfile
{
    public const uint Magic0 = 0x57E0E057, Magic1 = 0x10C0C010;

    public int UserTag { get; private init; }
    public int FileVersion { get; private init; }
    public int PointerSize { get; private init; }
    public bool LittleEndian { get; private init; }
    public bool ReusePadding { get; private init; }
    public bool EmptyBaseClass { get; private init; }
    public string ContentsVersion { get; private init; } = "";
    public int Flags { get; private init; }
    public int MaxPredicate { get; private init; }
    public byte[] Predicates { get; private init; } = [];
    public int ContentsSection { get; private init; }
    public int ContentsOffset { get; private init; }
    public int ContentsClassSection { get; private init; }
    public int ContentsClassOffset { get; private init; }
    public List<HkSection> Sections { get; } = [];
    public Dictionary<int, (uint Signature, string Name)> ClassNames { get; } = [];
    public int ClassNameSection { get; private set; } = -1;
    public int DataSection { get; private set; } = -1;
    public int Length { get; private init; }
    public int FileOffset { get; private set; }
    public List<HkObject> Objects { get; } = [];
    public HkObject? Root { get; private set; }
    public List<HkIssue> Issues { get; } = [];
    public HkAbi Abi => new(PointerSize, ReusePadding);

    public bool BigEndian => !LittleEndian;

    public short[] PredicateValues
    {
        get
        {
            var r = new short[Predicates.Length / 2];
            for (int i = 0; i < r.Length; i++)
                r[i] = LittleEndian ? BinaryPrimitives.ReadInt16LittleEndian(Predicates.AsSpan(i * 2)) : BinaryPrimitives.ReadInt16BigEndian(Predicates.AsSpan(i * 2));
            return r;
        }
    }

    public static bool IsPackfile(ReadOnlySpan<byte> d) =>
        d.Length >= 8 && (BinaryPrimitives.ReadUInt32BigEndian(d) == Magic0 && BinaryPrimitives.ReadUInt32BigEndian(d[4..]) == Magic1
                          || BinaryPrimitives.ReadUInt32LittleEndian(d) == Magic0 && BinaryPrimitives.ReadUInt32LittleEndian(d[4..]) == Magic1);

    public static List<HkPackfile> ReadAll(ReadOnlySpan<byte> data) => ReadAll(data, HkRegistry.Default);

    public static List<HkPackfile> ReadAll(ReadOnlySpan<byte> data, HkRegistry? registry)
    {
        var res = new List<HkPackfile>();
        int at = 0;
        while (at < data.Length && IsPackfile(data[at..]))
        {
            var p = FromBinary(data[at..], registry);
            p.FileOffset = at;
            res.Add(p);
            at += (p.Length + 15) & ~15;
            while (at < data.Length && at + 8 <= data.Length && !IsPackfile(data[at..]) && data[at] == 0) at += 16;
        }
        if (res.Count == 0) throw new InvalidDataException("Not a Havok packfile.");
        return res;
    }

    public static HkPackfile FromBinary(ReadOnlySpan<byte> d) => FromBinary(d, HkRegistry.Default);

    public static HkPackfile FromBinary(ReadOnlySpan<byte> d, HkRegistry? registry)
    {
        if (d.Length < 0x40 || !IsPackfile(d)) throw new InvalidDataException("Not a Havok packfile.");
        int ptr = d[0x10];
        bool le = d[0x11] != 0;
        int I32(ReadOnlySpan<byte> s, int at) => le ? BinaryPrimitives.ReadInt32LittleEndian(s[at..]) : BinaryPrimitives.ReadInt32BigEndian(s[at..]);
        short I16(ReadOnlySpan<byte> s, int at) => le ? BinaryPrimitives.ReadInt16LittleEndian(s[at..]) : BinaryPrimitives.ReadInt16BigEndian(s[at..]);
        if (ptr != 4 && ptr != 8) throw new InvalidDataException($"Unsupported pointer size {ptr}.");
        int version = I32(d, 0x0C);
        if (version != 11) throw new InvalidDataException($"Unsupported packfile version {version}.");
        int predPad = I16(d, 0x3E);
        int numSections = I32(d, 0x14);
        int sh = 0x40 + predPad;
        int end = 0;
        var secs = new List<HkSection>();
        for (int i = 0; i < numSections; i++)
        {
            var h = d.Slice(sh + i * 0x40, 0x40);
            int z = h[..20].IndexOf((byte)0);
            var tag = Encoding.ASCII.GetString(h[..(z < 0 ? 19 : Math.Min(z, 19))]);
            int start = I32(h, 0x14);
            int[] o = new int[7];
            for (int k = 0; k < 6; k++) o[k] = I32(h, 0x18 + k * 4);
            int local = o[0], global = o[1], virt = o[2], exports = o[3], imports = o[4], sEnd = o[5];
            if (start < 0 || start + sEnd > d.Length || local > global || global > virt || virt > exports || exports > imports || imports > sEnd)
                throw new InvalidDataException($"Section {tag} has invalid offsets.");
            var s = new HkSection { Tag = tag, Start = start, Data = d.Slice(start, local).ToArray(), ExportsSize = imports - exports, ImportsSize = sEnd - imports };
            var sd = d.Slice(start, sEnd);
            for (int at = local; at + 8 <= global; at += 8)
            {
                int src = I32(sd, at);
                if (src == -1) continue;
                s.LocalFixups[src] = I32(sd, at + 4);
            }
            for (int at = global; at + 12 <= virt; at += 12)
            {
                int src = I32(sd, at);
                if (src == -1) continue;
                s.GlobalFixups[src] = (I32(sd, at + 4), I32(sd, at + 8));
            }
            for (int at = virt; at + 12 <= exports; at += 12)
            {
                int src = I32(sd, at);
                if (src == -1) continue;
                s.VirtualFixups.Add((src, I32(sd, at + 4), I32(sd, at + 8)));
            }
            end = Math.Max(end, start + sEnd);
            secs.Add(s);
        }

        var p = new HkPackfile
        {
            UserTag = I32(d, 0x08), FileVersion = version, PointerSize = ptr, LittleEndian = le,
            ReusePadding = d[0x12] != 0, EmptyBaseClass = d[0x13] != 0,
            ContentsSection = I32(d, 0x18), ContentsOffset = I32(d, 0x1C),
            ContentsClassSection = I32(d, 0x20), ContentsClassOffset = I32(d, 0x24),
            ContentsVersion = CStr(d.Slice(0x28, 16)), Flags = I32(d, 0x38), MaxPredicate = I16(d, 0x3C),
            Predicates = d.Slice(0x40, predPad).ToArray(), Length = end
        };
        p.Sections.AddRange(secs);
        p.ClassNameSection = p.Sections.FindIndex(s => s.Tag == "__classnames__");
        p.DataSection = p.Sections.FindIndex(s => s.Tag == "__data__");
        if (p.ClassNameSection >= 0)
        {
            var cn = p.Sections[p.ClassNameSection].Data;
            for (int at = 0; at + 5 < cn.Length;)
            {
                if (cn[at] == 0xFF) break;
                uint sig = le ? BinaryPrimitives.ReadUInt32LittleEndian(cn.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(cn.AsSpan(at));
                if (cn[at + 4] != 0x09) break;
                var name = CStr(cn.AsSpan(at + 5));
                p.ClassNames[at + 5] = (sig, name);
                at += 5 + name.Length + 1;
            }
        }
        if (p.DataSection >= 0)
        {
            var ds = p.Sections[p.DataSection];
            var byOff = new Dictionary<int, HkObject>();
            foreach (var v in ds.VirtualFixups)
            {
                if (v.Section != p.ClassNameSection || !p.ClassNames.TryGetValue(v.NameOffset, out var cn))
                    throw new InvalidDataException($"Virtual fixup at 0x{v.Offset:X} names an unknown class.");
                var o = new HkObject { ClassName = cn.Name, Signature = cn.Signature, Offset = v.Offset };
                byOff[v.Offset] = o;
                p.Objects.Add(o);
            }
            if (p.ContentsSection == p.DataSection) p.Root = byOff.GetValueOrDefault(p.ContentsOffset);
            if (registry is not null) new HkDecoder(p, byOff, p.Issues).DecodeAll(registry);
        }
        return p;
    }

    static string CStr(ReadOnlySpan<byte> s)
    {
        int z = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(z < 0 ? s : s[..z]);
    }
}
