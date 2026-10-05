using LotamSentinel.Application.Classification;
using LotamSentinel.Domain.Entities;
using Xunit;

namespace LotamSentinel.Tests;

public class TechnologyClassifierTests
{
    [Fact]
    public void ExactMaterialCode_Matches_STATCOM()
    {
        // Real confirmed mapping from Phase A: material 52.2 = STATCOM.
        var rules = new List<TechnologyMappingRule>
        {
            new() { TechnologyId = 5, MatchType = MappingMatchType.MaterialCode, MatchValue = "52.2", Priority = 100, IsActive = true }
        };

        var result = new TechnologyClassifier().Classify("52.2", "STATIC SYNCHRONOUS COMPENSATOR (STATCOM)", rules);

        Assert.Single(result);
        Assert.Equal(5, result[0].TechnologyId);
    }

    [Fact]
    public void NonMatchingCode_ReturnsNoTechnology()
    {
        var rules = new List<TechnologyMappingRule>
        {
            new() { TechnologyId = 5, MatchType = MappingMatchType.MaterialCode, MatchValue = "52.2", Priority = 100, IsActive = true }
        };

        var result = new TechnologyClassifier().Classify("1.1", "BARE CONDUCTORS TYPE ACAR/ACSR", rules);
        Assert.Empty(result);
    }

    [Fact]
    public void KeywordRule_MatchesOnDescription()
    {
        var rules = new List<TechnologyMappingRule>
        {
            new() { TechnologyId = 7, MatchType = MappingMatchType.Keyword, MatchValue = "HVDC", Priority = 100, IsActive = true }
        };

        var result = new TechnologyClassifier().Classify("30.5", "HVDC CONVERTER STATION EQUIPMENT", rules);
        Assert.Single(result);
        Assert.Equal(7, result[0].TechnologyId);
    }

    [Fact]
    public void PrefixRule_MatchesWholeCategory()
    {
        var rules = new List<TechnologyMappingRule>
        {
            new() { TechnologyId = 2, MatchType = MappingMatchType.MaterialCodePrefix, MatchValue = "10.", Priority = 100, IsActive = true }
        };

        var result = new TechnologyClassifier().Classify("10.13", "SF6 GAS FREE TRANSMISSION LINES (GIL)", rules);
        Assert.Single(result);
    }

    [Fact]
    public void MultipleRules_CanProduceMultipleTechnologies_NoDuplicates()
    {
        var rules = new List<TechnologyMappingRule>
        {
            new() { TechnologyId = 1, MatchType = MappingMatchType.MaterialCode, MatchValue = "52.2", Priority = 10, IsActive = true },
            new() { TechnologyId = 1, MatchType = MappingMatchType.Keyword, MatchValue = "STATCOM", Priority = 20, IsActive = true },
            new() { TechnologyId = 3, MatchType = MappingMatchType.Keyword, MatchValue = "COMPENSATOR", Priority = 30, IsActive = true },
        };

        var result = new TechnologyClassifier().Classify("52.2", "STATIC SYNCHRONOUS COMPENSATOR (STATCOM)", rules);

        Assert.Equal(2, result.Count); // technology 1 once (deduped), technology 3 once
        Assert.Contains(result, r => r.TechnologyId == 1);
        Assert.Contains(result, r => r.TechnologyId == 3);
    }

    [Fact]
    public void InactiveRule_IsIgnored()
    {
        var rules = new List<TechnologyMappingRule>
        {
            new() { TechnologyId = 1, MatchType = MappingMatchType.MaterialCode, MatchValue = "52.2", Priority = 10, IsActive = false }
        };

        var result = new TechnologyClassifier().Classify("52.2", "STATCOM", rules);
        Assert.Empty(result);
    }
}
