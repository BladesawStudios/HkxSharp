using System.Buffers.Binary;
using System.Text;

namespace HkxSharp;

public sealed class HkTagfile
{
    public string SdkVersion { get; private set; } = "";
    public byte[] Data { get; private set; } = [];
    public List<HkTagType?> Types { get; } = [null];
    public List<HkTagItem> Items { get; } = [];
    public List<(HkTagType? Type, int[] Offsets)> Patches { get; } = [];
    public List<HkObject> Objects { get; } = [];
    public HkObject? Root { get; set; }
    public List<HkIssue> Issues { get; } = [];

    /// <summary>The TYPE chunk as read, header included.</summary>
    public byte[] TypeChunk { get; private set; } = [];

    /// <summary>The TYPE chunk's children in file order, with the bytes of those that are not rebuilt from the types (TPTR, TSHA, TPAD).</summary>
    public List<(string Tag, byte[] Body)> TypeParts { get; } = [];

    /// <summary>The string tables of the type names and field names, in the order the file stores them.</summary>
    public List<string> TypeStrings { get; private set; } = [];
    public List<string> FieldStrings { get; private set; } = [];

    /// <summary>The types with a body (TBDY) and with a hash (THSH), each in file order.</summary>
    public List<HkTagType> BodyOrder { get; } = [];
    public List<HkTagType> HashOrder { get; } = [];

    public HkTagType? FindType(string name) => Types.FirstOrDefault(t => t?.Name == name);

    public static bool IsTagfile(ReadOnlySpan<byte> d) => d.Length >= 8 && d[4..8].SequenceEqual("TAG0"u8);

    public static HkTagfile FromBinary(ReadOnlySpan<byte> d, bool decode = true)
    {
        if (!IsTagfile(d)) throw new InvalidDataException("Not a Havok tagfile.");
        int size = (int)(BinaryPrimitives.ReadUInt32BigEndian(d) & 0x3FFFFFFF);
        if (size > d.Length) throw new InvalidDataException("TAG0 runs past the end of the data.");
        var chunks = new Dictionary<string, (int Start, int Length)>();
        byte[] typeChunk = [];
        var typeParts = new List<(string, byte[])>();
        void Walk(ReadOnlySpan<byte> s, int start, int end, string parent)
        {
            while (start + 8 <= end)
            {
                uint w = BinaryPrimitives.ReadUInt32BigEndian(s[start..]);
                int len = (int)(w & 0x3FFFFFFF);
                if (len < 8 || start + len > end) throw new InvalidDataException($"Bad tagfile chunk at 0x{start:X}.");
                var tag = Encoding.ASCII.GetString(s.Slice(start + 4, 4));
                chunks.TryAdd(tag, (start + 8, len - 8));
                if (tag == "TYPE" && typeChunk.Length == 0) typeChunk = s.Slice(start, len).ToArray();
                if (parent == "TYPE") typeParts.Add((tag, s.Slice(start + 8, len - 8).ToArray()));
                if ((w & 0x40000000) == 0) Walk(s, start + 8, start + len, tag);
                start += len;
            }
        }
        Walk(d, 8, size, "TAG0");
        ReadOnlySpan<byte> Chunk(ReadOnlySpan<byte> s, string tag) => chunks.TryGetValue(tag, out var c) ? s.Slice(c.Start, c.Length) : default;

        var f = new HkTagfile
        {
            SdkVersion = Encoding.ASCII.GetString(Chunk(d, "SDKV")).TrimEnd('\0'),
            Data = Chunk(d, "DATA").ToArray(),
            TypeChunk = typeChunk
        };
        f.TypeParts.AddRange(typeParts);
        var typeStrings = f.TypeStrings = Strings(Chunk(d, "TSTR").IsEmpty ? Chunk(d, "TST1") : Chunk(d, "TSTR"));
        var fieldStrings = f.FieldStrings = Strings(Chunk(d, "FSTR").IsEmpty ? Chunk(d, "FST1") : Chunk(d, "FSTR"));
        f.ReadNames(Chunk(d, "TNAM").IsEmpty ? Chunk(d, "TNA1") : Chunk(d, "TNAM"), typeStrings);
        f.ReadBodies(Chunk(d, "TBDY").IsEmpty ? Chunk(d, "TBOD") : Chunk(d, "TBDY"), fieldStrings);
        f.ReadHashes(Chunk(d, "THSH"));
        f.ReadItems(Chunk(d, "ITEM"));
        f.ReadPatches(Chunk(d, "PTCH"));
        if (decode) new HkTagDecoder(f).DecodeAll();
        return f;
    }

    static List<string> Strings(ReadOnlySpan<byte> s)
    {
        var r = new List<string>();
        int at = 0;
        while (at < s.Length)
        {
            int z = s[at..].IndexOf((byte)0);
            if (z <= 0) break;
            r.Add(Encoding.UTF8.GetString(s.Slice(at, z)));
            at += z + 1;
        }
        return r;
    }

    HkTagType? TypeAt(long i) => i > 0 && i < Types.Count ? Types[(int)i] : i == 0 ? null : throw new InvalidDataException($"Type index {i} out of range.");

    void ReadNames(ReadOnlySpan<byte> s, List<string> strings)
    {
        if (s.IsEmpty) return;
        var r = new PackedReader(s);
        int count = (int)r.Read();
        var templates = new List<(HkTagType Type, string Name, int Value)>();
        for (int i = 1; i < count; i++)
        {
            var t = new HkTagType { Index = i, Name = strings[(int)r.Read()] };
            int n = (int)r.Read();
            for (int k = 0; k < n; k++) templates.Add((t, strings[(int)r.Read()], (int)r.Read()));
            Types.Add(t);
        }
        foreach (var (t, name, value) in templates)
            t.Templates.Add(new HkTagTemplate(name, value, name.StartsWith('t') ? TypeAt(value) : null));
    }

    void ReadBodies(ReadOnlySpan<byte> s, List<string> strings)
    {
        var r = new PackedReader(s);
        while (r.Remaining > 0)
        {
            int index = (int)r.Read();
            if (index == 0) break;
            var t = TypeAt(index)!;
            BodyOrder.Add(t);
            t.Parent = TypeAt(r.Read());
            int p = (int)r.Read();
            t.Presence = p;
            if ((p & 0x01) != 0) t.Format = (int)r.Read();
            if ((p & 0x02) != 0) t.SubType = TypeAt(r.Read());
            if ((p & 0x04) != 0) t.Version = (int)r.Read();
            if ((p & 0x08) != 0)
            {
                t.Size = (int)r.Read();
                t.AlignAndFlags = (int)r.Read();
            }
            if ((p & 0x10) != 0) t.AbstractValue = (int)r.Read();
            if ((p & 0x20) != 0)
            {
                int n = (int)r.Read();
                for (int k = 0; k < n; k++)
                {
                    var name = strings[(int)r.Read()];
                    int flags = (int)r.Read();
                    int extra = (flags & 0x80) != 0 ? (int)r.Read() : 0;
                    int offset = (int)r.Read();
                    t.Fields.Add(new HkTagField(name, flags, offset, TypeAt(r.Read())!, extra));
                }
            }
            if ((p & 0x40) != 0)
            {
                int n = (int)r.Read();
                for (int k = 0; k < n; k++) t.Interfaces.Add((TypeAt(r.Read())!, (int)r.Read()));
            }
            if ((p & 0x80) != 0) t.Attribute = (int)r.Read();
            if ((p & ~0xFF) != 0) throw new InvalidDataException($"Unknown type body flags 0x{p:X} on {t.Name}.");
        }
    }

    void ReadHashes(ReadOnlySpan<byte> s)
    {
        if (s.IsEmpty) return;
        var r = new PackedReader(s);
        int n = (int)r.Read();
        for (int i = 0; i < n; i++)
        {
            var t = TypeAt(r.Read());
            uint h = r.U32();
            if (t is null) continue;
            t.Hash = h;
            HashOrder.Add(t);
        }
    }

    void ReadItems(ReadOnlySpan<byte> s)
    {
        for (int at = 0; at + 12 <= s.Length; at += 12)
        {
            uint w = BinaryPrimitives.ReadUInt32LittleEndian(s[at..]);
            Items.Add(new HkTagItem(TypeAt(w & 0xFFFFFF), (int)(w >> 24),
                BinaryPrimitives.ReadInt32LittleEndian(s[(at + 4)..]), BinaryPrimitives.ReadInt32LittleEndian(s[(at + 8)..])));
        }
    }

    void ReadPatches(ReadOnlySpan<byte> s)
    {
        int at = 0;
        while (at + 8 <= s.Length)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(s[at..]);
            int n = BinaryPrimitives.ReadInt32LittleEndian(s[(at + 4)..]);
            at += 8;
            if (n < 0 || at + n * 4 > s.Length) throw new InvalidDataException("Bad PTCH entry.");
            var offs = new int[n];
            for (int i = 0; i < n; i++) offs[i] = BinaryPrimitives.ReadInt32LittleEndian(s[(at + i * 4)..]);
            Patches.Add((TypeAt(type), offs));
            at += n * 4;
        }
    }

    ref struct PackedReader(ReadOnlySpan<byte> s)
    {
        readonly ReadOnlySpan<byte> _s = s;
        int _at;

        public readonly int Remaining => _s.Length - _at;

        public long Read()
        {
            byte b = _s[_at++];
            if ((b & 0x80) == 0) return b;
            (int extra, long mask) = (b >> 3) switch
            {
                >= 0x10 and <= 0x17 => (1, 0x3FFFL),
                >= 0x18 and <= 0x1B => (2, 0x1FFFFFL),
                0x1C => (3, 0x7FFFFFFL),
                0x1D => (4, 0x7FFFFFFFFL),
                0x1E => (7, 0x7FFFFFFFFFFFFFFL),
                _ when b == 0xF8 => (8, -1L),
                _ => throw new InvalidDataException($"Bad packed integer prefix 0x{b:X2}.")
            };
            long v = b;
            for (int i = 0; i < extra; i++) v = (v << 8) | _s[_at++];
            return v & mask;
        }

        public uint U32()
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_s[_at..]);
            _at += 4;
            return v;
        }
    }
}
