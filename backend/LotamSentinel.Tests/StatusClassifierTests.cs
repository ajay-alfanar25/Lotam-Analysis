using LotamSentinel.Application.Classification;
using LotamSentinel.Domain.Entities;
using Xunit;

namespace LotamSentinel.Tests;

public class StatusClassifierTests
{
    private static List<StatusKeywordRule> DefaultRules() => new()
    {
        new StatusKeywordRule { Pattern = "SUSPENDED", StatusValue = ApprovalStatus.Suspended, Priority = 10, IsActive = true },
        new StatusKeywordRule { Pattern = "DISQUALIFIED", StatusValue = ApprovalStatus.Disqualified, Priority = 10, IsActive = true },
        new StatusKeywordRule { Pattern = "conditional approval", StatusValue = ApprovalStatus.Conditional, Priority = 20, IsActive = true },
        new StatusKeywordRule { Pattern = "BLACKLISTED", StatusValue = ApprovalStatus.Blacklisted, Priority = 10, IsActive = true },
        new StatusKeywordRule { Pattern = "pre-?qualified|PQ Letter", StatusValue = ApprovalStatus.Qualified, Priority = 100, IsActive = true },
    };

    [Fact]
    public void Suspended_RealRemarkText_ClassifiesAsSuspended()
    {
        // Verbatim excerpt style confirmed in Phase A (material 39.1 CABLE ISOLATION EQUIPMENT area)
        var remarks = "SUSPENDED for all SEC/National Grid SA ongoing and forthcoming projects based on VP, Engineering letter # 21-00877-30504401 dated August 03, 2021.";
        var result = new StatusClassifier().Classify(remarks, DefaultRules());
        Assert.Equal(ApprovalStatus.Suspended, result.Status);
    }

    [Fact]
    public void Disqualified_RealRemarkText_ClassifiesAsDisqualified()
    {
        var remarks = "Based on TS&SD letter 11-01374-03032301 dated April 27, 2011. It is DISQUALIFIED for 380KV XLPE Cable.";
        var result = new StatusClassifier().Classify(remarks, DefaultRules());
        Assert.Equal(ApprovalStatus.Disqualified, result.Status);
    }

    [Fact]
    public void PqLetter_ClassifiesAsQualified()
    {
        var remarks = "PQ Letter Based on S&SD letters # 15-01562-1-30504401 dated 18th March 2015, it is pre-qualified with the following conditions:";
        var result = new StatusClassifier().Classify(remarks, DefaultRules());
        Assert.Equal(ApprovalStatus.Qualified, result.Status);
    }

    [Fact]
    public void ConditionalApproval_ClassifiesAsConditional()
    {
        var remarks = "The Products being reviewed and given the conditional approval from Fire Department, manufacturer compliance is confirmed through...";
        var result = new StatusClassifier().Classify(remarks, DefaultRules());
        Assert.Equal(ApprovalStatus.Conditional, result.Status);
    }

    [Fact]
    public void NoMatchingPattern_ReturnsUnknown_NotAGuess()
    {
        var result = new StatusClassifier().Classify("Contact person changed to Mr. Smith.", DefaultRules());
        Assert.Equal(ApprovalStatus.Unknown, result.Status);
    }

    [Fact]
    public void EmptyRemarks_ReturnsUnknown()
    {
        var result = new StatusClassifier().Classify(null, DefaultRules());
        Assert.Equal(ApprovalStatus.Unknown, result.Status);
    }

    [Fact]
    public void SuspendedTakesPriorityOverQualifiedWhenBothPatternsPresent()
    {
        // Real documents can mention "PQ Letter" as a historical reference even
        // in a remark that also suspends the manufacturer - priority ordering
        // must ensure SUSPENDED wins.
        var remarks = "Previously PQ Letter based prequalification. SUSPENDED for all SEC/National Grid SA ongoing projects.";
        var result = new StatusClassifier().Classify(remarks, DefaultRules());
        Assert.Equal(ApprovalStatus.Suspended, result.Status);
    }
}
