namespace LotamSentinel.Domain.Entities;

/// <summary>Configurable (change_type, condition) -> severity policy. Data,
/// not code, so severity rules can change without a redeploy.</summary>
public class SeverityRule
{
    public long Id { get; set; }
    public ChangeType ChangeType { get; set; }
    /// <summary>Optional structured condition, e.g. {"status_to":"SUSPENDED"}.
    /// Stored as JSON text; mapped to jsonb in Postgres.</summary>
    public string? ConditionJson { get; set; }
    public Severity Severity { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
}

/// <summary>One detected, business-meaningful difference between an explicit
/// (previous_release, current_release) pair. Produced entirely by the
/// deterministic comparator; AI may only add AiGeneratedSummary afterwards.</summary>
public class ChangeRecord
{
    public long Id { get; set; }

    public long PreviousReleaseId { get; set; }
    public LotamRelease PreviousRelease { get; set; } = null!;
    public long CurrentReleaseId { get; set; }
    public LotamRelease CurrentRelease { get; set; } = null!;

    public long? PreviousEntryId { get; set; }
    public LotamEntry? PreviousEntry { get; set; }
    public long? CurrentEntryId { get; set; }
    public LotamEntry? CurrentEntry { get; set; }

    public long? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public long? MaterialSectionId { get; set; }
    public MaterialSection? MaterialSection { get; set; }

    public ChangeType ChangeType { get; set; }
    public string? PreviousValue { get; set; }
    public string? CurrentValue { get; set; }

    public Severity Severity { get; set; }
    public DerivationSource SeveritySource { get; set; }

    /// <summary>Deterministic, rule-generated explanation — always present.</summary>
    public string Explanation { get; set; } = null!;
    /// <summary>Optional AI-generated human-readable summary, kept distinct
    /// from Explanation so the deterministic reasoning is never overwritten.</summary>
    public string? AiGeneratedSummary { get; set; }

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.AutoMatched;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<SourceEvidence> Evidence { get; set; } = new List<SourceEvidence>();
}

/// <summary>Explicit evidence pointer(s) back into the source PDF(s) for a
/// ChangeRecord — answers "why did the system say this changed?".</summary>
public class SourceEvidence
{
    public long Id { get; set; }
    public long ChangeRecordId { get; set; }
    public ChangeRecord ChangeRecord { get; set; } = null!;
    public long ReleaseId { get; set; }
    public LotamRelease Release { get; set; } = null!;
    public EvidenceType EvidenceType { get; set; }
    public int? PageNumber { get; set; }
    public long? LotamEntryId { get; set; }
    public LotamEntry? LotamEntry { get; set; }
    public string? RawExcerpt { get; set; }
}

/// <summary>Audit log for every AI call (alias suggestion, ambiguous
/// classification, remarks interpretation, summary generation), so any
/// AI-derived value in the UI can be traced to exactly what was asked and
/// returned.</summary>
public class AiEnrichmentLog
{
    public long Id { get; set; }
    public AiSubjectType SubjectType { get; set; }
    public long SubjectId { get; set; }
    public string PromptVersion { get; set; } = null!;
    public string ModelName { get; set; } = null!;
    public string RawResponseJson { get; set; } = null!;
    public decimal? Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
