namespace HkxSharp.Tests;

public class TagfileTests
{
    [Fact]
    public void SomethingThatIsNotATagfileIsRefused()
    {
        byte[] junk = new byte[64];
        Assert.False(HkTagfile.IsTagfile(junk));
        Assert.Throws<InvalidDataException>(() => HkTagfile.FromBinary(junk));
    }
}
