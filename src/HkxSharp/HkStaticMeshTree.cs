namespace HkxSharp;

public sealed class HkMeshTriangle
{
    public required float[] Vertices { get; init; }
    public required int Section { get; init; }
    public required int Primitive { get; init; }
    public required uint UserData { get; init; }
    public required byte[] Indices { get; init; }
    public bool Quad { get; init; }
    public bool Custom { get; init; }
}

public static class HkStaticMeshTree
{
    public static List<HkMeshTriangle> Decode(HkStruct tree)
    {
        var domain = (HkStruct)tree["domain"]!;
        var dmin = (float[])domain["min"]!;
        var dmax = (float[])domain["max"]!;
        var sections = (object?[])tree["sections"]!;
        var prims = (object?[])tree["primitives"]!;
        var sharedIndex = (ushort[])tree["sharedVerticesIndex"]!;
        var packed = (uint[])tree["packedVertices"]!;
        var shared = (ulong[])tree["sharedVertices"]!;
        var runs = (object?[])tree["primitiveDataRuns"]!;
        float sx = (dmax[0] - dmin[0]) / 0x1FFFFF, sy = (dmax[1] - dmin[1]) / 0x1FFFFF, sz = (dmax[2] - dmin[2]) / 0x3FFFFF;
        var res = new List<HkMeshTriangle>();
        for (int si = 0; si < sections.Length; si++)
        {
            var s = (HkStruct)sections[si]!;
            var parms = (float[])s["codecParms"]!;
            uint firstPacked = (uint)s["firstPackedVertex"]!;
            int numPacked = (byte)s["numPackedVertices"]!;
            uint sv = (uint)((HkStruct)s["sharedVertices"]!)["data"]!;
            uint pr = (uint)((HkStruct)s["primitives"]!)["data"]!;
            uint dr = (uint)((HkStruct)s["dataRuns"]!)["data"]!;
            int sharedStart = (int)(sv >> 8), numShared = (int)(sv & 0xFF), firstPrim = (int)(pr >> 8), numPrim = (int)(pr & 0xFF);
            int firstRun = (int)(dr >> 8), numRuns = (int)(dr & 0xFF);
            var userData = new uint[numPrim];
            for (int r = 0; r < numRuns; r++)
            {
                var run = (HkStruct)runs[firstRun + r]!;
                int idx = (byte)run["index"]!, cnt = (byte)run["count"]!;
                uint val = (uint)run["value"]!;
                for (int k = idx; k < idx + cnt && k < numPrim; k++) userData[k] = val;
            }
            void Vertex(int i, Span<float> o)
            {
                if (i < numPacked)
                {
                    uint v = packed[firstPacked + i];
                    o[0] = (v & 0x7FF) * parms[3] + parms[0];
                    o[1] = ((v >> 11) & 0x7FF) * parms[4] + parms[1];
                    o[2] = (v >> 22) * parms[5] + parms[2];
                }
                else
                {
                    ulong v = shared[sharedIndex[sharedStart + i - numPacked]];
                    o[0] = (v & 0x1FFFFF) * sx + dmin[0];
                    o[1] = ((v >> 21) & 0x1FFFFF) * sy + dmin[1];
                    o[2] = (v >> 42) * sz + dmin[2];
                }
            }
            for (int p = 0; p < numPrim; p++)
            {
                var idx = (byte[])((HkStruct)prims[firstPrim + p]!)["indices"]!;
                bool quad = idx[2] != idx[3];
                bool custom = idx.Any(i => i >= numPacked + numShared);
                var verts = custom ? [] : new float[quad ? 12 : 9];
                for (int k = 0; k < verts.Length / 3; k++) Vertex(idx[k], verts.AsSpan(k * 3, 3));
                res.Add(new HkMeshTriangle { Vertices = verts, Indices = idx, Section = si, Primitive = p, UserData = userData[p], Quad = quad && !custom, Custom = custom });
            }
        }
        return res;
    }
}
