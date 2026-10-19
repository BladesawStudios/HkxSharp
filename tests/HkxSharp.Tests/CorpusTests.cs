using System.Collections.Concurrent;

namespace HkxSharp.Tests;

/// <summary>
/// Every check that needs the romfs, made in one pass so each tagfile is decoded once.
/// </summary>
public class CorpusTests
{
    private sealed class Checks
    {
        private readonly ConcurrentDictionary<string, Problems> _byCheck = new();
        private readonly ConcurrentDictionary<string, long> _counts = new();

        public void Fail(string check, string problem) => _byCheck.GetOrAdd(check, _ => new Problems()).Add(problem);

        public void Count(string what, long n = 1) => _counts.AddOrUpdate(what, n, (_, c) => c + n);

        public long this[string what] => _counts.GetValueOrDefault(what);

        public void AssertNone()
        {
            string failed = string.Join("\n\n", _byCheck.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {Capture(kv.Value)}"));
            Assert.True(failed.Length == 0, failed);
        }

        private static string Capture(Problems problems)
        {
            try { problems.AssertNone(); return ""; }
            catch (Exception e) { return e.Message; }
        }
    }

    [TotkCorpusFact]
    public void EveryPhysicsTagfileChecksOut()
    {
        Checks checks = new();
        ConcurrentDictionary<string, byte> enumTypes = [];
        Corpus.ForEachTagfile((name, ext, tag) =>
        {
            checks.Count("tagfiles");
            checks.Count(ext);
            HkTagfile t;
            try { t = HkTagfile.FromBinary(tag); }
            catch (Exception e) { checks.Fail("decode", $"{name}: {e.GetType().Name} {e.Message}"); return; }
            if (t.Issues.Count > 0) checks.Fail("decode", $"{name}: {t.Issues.Count} issues, first {t.Issues[0]}");
            if (t.Root is null) checks.Fail("decode", $"{name}: no root object");

            try
            {
                byte[] back = HkTagfileWriter.Write(t);
                if (!back.AsSpan().SequenceEqual(tag)) checks.Fail("write back", $"{name}: {tag.Length} bytes in, {back.Length} out");
            }
            catch (Exception e) { checks.Fail("write back", $"{name}: {e.GetType().Name} {e.Message}"); }

            Walk(name, t, checks, enumTypes);
            foreach (HkObject o in t.Objects)
            {
                try
                {
                    switch (o.ClassName)
                    {
                        case "hknpMeshShape": CheckMesh(name, o, checks); break;
                        case "hknpCompoundShape": CheckCompound(name, o, checks); break;
                        case "hkaiNavMesh": CheckNavMesh(name, o, checks); break;
                    }
                }
                catch (Exception e) { checks.Fail(o.ClassName, $"{name}: {e.GetType().Name} {e.Message}"); }
            }
        });

        checks.AssertNone();
        Assert.True(checks["tagfiles"] > 26000, $"only {checks["tagfiles"]} tagfiles");
        foreach (string ext in new[] { ".bphcl", ".bphnm", ".bphsc", ".bphsh" }) Assert.True(checks[ext] > 0, $"no {ext} tagfiles");
        Assert.True(checks["meshes"] > 70000, $"only {checks["meshes"]} meshes");
        Assert.True(checks["compounds"] > 29000, $"only {checks["compounds"]} compound shapes");
        Assert.True(checks["navmeshes"] > 2000, $"only {checks["navmeshes"]} navmeshes");
        Assert.True(checks["packed vectors"] > 30000, $"only {checks["packed vectors"]} packed vectors");
        Assert.True(enumTypes.Count >= 13, $"only {enumTypes.Count} enum types seen");
    }

    /// <summary>Floats come back as floats, every enum value has a name, and packed vectors repack.</summary>
    private static void Walk(string name, HkTagfile t, Checks checks, ConcurrentDictionary<string, byte> enumTypes)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        void Visit(object? v)
        {
            if (v is null || !visited.Add(v)) return;
            switch (v)
            {
                case HkObject o: Visit(o.Fields); break;
                case HkStruct { Class.Name: "hkPackedVector3" } s:
                    checks.Count("packed vectors");
                    short[] q = (short[])s["values"]!;
                    var (x, y, z) = HkPackedVector3.Unpack(q);
                    if (!HkPackedVector3.Pack(x, y, z).AsSpan().SequenceEqual(q))
                        checks.Fail("packed vectors", $"{name}: [{string.Join(", ", q)}] repacks differently");
                    break;
                case HkStruct s:
                    for (int i = 0; i < s.Members.Count; i++)
                    {
                        HkMember m = s.Members[i];
                        if (m.Type.Kind == HkKind.F32 && s.Values[i] is not float)
                            checks.Fail("floats", $"{name}: {s.Class.Name}.{m.Name} is {s.Values[i]?.GetType().Name}");
                        if (HkEnums.EnumType(s, m.Name) is not { } e || s.Values[i] is not { } raw || raw is Array) continue;
                        enumTypes.TryAdd(e, 0);
                        long value = Convert.ToInt64(raw);
                        if (!HkEnums.Tables.ContainsKey(e)) { checks.Fail("enum names", $"{name}: no table for {e}"); continue; }
                        bool named = HkEnums.Name(e, value) is not null ||
                                     HkEnums.Flags(e, value).Aggregate(0L, (acc, n) => acc | HkEnums.Tables[e].First(t => t.Name == n).Value) == value;
                        if (!named) checks.Fail("enum names", $"{name}: {s.Class.Name}.{m.Name} = 0x{value:X} has no name in {e}");
                    }
                    foreach (object? item in s.Values) Visit(item);
                    break;
                case object?[] a: foreach (object? item in a) Visit(item); break;
            }
        }
        foreach (HkObject o in t.Objects) Visit(o);
    }

    /// <summary>
    /// Sections decode to the bounds their tree stores, the tag table is keyed the way primitives are,
    /// and every tree covers what it should.
    /// </summary>
    private static void CheckMesh(string name, HkObject mesh, Checks checks)
    {
        checks.Count("meshes");
        List<HkMeshTriangle> prims = HkMeshShape.Decode(mesh);
        var sections = ((object?[])mesh["geometrySections"]!).Cast<HkStruct>().ToArray();

        List<HkAabbNode> top = HkAabbTree.SimdTree((HkStruct)mesh["topLevelTree"]!);
        string r = CheckTree(top, sections.Length, [1e-3f, 1e-3f, 1e-3f]);
        if (r != "") checks.Fail("mesh trees", $"{name}: top level tree: {r}");
        foreach (HkAabbNode leaf in top.Where(n => n.IsLeaf))
        {
            var (min, max) = Bounds(prims.Where(p => p.Section == leaf.Leaf));
            float size = Math.Max(1f, Enumerable.Range(0, 3).Max(k => leaf.Max[k] - leaf.Min[k]));
            for (int k = 0; k < 3; k++)
                if (Math.Abs(min[k] - leaf.Min[k]) > size * 1e-4f || Math.Abs(max[k] - leaf.Max[k]) > size * 1e-4f)
                {
                    checks.Fail("mesh bounds", $"{name}: section {leaf.Leaf} axis {k} decodes to {min[k]}..{max[k]}, tree says {leaf.Min[k]}..{leaf.Max[k]}");
                    break;
                }
        }

        uint[] keys = [.. ((object?[])mesh["shapeTagTable"]!).Cast<HkStruct>().Select(e => (uint)e["meshPrimitiveKey"]!)];
        if (keys.Length > 0 && keys[0] != 0) checks.Fail("shape tags", $"{name}: tag table starts at key {keys[0]:X}");
        for (int i = 1; i < keys.Length; i++)
            if (keys[i] <= keys[i - 1]) checks.Fail("shape tags", $"{name}: tag table keys not ascending at {i}");
        foreach (HkMeshTriangle p in prims)
        {
            if (p.Key != (uint)(p.Section << 9 | p.Primitive << 1)) checks.Fail("shape tags", $"{name}: key {p.Key:X} for {p.Section}/{p.Primitive}");
            if (!p.Quad && p.Interior > 1) checks.Fail("shape tags", $"{name}: triangle {p.Section}/{p.Primitive} has interior bits {p.Interior}");
        }

        foreach (var group in prims.GroupBy(p => p.Section))
        {
            HkStruct section = sections[group.Key];
            HkStruct step = (HkStruct)section["bitScale8Inv"]!;
            float[] tolerance = [(float)step["x"]! * 1.01f + 1e-4f, (float)step["y"]! * 1.01f + 1e-4f, (float)step["z"]! * 1.01f + 1e-4f];
            HkMeshTriangle[] inSection = [.. group];
            List<HkAabbNode> tree = HkAabbTree.Aabb8Tree(section);
            string rs = CheckTree(tree, inSection.Length, tolerance);
            if (rs == "")
                foreach (HkAabbNode leaf in tree.Where(n => n.IsLeaf))
                {
                    float[] v = inSection[leaf.Leaf].Vertices;
                    for (int at = 0; at < v.Length && rs == ""; at += 3)
                        if (!Inside(v.AsSpan(at, 3), leaf, tolerance)) rs = $"primitive {leaf.Leaf} outside its leaf";
                    if (rs != "") break;
                }
            if (rs != "") checks.Fail("mesh trees", $"{name}: section {group.Key}: {rs}");
        }
    }

    private static void CheckCompound(string name, HkObject compound, Checks checks)
    {
        checks.Count("compounds");
        int instances = ((object?[])((HkStruct)compound["instances"]!)["elements"]!).Length;
        var tree = HkAabbTree.SimdTree((HkStruct)((HkStruct)compound["boundingVolumeData"]!)["simdTree"]!);
        string r = CheckTree(tree, instances, [1e-3f, 1e-3f, 1e-3f]);
        if (r != "") checks.Fail("compound trees", $"{name}: {r}");
    }

    private static void CheckNavMesh(string name, HkObject mesh, Checks checks)
    {
        if (mesh["cachedFaceIterator"] is not HkObject { } it || it["tree"] is not HkObject sat) return;
        checks.Count("navmeshes");
        var faces = ((object?[])mesh["faces"]!).Cast<HkStruct>().ToArray();
        var edges = ((object?[])mesh["edges"]!).Cast<HkStruct>().ToArray();
        float[] vertices = (float[])mesh["vertices"]!;
        List<HkAabbNode> tree = HkAabbTree.StaticTree((HkStruct)((HkObject)sat["treePtr"]!)["tree"]!);
        if (tree.Count == 0)
        {
            if (faces.Length > 0) checks.Fail("navmesh trees", $"{name}: {faces.Length} faces but an empty tree");
            return;
        }
        float[] tolerance = [.. Enumerable.Range(0, 3).Select(k => (tree[0].Max[k] - tree[0].Min[k]) * 0.01f + 1e-3f)];
        string r = CheckTree(tree, faces.Length, tolerance);
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
        if (r != "") checks.Fail("navmesh trees", $"{name}: {r}");
    }

    /// <summary>Every leaf in [0, leaves) reached once, every child inside its parent; empty when all holds.</summary>
    private static string CheckTree(List<HkAabbNode> nodes, int leaves, float[] tolerance)
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
}
