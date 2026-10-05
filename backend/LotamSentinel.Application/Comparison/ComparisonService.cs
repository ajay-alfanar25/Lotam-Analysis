using LotamSentinel.Application.Common;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Application.Comparison;

public class ComparisonService
{
    private readonly LotamSentinelDbContext _db;
    private readonly ComparisonEngine _engine;
    private readonly SeverityCalculator _severity;

    public ComparisonService(LotamSentinelDbContext db, ComparisonEngine engine, SeverityCalculator severity)
    {
        _db = db;
        _engine = engine;
        _severity = severity;
    }

    public async Task<int> RunComparisonAsync(long previousReleaseId, long currentReleaseId, CancellationToken ct = default)
    {
        // Wipe any prior run for this exact release pair so re-running is idempotent.
        var existing = _db.ChangeRecords.Where(c =>
            c.PreviousReleaseId == previousReleaseId && c.CurrentReleaseId == currentReleaseId);
        _db.ChangeRecords.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);

        var previousSnapshots = await LoadSnapshotsAsync(previousReleaseId, ct);
        var currentSnapshots = await LoadSnapshotsAsync(currentReleaseId, ct);

        var remaps = await _db.MaterialCodeRemaps
            .Include(r => r.FromMaterialSection)
            .Include(r => r.ToMaterialSection)
            .Where(r => r.FromMaterialSection.ReleaseId == previousReleaseId
                     && r.ToMaterialSection.ReleaseId == currentReleaseId)
            .Select(r => new CodeRemap(r.FromMaterialSection.Code, r.ToMaterialSection.Code))
            .ToListAsync(ct);

        var candidates = _engine.Compare(previousSnapshots, currentSnapshots, remaps);
        var severityRules = await _db.SeverityRules.ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var (severity, source) = _severity.Resolve(candidate, severityRules);

            var record = new ChangeRecord
            {
                PreviousReleaseId = previousReleaseId,
                CurrentReleaseId = currentReleaseId,
                PreviousEntryId = candidate.PreviousEntryId,
                CurrentEntryId = candidate.CurrentEntryId,
                SupplierId = candidate.SupplierId,
                MaterialSectionId = candidate.MaterialSectionId,
                ChangeType = candidate.ChangeType,
                PreviousValue = candidate.PreviousValue,
                CurrentValue = candidate.CurrentValue,
                Severity = severity,
                SeveritySource = source,
                Explanation = candidate.Explanation,
                ReviewStatus = ReviewStatus.AutoMatched,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ChangeRecords.Add(record);
            await _db.SaveChangesAsync(ct); // need record.Id for evidence rows

            if (candidate.PreviousEntryId is { } prevId)
            {
                _db.SourceEvidences.Add(new SourceEvidence
                {
                    ChangeRecordId = record.Id,
                    ReleaseId = previousReleaseId,
                    EvidenceType = EvidenceType.Previous,
                    LotamEntryId = prevId
                });
            }
            if (candidate.CurrentEntryId is { } currId)
            {
                _db.SourceEvidences.Add(new SourceEvidence
                {
                    ChangeRecordId = record.Id,
                    ReleaseId = currentReleaseId,
                    EvidenceType = EvidenceType.Current,
                    LotamEntryId = currId
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return candidates.Count;
    }

    private async Task<List<EntrySnapshot>> LoadSnapshotsAsync(long releaseId, CancellationToken ct)
    {
        var rows = await _db.LotamEntries
            .Include(e => e.MaterialSection)
            .Include(e => e.SupplierAlias).ThenInclude(a => a.Supplier)
            .Where(e => e.MaterialSection.ReleaseId == releaseId)
            .ToListAsync(ct);

        return rows.Select(e => new EntrySnapshot(
            EntryId: e.Id,
            SupplierId: e.SupplierAlias.SupplierId ?? -e.SupplierAlias.Id, // unmatched aliases still need a stable identity for pairing
            SupplierCanonicalName: e.SupplierAlias.Supplier?.CanonicalName ?? e.SupplierAlias.RawName,
            MaterialCode: e.MaterialSection.Code,
            MaterialSectionId: e.MaterialSectionId,
            Status: e.DerivedStatus,
            ModelRatingNormalized: e.ModelRatingNormalized,
            ModelRatingRaw: e.ModelRatingRaw,
            RemarksNormalized: e.RemarksRaw is null ? null : TextNormalizer.Normalize(e.RemarksRaw),
            RemarksRaw: e.RemarksRaw,
            LocalRepresentativeRaw: e.LocalRepresentativeRaw,
            CountryNormalized: e.ManufacturerCountryNormalized,
            CountryRaw: e.ManufacturerCountryRaw,
            PageNumber: e.PageNumber ?? 0
        )).ToList();
    }
}
