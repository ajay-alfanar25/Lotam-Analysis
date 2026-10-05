using LotamSentinel.Domain.Entities;

namespace LotamSentinel.Application.Comparison;

/// <summary>
/// Deterministic Version-A-vs-Version-B comparator. No AI involvement -
/// identity matching is (supplier id, material code) equality, all field
/// comparisons run on already-normalized text (see TextNormalizer), and every
/// output is one ChangeCandidate with a concrete explanation. Material-code
/// renumbering is REMOVED+NEW by default; supplying a CodeRemap entry is the
/// only way to compare a section across a code change (see
/// material_code_remap in the schema - this must be an explicit, confirmed
/// input, never inferred here).
/// </summary>
public class ComparisonEngine
{
    public List<ChangeCandidate> Compare(
        IReadOnlyList<EntrySnapshot> previous,
        IReadOnlyList<EntrySnapshot> current,
        IReadOnlyList<CodeRemap>? remaps = null)
    {
        var results = new List<ChangeCandidate>();
        var remapMap = (remaps ?? Array.Empty<CodeRemap>()).ToDictionary(r => r.FromCode, r => r.ToCode);

        // Key previous entries by the code they'll be compared under (remapped if applicable).
        var previousByKey = previous.ToLookup(e =>
            (Code: remapMap.TryGetValue(e.MaterialCode, out var mapped) ? mapped : e.MaterialCode, e.SupplierId));
        var currentByKey = current.ToLookup(e => (e.MaterialCode, e.SupplierId));

        var allKeys = previousByKey.Select(g => g.Key).Concat(currentByKey.Select(g => g.Key)).Distinct();

        foreach (var key in allKeys)
        {
            var prevEntries = previousByKey[key].ToList();
            var currEntries = currentByKey[key].ToList();

            if (prevEntries.Count == 0 && currEntries.Count > 0)
            {
                foreach (var c in currEntries)
                {
                    results.Add(new ChangeCandidate(
                        ChangeType.NewRecord, c.SupplierId, c.MaterialCode, c.MaterialSectionId,
                        null, c.EntryId, null, c.Status.ToString(),
                        $"{c.SupplierCanonicalName} newly appears qualified for material {c.MaterialCode} " +
                        $"with status {c.Status}."));
                }
                continue;
            }

            if (currEntries.Count == 0 && prevEntries.Count > 0)
            {
                foreach (var p in prevEntries)
                {
                    results.Add(new ChangeCandidate(
                        ChangeType.RemovedRecord, p.SupplierId, p.MaterialCode, p.MaterialSectionId,
                        p.EntryId, null, p.Status.ToString(), null,
                        $"{p.SupplierCanonicalName} no longer appears qualified for material {p.MaterialCode} " +
                        $"(was {p.Status})."));
                }
                continue;
            }

            // Present in both: pairwise compare (normally 1:1; if a supplier has
            // multiple entries for the same material in one release, pair by order).
            var pairCount = Math.Min(prevEntries.Count, currEntries.Count);
            for (int i = 0; i < pairCount; i++)
            {
                results.AddRange(CompareEntryPair(prevEntries[i], currEntries[i]));
            }
            for (int i = pairCount; i < currEntries.Count; i++)
            {
                results.Add(new ChangeCandidate(
                    ChangeType.NewRecord, currEntries[i].SupplierId, currEntries[i].MaterialCode,
                    currEntries[i].MaterialSectionId, null, currEntries[i].EntryId, null,
                    currEntries[i].Status.ToString(),
                    $"Additional qualification record for {currEntries[i].SupplierCanonicalName} " +
                    $"on material {currEntries[i].MaterialCode}."));
            }
            for (int i = pairCount; i < prevEntries.Count; i++)
            {
                results.Add(new ChangeCandidate(
                    ChangeType.RemovedRecord, prevEntries[i].SupplierId, prevEntries[i].MaterialCode,
                    prevEntries[i].MaterialSectionId, prevEntries[i].EntryId, null,
                    prevEntries[i].Status.ToString(), null,
                    $"A qualification record for {prevEntries[i].SupplierCanonicalName} " +
                    $"on material {prevEntries[i].MaterialCode} is no longer present."));
            }
        }

        return results;
    }

    private static IEnumerable<ChangeCandidate> CompareEntryPair(EntrySnapshot prev, EntrySnapshot curr)
    {
        if (prev.Status != curr.Status)
        {
            yield return new ChangeCandidate(
                ChangeType.StatusChanged, curr.SupplierId, curr.MaterialCode, curr.MaterialSectionId,
                prev.EntryId, curr.EntryId, prev.Status.ToString(), curr.Status.ToString(),
                $"{curr.SupplierCanonicalName}'s status for material {curr.MaterialCode} changed " +
                $"from {prev.Status} to {curr.Status}.");
        }

        if (!string.Equals(prev.ModelRatingNormalized, curr.ModelRatingNormalized, StringComparison.OrdinalIgnoreCase))
        {
            var changeType = LooksLikeVoltageField(curr.ModelRatingNormalized) || LooksLikeVoltageField(prev.ModelRatingNormalized)
                ? ChangeType.VoltageChanged
                : ChangeType.RatingChanged;

            yield return new ChangeCandidate(
                changeType, curr.SupplierId, curr.MaterialCode, curr.MaterialSectionId,
                prev.EntryId, curr.EntryId, prev.ModelRatingRaw, curr.ModelRatingRaw,
                $"{curr.SupplierCanonicalName}'s approved model/rating for material {curr.MaterialCode} " +
                "changed.");
        }

        if (!string.Equals(prev.LocalRepresentativeRaw?.Trim(), curr.LocalRepresentativeRaw?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChangeCandidate(
                ChangeType.AgentChanged, curr.SupplierId, curr.MaterialCode, curr.MaterialSectionId,
                prev.EntryId, curr.EntryId, prev.LocalRepresentativeRaw, curr.LocalRepresentativeRaw,
                $"Authorized local representative changed for {curr.SupplierCanonicalName} " +
                $"on material {curr.MaterialCode}.");
        }

        if (!string.Equals(prev.CountryNormalized, curr.CountryNormalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChangeCandidate(
                ChangeType.CountryChanged, curr.SupplierId, curr.MaterialCode, curr.MaterialSectionId,
                prev.EntryId, curr.EntryId, prev.CountryRaw, curr.CountryRaw,
                $"Manufacturer country changed for {curr.SupplierCanonicalName} on material {curr.MaterialCode}.");
        }

        // Only flag remark drift if status didn't already change (status change
        // is almost always accompanied by different remarks text, and we don't
        // want to double-report the same underlying event).
        if (prev.Status == curr.Status &&
            !string.Equals(prev.RemarksNormalized, curr.RemarksNormalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChangeCandidate(
                ChangeType.RemarkChanged, curr.SupplierId, curr.MaterialCode, curr.MaterialSectionId,
                prev.EntryId, curr.EntryId, prev.RemarksRaw, curr.RemarksRaw,
                $"Remarks/conditions text changed for {curr.SupplierCanonicalName} on material {curr.MaterialCode}.");
        }
    }

    private static bool LooksLikeVoltageField(string? text)
        => text is not null && text.Contains("kv", StringComparison.OrdinalIgnoreCase);
}
