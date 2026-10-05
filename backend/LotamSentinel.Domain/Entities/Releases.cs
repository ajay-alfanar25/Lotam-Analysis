namespace LotamSentinel.Domain.Entities;

/// <summary>One ingested LOTAM PDF. Identity is release_date (from the document
/// header) + surrogate id — never the source filename, which was found to be
/// unreliable (off-by-one-day vs. printed date in the sampled files).</summary>
public class LotamRelease
{
    public long Id { get; set; }
    public DateOnly ReleaseDate { get; set; }
    public string SourceFilename { get; set; } = null!;
    public string FileSha256 { get; set; } = null!;
    public int? PageCount { get; set; }
    public int? PrintedGrandTotalEntries { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
    public string? ProcessingError { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset? ProcessingCompletedAt { get; set; }
    public string? Notes { get; set; }

    public ICollection<MaterialSection> MaterialSections { get; set; } = new List<MaterialSection>();
}

/// <summary>Top-level printed grouping, e.g. "CONDUCTORS, BARE", "SWITCHGEAR".
/// Stable across releases in the sampled data.</summary>
public class MaterialCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;

    public ICollection<MaterialSection> MaterialSections { get; set; } = new List<MaterialSection>();
}

/// <summary>One material code (e.g. "52.2") scoped to exactly one release.
/// Never assumed identical to the "same" code in another release unless a
/// MaterialCodeRemap explicitly says so.</summary>
public class MaterialSection
{
    public long Id { get; set; }
    public long ReleaseId { get; set; }
    public LotamRelease Release { get; set; } = null!;
    public long MaterialCategoryId { get; set; }
    public MaterialCategory MaterialCategory { get; set; } = null!;

    public string Code { get; set; } = null!;
    public string RawDescription { get; set; } = null!;
    public string NormalizedDescription { get; set; } = null!;
    public string? ApplicableStandard { get; set; }

    /// <summary>SEC's own "Total = N" printed at the end of the section — used
    /// to validate extraction completeness.</summary>
    public int PrintedTotal { get; set; }
    public int ExtractedEntryCount { get; set; }
    public SectionValidationStatus ValidationStatus { get; set; } = SectionValidationStatus.Pending;

    /// <summary>Whether at least one technology_mapping_rule matched this
    /// section. NeedsReview (the default) means nobody has told the system
    /// what technology this material belongs to yet - surfaced in the UI
    /// rather than silently left unclassified.</summary>
    public TechnologyClassificationStatus TechnologyClassificationStatus { get; set; }
        = TechnologyClassificationStatus.NeedsReview;

    public int? FirstPageNumber { get; set; }
    public int? LastPageNumber { get; set; }

    public ICollection<LotamEntry> Entries { get; set; } = new List<LotamEntry>();
    public ICollection<MaterialSectionTechnology> Technologies { get; set; } = new List<MaterialSectionTechnology>();
}

/// <summary>Manual-only linkage confirming that a material code in an older
/// release was renumbered into a different code in a newer release. Absence
/// of a row here means the comparator treats the pair as REMOVED + NEW.</summary>
public class MaterialCodeRemap
{
    public long Id { get; set; }
    public long FromMaterialSectionId { get; set; }
    public MaterialSection FromMaterialSection { get; set; } = null!;
    public long ToMaterialSectionId { get; set; }
    public MaterialSection ToMaterialSection { get; set; } = null!;
    public string Rationale { get; set; } = null!;
    public string ConfirmedBy { get; set; } = null!;
    public DateTimeOffset ConfirmedAt { get; set; }
}
