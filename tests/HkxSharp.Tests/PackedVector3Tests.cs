namespace HkxSharp.Tests;

public class PackedVector3Tests
{
    [Theory]
    [InlineData(1f, 2f, 3f)]
    [InlineData(-0.125f, 0.5f, 100f)]
    [InlineData(0f, 0f, 0f)]
    public void APackedVectorUnpacksClose(float x, float y, float z)
    {
        var (ux, uy, uz) = HkPackedVector3.Unpack(HkPackedVector3.Pack(x, y, z));
        float tolerance = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) / 16384f + 1e-6f;
        Assert.InRange(ux, x - tolerance, x + tolerance);
        Assert.InRange(uy, y - tolerance, y + tolerance);
        Assert.InRange(uz, z - tolerance, z + tolerance);
    }
}
