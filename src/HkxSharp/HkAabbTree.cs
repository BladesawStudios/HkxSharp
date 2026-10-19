namespace HkxSharp;

public sealed class HkAabbNode
{
    public required float[] Min { get; init; }
    public required float[] Max { get; init; }
    public List<int> Children { get; } = [];
    public int Leaf { get; init; } = -1;
    public bool IsLeaf => Leaf >= 0;
}

public static class HkAabbTree
{
    public static List<HkAabbNode> SimdTree(HkStruct tree)
    {
        var nodes = ((object?[])tree["nodes"]!).Cast<HkStruct>().ToArray();
        var res = new List<HkAabbNode>();
        if (nodes.Length < 2) return res;
        res.Add(new HkAabbNode { Min = [float.MaxValue, float.MaxValue, float.MaxValue], Max = [float.MinValue, float.MinValue, float.MinValue] });
        var seen = new HashSet<int>();
        void Expand(int ni, int into)
        {
            if (ni <= 0 || ni >= nodes.Length || !seen.Add(ni)) throw new InvalidDataException($"Bad simd tree child {ni}.");
            var n = nodes[ni];
            bool leaf = (bool)n["isLeaf"]!;
            var data = (uint[])n["data"]!;
            float[] lx = (float[])n["lx"]!, ly = (float[])n["ly"]!, lz = (float[])n["lz"]!, hx = (float[])n["hx"]!, hy = (float[])n["hy"]!, hz = (float[])n["hz"]!;
            for (int c = 0; c < 4; c++)
            {
                if (lx[c] > hx[c]) continue;
                int at = Add(res, into, [lx[c], ly[c], lz[c]], [hx[c], hy[c], hz[c]], leaf ? (int)data[c] : -1);
                if (!leaf) Expand((int)data[c], at);
            }
        }
        Expand(1, 0);
        Union(res);
        return res;
    }

    public static List<HkAabbNode> Aabb8Tree(HkStruct section)
    {
        var nodes = ((object?[])section["sectionBvh"]!).Cast<HkStruct>().ToArray();
        var bo = (short[])section["bitOffset"]!;
        var inv = (HkStruct)section["bitScale8Inv"]!;
        float[] scale = [(float)inv["x"]!, (float)inv["y"]!, (float)inv["z"]!];
        var res = new List<HkAabbNode>();
        if (nodes.Length == 0) return res;
        res.Add(new HkAabbNode { Min = [float.MaxValue, float.MaxValue, float.MaxValue], Max = [float.MinValue, float.MinValue, float.MinValue] });
        float At(uint packed, int c, int axis) => ((int)((packed >> (c * 8)) & 0xFF) + bo[axis]) * scale[axis];
        var seen = new HashSet<int>();
        void Expand(int ni, int into)
        {
            if (ni < 0 || ni >= nodes.Length || !seen.Add(ni)) throw new InvalidDataException($"Bad aabb8 tree child {ni}.");
            var n = nodes[ni];
            var data = (byte[])n["data"]!;
            bool leaf = data[2] > data[3];
            uint lx = (uint)n["lx"]!, ly = (uint)n["ly"]!, lz = (uint)n["lz"]!, hx = (uint)n["hx"]!, hy = (uint)n["hy"]!, hz = (uint)n["hz"]!;
            for (int c = 0; c < 4; c++)
            {
                if (((lx >> (c * 8)) & 0xFF) > ((hx >> (c * 8)) & 0xFF)) continue;
                int at = Add(res, into, [At(lx, c, 0), At(ly, c, 1), At(lz, c, 2)], [At(hx, c, 0), At(hy, c, 1), At(hz, c, 2)], leaf ? data[c] : -1);
                if (!leaf) Expand(data[c], at);
            }
        }
        Expand(0, 0);
        Union(res);
        return res;
    }

    public static List<HkAabbNode> StaticTree(HkStruct tree)
    {
        var nodes = ((object?[])tree["nodes"]!).Cast<HkStruct>().ToArray();
        var domain = (HkStruct)tree["domain"]!;
        var res = new List<HkAabbNode>();
        if (nodes.Length == 0) return res;
        void Expand(int ni, int parent, float[] pmin, float[] pmax)
        {
            if (ni < 0 || ni >= nodes.Length) throw new InvalidDataException($"Bad static tree child {ni}.");
            var n = nodes[ni];
            var xyz = (byte[])n["xyz"]!;
            int hi = (byte)n["hiData"]!, data = (hi & 0x7F) << 16 | (ushort)n["loData"]!;
            float[] mn = new float[3], mx = new float[3];
            for (int k = 0; k < 3; k++)
            {
                float ext = pmax[k] - pmin[k];
                int a = xyz[k] >> 4, b = xyz[k] & 0xF;
                mn[k] = parent < 0 ? pmin[k] : a * (float)a * (1f / 226f) * ext + pmin[k];
                mx[k] = parent < 0 ? pmax[k] : -(b * (float)b) * (1f / 226f) * ext + pmax[k];
            }
            bool leaf = (hi & 0x80) == 0;
            int at = Add(res, parent, mn, mx, leaf ? data : -1);
            if (leaf) return;
            if (data == 0) throw new InvalidDataException($"Static tree node {ni} has no right child.");
            Expand(ni + 1, at, mn, mx);
            Expand(ni + data * 2, at, mn, mx);
        }
        Expand(0, -1, ((float[])domain["min"]!)[..3], ((float[])domain["max"]!)[..3]);
        return res;
    }

    static int Add(List<HkAabbNode> res, int parent, float[] min, float[] max, int leaf)
    {
        res.Add(new HkAabbNode { Min = min, Max = max, Leaf = leaf });
        if (parent >= 0) res[parent].Children.Add(res.Count - 1);
        return res.Count - 1;
    }

    static void Union(List<HkAabbNode> res)
    {
        var root = res[0];
        foreach (int c in root.Children)
            for (int k = 0; k < 3; k++)
            {
                root.Min[k] = Math.Min(root.Min[k], res[c].Min[k]);
                root.Max[k] = Math.Max(root.Max[k], res[c].Max[k]);
            }
    }
}
