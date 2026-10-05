using LotamSentinel.Domain.Entities;

namespace LotamSentinel.Application.Comparison;

/// <summary>Flat, DB-independent view of one lotam_entry, built by the caller
/// from EF-loaded data so the comparison engine itself has zero DB
/// dependency and is fully unit-testable.</summary>
public record EntrySnapshot(
    long EntryId,
    long SupplierId,
    string SupplierCanonicalName,
    string MaterialCode,
    long MaterialSectionId,
    ApprovalStatus Status,
    string? ModelRatingNormalized,
    string? ModelRatingRaw,
    string? RemarksNormalized,
    string? RemarksRaw,
    string? LocalRepresentativeRaw,
    string? CountryNormalized,
    string? CountryRaw,
    int PageNumber
);

/// <summary>One confirmed manual renumbering link: an old material code
/// compares against a different new material code instead of REMOVED+NEW.</summary>
public record CodeRemap(string FromCode, string ToCode);

public record ChangeCandidate(
    ChangeType ChangeType,
    long? SupplierId,
    string? MaterialCode,
    long? MaterialSectionId,
    long? PreviousEntryId,
    long? CurrentEntryId,
    string? PreviousValue,
    string? CurrentValue,
    string Explanation
);
