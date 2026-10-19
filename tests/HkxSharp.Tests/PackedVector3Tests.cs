using System.Collections.Concurrent;

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

    [TotkCorpusFact]
    public void EveryPackedVectorInTheClothRepacksIdentically()
    {
        Problems problems = new();
        int count = 0;
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            void Walk(object? v)
            {
                if (v is null || !visited.Add(v)) return;
                switch (v)
                {
                    case HkObject o: Walk(o.Fields); break;
                    case HkStruct { Class.Name: "hkPackedVector3" } s:
                        Interlocked.Increment(ref count);
                        short[] q = (short[])s["values"]!;
                        var (x, y, z) = HkPackedVector3.Unpack(q);
                        if (!HkPackedVector3.Pack(x, y, z).AsSpan().SequenceEqual(q)) problems.Add($"{name}: [{string.Join(", ", q)}] repacks differently");
                        break;
                    case HkStruct s: foreach (object? item in s.Values) Walk(item); break;
                    case object?[] a: foreach (object? item in a) Walk(item); break;
                }
            }
            foreach (HkObject o in HkTagfile.FromBinary(tag).Objects) Walk(o);
        }, ".bphcl");

        problems.AssertNone();
        Assert.True(count > 30000, $"only {count} packed vectors found");
    }
}
