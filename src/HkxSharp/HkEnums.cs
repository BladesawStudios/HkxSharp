namespace HkxSharp;

public static class HkEnums
{
    static (long, string)[] Seq(params string[] names) => names.Select((n, i) => ((long)i, n)).ToArray();

    static (long, string)[] Bits(params string[] names) => names.Select((n, i) => (1L << i, n)).ToArray();

    static readonly string[] User = ["USER_0", "USER_1", "USER_2", "USER_3", "USER_4", "USER_5", "USER_6", "USER_7"];

    public static IReadOnlyDictionary<string, (long Value, string Name)[]> Tables { get; } = new Dictionary<string, (long, string)[]>
    {
        ["hknpShapeType::Enum"] = Seq(
        [
            "CONVEX", "SPHERE", "CAPSULE", "CYLINDER", "TRIANGLE", "BOX", "DEBRIS", "COMPOSITE", "MESH",
            "COMPRESSED_MESH_DEPRECATED", "EXTERN_MESH", "COMPOUND", "DISTANCE_FIELD_BASE", "HEIGHT_FIELD",
            "DISTANCE_FIELD", "PARTICLE_SYSTEM", "SCALED_CONVEX", "TRANSFORMED", "MASKED", "MASKED_COMPOUND",
            "BREAKABLE_COMPOUND", "LOD", "DUMMY", .. User, "NUM_SHAPE_TYPES", "INVALID"
        ]),
        ["hknpCollisionDispatchType::Enum"] = Seq(["NONE", "DEBRIS", "CONVEX", "COMPOSITE", "DISTANCE_FIELD", .. User, "NUM_TYPES"]),
        ["hknpCompoundShapeBoundingVolumeType::Enum"] = Seq("NONE", "AABB_TREE", "SIMD_TREE"),
        ["hknpShape::FlagsEnum"] =
        [
            (0x1, "IS_CONVEX_SHAPE"), (0x2, "IS_CONVEX_POLYTOPE_SHAPE"), (0x4, "IS_COMPOSITE_SHAPE"),
            (0x8, "IS_HEIGHT_FIELD_SHAPE"), (0x10, "USE_SINGLE_POINT_MANIFOLD"), (0x20, "IS_INTERIOR_TRIANGLE"),
            (0x40, "SUPPORTS_COLLISIONS_WITH_INTERIOR_TRIANGLES"), (0x80, "USE_NORMAL_TO_FIND_SUPPORT_PLANE"),
            (0x200, "IS_TRIANGLE_OR_QUAD_SHAPE"), (0x400, "IS_QUAD_SHAPE"), (0x800, "IS_SDF_EDGE_COLLISION_ENABLED"),
            (0x1000, "USER_FLAG_0"), (0x2000, "USER_FLAG_1"), (0x4000, "USER_FLAG_2"), (0x8000, "HAS_SURFACE_VELOCITY"),
            (0x8090, "INHERITED_FLAGS")
        ],
        ["hkaiNavMesh::EdgeFlagBits"] = Bits(
            "EDGE_SILHOUETTE", "EDGE_RETRIANGULATED", "EDGE_ORIGINAL", "EDGE_ORPHANED_USER_EDGE", "EDGE_USER",
            "EDGE_BLOCKED", "EDGE_EXTERNAL_OPPOSITE", "EDGE_GARBAGE"),
        ["hkaiClusterGraph::EdgeBits"] = [(0x2, "EDGE_IS_USER"), (0x40, "EDGE_EXTERNAL_OPPOSITE")],
        ["hkaiAnnotatedStreamingSet::Side"] = Seq("SIDE_A", "SIDE_B"),
        ["hclBufferLayout::SlotFlags"] = [(0, "SF_NO_ALIGNED_START"), (1, "SF_16BYTE_ALIGNED_START"), (3, "SF_64BYTE_ALIGNED_START")],
        ["hclBufferLayout::TriangleFormat"] = Seq("TF_THREE_INT32S", "TF_THREE_INT16S", "TF_OTHER"),
        ["hclClothData::Platform"] =
        [
            (0, "HCL_PLATFORM_INVALID"),
            .. Bits(
                "HCL_PLATFORM_WIN32", "HCL_PLATFORM_X64", "HCL_PLATFORM_MACPPC", "HCL_PLATFORM_IOS", "HCL_PLATFORM_MAC386",
                "HCL_PLATFORM_PS3", "HCL_PLATFORM_XBOX360", "HCL_PLATFORM_WII", "HCL_PLATFORM_LRB", "HCL_PLATFORM_LINUX",
                "HCL_PLATFORM_PSVITA", "HCL_PLATFORM_ANDROID", "HCL_PLATFORM_CTR", "HCL_PLATFORM_WIIU", "HCL_PLATFORM_PS4",
                "HCL_PLATFORM_XBOXONE", "HCL_PLATFORM_MAC64", "HCL_PLATFORM_NX", "HCL_PLATFORM_GDK", "HCL_PLATFORM_PS5",
                "HCL_PLATFORM_MACARM64")
        ],
        ["hclLocalRangeConstraintSet::ShapeType"] = Seq("SHAPE_SPHERE", "SHAPE_CYLINDER", "SHAPE_CAPSULE"),
        ["hclMeshBoneDeformUtility::BoneAxis"] = Seq("X_AXIS", "Y_AXIS", "Z_AXIS", "LEGACY"),
        ["hclRuntimeConversionInfo::VectorConversion"] =
        [
            (0, "VC_FLOAT4"), (1, "VC_FLOAT3"), (2, "VC_BYTE4"), (3, "VC_SHORT3"), (4, "VC_HFLOAT3"), (5, "VC_SBYTE4"),
            (0x14, "VC_CUSTOM_A"), (0x15, "VC_CUSTOM_B"), (0x16, "VC_CUSTOM_C"), (0x17, "VC_CUSTOM_D"), (0x18, "VC_CUSTOM_E"),
            (0x20, "VC_MAX_NUM"), (0xFA, "VC_NONE")
        ],
    };

    public static string? Name(string enumType, long value) =>
        Tables.TryGetValue(enumType, out var t) ? t.FirstOrDefault(e => e.Value == value).Name : null;

    public static IEnumerable<string> Flags(string enumType, long value)
    {
        if (!Tables.TryGetValue(enumType, out var t)) yield break;
        foreach (var (bit, name) in t)
            if (bit != 0 && (bit & (bit - 1)) == 0 && (value & bit) != 0) yield return name;
    }

    public static string? EnumType(HkStruct s, string field)
    {
        for (var t = s.Class.TagType; t is not null; t = t.Parent)
            foreach (var f in t.Fields)
                if (f.Name == field) return (f.Type.Canonical.Template("tENUM") ?? f.Type.Canonical.Template("tBITS"))?.FullName;
        return null;
    }

    public static string? Name(HkStruct s, string field) =>
        EnumType(s, field) is { } e && s[field] is { } v and not Array ? Name(e, Convert.ToInt64(v)) : null;

    public static string? Name(HkObject o, string field) => o.Fields is { } s ? Name(s, field) : null;

    public static IEnumerable<string> Flags(HkStruct s, string field) =>
        EnumType(s, field) is { } e && s[field] is { } v and not Array ? Flags(e, Convert.ToInt64(v)) : [];

    public static IEnumerable<string> Flags(HkObject o, string field) => o.Fields is { } s ? Flags(s, field) : [];
}
