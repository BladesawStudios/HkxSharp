using System.Collections.Concurrent;

namespace HkxSharp.Tests;

public class MeshShapeTests
{
    internal static void ForEachMesh(Action<string, HkObject> visit) =>
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            foreach (HkObject o in HkTagfile.FromBinary(tag).Objects)
                if (o.ClassName == "hknpMeshShape") visit(name, o);
        }, ".bphsh", ".bphsc");

    private static (float[] Min, float[] Max) Bounds(IEnumerable<HkMeshTriangle> prims)
    {
        float[] min = [float.MaxValue, float.MaxValue, float.MaxValue], max = [float.MinValue, float.MinValue, float.MinValue];
        foreach (HkMeshTriangle p in prims)
            for (int v = 0; v < p.Vertices.Length; v += 3)
                for (int k = 0; k < 3; k++)
                {
                    min[k] = Math.Min(min[k], p.Vertices[v + k]);
                    max[k] = Math.Max(max[k], p.Vertices[v + k]);
                }
        return (min, max);
    }

    [TotkCorpusFact]
    public void EverySectionDecodesToTheBoundsItsTreeStores()
    {
        Problems problems = new();
        int meshes = 0;
        ForEachMesh((name, mesh) =>
        {
            Interlocked.Increment(ref meshes);
            List<HkMeshTriangle> prims = HkMeshShape.Decode(mesh);
            List<HkAabbNode> top = HkAabbTree.SimdTree((HkStruct)mesh["topLevelTree"]!);
            foreach (HkAabbNode leaf in top.Where(n => n.IsLeaf))
            {
                var (min, max) = Bounds(prims.Where(p => p.Section == leaf.Leaf));
                float size = Math.Max(1f, Enumerable.Range(0, 3).Max(k => leaf.Max[k] - leaf.Min[k]));
                for (int k = 0; k < 3; k++)
                    if (Math.Abs(min[k] - leaf.Min[k]) > size * 1e-4f || Math.Abs(max[k] - leaf.Max[k]) > size * 1e-4f)
                    {
                        problems.Add($"{name}: section {leaf.Leaf} axis {k} decodes to {min[k]}..{max[k]}, tree says {leaf.Min[k]}..{leaf.Max[k]}");
                        break;
                    }
            }
        });

        problems.AssertNone();
        Assert.True(meshes > 70000, $"only {meshes} meshes found");
    }

    [TotkCorpusFact]
    public void ShapeTagsComeFromTheTagTable()
    {
        Problems problems = new();
        ForEachMesh((name, mesh) =>
        {
            uint[] keys = [.. ((object?[])mesh["shapeTagTable"]!).Cast<HkStruct>().Select(e => (uint)e["meshPrimitiveKey"]!)];
            if (keys.Length > 0 && keys[0] != 0) problems.Add($"{name}: tag table starts at key {keys[0]:X}");
            for (int i = 1; i < keys.Length; i++)
                if (keys[i] <= keys[i - 1]) problems.Add($"{name}: tag table keys not ascending at {i}");
            foreach (HkMeshTriangle p in HkMeshShape.Decode(mesh))
            {
                if (p.Key != (uint)(p.Section << 9 | p.Primitive << 1)) problems.Add($"{name}: key {p.Key:X} for {p.Section}/{p.Primitive}");
                if (!p.Quad && p.Interior > 1) problems.Add($"{name}: triangle {p.Section}/{p.Primitive} has interior bits {p.Interior}");
            }
        });

        problems.AssertNone();
    }
}
