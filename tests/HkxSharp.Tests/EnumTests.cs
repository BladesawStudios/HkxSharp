using System.Collections.Concurrent;

namespace HkxSharp.Tests;

public class EnumTests
{
    [Fact]
    public void ValuesAreNamedTheWayTheGameNamesThem()
    {
        Assert.Equal("MESH", HkEnums.Name("hknpShapeType::Enum", 8));
        Assert.Equal("COMPOUND", HkEnums.Name("hknpShapeType::Enum", 11));
        Assert.Equal("COMPOSITE", HkEnums.Name("hknpCollisionDispatchType::Enum", 3));
        Assert.Equal("HCL_PLATFORM_NX", HkEnums.Name("hclClothData::Platform", 0x20000));
        Assert.Equal("VC_NONE", HkEnums.Name("hclRuntimeConversionInfo::VectorConversion", 0xFA));
        Assert.Null(HkEnums.Name("hknpShapeType::Enum", 999));
        Assert.Null(HkEnums.Name("NotAnEnum", 0));
    }

    [Fact]
    public void FlagsAreSplitIntoTheirBits()
    {
        Assert.Equal(["IS_CONVEX_SHAPE", "IS_CONVEX_POLYTOPE_SHAPE", "SUPPORTS_COLLISIONS_WITH_INTERIOR_TRIANGLES"],
            HkEnums.Flags("hknpShape::FlagsEnum", 0x43));
        Assert.Empty(HkEnums.Flags("hknpShape::FlagsEnum", 0));
    }

    [TotkCorpusFact]
    public void EveryEnumValueInThePhysicsHasAName()
    {
        Problems problems = new();
        ConcurrentDictionary<string, byte> seen = [];
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            void Walk(object? v)
            {
                if (v is null || !visited.Add(v)) return;
                switch (v)
                {
                    case HkObject o: Walk(o.Fields); break;
                    case HkStruct s:
                        foreach (HkMember m in s.Members)
                        {
                            if (HkEnums.EnumType(s, m.Name) is not { } e || s[m.Name] is not { } raw || raw is Array) continue;
                            long value = Convert.ToInt64(raw);
                            seen.TryAdd(e, 0);
                            if (!HkEnums.Tables.ContainsKey(e)) { problems.Add($"{name}: no table for {e}"); continue; }
                            bool named = HkEnums.Name(e, value) is not null ||
                                         HkEnums.Flags(e, value).Aggregate(0L, (acc, n) => acc | HkEnums.Tables[e].First(t => t.Name == n).Value) == value;
                            if (!named) problems.Add($"{name}: {s.Class.Name}.{m.Name} = 0x{value:X} has no name in {e}");
                        }
                        foreach (object? x in s.Values) Walk(x);
                        break;
                    case object?[] a: foreach (object? x in a) Walk(x); break;
                }
            }
            foreach (HkObject o in HkTagfile.FromBinary(tag).Objects) Walk(o);
        });

        problems.AssertNone();
        Assert.True(seen.Count >= 13, $"only {seen.Count} enum types seen");
    }
}
