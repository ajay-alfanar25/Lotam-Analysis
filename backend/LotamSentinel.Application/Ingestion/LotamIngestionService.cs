using System.Security.Cryptography;
using LotamSentinel.Application.Classification;
using LotamSentinel.Application.Common;
using LotamSentinel.Application.Normalization;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Application.Ingestion;

public class LotamIngestionService
{
    private readonly LotamSentinelDbContext _db;
    private readonly PdfLotamParser _parser;
    private readonly SupplierNormalizationService _supplierNormalization;
    private readonly StatusClassifier _statusClassifier;
    private readonly TechnologyClassifier _technologyClassifier;

    public LotamIngestionService(
        LotamSentinelDbContext db,
        PdfLotamParser parser,
        SupplierNormalizationService supplierNormalization,
        StatusClassifier statusClassifier,
        TechnologyClassifier technologyClassifier)
    {
        _db = db;
        _parser = parser;
        _supplierNormalization = supplierNormalization;
        _statusClassifier = statusClassifier;
        _technologyClassifier = technologyClassifier;
    }

    public async Task<LotamRelease> IngestAsync(string filePath, CancellationToken ct = default)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        var hash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

        var existing = await _db.LotamReleases.FirstOrDefaultAsync(r => r.FileSha256 == hash, ct);
        if (existing is not null) return existing; // duplicate-import guard (FR: file hash traceability)

        var release = new LotamRelease
        {
            SourceFilename = Path.GetFileName(filePath),
            FileSha256 = hash,
            ProcessingStatus = ProcessingStatus.Processing,
            ImportedAt = DateTimeOffset.UtcNow
        };
        _db.LotamReleases.Add(release);
        await _db.SaveChangesAsync(ct);

        try
        {
            // Loaded once per ingestion run rather than per-entry/per-section -
            // both classifiers are pure functions over this data (see
            // StatusClassifier / TechnologyClassifier), so there's no reason
            // to re-query the config tables thousands of times.
            var statusRules = await _db.StatusKeywordRules.Where(r => r.IsActive).ToListAsync(ct);
            var technologyRules = await _db.TechnologyMappingRules.Where(r => r.IsActive).ToListAsync(ct);

            var parsed = _parser.Parse(filePath);
            release.ReleaseDate = parsed.ReleaseDate ?? throw new InvalidOperationException(
                "Could not extract release date from document header. Ingestion halted rather than " +
                "guessing - release date is the canonical version identity per project decision.");
            release.PageCount = parsed.PageCount;

            foreach (var parsedSection in parsed.Sections)
            {
                var category = await GetOrCreateCategoryAsync(parsedSection.CategoryName, ct);
                var normalizedDescription = TextNormalizer.Normalize(parsedSection.RawDescription);

                var section = new MaterialSection
                {
                    ReleaseId = release.Id,
                    MaterialCategoryId = category.Id,
                    Code = parsedSection.Code,
                    RawDescription = parsedSection.RawDescription,
                    NormalizedDescription = normalizedDescription,
                    ApplicableStandard = parsedSection.ApplicableStandard,
                    PrintedTotal = parsedSection.PrintedTotal,
                    ExtractedEntryCount = parsedSection.Entries.Count,
                    FirstPageNumber = parsedSection.FirstPageNumber,
                    LastPageNumber = parsedSection.LastPageNumber,
                    ValidationStatus = parsedSection.PrintedTotal == parsedSection.Entries.Count
                        ? SectionValidationStatus.Matched
                        : SectionValidationStatus.Mismatch
                };
                _db.MaterialSections.Add(section);
                await _db.SaveChangesAsync(ct); // need section.Id for entries/technology rows

                // ---- Technology classification (previously built, never called) ----
                var techMatches = _technologyClassifier.Classify(section.Code, normalizedDescription, technologyRules);
                section.TechnologyClassificationStatus = techMatches.Count > 0
                    ? TechnologyClassificationStatus.Matched
                    : TechnologyClassificationStatus.NeedsReview;

                for (int i = 0; i < techMatches.Count; i++)
                {
                    var match = techMatches[i];
                    _db.MaterialSectionTechnologies.Add(new MaterialSectionTechnology
                    {
                        MaterialSectionId = section.Id,
                        TechnologyId = match.TechnologyId,
                        Source = match.Source,
                        Confidence = match.Confidence,
                        IsPrimary = i == 0 // first matching rule (by priority, already ordered by the classifier)
                    });
                }

                foreach (var parsedEntry in parsedSection.Entries)
                {
                    var alias = await _supplierNormalization.ResolveAliasAsync(
                        parsedEntry.GuessedManufacturerName, parsedEntry.ManufacturerCountryRaw, release.Id, ct);

                    // ---- Status classification (previously built, never called) ----
                    var statusResult = _statusClassifier.Classify(parsedEntry.RemarksRaw, statusRules);

                    _db.LotamEntries.Add(new LotamEntry
                    {
                        MaterialSectionId = section.Id,
                        SupplierAliasId = alias.Id,
                        OrdinalInSection = parsedEntry.OrdinalInSection,
                        PageNumber = parsedEntry.PageNumber,
                        RawManufacturerBlock = parsedEntry.RawManufacturerBlock,
                        ManufacturerCountryRaw = parsedEntry.ManufacturerCountryRaw,
                        ManufacturerCountryNormalized = parsedEntry.ManufacturerCountryRaw is null
                            ? null : TextNormalizer.Normalize(parsedEntry.ManufacturerCountryRaw),
                        LocalRepresentativeRaw = parsedEntry.LocalRepresentativeRaw,
                        ModelRatingRaw = parsedEntry.ModelRatingRaw,
                        ModelRatingNormalized = parsedEntry.ModelRatingRaw is null
                            ? null : TextNormalizer.Normalize(parsedEntry.ModelRatingRaw),
                        TypeTestsRaw = parsedEntry.TypeTestsRaw,
                        RemarksRaw = parsedEntry.RemarksRaw,
                        DerivedStatus = statusResult.Status,
                        StatusConfidence = statusResult.Confidence,
                        StatusSource = statusResult.Source,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
                await _db.SaveChangesAsync(ct);
            }

            release.ProcessingStatus = ProcessingStatus.Completed;
            release.ProcessingCompletedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            release.ProcessingStatus = ProcessingStatus.Failed;
            release.ProcessingError = ex.Message;
        }

        await _db.SaveChangesAsync(ct);
        return release;
    }

    private async Task<MaterialCategory> GetOrCreateCategoryAsync(string name, CancellationToken ct)
    {
        var normalized = TextNormalizer.Normalize(name).ToUpperInvariant();
        var existing = await _db.MaterialCategories
            .FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct);
        if (existing is not null) return existing;

        var category = new MaterialCategory { Name = name, NormalizedName = normalized };
        _db.MaterialCategories.Add(category);
        await _db.SaveChangesAsync(ct);
        return category;
    }
}
