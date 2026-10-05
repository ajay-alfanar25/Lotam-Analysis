using LotamSentinel.Application.Comparison;
using LotamSentinel.Domain.Entities;
using Xunit;

namespace LotamSentinel.Tests;

public class SeverityCalculatorTests
{
    private static List<SeverityRule> DefaultRules() => new()
    {
        new SeverityRule { ChangeType = ChangeType.StatusChanged, ConditionJson = "{\"status_to\":\"Suspended\"}", Severity = Severity.Critical, Priority = 10, IsActive = true },
        new SeverityRule { ChangeType = ChangeType.StatusChanged, ConditionJson = "{\"status_from\":\"Qualified\",\"status_to\":\"Conditional\"}", Severity = Severity.High, Priority = 30, IsActive = true },
        new SeverityRule { ChangeType = ChangeType.RemovedRecord, ConditionJson = null, Severity = Severity.Critical, Priority = 20, IsActive = true },
        new SeverityRule { ChangeType = ChangeType.NewRecord, ConditionJson = null, Severity = Severity.Medium, Priority = 50, IsActive = true },
        new SeverityRule { ChangeType = ChangeType.RemarkChanged, ConditionJson = null, Severity = Severity.Low, Priority = 90, IsActive = true },
    };

    [Fact]
    public void StatusChangeToSuspended_IsCritical()
    {
        var candidate = new ChangeCandidate(ChangeType.StatusChanged, 1, "1.1", 1, 1, 2, "Qualified", "Suspended", "x");
        var (severity, source) = new SeverityCalculator().Resolve(candidate, DefaultRules());
        Assert.Equal(Severity.Critical, severity);
        Assert.Equal(DerivationSource.Rule, source);
    }

    [Fact]
    public void QualifiedToConditional_IsHigh()
    {
        var candidate = new ChangeCandidate(ChangeType.StatusChanged, 1, "1.1", 1, 1, 2, "Qualified", "Conditional", "x");
        var (severity, _) = new SeverityCalculator().Resolve(candidate, DefaultRules());
        Assert.Equal(Severity.High, severity);
    }

    [Fact]
    public void RemovedRecord_IsCritical_RegardlessOfValues()
    {
        var candidate = new ChangeCandidate(ChangeType.RemovedRecord, 1, "39.1", 1, 1, null, "Qualified", null, "x");
        var (severity, _) = new SeverityCalculator().Resolve(candidate, DefaultRules());
        Assert.Equal(Severity.Critical, severity);
    }

    [Fact]
    public void NewRecord_IsMedium()
    {
        var candidate = new ChangeCandidate(ChangeType.NewRecord, 1, "52.2", 1, null, 2, null, "Qualified", "x");
        var (severity, _) = new SeverityCalculator().Resolve(candidate, DefaultRules());
        Assert.Equal(Severity.Medium, severity);
    }

    [Fact]
    public void NoMatchingRule_FallsBackToMediumAndFlagsForReview()
    {
        var candidate = new ChangeCandidate(ChangeType.SpecificationChanged, 1, "2.5", 1, 1, 2, "a", "b", "x");
        var (severity, source) = new SeverityCalculator().Resolve(candidate, DefaultRules());
        Assert.Equal(Severity.Medium, severity);
        Assert.Equal(DerivationSource.Manual, source); // not silently authoritative
    }
}
