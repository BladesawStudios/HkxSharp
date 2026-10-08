namespace HkxSharp;

public static class HkPackedVector3
{
    public static (float X, float Y, float Z) Unpack(ReadOnlySpan<short> v)
    {
        float scale = BitConverter.Int32BitsToSingle(v[3] << 16);
        return ((v[0] << 16) * scale, (v[1] << 16) * scale, (v[2] << 16) * scale);
    }

    /// <summary>Packs as Havok does: round half up, with the exponent raised when the largest component would round past 32767.</summary>
    public static short[] Pack(float x, float y, float z)
    {
        const float minMax = 1.4210855E-14f;
        float max = Math.Max(minMax, Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))));
        int exp = (BitConverter.SingleToInt32Bits(max) >> 23) & 0xFF;
        if (Round(max * MathF.ScaleB(1f, 141 - exp)) > short.MaxValue) exp++;
        float inv = MathF.ScaleB(1f, 141 - exp);
        return [Quantize(x * inv), Quantize(y * inv), Quantize(z * inv), (short)((exp - 30) << 7)];
    }

    static int Round(float v) => (int)MathF.Floor(v + 0.5f);

    static short Quantize(float v) => (short)Math.Clamp(Round(v), short.MinValue, short.MaxValue);
}
