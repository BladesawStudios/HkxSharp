namespace HkxSharp;

public sealed class HkRegistry
{
    readonly Dictionary<string, HkClass> _classes = new(StringComparer.Ordinal);

    public static HkRegistry Default { get; } = Parse(HkClasses.Definitions);

    public IReadOnlyDictionary<string, HkClass> Classes => _classes;

    public HkClass? Find(string name) => _classes.GetValueOrDefault(name);

    public static HkRegistry Parse(string text)
    {
        var r = new HkRegistry();
        HkClass? cur = null;
        var pending = new List<HkType>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            if (line.Trim().Length == 0) continue;
            var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!char.IsWhiteSpace(line[0]))
            {
                string? parent = null;
                bool virt = false;
                int align = 0;
                uint sig = 0;
                for (int i = 1; i < tok.Length; i++)
                {
                    if (tok[i] == ":") parent = tok[++i];
                    else if (tok[i] == "virtual") virt = true;
                    else if (tok[i].StartsWith("align")) align = int.Parse(tok[i][5..]);
                    else if (tok[i].StartsWith("0x")) sig = Convert.ToUInt32(tok[i], 16);
                    else throw new FormatException($"bad class line '{line}'");
                }
                cur = new HkClass { Name = tok[0], ParentName = parent, Virtual = virt, ForceAlign = align, Signature = sig };
                r._classes.Add(cur.Name, cur);
                continue;
            }
            if (cur is null) throw new FormatException("member outside class");
            var type = ParseType(tok[0], pending);
            cur.Members.Add(new HkMember(tok[1], type, tok.Length > 2 && tok[2] == "nosave"));
        }
        foreach (var c in r._classes.Values)
            if (c.ParentName is not null)
                c.Parent = r._classes.GetValueOrDefault(c.ParentName) ?? throw new FormatException($"unknown parent {c.ParentName} of {c.Name}");
        foreach (var t in pending)
            t.Class = r._classes.GetValueOrDefault(t.ClassName!) ?? throw new FormatException($"unknown struct type {t.ClassName}");
        return r;
    }

    static HkType ParseType(string s, List<HkType> pending)
    {
        if (s.EndsWith(']'))
        {
            int b = s.LastIndexOf('[');
            return HkType.FixedOf(ParseType(s[..b], pending), int.Parse(s[(b + 1)..^1]));
        }
        int lt = s.IndexOf('<');
        if (lt >= 0)
        {
            var inner = s[(lt + 1)..^1];
            return s[..lt] switch
            {
                "array" => HkType.ArrayOf(ParseType(inner, pending)),
                "sarray" => HkType.SmallArrayOf(ParseType(inner, pending)),
                "relarray" => HkType.RelArrayOf(ParseType(inner, pending)),
                "ptr" => HkType.Pointer(inner),
                _ => throw new FormatException($"bad type {s}")
            };
        }
        switch (s)
        {
            case "bool": return HkType.Prim(HkKind.Bool);
            case "u8": return HkType.Prim(HkKind.U8);
            case "s8": return HkType.Prim(HkKind.S8);
            case "u16": return HkType.Prim(HkKind.U16);
            case "s16": return HkType.Prim(HkKind.S16);
            case "u32": return HkType.Prim(HkKind.U32);
            case "s32": return HkType.Prim(HkKind.S32);
            case "u64": return HkType.Prim(HkKind.U64);
            case "s64": return HkType.Prim(HkKind.S64);
            case "f32": return HkType.Prim(HkKind.F32);
            case "half": return HkType.Prim(HkKind.Half);
            case "ulong": return HkType.Prim(HkKind.Ulong);
            case "vec4": return HkType.Prim(HkKind.Vec4);
            case "quat": return HkType.Prim(HkKind.Quat);
            case "mat3": return HkType.Prim(HkKind.Mat3);
            case "rot": return HkType.Prim(HkKind.Rot);
            case "transform": return HkType.Prim(HkKind.Transform);
            case "qstransform": return HkType.Prim(HkKind.QsTransform);
            case "mat4": return HkType.Prim(HkKind.Mat4);
            case "vptr": return HkType.Prim(HkKind.Vptr);
            case "ptr": return HkType.Pointer(null);
            case "str": return HkType.Str;
            default:
                var t = HkType.StructOf(s);
                pending.Add(t);
                return t;
        }
    }
}
