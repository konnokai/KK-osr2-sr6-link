using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class CharacterPairTests
{
    [Fact]
    public void Normalize_UsesRootsFromDisplayAndRootOnlyLabels()
    {
        Assert.Equal("chaF_001-chaM_001", CharacterPair.Normalize("Girl Name(chaF_001)-Boy Name(chaM_001)"));
        Assert.Equal("chaF_001-chaM_001", CharacterPair.Normalize("chaF_001-chaM_001"));
    }

    [Fact]
    public void SplitLabels_DoesNotBreakNamesContainingHyphens()
    {
        Assert.True(CharacterPair.TrySplitLabels(
            "Anne-Marie(chaF_001)-Bob-Joe(chaM_001)", out var female, out var male));
        Assert.Equal("Anne-Marie(chaF_001)", female);
        Assert.Equal("Bob-Joe(chaM_001)", male);
    }

    [Fact]
    public void CustomPartnerRoot_WorksAsMaleSide()
    {
        Assert.Equal("chaF_001-chaP_001", CharacterPair.Normalize("YanHua 4.0 改(chaF_001)-canine(chaP_001)"));
        Assert.True(CharacterPair.TrySplitLabels(
            "YanHua 4.0 改(chaF_001)-canine(chaP_001)", out var female, out var male));
        Assert.Equal("YanHua 4.0 改(chaF_001)", female);
        Assert.Equal("canine(chaP_001)", male);
    }

    [Fact]
    public void Normalize_RejectsIncompletePair()
    {
        Assert.False(CharacterPair.TryNormalize("chaF_001-only", out _));
    }
}
