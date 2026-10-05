using LotamSentinel.Application.Common;
using Xunit;

namespace LotamSentinel.Tests;

public class TextNormalizerTests
{
    [Fact]
    public void EnDashAndHyphen_AreEquivalent()
    {
        // Real example found during Phase A analysis between the two supplied releases.
        var v1 = "WOOD POLES, ANSI CLASS HI\u2013H3"; // en dash
        var v2 = "WOOD POLES, ANSI CLASS HI-H3";      // plain hyphen

        Assert.True(TextNormalizer.AreEquivalent(v1, v2));
    }

    [Fact]
    public void ExtraWhitespace_DoesNotCauseFalseDifference()
    {
        Assert.True(TextNormalizer.AreEquivalent("MEDIUM   VOLTAGE  CABLE", "MEDIUM VOLTAGE CABLE"));
    }

    [Fact]
    public void GenuinelyDifferentText_IsNotEquivalent()
    {
        Assert.False(TextNormalizer.AreEquivalent("QUALIFIED", "SUSPENDED"));
    }

    [Theory]
    [InlineData("ABB", "ABB")]
    [InlineData("ABB Ltd.", "ABB")]
    [InlineData("ABB AG", "ABB")]
    public void SupplierMatchKey_StripsCommonCorporateSuffixes(string raw, string expected)
    {
        Assert.Equal(expected, TextNormalizer.NormalizeForSupplierMatch(raw));
    }

    [Fact]
    public void SupplierMatchKey_DoesNotEquateDifferentSubsidiaries()
    {
        // Confirmed real example from Phase A - these should NOT collapse to
        // the same match key, since they may be genuinely different entities.
        var a = TextNormalizer.NormalizeForSupplierMatch("ABB Contracting Co.");
        var b = TextNormalizer.NormalizeForSupplierMatch("ABB EIL");
        Assert.NotEqual(a, b);
    }
}
