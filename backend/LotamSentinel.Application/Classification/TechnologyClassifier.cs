using System.Text.RegularExpressions;
using LotamSentinel.Domain.Entities;

namespace LotamSentinel.Application.Classification;

public record TechnologyMatch(long TechnologyId, decimal Confidence, DerivationSource Source);

/// <summary>
/// Classifies a material section into zero or more technologies using
/// deterministic TechnologyMappingRule rows (material_code exact match,
/// material_code_prefix, or keyword-against-description). Rules are
/// evaluated in priority order; multiple matching rules all apply (a
/// material can legitimately serve more than one technology). No AI
/// involvement here by design - AI classification, when used, is a separate
/// fallback step for sections no rule covers, and its output is stored with
/// Source = Ai, never silently equated to a rule match.
/// </summary>
public class TechnologyClassifier
{
    public List<TechnologyMatch> Classify(
        string materialCode, string normalizedDescription, IEnumerable<TechnologyMappingRule> rules)
    {
        var matches = new List<TechnologyMatch>();

        foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Priority))
        {
            bool isMatch = rule.MatchType switch
            {
                MappingMatchType.MaterialCode => string.Equals(rule.MatchValue, materialCode, StringComparison.OrdinalIgnoreCase),
                MappingMatchType.MaterialCodePrefix => materialCode.StartsWith(rule.MatchValue, StringComparison.OrdinalIgnoreCase),
                MappingMatchType.Keyword => Regex.IsMatch(normalizedDescription, rule.MatchValue, RegexOptions.IgnoreCase),
                _ => false
            };

            if (isMatch && matches.All(m => m.TechnologyId != rule.TechnologyId))
            {
                matches.Add(new TechnologyMatch(rule.TechnologyId, 1.0m, DerivationSource.Rule));
            }
        }

        return matches;
    }
}
