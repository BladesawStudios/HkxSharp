using System.Collections.Concurrent;

namespace HkxSharp.Tests;

public class AabbTreeTests
{
    /// <summary>Every leaf in [0, leaves) reached once, every child inside its parent; empty when all holds.</summary>
    private static string Check(List<HkAabbNode> nodes, int leaves, float[] tolerance)
    {
        int[] seen = new int[Math.Max(leaves, 1)];
        foreach (HkAabbNode n in nodes)
        {
            if (n.IsLeaf)
            {
                if (n.Leaf >= leaves) return $"leaf {n.Leaf} past {leaves}";
                seen[n.Leaf]++;
            }
            foreach (int c in n.Children)
                for (int k = 0; k < 3; k++)
                    if (nodes[c].Min[k] < n.Min[k] - tolerance[k] || nodes[c].Max[k] > n.Max[k] + tolerance[k])
                        return "a child outside its parent";
        }
        if (leaves > 0 && seen.Any(s => s != 1))
            return $"{seen.Count(s => s == 0)} leaves missing, {seen.Count(s => s > 1)} repeated";
        return "";
    }

    private static bool Inside(ReadOnlySpan<float> p, HkAabbNode n, float[] tolerance)
    {
        for (int k = 0; k < 3; k++)
            if (p[k] < n.Min[k] - tolerance[k] || p[k] > n.Max[k] + tolerance[k]) return false;
        return true;
    }

    [TotkCorpusFact]
    public void MeshTreesCoverEverySectionAndPrimitive()
    {
        Problems problems = new();
        MeshShapeTests.ForEachMesh((name, mesh) =>
        {
            var sections = ((object?[])mesh["geometrySections"]!).Cast<HkStruct>().ToArray();
            string top = Check(HkAabbTree.SimdTree((HkStruct)mesh["topLevelTree"]!), sections.Length, [1e-3f, 1e-3f, 1e-3f]);
            if (top != "") problems.Add($"{name}: top level tree: {top}");

            foreach (var group in HkMeshShape.Decode(mesh).GroupBy(p => p.Section))
            {
                HkStruct section = sections[group.Key];
                HkStruct step = (HkStruct)section["bitScale8Inv"]!;
                float[] tolerance = [(float)step["x"]! * 1.01f + 1e-4f, (float)step["y"]! * 1.01f + 1e-4f, (float)step["z"]! * 1.01f + 1e-4f];
                HkMeshTriangle[] prims = [.. group];
                List<HkAabbNode> tree = HkAabbTree.Aabb8Tree(section);
                string r = Check(tree, prims.Length, tolerance);
                if (r == "")
                    foreach (HkAabbNode leaf in tree.Where(n => n.IsLeaf))
                        for (int v = 0; v < prims[leaf.Leaf].Vertices.Length; v += 3)
                            if (!Inside(prims[leaf.Leaf].Vertices.AsSpan(v, 3), leaf, tolerance))
                            {
                                r = $"primitive {leaf.Leaf} outside its leaf";
                                goto Done;
                            }
                Done:
                if (r != "") problems.Add($"{name}: section {group.Key}: {r}");
            }
        });

        problems.AssertNone();
    }

    [TotkCorpusFact]
    public void CompoundTreesCoverEveryInstance()
    {
        Problems problems = new();
        int compounds = 0;
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            foreach (HkObject o in HkTagfile.FromBinary(tag).Objects.Where(o => o.ClassName == "hknpCompoundShape"))
            {
                Interlocked.Increment(ref compounds);
                int instances = ((object?[])((HkStruct)o["instances"]!)["elements"]!).Length;
                var tree = HkAabbTree.SimdTree((HkStruct)((HkStruct)o["boundingVolumeData"]!)["simdTree"]!);
                string r = Check(tree, instances, [1e-3f, 1e-3f, 1e-3f]);
                if (r != "") problems.Add($"{name}: {r}");
            }
        }, ".bphsc");

        problems.AssertNone();
        Assert.True(compounds > 29000, $"only {compounds} compound shapes found");
    }

    [TotkCorpusFact]
    public void NavMeshTreesCoverEveryFace()
    {
        Problems problems = new();
        int navmeshes = 0;
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            foreach (HkObject mesh in HkTagfile.FromBinary(tag).Objects.Where(o => o.ClassName == "hkaiNavMesh"))
            {
                if (mesh["cachedFaceIterator"] is not HkObject { } it || it["tree"] is not HkObject sat) continue;
                Interlocked.Increment(ref navmeshes);
                var faces = ((object?[])mesh["faces"]!).Cast<HkStruct>().ToArray();
                var edges = ((object?[])mesh["edges"]!).Cast<HkStruct>().ToArray();
                float[] vertices = (float[])mesh["vertices"]!;
                List<HkAabbNode> tree = HkAabbTree.StaticTree((HkStruct)((HkObject)sat["treePtr"]!)["tree"]!);
                if (tree.Count == 0)
                {
                    if (faces.Length > 0) problems.Add($"{name}: {faces.Length} faces but an empty tree");
                    continue;
                }
                float[] tolerance = [.. Enumerable.Range(0, 3).Select(k => (tree[0].Max[k] - tree[0].Min[k]) * 0.01f + 1e-3f)];
                string r = Check(tree, faces.Length, tolerance);
                if (r == "")
                    foreach (HkAabbNode leaf in tree.Where(n => n.IsLeaf))
                    {
                        int start = Convert.ToInt32(faces[leaf.Leaf]["startEdgeIndex"]), count = Convert.ToInt32(faces[leaf.Leaf]["numEdges"]);
                        for (int e = start; e < start + count && r == ""; e++)
                        {
                            int a = Convert.ToInt32(edges[e]["a"]);
                            if (!Inside(vertices.AsSpan(a * 4, 3), leaf, tolerance)) r = $"face {leaf.Leaf} outside its leaf";
                        }
                        if (r != "") break;
                    }
                if (r != "") problems.Add($"{name}: {r}");
            }
        }, ".bphnm");

        problems.AssertNone();
        Assert.True(navmeshes > 2000, $"only {navmeshes} navmeshes found");
    }
}
