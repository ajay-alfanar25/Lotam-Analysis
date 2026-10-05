using System.Text.RegularExpressions;
using LotamSentinel.Domain.Entities;

namespace LotamSentinel.Application.Classification;

public record StatusClassificationResult(ApprovalStatus Status, decimal Confidence, DerivationSource Source);

/// <summary>
/// Applies StatusKeywordRule patterns (loaded from the DB-backed configuration
/// table, passed in here as plain data so this class has zero DB dependency
/// and is fully unit-testable) against RemarksRaw text. Status is NEVER a
/// printed field in LOTAM - it must be derived from free text, and UNKNOWN is
/// a legitimate, expected result when nothing matches (never force a guess).
/// </summary>
public class StatusClassifier
{
    public StatusClassificationResult Classify(string? remarksRaw, IEnumerable<StatusKeywordRule> rules)
    {
        if (string.IsNullOrWhiteSpace(remarksRaw))
            return new StatusClassificationResult(ApprovalStatus.Unknown, 0m, DerivationSource.Rule);

        var ordered = rules.Where(r => r.IsActive).OrderBy(r => r.Priority);

        foreach (var rule in ordered)
        {
            if (Regex.IsMatch(remarksRaw, rule.Pattern, RegexOptions.IgnoreCase))
            {
                return new StatusClassificationResult(rule.StatusValue, 0.9m, DerivationSource.Rule);
            }
        }

        return new StatusClassificationResult(ApprovalStatus.Unknown, 0m, DerivationSource.Rule);
    }
}
