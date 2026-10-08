namespace HkxSharp;

/// <summary>
/// Changes a decoded BotW packfile's contents to what the other platform stores. The layout itself is the writer's job
/// (<see cref="HkPackfileWriter"/>); this covers the data that differs: cloth is tagged with its target platform, and
/// skinning keeps its local positions unpacked on Wii U but packed (object space) or in the packed slot (bone space) on Switch.
/// </summary>
public static class HkConvert
{
    public static void ToPlatform(HkPackfile pf, HkTarget target)
    {
        foreach (var o in pf.Objects)
        {
            if (o.Fields is not { } s) continue;
            if (s.Class.IsA("hclClothData")) Set(s, "targetPlatform", target.ClothPlatform);
            else if (s.Class.IsA("hclObjectSpaceSkinPOperator"))
            {
                if (target.LittleEndian) PackObjectSpace(s); else UnpackObjectSpace(s);
            }
            else if (s.Class.IsA("hclBoneSpaceSkinPOperator"))
            {
                if (target.LittleEndian) Move(s, "localUnpackedPs", "localPs"); else Move(s, "localPs", "localUnpackedPs");
            }
        }
    }

    public static void ToPlatform(IEnumerable<HkPackfile> packfiles, HkTarget target)
    {
        foreach (var pf in packfiles) ToPlatform(pf, target);
    }

    static void PackObjectSpace(HkStruct op)
    {
        var unpacked = (object?[])op["localUnpackedPs"]!;
        if (unpacked.Length == 0) return;
        var blockClass = ElementClass(op, "localPs");
        var vecClass = blockClass.FlatMembers[0].Type.Elem!.Class!;
        var packed = new object?[unpacked.Length];
        for (int b = 0; b < unpacked.Length; b++)
        {
            var f = (float[])((HkStruct)unpacked[b]!)["localPosition"]!;
            var vecs = new object?[f.Length / 4];
            for (int i = 0; i < vecs.Length; i++)
                vecs[i] = new HkStruct(vecClass, [HkPackedVector3.Pack(f[i * 4], f[i * 4 + 1], f[i * 4 + 2])]);
            packed[b] = new HkStruct(blockClass, [vecs]);
        }
        Set(op, "localPs", packed);
        Set(op, "localUnpackedPs", Array.Empty<object?>());
    }

    /// <summary>Packing is lossy, so the floats come back within the packed precision rather than as Wii U had them.</summary>
    static void UnpackObjectSpace(HkStruct op)
    {
        var packed = (object?[])op["localPs"]!;
        if (packed.Length == 0) return;
        var blockClass = ElementClass(op, "localUnpackedPs");
        var unpacked = new object?[packed.Length];
        for (int b = 0; b < packed.Length; b++)
        {
            var vecs = (object?[])((HkStruct)packed[b]!)["localPosition"]!;
            var f = new float[vecs.Length * 4];
            for (int i = 0; i < vecs.Length; i++)
            {
                var (x, y, z) = HkPackedVector3.Unpack((short[])((HkStruct)vecs[i]!).Values[0]!);
                (f[i * 4], f[i * 4 + 1], f[i * 4 + 2], f[i * 4 + 3]) = (x, y, z, 1f);
            }
            unpacked[b] = new HkStruct(blockClass, [f]);
        }
        Set(op, "localUnpackedPs", unpacked);
        Set(op, "localPs", Array.Empty<object?>());
    }

    static void Move(HkStruct op, string from, string to)
    {
        var blocks = (object?[])op[from]!;
        if (blocks.Length == 0) return;
        var blockClass = ElementClass(op, to);
        Set(op, to, blocks.Select(u => (object?)new HkStruct(blockClass, [((HkStruct)u!).Values[0]])).ToArray());
        Set(op, from, Array.Empty<object?>());
    }

    static HkClass ElementClass(HkStruct s, string member) => s.Members[s.Class.IndexOf(member)].Type.Elem!.Class!;

    static void Set(HkStruct s, string member, object? value) => s.Values[s.Class.IndexOf(member)] = value;
}
