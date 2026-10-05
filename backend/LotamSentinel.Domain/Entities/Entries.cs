namespace LotamSentinel.Domain.Entities;

/// <summary>Core fact table: one manufacturer-qualification record within one
/// material section of one release. Raw text is always preserved separately
/// from anything derived from it (status, normalized model/rating, etc.).</summary>
public class LotamEntry
{
    public long Id { get; set; }

    public long MaterialSectionId { get; set; }
    public MaterialSection MaterialSection { get; set; } = null!;

    public long SupplierAliasId { get; set; }
    public SupplierAlias SupplierAlias { get; set; } = null!;

    /// <summary>Position within the section's "Total = N" list — an evidence
    /// pointer, not a business key.</summary>
    public int OrdinalInSection { get; set; }
    public int? PageNumber { get; set; }

    /// <summary>Full, unparsed manufacturer/plant/address/contact text block,
    /// exactly as printed. Segmentation heuristics evolve; this doesn't.</summary>
    public string RawManufacturerBlock { get; set; } = null!;
    public string? ManufacturerCountryRaw { get; set; }
    public string? ManufacturerCountryNormalized { get; set; }

    /// <summary>Optional. NULL is valid data (e.g. Saudi-domiciled
    /// manufacturers with no local agent) — never inferred or defaulted.</summary>
    public string? LocalRepresentativeRaw { get; set; }

    public string? ModelRatingRaw { get; set; }
    public string? ModelRatingNormalized { get; set; }

    public string? TypeTestsRaw { get; set; }

    public string? RemarksRaw { get; set; }

    /// <summary>Not a printed field — derived from RemarksRaw via
    /// StatusKeywordRule (or AI fallback), always tagged with its source.</summary>
    public ApprovalStatus DerivedStatus { get; set; } = ApprovalStatus.Unknown;
    public decimal? StatusConfidence { get; set; }
    public DerivationSource? StatusSource { get; set; }

    public decimal? ExtractionConfidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Deterministic pattern -> ApprovalStatus mapping, matched against
/// RemarksRaw. Seeded from the vocabulary actually observed in source PDFs
/// (SUSPENDED, DISQUALIFIED, "conditional approval", "PQ Letter"/"pre-qualified").
/// BLACKLISTED exists as a target with no rule yet — none observed so far.</summary>
public class StatusKeywordRule
{
    public long Id { get; set; }
    public string Pattern { get; set; } = null!;
    public ApprovalStatus StatusValue { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
