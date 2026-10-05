using LotamSentinel.Application.Reporting;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Application.Comparison;

public class MaterialReportBuilder
{
    private readonly LotamSentinelDbContext _db;
    public MaterialReportBuilder(LotamSentinelDbContext db) => _db = db;

    // Same set as ReleasesController.GetEntriesSummary and the same
    // ChangeType->section mapping as the frontend's Material Explorer
    // highlight logic - kept in sync deliberately, not by shared code
    // (C# backend / TypeScript frontend can't literally share this map),
    // but the semantics must match or the PDF would highlight a different
    // field than the live UI does for the same "Updated" row.
    private static readonly ChangeType[] UpdateChangeTypes =
    {
        ChangeType.StatusChanged, ChangeType.ModelAdded, ChangeType.ModelRemoved,
        ChangeType.RatingChanged, ChangeType.VoltageChanged, ChangeType.SpecificationChanged,
        ChangeType.ConditionChanged, ChangeType.RemarkChanged, ChangeType.AgentChanged,
        ChangeType.CountryChanged, ChangeType.ScopeChanged, ChangeType.OtherMaterialChange,
    };

    private static readonly Dictionary<ChangeType, string> ChangeTypeToSection = new()
    {
        [ChangeType.StatusChanged] = "status",
        [ChangeType.ModelAdded] = "model",
        [ChangeType.ModelRemoved] = "model",
        [ChangeType.RatingChanged] = "model",
        [ChangeType.VoltageChanged] = "model",
        [ChangeType.SpecificationChanged] = "model",
        [ChangeType.ScopeChanged] = "model",
        [ChangeType.ConditionChanged] = "remarks",
        [ChangeType.RemarkChanged] = "remarks",
        [ChangeType.OtherMaterialChange] = "remarks",
        [ChangeType.AgentChanged] = "rep",
        [ChangeType.CountryChanged] = "manufacturer",
    };

    public async Task<ExportReport> BuildAsync(
        long releaseId, List<string>? materialCodes, long? previousReleaseId, long? currentReleaseId,
        CancellationToken ct = default)
    {
        var query = _db.LotamEntries
            .Include(e => e.MaterialSection).ThenInclude(m => m.MaterialCategory)
            .Include(e => e.SupplierAlias).ThenInclude(a => a.Supplier)
            .Where(e => e.MaterialSection.ReleaseId == releaseId);

        if (materialCodes is { Count: > 0 })
        {
            var codesSet = materialCodes.ToHashSet();
            query = query.Where(e => codesSet.Contains(e.MaterialSection.Code));
        }

        var entries = await query.ToListAsync(ct);

        HashSet<long> newIds = new(), removedIds = new(), updatedIds = new();
        Dictionary<long, List<ChangeType>> changeTypesByEntry = new();

        if (previousReleaseId is not null && currentReleaseId is not null)
        {
            var pairChanges = await _db.ChangeRecords
                .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId)
                .ToListAsync(ct);

            if (releaseId == currentReleaseId)
            {
                newIds = pairChanges.Where(c => c.ChangeType == ChangeType.NewRecord && c.CurrentEntryId != null)
                    .Select(c => c.CurrentEntryId!.Value).ToHashSet();
                var updates = pairChanges.Where(c => UpdateChangeTypes.Contains(c.ChangeType) && c.CurrentEntryId != null).ToList();
                updatedIds = updates.Select(c => c.CurrentEntryId!.Value).ToHashSet();
                changeTypesByEntry = updates.GroupBy(c => c.CurrentEntryId!.Value)
                    .ToDictionary(g => g.Key, g => g.Select(c => c.ChangeType).ToList());
            }
            else if (releaseId == previousReleaseId)
            {
                removedIds = pairChanges.Where(c => c.ChangeType == ChangeType.RemovedRecord && c.PreviousEntryId != null)
                    .Select(c => c.PreviousEntryId!.Value).ToHashSet();
                var updates = pairChanges.Where(c => UpdateChangeTypes.Contains(c.ChangeType) && c.PreviousEntryId != null).ToList();
                updatedIds = updates.Select(c => c.PreviousEntryId!.Value).ToHashSet();
                changeTypesByEntry = updates.GroupBy(c => c.PreviousEntryId!.Value)
                    .ToDictionary(g => g.Key, g => g.Select(c => c.ChangeType).ToList());
            }
        }

        var report = new ExportReport
        {
            Title = "LOTAM Sentinel — Material Report",
            Subtitle = $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC · {entries.Count} record(s)" +
                       (materialCodes is { Count: > 0 } ? $" · Filtered to {materialCodes.Count} material group(s)" : " · All materials"),
            Columns = new List<ReportColumn>
            {
                new("Manufacturer Name, Plant, Location/Country, Address, Website, Telephone & E-mail", 3.4f),
                new("Authorized Local Representative/Vendor", 2f),
                new("Type/Model and Rating", 2f),
                new("Type Tests Details", 2f),
                new("Remarks/Observations", 3.6f),
            },
        };

        var ordered = entries
            .OrderBy(e => ParseSortKey(e.MaterialSection.Code))
            .ThenBy(e => e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName);

        foreach (var e in ordered)
        {
            string? badge = newIds.Contains(e.Id) ? "New" : removedIds.Contains(e.Id) ? "Removed" : updatedIds.Contains(e.Id) ? "Updated" : null;

            var tags = new List<ReportTag> { new(e.DerivedStatus.ToString(), StatusColorTag(e.DerivedStatus)) };
            if (badge is not null)
                tags.Add(new ReportTag(badge.ToUpperInvariant(), badge switch { "New" => "emerald", "Removed" => "rose", _ => "amber" }));

            var changedSections = badge == "Updated" && changeTypesByEntry.TryGetValue(e.Id, out var types)
                ? types.Select(t => ChangeTypeToSection.GetValueOrDefault(t)).Where(s => s is not null).ToHashSet()
                : new HashSet<string?>();

            report.Rows.Add(new ReportRow
            {
                Tags = tags,
                Cells = new List<ReportCell>
                {
                    new(e.RawManufacturerBlock, changedSections.Contains("manufacturer") ? "changed" : null),
                    new(e.LocalRepresentativeRaw ?? "", changedSections.Contains("rep") ? "changed" : null),
                    new(e.ModelRatingRaw ?? "", changedSections.Contains("model") ? "changed" : null),
                    new(e.TypeTestsRaw ?? ""),
                    new(e.RemarksRaw ?? "", changedSections.Contains("remarks") ? "changed" : null),
                },
                // Grouping/identity metadata - ignored by PdfReportRenderer,
                // used by HtmlReportRenderer to build its Material ->
                // Supplier collapsible cards (the same grouping as Material
                // Explorer's "New Hierarchy" tab).
                GroupCode = e.MaterialSection.Code,
                GroupLabel = $"{e.MaterialSection.MaterialCategory.Name} — {e.MaterialSection.RawDescription}",
                GroupStandard = e.MaterialSection.ApplicableStandard,
                SupplierName = e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName,
                Country = e.ManufacturerCountryRaw,
            });
        }

        // Computed from the group labels the rows actually ended up with
        // (set just above), so the "Report Summary" section names the real
        // material group(s) this export covers - see ReportSummaryText.
        report.ReportSummary = ReportSummaryText.Build(report.Rows.Select(r => r.GroupLabel));

        ApplyExecutiveSummary(report, entries, newIds, removedIds, updatedIds);

        return report;
    }

    /// <summary>Headline numbers + a one-paragraph narrative for whoever
    /// opens this report first - distinct suppliers, how many material
    /// groups are covered, and (when this export is scoped to a
    /// previous/current release pair) how many records are new, removed,
    /// updated, or currently suspended/disqualified/blacklisted. Reuses the
    /// same newIds/removedIds/updatedIds already computed above rather than
    /// re-deriving them from tags, so the numbers can never drift from what
    /// the row badges themselves show.</summary>
    private static void ApplyExecutiveSummary(
        ExportReport report, List<LotamEntry> entries,
        HashSet<long> newIds, HashSet<long> removedIds, HashSet<long> updatedIds)
    {
        if (entries.Count == 0) return;

        var distinctSuppliers = entries
            .Select(e => e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var groupCount = entries.Select(e => e.MaterialSection.Code).Distinct().Count();
        var suspendedCount = entries.Count(e =>
            e.DerivedStatus is ApprovalStatus.Suspended or ApprovalStatus.Disqualified or ApprovalStatus.Blacklisted);

        var hasComparison = newIds.Count > 0 || removedIds.Count > 0 || updatedIds.Count > 0;

        var narrative = $"This report covers {distinctSuppliers} supplier{(distinctSuppliers == 1 ? "" : "s")} " +
            $"across {groupCount} material group{(groupCount == 1 ? "" : "s")}, totaling {entries.Count} record{(entries.Count == 1 ? "" : "s")}.";
        if (hasComparison)
            narrative += $" Compared with the previous release, {newIds.Count} new record{(newIds.Count == 1 ? " was" : "s were")} added, " +
                $"{removedIds.Count} {(removedIds.Count == 1 ? "was" : "were")} removed, and {updatedIds.Count} {(updatedIds.Count == 1 ? "was" : "were")} updated.";
        if (suspendedCount > 0)
            narrative += $" {suspendedCount} record{(suspendedCount == 1 ? " is" : "s are")} currently suspended, disqualified, or blacklisted and may need review.";

        report.ExecutiveSummary = narrative;
        report.SummaryStats = new List<SummaryStat>
        {
            new("Suppliers", distinctSuppliers.ToString()),
            new("Material Groups", groupCount.ToString()),
            new("Total Records", entries.Count.ToString()),
        };
        if (hasComparison)
        {
            report.SummaryStats.Add(new("New", newIds.Count.ToString(), "emerald"));
            report.SummaryStats.Add(new("Removed", removedIds.Count.ToString(), "rose"));
            report.SummaryStats.Add(new("Updated", updatedIds.Count.ToString(), "amber"));
        }
        if (suspendedCount > 0)
            report.SummaryStats.Add(new("Suspended/Blacklisted", suspendedCount.ToString(), "rose"));
    }

    private static string StatusColorTag(ApprovalStatus status) => status switch
    {
        ApprovalStatus.Qualified or ApprovalStatus.Reactivated => "emerald",
        ApprovalStatus.Conditional => "amber",
        ApprovalStatus.Suspended or ApprovalStatus.Disqualified or ApprovalStatus.Blacklisted => "rose",
        _ => "neutral",
    };

    private static (int, int) ParseSortKey(string code)
    {
        var parts = code.Split('.', 2);
        int.TryParse(parts[0], out var a);
        int.TryParse(parts.Length > 1 ? parts[1] : "0", out var b);
        return (a, b);
    }
}
