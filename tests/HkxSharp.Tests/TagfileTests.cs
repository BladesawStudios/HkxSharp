using System.Collections.Concurrent;

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

    [TotkCorpusFact]
    public void EveryPhysicsTagfileDecodesWithNoIssues()
    {
        Problems problems = new();
        ConcurrentDictionary<string, int> kinds = [];
        int count = Corpus.ForEachTagfile((name, ext, tag) =>
        {
            kinds.AddOrUpdate(ext, 1, (_, n) => n + 1);
            try
            {
                HkTagfile t = HkTagfile.FromBinary(tag);
                if (t.Issues.Count > 0) problems.Add($"{name}: {t.Issues.Count} issues, first {t.Issues[0]}");
                if (t.Root is null) problems.Add($"{name}: no root object");
            }
            catch (Exception e) { problems.Add($"{name}: {e.GetType().Name} {e.Message}"); }
        });

        problems.AssertNone();
        Assert.True(count > 26000, $"only {count} tagfiles found");
        Assert.Equal([".bphcl", ".bphnm", ".bphsc", ".bphsh"], kinds.Keys.Order());
    }

    [TotkCorpusFact]
    public void EveryPhysicsTagfileWritesBackUnchanged()
    {
        Problems problems = new();
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            try
            {
                byte[] back = HkxFile.FromBinary(tag).ToBinary();
                if (!back.AsSpan().SequenceEqual(tag))
                {
                    int at = 0;
                    while (at < Math.Min(back.Length, tag.Length) && back[at] == tag[at]) at++;
                    problems.Add($"{name}: differs at 0x{at:X} ({tag.Length} bytes in, {back.Length} out)");
                }
            }
            catch (Exception e) { problems.Add($"{name}: {e.GetType().Name} {e.Message}"); }
        });

        problems.AssertNone();
    }

    [TotkCorpusFact]
    public void FloatsComeBackAsFloats()
    {
        Problems problems = new();
        Corpus.ForEachTagfile((name, _, tag) =>
        {
            HkTagfile t = HkTagfile.FromBinary(tag);
            foreach (HkObject o in t.Objects)
                for (int i = 0; i < o.Fields!.Members.Count; i++)
                    if (o.Fields.Members[i].Type.Kind == HkKind.F32 && o.Fields.Values[i] is not float)
                        problems.Add($"{name}: {o}.{o.Fields.Members[i].Name} is {o.Fields.Values[i]?.GetType().Name}");
        }, ".bphcl");

        problems.AssertNone();
    }
}
