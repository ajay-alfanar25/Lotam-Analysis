namespace LotamSentinel.Api.Dtos;

public record ReleaseDto(
    long Id, DateOnly ReleaseDate, string SourceFilename, int? PageCount,
    string ProcessingStatus, DateTimeOffset ImportedAt);

public record ImportResultDto(long ReleaseId, string ProcessingStatus, string? Error);

public record RunComparisonRequest(long PreviousReleaseId, long CurrentReleaseId);
public record RunComparisonResultDto(int ChangeCount);

public record DashboardSummaryDto(
    ReleaseDto? PreviousRelease,
    ReleaseDto? CurrentRelease,
    int TotalRecordsCurrent,
    int TotalChanges,
    int Critical,
    int High,
    int Medium,
    int Low,
    List<TechnologyChangeCountDto> ChangesByTechnology,
    List<ChangeTypeCountDto> ChangesByType,
    List<ChangeRecordDto> RecentImportantChanges);

public record TechnologyChangeCountDto(string TechnologyCode, string TechnologyName, int Count);
public record ChangeTypeCountDto(string ChangeType, int Count);

public record ChangeRecordDto(
    long Id,
    string SupplierName,
    string? MaterialCode,
    string? MaterialDescription,
    string ChangeType,
    string? PreviousValue,
    string? CurrentValue,
    string Severity,
    string Explanation,
    DateOnly PreviousReleaseDate,
    DateOnly CurrentReleaseDate,
    List<string> TechnologyCodes);

public record ChangeRecordDetailDto(
    ChangeRecordDto Change,
    string? PreviousPageEvidence,
    string? CurrentPageEvidence,
    string? PreviousRawExcerpt,
    string? CurrentRawExcerpt);

public record TechnologyDto(long Id, string Code, string Name, string? Description);

public record SupplierDto(long Id, string CanonicalName, string? CanonicalCountry, int AliasCount);

public record SupplierHistoryDto(
    long SupplierId,
    string CanonicalName,
    List<SupplierMaterialStatusDto> MaterialStatuses);

public record SupplierMaterialStatusDto(
    long LotamEntryId, string MaterialCode, string CategoryName, string MaterialDescription,
    string? ApplicableStandard, DateOnly ReleaseDate, string Status);

public record LotamEntryDetailDto(
    long Id,
    string SupplierCanonicalName,
    string MaterialCode,
    string CategoryName,
    string MaterialDescription,
    string? ApplicableStandard,
    DateOnly ReleaseDate,
    string SourceFilename,
    int? PageNumber,
    string Status,
    decimal? StatusConfidence,
    string? LocalRepresentativeRaw,
    string? ModelRatingRaw,
    string? TypeTestsRaw,
    string? RemarksRaw,
    string? ManufacturerCountryRaw,
    string RawManufacturerBlock);

// ---- Alias review (Data Quality) ----

public record SupplierAliasReviewDto(
    long AliasId,
    string RawName,
    string? RawCountry,
    long? SuggestedSupplierId,
    string? SuggestedSupplierName,
    decimal? Confidence,
    DateOnly FirstSeenReleaseDate,
    int EntryCount);

public record SupplierOptionDto(long Id, string CanonicalName, string? CanonicalCountry);

public record ConfirmAliasRequest(long SupplierId, string ReviewedBy);
public record RejectAliasRequest(string ReviewedBy);
public record CreateSupplierForAliasRequest(string CanonicalName, string? CanonicalCountry, string ReviewedBy);

// ---- Material code remap ----

public record UnmatchedMaterialSectionDto(
    long MaterialSectionId, string Code, string Description, int EntryCount);

public record UnmatchedSectionsDto(
    List<UnmatchedMaterialSectionDto> RemovedFromPrevious,
    List<UnmatchedMaterialSectionDto> NewInCurrent);

public record CreateRemapRequest(
    long FromMaterialSectionId, long ToMaterialSectionId, string Rationale, string ConfirmedBy);

public record MaterialCodeRemapDto(
    long Id, string FromCode, string ToCode, string Rationale, string ConfirmedBy, DateTimeOffset ConfirmedAt);

// ---- Material Explorer (per-release document browser) ----

public record MaterialSectionOptionDto(
    long MaterialSectionId, string Code, string Label, int EntryCount,
    int PrintedTotal, string ValidationStatus);

// Lightweight per-entry row for Material Explorer's default flat supplier
// list - deliberately no raw manufacturer text here (that's fetched
// separately, lazily, per row via LotamEntryDetailDto when expanded).
// CategoryName/MaterialDescription/ApplicableStandard are the same section
// fields folded into MaterialLabel (kept as-is so Tab 1's existing display
// never changes) but broken out separately for Tab 2's Material ->
// Equipment Description -> Supplier hierarchy, which needs to group by
// CategoryName ("MATERIAL", e.g. "CONDUCTORS, BARE") independently of the
// per-section equipment description.
public record EntrySummaryDto(
    long EntryId, string MaterialCode, string MaterialLabel,
    string SupplierName, string Status, string? Badge,
    string CategoryName, string MaterialDescription, string? ApplicableStandard);

public record MaterialSectionEntryDto(
    long EntryId,
    string RawManufacturerBlock,
    string? LocalRepresentativeRaw,
    string? ModelRatingRaw,
    string? TypeTestsRaw,
    string? RemarksRaw,
    string Status,
    string? Badge); // "New", "Removed", "Updated", or null

public record MaterialSectionDetailDto(
    long MaterialSectionId,
    string Code,
    string CategoryName,
    string RawDescription,
    string? ApplicableStandard,
    int PrintedTotal,
    string ValidationStatus,
    List<MaterialSectionEntryDto> Entries);

// ---- Supply Chain Map (Feature 1) ----

// One row per (country, technology) combination for the latest release.
// SupplierCount is DISTINCT suppliers, not entry rows, so a supplier
// qualified for multiple materials under the same technology is counted
// once, not multiple times - undercounting real diversity would be a
// meaningful accuracy error here specifically.
public record GeoConcentrationDto(
    string Country, string TechnologyCode, string TechnologyName, int SupplierCount);

public record GeoConcentrationSummaryDto(
    DateOnly ReleaseDate,
    int UnmappedEntryCount, // entries with no extracted country - never silently dropped without saying so
    List<GeoConcentrationDto> Rows);

// One row per qualifying entry for a selected country (+ optional
// technology filter) - deliberately light detail, backs the "click a
// country/pin" panel, not a full record view.
public record GeoConcentrationSupplierDto(
    long EntryId, string SupplierName, string? LocalRepresentative, string Status,
    string MaterialCode, string MaterialDescription);

// ---- Tender Readiness (Feature 3) reuses MaterialSectionOptionDto /
// MaterialSectionDetailDto directly - no new DTOs, deliberately, so its
// numbers can never drift from what Material Explorer already shows for
// the exact same section.

// ---- My Suppliers watchlist (Feature 4) ----

public record WatchlistItemDto(
    long WatchlistId,
    long SupplierId,
    string CanonicalName,
    string? CanonicalCountry,
    string AddedBy,
    string? Notes,
    DateTimeOffset AddedAt);

public record AddWatchlistRequest(long SupplierId, string AddedBy, string? Notes);

// ---- Release Audit page ----

public record AuditReleaseSummaryDto(
    long ReleaseId, DateOnly ReleaseDate, string SourceFilename,
    int SectionCount, int MismatchCount, DateTimeOffset ImportedAt);

public record AuditSectionOptionDto(string Code, string Label);

public record AuditManufacturerDto(string SupplierName, string Status);

public record AuditSectionReleaseSnapshotDto(
    long ReleaseId, DateOnly ReleaseDate, string CategoryName, string RawDescription,
    int PrintedTotal, int ExtractedEntryCount, string ValidationStatus,
    List<AuditManufacturerDto> Manufacturers);

public record AuditSectionHistoryDto(string Code, List<AuditSectionReleaseSnapshotDto> Snapshots);

// ---- SEC monitoring ----

public record SecMonitorCheckResultDto(bool Configured, string? Message, int NewDocumentsFound, DateTimeOffset CheckedAt);

public record SecDocumentWatchDto(long Id, string Url, string? FileName, DateTimeOffset FirstSeenAt);

// ---- Press release search (Tender Readiness "recent mentions" box) ----
// Deliberately just title/snippet/url - never a structured "supplier
// approved" claim. See PressReleaseSearchService for why.

public record PressReleaseResultDto(string Title, string Snippet, string Url);

public record PressReleaseSearchResponseDto(bool Configured, string? Message, List<PressReleaseResultDto> Hits);

// ---- Auth ----

public record LoginRequest(string Email, string Password);
public record LoginResponseDto(string Token, UserDto User);
public record UserDto(long Id, string Email, string FullName, string? Mobile, string? Address, string? Department, bool IsAdmin);

public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Otp, string NewPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record GenericMessageDto(string Message);

// ---- Admin (Profile page "Admin" tab - visible only when UserDto.IsAdmin) ----

public record AdminUserDto(
    long Id, string Email, string FullName, string? Department,
    bool IsActive, bool IsAdmin, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public record InviteUserRequest(string Email, string FullName, string? Department, bool IsAdmin);
public record InviteUserResultDto(AdminUserDto User, bool EmailSent);
public record SetUserActiveRequest(bool IsActive);

// ---- Profile ----

public record UpdateProfileRequest(string FullName, string? Mobile, string? Address, string? Department);

public record NotificationSubscriptionDto(
    long Id, string SubscriptionType, string TargetCode, string TargetLabel, DateTimeOffset CreatedAt,
    List<SubscriptionRecipientDto> Recipients);

public record AddNotificationSubscriptionRequest(string SubscriptionType, string TargetCode, string TargetLabel);

public record SubscribableOptionsDto(
    List<string> MaterialCategories, List<TechnologyDto> Technologies, List<SubscribableMaterialDto> MaterialSections);

/// <summary>One specific material group/subgroup a user can subscribe to
/// (NotificationSubscriptionType.MaterialSection) - Code is the stable
/// material section code (e.g. "52.2"), Label is a human-readable
/// "Category — Description" for display.</summary>
public record SubscribableMaterialDto(string Code, string Label);

// ---- Notification subscription recipients ----

public record SubscriptionRecipientDto(long Id, long SubscriptionId, string Email, string? Name, DateTimeOffset CreatedAt);

public record AddSubscriptionRecipientRequest(string Email, string? Name);

/// <summary>Response for "Send digest now" - how many subscriptions were
/// queued, not how many emails were actually sent (those happen
/// asynchronously afterward - see SubscriptionDigestWorker).</summary>
public record SendDigestResultDto(int Queued);

// ---- Export (format choice + optional email delivery) ----

public record ExportSendResultDto(bool Sent, string Message);
