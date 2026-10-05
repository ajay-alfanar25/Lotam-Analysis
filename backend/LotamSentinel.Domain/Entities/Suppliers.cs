namespace LotamSentinel.Domain.Entities;

/// <summary>Canonical, reviewer-controlled manufacturer identity.</summary>
public class Supplier
{
    public long Id { get; set; }
    public string CanonicalName { get; set; } = null!;
    public string? CanonicalCountry { get; set; }
    public long? ParentSupplierId { get; set; }
    public Supplier? ParentSupplier { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SupplierAlias> Aliases { get; set; } = new List<SupplierAlias>();
}

/// <summary>A raw manufacturer name/country pair exactly as printed in a
/// specific release. Never overwritten. May exist unmatched (SupplierId ==
/// null) pending human review — the system never guesses just to fill the
/// field.</summary>
public class SupplierAlias
{
    public long Id { get; set; }
    public long? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string RawName { get; set; } = null!;
    public string? RawCountry { get; set; }

    public long FirstSeenReleaseId { get; set; }
    public LotamRelease FirstSeenRelease { get; set; } = null!;

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.NeedsReview;
    public DerivationSource? MatchSource { get; set; }
    public decimal? Confidence { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<LotamEntry> Entries { get; set; } = new List<LotamEntry>();
}
