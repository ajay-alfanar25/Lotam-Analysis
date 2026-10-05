using System.Text.Json;
using LotamSentinel.Domain.Entities;

namespace LotamSentinel.Application.Comparison;

/// <summary>
/// Resolves a ChangeCandidate's Severity from the configurable SeverityRule
/// table (condition_json, matched against a small set of known keys).
/// Falls back to MEDIUM if no rule matches, and flags that as AI/Manual-
/// reviewable via SeveritySource = Manual so it doesn't silently look
/// authoritative.
/// </summary>
public class SeverityCalculator
{
    public (Severity Severity, DerivationSource Source) Resolve(
        ChangeCandidate candidate, IEnumerable<SeverityRule> rules)
    {
        foreach (var rule in rules.Where(r => r.IsActive && r.ChangeType == candidate.ChangeType)
                                  .OrderBy(r => r.Priority))
        {
            if (string.IsNullOrWhiteSpace(rule.ConditionJson))
                return (rule.Severity, DerivationSource.Rule);

            if (MatchesCondition(candidate, rule.ConditionJson))
                return (rule.Severity, DerivationSource.Rule);
        }

        return (Severity.Medium, DerivationSource.Manual); // no rule matched - needs review
    }

    private static bool MatchesCondition(ChangeCandidate candidate, string conditionJson)
    {
        using var doc = JsonDocument.Parse(conditionJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("status_to", out var statusTo)
            && !string.Equals(candidate.CurrentValue, statusTo.GetString(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (root.TryGetProperty("status_from", out var statusFrom)
            && !string.Equals(candidate.PreviousValue, statusFrom.GetString(), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
