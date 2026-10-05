using LotamSentinel.Application.Common;
using LotamSentinel.Domain.Entities;
using LotamSentinel.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LotamSentinel.Application.Normalization;

/// <summary>
/// Resolves a raw manufacturer name into a SupplierAlias row. Per the
/// project's non-negotiable rule: this NEVER silently merges two different
/// raw names into one Supplier unless there is an exact normalized-name match
/// to an alias already confirmed. Anything less certain creates a new,
/// unlinked alias flagged NeedsReview - a human (or a later AI-assist step
/// whose output still lands in NeedsReview) resolves it explicitly.
/// </summary>
public class SupplierNormalizationService
{
    private readonly LotamSentinelDbContext _db;

    public SupplierNormalizationService(LotamSentinelDbContext db) => _db = db;

    public async Task<SupplierAlias> ResolveAliasAsync(
        string rawName, string? rawCountry, long releaseId, CancellationToken ct = default)
    {
        var matchKey = TextNormalizer.NormalizeForSupplierMatch(rawName);

        // 1) Exact raw-name match to an alias already seen (any release) - same
        //    printed name, just reuse the existing alias/supplier link as-is.
        var existingAliases = await _db.SupplierAliases
            .Where(a => a.RawName == rawName)
            .ToListAsync(ct);
        var exact = existingAliases.FirstOrDefault();
        if (exact is not null) return exact;

        // 2) Fuzzy signal only: an existing CONFIRMED supplier whose canonical
        //    name reduces to the same match-key. This does NOT auto-link -
        //    it only pre-fills SupplierId with the match while still marking
        //    the alias NeedsReview, so a reviewer can confirm or reject it.
        var candidateSuppliers = await _db.Suppliers.ToListAsync(ct);
        var candidate = candidateSuppliers
            .FirstOrDefault(s => TextNormalizer.NormalizeForSupplierMatch(s.CanonicalName) == matchKey);

        var alias = new SupplierAlias
        {
            RawName = rawName,
            RawCountry = rawCountry,
            FirstSeenReleaseId = releaseId,
            SupplierId = candidate?.Id,
            ReviewStatus = candidate is not null ? ReviewStatus.AutoMatched : ReviewStatus.NeedsReview,
            MatchSource = candidate is not null ? DerivationSource.Rule : null,
            Confidence = candidate is not null ? 0.75m : null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // If nothing matched at all, also create a brand-new Supplier so the
        // alias isn't left permanently orphaned for common single-occurrence
        // manufacturers - still reviewable/mergeable later, never auto-merged
        // with anything else.
        if (candidate is null)
        {
            var newSupplier = new Supplier
            {
                CanonicalName = rawName.Trim(),
                CanonicalCountry = rawCountry,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Suppliers.Add(newSupplier);
            await _db.SaveChangesAsync(ct);
            alias.SupplierId = newSupplier.Id;
            alias.ReviewStatus = ReviewStatus.NeedsReview; // still flagged - identity not confirmed
        }

        _db.SupplierAliases.Add(alias);
        await _db.SaveChangesAsync(ct);
        return alias;
    }
}
