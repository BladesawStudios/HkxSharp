namespace HkxSharp;

public static class HkEnums
{
    static readonly string[] UserTypes = ["USER_0", "USER_1", "USER_2", "USER_3", "USER_4", "USER_5", "USER_6", "USER_7"];

    public static IReadOnlyDictionary<string, string[]> Tables { get; } = new Dictionary<string, string[]>
    {
        ["hknpShapeType::Enum"] =
        [
            "CONVEX", "SPHERE", "CAPSULE", "CYLINDER", "TRIANGLE", "BOX", "DEBRIS", "COMPOSITE", "MESH",
            "COMPRESSED_MESH_DEPRECATED", "EXTERN_MESH", "COMPOUND", "DISTANCE_FIELD_BASE", "HEIGHT_FIELD",
            "DISTANCE_FIELD", "PARTICLE_SYSTEM", "SCALED_CONVEX", "TRANSFORMED", "MASKED", "MASKED_COMPOUND",
            "BREAKABLE_COMPOUND", "LOD", "DUMMY", .. UserTypes, "NUM_SHAPE_TYPES", "INVALID"
        ],
        ["hknpCollisionDispatchType::Enum"] = ["NONE", "DEBRIS", "CONVEX", "COMPOSITE", "DISTANCE_FIELD", .. UserTypes, "NUM_TYPES"],
    };

    public static string? Name(string enumType, long value) =>
        Tables.TryGetValue(enumType, out var t) && value >= 0 && value < t.Length ? t[value] : null;

    public static string? EnumType(HkStruct s, string field)
    {
        for (var t = s.Class.TagType; t is not null; t = t.Parent)
            foreach (var f in t.Fields)
                if (f.Name == field) return f.Type.Canonical.Template("tENUM")?.FullName;
        return null;
    }

    public static string? Name(HkStruct s, string field) =>
        EnumType(s, field) is { } e && s[field] is { } v ? Name(e, Convert.ToInt64(v)) : null;

    public static string? Name(HkObject o, string field) => o.Fields is { } s ? Name(s, field) : null;
}
