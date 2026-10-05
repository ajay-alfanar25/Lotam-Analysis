using LotamSentinel.Application.Reporting;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Application.Comparison;

public class ChangeReportBuilder
{
    private readonly LotamSentinelDbContext _db;
    public ChangeReportBuilder(LotamSentinelDbContext db) => _db = db;

    public async Task<ExportReport> BuildAsync(
        long previousReleaseId, long currentReleaseId,
        string? technology, string? supplier, string? severity, string? changeType, string? search,
        CancellationToken ct = default, long? supplierId = null, string? materialCategory = null,
        string? materialSectionCode = null)
    {
        // Deliberately the same filter logic as ChangesController.GetChanges,
        // just without pagination - an export is expected to cover every
        // matching record, not just the currently-visible page.
        //
        // supplierId/materialCategory are additional filters used only by
        // the subscription-digest job (SubscriptionDigestWorker) - a
        // MaterialCategory subscription's TargetCode is a category *name*
        // (see UserNotificationSubscription), and a Supplier subscription's
        // TargetCode is the supplier's numeric id (see ProfileComponent's
        // addSubscription - it stores String(selectedSupplier.id), not the
        // name), so both need an exact-match filter rather than the
        // free-text `supplier` ILike search used by the Changes page's own
        // filter bar.
        var query = _db.ChangeRecords
            .Include(c => c.Supplier)
            .Include(c => c.MaterialSection).ThenInclude(m => m!.MaterialCategory)
            .Include(c => c.MaterialSection).ThenInclude(m => m!.Technologies).ThenInclude(t => t.Technology)
            .Include(c => c.PreviousEntry)
            .Include(c => c.CurrentEntry)
            .Where(c => c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId);

        if (!string.IsNullOrWhiteSpace(supplier))
            query = query.Where(c => c.Supplier != null && EF.Functions.ILike(c.Supplier.CanonicalName, $"%{supplier}%"));

        if (supplierId is not null)
            query = query.Where(c => c.SupplierId == supplierId);

        if (!string.IsNullOrWhiteSpace(materialCategory))
            query = query.Where(c => c.MaterialSection != null && c.MaterialSection.MaterialCategory.Name == materialCategory);

        if (!string.IsNullOrWhiteSpace(materialSectionCode))
            // Material section codes (e.g. "52.2") are stable across
            // releases - the same identifier ReleasesController.GetEntriesSummary
            // and Material Explorer's own filter use - so this matches
            // regardless of which release each side of the change came from.
            query = query.Where(c => c.MaterialSection != null && c.MaterialSection.Code == materialSectionCode);

        if (!string.IsNullOrWhiteSpace(severity) && Enum.TryParse<Severity>(severity, true, out var sev))
            query = query.Where(c => c.Severity == sev);

        if (!string.IsNullOrWhiteSpace(changeType) && Enum.TryParse<ChangeType>(changeType, true, out var parsedChangeType))
            query = query.Where(c => c.ChangeType == parsedChangeType);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c =>
                EF.Functions.ILike(c.Explanation, $"%{search}%") ||
                (c.Supplier != null && EF.Functions.ILike(c.Supplier.CanonicalName, $"%{search}%")) ||
                (c.MaterialSection != null && EF.Functions.ILike(c.MaterialSection.RawDescription, $"%{search}%")));

        if (!string.IsNullOrWhiteSpace(technology))
        {
            var sectionIds = await _db.MaterialSectionTechnologies
                .Include(mst => mst.Technology)
                .Where(mst => mst.Technology.Code == technology)
                .Select(mst => mst.MaterialSectionId)
                .ToListAsync(ct);
            query = query.Where(c => c.MaterialSectionId != null && sectionIds.Contains(c.MaterialSectionId!.Value));
        }

        var changes = await query
            .OrderByDescending(c => c.Severity == Severity.Critical)
            .ThenByDescending(c => c.Severity == Severity.High)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

        var report = new ExportReport
        {
            Title = "LOTAM Sentinel — Change Report",
            Subtitle = $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC · {changes.Count} record(s) · " +
                       "Removed rows reflect the previous release's data (no current version exists); " +
                       "all other rows reflect the current release.",
            Columns = new List<ReportColumn>
            {
                new("Manufacturer Name, Plant, Location/Country, Address, Website, Telephone & E-mail", 3f),
                new("Authorized Local Representative/Vendor", 1.6f),
                new("Type/Model and Rating", 1.6f),
                new("Type Tests Details", 1.6f),
                new("Remarks/Observations", 2.6f),
                new("Change Type", 1f),
                new("Technology", 1.3f),
            },
        };

        foreach (var c in changes)
        {
            // Current-release entry where one exists (covers New/Updated);
            // otherwise the previous-release entry (Removed records have
            // no current version to show at all).
            var entry = c.CurrentEntry ?? c.PreviousEntry;
            if (entry is null) continue;

            var techNames = c.MaterialSection?.Technologies.Select(t => t.Technology.Name).Distinct().ToList() ?? new List<string>();

            report.Rows.Add(new ReportRow
            {
                Tags = new List<ReportTag>
                {
                    new(c.Severity.ToString(), SeverityColorTag(c.Severity)),
                    new(c.ChangeType.ToString(), "neutral"),
                },
                Cells = new List<ReportCell>
                {
                    new(entry.RawManufacturerBlock),
                    new(entry.LocalRepresentativeRaw ?? ""),
                    new(entry.ModelRatingRaw ?? ""),
                    new(entry.TypeTestsRaw ?? ""),
                    new(entry.RemarksRaw ?? ""),
                    new(c.ChangeType.ToString()),
                    new(techNames.Count > 0 ? string.Join(", ", techNames) : ""),
                },
                // Same grouping metadata purpose as MaterialReportBuilder -
                // see there. Falls back to "—"/"Ungrouped" for a change with
                // no MaterialSection link (rare, but MaterialSectionId is
                // nullable on ChangeRecord).
                GroupCode = c.MaterialSection?.Code ?? "—",
                GroupLabel = c.MaterialSection is not null
                    ? $"{c.MaterialSection.MaterialCategory.Name} — {c.MaterialSection.RawDescription}"
                    : "Ungrouped",
                GroupStandard = c.MaterialSection?.ApplicableStandard,
                SupplierName = c.Supplier?.CanonicalName ?? "Unknown supplier",
                Country = entry.ManufacturerCountryRaw,
                Explanation = string.IsNullOrWhiteSpace(c.Explanation) ? null : c.Explanation,
            });
        }

        // Computed from the group labels the rows actually ended up with
        // (set just above), so the "Report Summary" section names the real
        // material group(s) this export covers - see ReportSummaryText.
        report.ReportSummary = ReportSummaryText.Build(report.Rows.Select(r => r.GroupLabel));

        ApplyExecutiveSummary(report, changes);

        return report;
    }

    /// <summary>Same idea as MaterialReportBuilder's version - headline
    /// numbers + a narrative, computed straight from the already-filtered
    /// ChangeRecord list rather than re-derived from row tags.</summary>
    private static void ApplyExecutiveSummary(ExportReport report, List<ChangeRecord> changes)
    {
        if (changes.Count == 0) return;

        var distinctSuppliers = changes
            .Select(c => c.Supplier?.CanonicalName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var groupCount = changes.Select(c => c.MaterialSection?.Code).Where(c => c is not null).Distinct().Count();

        var newCount = changes.Count(c => c.ChangeType == ChangeType.NewRecord);
        var removedCount = changes.Count(c => c.ChangeType == ChangeType.RemovedRecord);
        var statusChangedCount = changes.Count(c => c.ChangeType == ChangeType.StatusChanged);
        var criticalCount = changes.Count(c => c.Severity == Severity.Critical);
        var highCount = changes.Count(c => c.Severity == Severity.High);

        var narrative = $"This report covers {changes.Count} change{(changes.Count == 1 ? "" : "s")} " +
            $"across {distinctSuppliers} supplier{(distinctSuppliers == 1 ? "" : "s")} and " +
            $"{groupCount} material group{(groupCount == 1 ? "" : "s")}. Of these, {newCount} " +
            $"{(newCount == 1 ? "is a new supplier qualification" : "are new supplier qualifications")}, " +
            $"{removedCount} {(removedCount == 1 ? "was" : "were")} removed, and {statusChangedCount} " +
            $"reflect{(statusChangedCount == 1 ? "s" : "")} a status change.";
        if (criticalCount > 0 || highCount > 0)
            narrative += $" {criticalCount} {(criticalCount == 1 ? "is" : "are")} flagged Critical and " +
                $"{highCount} High severity — these should be reviewed first.";

        report.ExecutiveSummary = narrative;
        report.SummaryStats = new List<SummaryStat>
        {
            new("Total Changes", changes.Count.ToString()),
            new("Suppliers", distinctSuppliers.ToString()),
            new("Material Groups", groupCount.ToString()),
            new("New", newCount.ToString(), "emerald"),
            new("Removed", removedCount.ToString(), "rose"),
        };
        if (criticalCount > 0) report.SummaryStats.Add(new("Critical", criticalCount.ToString(), "rose"));
        if (highCount > 0) report.SummaryStats.Add(new("High", highCount.ToString(), "amber"));
    }

    private static string SeverityColorTag(Severity severity) => severity switch
    {
        Severity.Critical => "rose",
        Severity.High => "amber",
        Severity.Medium => "amber",
        _ => "neutral",
    };
}
