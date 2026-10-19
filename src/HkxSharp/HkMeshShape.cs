namespace HkxSharp;

public static class HkMeshShape
{
    public static List<HkMeshTriangle> Decode(HkObject mesh) => Decode(mesh.Fields ?? throw new ArgumentException($"{mesh} was not decoded."));

    public static List<HkMeshTriangle> Decode(HkStruct mesh)
    {
        var inv = (float[])((HkStruct)mesh["vertexConversionUtil"]!)["bitScale16Inv"]!;
        var sections = (object?[])mesh["geometrySections"]!;
        var tags = ((object?[])mesh["shapeTagTable"]!).Cast<HkStruct>()
            .Select(e => ((uint)e["meshPrimitiveKey"]!, (ushort)e["shapeTag"]!)).ToArray();
        var res = new List<HkMeshTriangle>();
        int run = -1;
        for (int si = 0; si < sections.Length; si++)
        {
            var s = (HkStruct)sections[si]!;
            var origin = (uint[])s["sectionOffset"]!;
            var vb = (byte[])s["vertexBuffer"]!;
            var prims = (object?[])s["primitives"]!;
            var interior = (byte[])s["interiorPrimitiveBitField"]!;
            bool raw = origin.All(o => o == int.MaxValue);
            int numVerts = vb.Length / (raw ? 12 : 6);
            void Vertex(int i, Span<float> o)
            {
                for (int k = 0; k < 3; k++)
                    o[k] = raw ? BitConverter.ToSingle(vb, i * 12 + k * 4) : (BitConverter.ToUInt16(vb, i * 6 + k * 2) + (int)origin[k]) * inv[k];
            }
            for (int p = 0; p < prims.Length; p++)
            {
                var prim = (HkStruct)prims[p]!;
                byte[] idx = [(byte)prim["aId"]!, (byte)prim["bId"]!, (byte)prim["cId"]!, (byte)prim["dId"]!];
                bool quad = idx[2] != idx[3];
                if (idx.Any(i => i >= numVerts)) throw new InvalidDataException($"Section {si} primitive {p} indexes past its {numVerts} vertices.");
                var verts = new float[quad ? 12 : 9];
                for (int k = 0; k < verts.Length / 3; k++) Vertex(idx[k], verts.AsSpan(k * 3, 3));
                uint key = (uint)(si << 9 | p << 1);
                while (run + 1 < tags.Length && tags[run + 1].Item1 <= key) run++;
                int bit = p * 2;
                res.Add(new HkMeshTriangle
                {
                    Vertices = verts, Indices = idx, Section = si, Primitive = p, Key = key, Quad = quad,
                    UserData = run >= 0 ? tags[run].Item2 : 0xFFFFu,
                    Interior = bit / 8 < interior.Length ? (interior[bit / 8] >> (bit % 8)) & 3 : 0
                });
            }
        }
        return res;
    }
}
