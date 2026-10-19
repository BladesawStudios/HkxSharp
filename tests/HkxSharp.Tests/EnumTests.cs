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
}
