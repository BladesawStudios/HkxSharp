using System.Buffers.Binary;
using System.Collections.Concurrent;
using SarcSharp;
using ZsDicSharp;

namespace HkxSharp.Tests;

// Corpus tests run only when TOTK_ROMFS points at a romfs root; otherwise they are skipped.
internal static class Corpus
{
    internal const string TotkEnv = "TOTK_ROMFS";

    internal static string? TotkRoot =>
        Environment.GetEnvironmentVariable(TotkEnv) is { } root && Directory.Exists(root) ? root : null;

    // Actor packs carry the cloth and most object shapes; reading every pack is the slow part, so it happens once.
    private static readonly Lazy<IReadOnlyList<(string Name, byte[] Data)>> PackFiles = new(ReadPacks);

    private static IReadOnlyList<(string Name, byte[] Data)> ReadPacks()
    {
        string root = TotkRoot!;
        using ZsDic zs = ZsDic.FromRomfs(root);
        ConcurrentDictionary<string, byte[]> found = new();
        Parallel.ForEach(Directory.EnumerateFiles(Path.Combine(root, "Pack"), "*.pack.zs", SearchOption.AllDirectories), Workers, path =>
        {
            SarcFile pack;
            try { pack = SarcFile.FromBinary(zs.DecompressFile(path)); }
            catch (InvalidDataException) { return; }
            foreach (SarcEntry e in pack.Entries)
            {
                if (!e.Name.Contains(".bph") || e.Name.EndsWith(".bphhb") || e.Name.EndsWith(".bphhb.zs")) continue;
                string name = e.Name.EndsWith(".zs") ? e.Name[..^3] : e.Name;
                if (File.Exists(Path.Combine(root, name + ".zs"))) continue;
                if (!found.ContainsKey(name)) found.TryAdd(name, e.Name.EndsWith(".zs") ? zs.Decompress(e.Data, e.Name) : e.Data);
            }
        });
        return [.. found.Select(kv => (kv.Key, kv.Value))];
    }

    /// <summary>
    /// Every Havok tagfile in the romfs's Phive files - loose and in packs, helper bones aside since
    /// they are AAMP - visited in parallel with its file name and extension.
    /// </summary>
    internal static int ForEachTagfile(Action<string, string, byte[]> visit, params string[] extensions)
    {
        string root = TotkRoot!;
        bool Wanted(string name) => extensions.Length == 0 || extensions.Any(name.EndsWith);
        int count = 0;
        void Visit(string name, byte[] data)
        {
            string ext = Path.GetExtension(name);
            foreach (byte[] tag in Tagfiles(data))
            {
                Interlocked.Increment(ref count);
                visit(name, ext, tag);
            }
        }

        Parallel.ForEach(PackFiles.Value.Where(f => Wanted(f.Name)), Workers, f => Visit(f.Name, f.Data));

        using ZsDic zs = ZsDic.FromRomfs(root);
        Parallel.ForEach(Directory.EnumerateFiles(Path.Combine(root, "Phive"), "*.bph*.zs", SearchOption.AllDirectories), Workers, path =>
        {
            string name = Path.GetRelativePath(root, path)[..^3];
            if (Wanted(name)) Visit(name, zs.DecompressFile(path));
        });
        return count;
    }

    /// <summary>The tagfiles in a Phive container, nested containers included.</summary>
    internal static IEnumerable<byte[]> Tagfiles(byte[] data)
    {
        if (HkTagfile.IsTagfile(data))
        {
            int length = (int)(BinaryPrimitives.ReadUInt32BigEndian(data) & 0x3FFFFFFF);
            yield return length == data.Length ? data : data[..length];
            yield break;
        }
        if (data.Length < 12 || !data.AsSpan(0, 6).SequenceEqual("Phive\0"u8)) yield break;
        int slots = data[11];
        for (int i = 0; i < slots; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0xC + i * 4));
            int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0xC + slots * 4 + i * 4));
            if (offset == 0) break;
            if (size == 0) continue;
            foreach (byte[] tag in Tagfiles(data[offset..(offset + size)])) yield return tag;
        }
    }

    internal static readonly ParallelOptions Workers = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };
}

/// <summary>What a corpus test found wrong: a count, and the first few to show.</summary>
internal sealed class Problems
{
    private readonly ConcurrentQueue<string> _first = new();
    private int _count;

    public void Add(string problem)
    {
        if (Interlocked.Increment(ref _count) <= 20) _first.Enqueue(problem);
    }

    public void AssertNone() => Assert.True(_count == 0, $"{_count} problems, first:\n{string.Join("\n", _first)}");
}

public sealed class TotkCorpusFactAttribute : FactAttribute
{
    public TotkCorpusFactAttribute()
    {
        if (Corpus.TotkRoot is null) Skip = $"Set {Corpus.TotkEnv} to a TotK romfs root to run the corpus tests.";
    }
}
