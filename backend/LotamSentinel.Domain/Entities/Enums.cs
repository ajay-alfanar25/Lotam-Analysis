namespace LotamSentinel.Domain.Entities;

public enum ProcessingStatus { Pending, Processing, Completed, Failed }

public enum SectionValidationStatus { Pending, Matched, Mismatch }

public enum TechnologyClassificationStatus { NeedsReview, Matched }

public enum ReviewStatus { AutoMatched, Confirmed, NeedsReview, Rejected }

public enum DerivationSource { Rule, Ai, Manual }

public enum ApprovalStatus
{
    Qualified,
    Conditional,
    Suspended,
    Disqualified,
    Blacklisted,
    Reactivated,
    Unknown
}

public enum ChangeType
{
    NewRecord,
    RemovedRecord,
    StatusChanged,
    ModelAdded,
    ModelRemoved,
    RatingChanged,
    VoltageChanged,
    SpecificationChanged,
    ConditionChanged,
    RemarkChanged,
    AgentChanged,
    CountryChanged,
    ScopeChanged,
    OtherMaterialChange
}

public enum Severity { Critical, High, Medium, Low }

public enum EvidenceType { Previous, Current }

public enum MappingMatchType { MaterialCode, MaterialCodePrefix, Keyword }

public enum AiSubjectType { SupplierAlias, MaterialSectionTechnology, ChangeRecord, LotamEntryStatus }

/// <summary>What a UserNotificationSubscription row points at.
/// MaterialCategory = a whole material group (TargetCode is the category
/// name, e.g. "Cables"); MaterialSection = one specific material
/// group/subgroup within a category (TargetCode is the section code, e.g.
/// "52.2" - stable across releases, same identifier Material Explorer's own
/// filters use).</summary>
public enum NotificationSubscriptionType { MaterialCategory, Technology, Supplier, MaterialSection }
