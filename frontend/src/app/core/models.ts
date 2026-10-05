export interface ReleaseDto {
  id: number;
  releaseDate: string; // ISO date
  sourceFilename: string;
  pageCount: number | null;
  processingStatus: string;
  importedAt: string;
}

export interface ImportResultDto {
  releaseId: number;
  processingStatus: string;
  error: string | null;
}

export interface RunComparisonResultDto {
  changeCount: number;
}

export interface TechnologyChangeCountDto {
  technologyCode: string;
  technologyName: string;
  count: number;
}

export interface ChangeTypeCountDto {
  changeType: string;
  count: number;
}

export interface ChangeRecordDto {
  id: number;
  supplierName: string;
  materialCode: string | null;
  materialDescription: string | null;
  changeType: string;
  previousValue: string | null;
  currentValue: string | null;
  severity: 'Critical' | 'High' | 'Medium' | 'Low' | string;
  explanation: string;
  previousReleaseDate: string;
  currentReleaseDate: string;
  technologyCodes: string[];
}

export interface ChangeRecordDetailDto {
  change: ChangeRecordDto;
  previousPageEvidence: string | null;
  currentPageEvidence: string | null;
  previousRawExcerpt: string | null;
  currentRawExcerpt: string | null;
}

export interface DashboardSummaryDto {
  previousRelease: ReleaseDto | null;
  currentRelease: ReleaseDto | null;
  totalRecordsCurrent: number;
  totalChanges: number;
  critical: number;
  high: number;
  medium: number;
  low: number;
  changesByTechnology: TechnologyChangeCountDto[];
  changesByType: ChangeTypeCountDto[];
  recentImportantChanges: ChangeRecordDto[];
}

export interface TechnologyDto {
  id: number;
  code: string;
  name: string;
  description: string | null;
}

export interface SupplierDto {
  id: number;
  canonicalName: string;
  canonicalCountry: string | null;
  aliasCount: number;
}

export interface SupplierMaterialStatusDto {
  lotamEntryId: number;
  materialCode: string;
  categoryName: string;
  materialDescription: string;
  applicableStandard: string | null;
  releaseDate: string;
  status: string;
}

export interface LotamEntryDetailDto {
  id: number;
  supplierCanonicalName: string;
  materialCode: string;
  categoryName: string;
  materialDescription: string;
  applicableStandard: string | null;
  releaseDate: string;
  sourceFilename: string;
  pageNumber: number | null;
  status: string;
  statusConfidence: number | null;
  localRepresentativeRaw: string | null;
  modelRatingRaw: string | null;
  typeTestsRaw: string | null;
  remarksRaw: string | null;
  manufacturerCountryRaw: string | null;
  rawManufacturerBlock: string;
}

export interface SupplierAliasReviewDto {
  aliasId: number;
  rawName: string;
  rawCountry: string | null;
  suggestedSupplierId: number | null;
  suggestedSupplierName: string | null;
  confidence: number | null;
  firstSeenReleaseDate: string;
  entryCount: number;
}

export interface SupplierOptionDto {
  id: number;
  canonicalName: string;
  canonicalCountry: string | null;
}

export interface UnmatchedMaterialSectionDto {
  materialSectionId: number;
  code: string;
  description: string;
  entryCount: number;
}

export interface UnmatchedSectionsDto {
  removedFromPrevious: UnmatchedMaterialSectionDto[];
  newInCurrent: UnmatchedMaterialSectionDto[];
}

export interface MaterialCodeRemapDto {
  id: number;
  fromCode: string;
  toCode: string;
  rationale: string;
  confirmedBy: string;
  confirmedAt: string;
}

export interface MaterialSectionOptionDto {
  materialSectionId: number;
  code: string;
  label: string;
  entryCount: number;
  printedTotal: number;
  validationStatus: string;
}

export interface EntrySummaryDto {
  entryId: number;
  materialCode: string;
  materialLabel: string;
  supplierName: string;
  status: string;
  badge: string | null;
  // CategoryName/MaterialDescription/ApplicableStandard are the same
  // section fields already folded into materialLabel above (kept as-is so
  // the existing flat view never changes) but broken out separately for
  // the Material -> Equipment Description -> Supplier hierarchy view.
  categoryName: string;
  materialDescription: string;
  applicableStandard: string | null;
}

export interface MaterialSectionEntryDto {
  entryId: number;
  rawManufacturerBlock: string;
  localRepresentativeRaw: string | null;
  modelRatingRaw: string | null;
  typeTestsRaw: string | null;
  remarksRaw: string | null;
  status: string;
  badge: 'New' | 'Removed' | 'Updated' | null;
}

export interface MaterialSectionDetailDto {
  materialSectionId: number;
  code: string;
  categoryName: string;
  rawDescription: string;
  applicableStandard: string | null;
  printedTotal: number;
  validationStatus: string;
  entries: MaterialSectionEntryDto[];
}

export interface SupplierHistoryDto {
  supplierId: number;
  canonicalName: string;
  materialStatuses: SupplierMaterialStatusDto[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ChangeFilters {
  previousReleaseId: number;
  currentReleaseId: number;
  technology?: string;
  supplier?: string;
  severity?: string;
  changeType?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

// ---- Supply Chain Map ----

export interface GeoConcentrationDto {
  country: string;
  technologyCode: string;
  technologyName: string;
  supplierCount: number;
}

export interface GeoConcentrationSummaryDto {
  releaseDate: string;
  unmappedEntryCount: number;
  rows: GeoConcentrationDto[];
}

export interface GeoConcentrationSupplierDto {
  entryId: number;
  supplierName: string;
  localRepresentative: string | null;
  status: string;
  materialCode: string;
  materialDescription: string;
}

// ---- My Suppliers watchlist ----

export interface WatchlistItemDto {
  watchlistId: number;
  supplierId: number;
  canonicalName: string;
  canonicalCountry: string | null;
  addedBy: string;
  notes: string | null;
  addedAt: string;
}

// ---- Release Audit page ----

export interface AuditReleaseSummaryDto {
  releaseId: number;
  releaseDate: string;
  sourceFilename: string;
  sectionCount: number;
  mismatchCount: number;
  importedAt: string;
}

export interface AuditSectionOptionDto {
  code: string;
  label: string;
}

export interface AuditManufacturerDto {
  supplierName: string;
  status: string;
}

export interface AuditSectionReleaseSnapshotDto {
  releaseId: number;
  releaseDate: string;
  categoryName: string;
  rawDescription: string;
  printedTotal: number;
  extractedEntryCount: number;
  validationStatus: string;
  manufacturers: AuditManufacturerDto[];
}

export interface AuditSectionHistoryDto {
  code: string;
  snapshots: AuditSectionReleaseSnapshotDto[];
}

// ---- SEC monitoring ----

export interface SecMonitorCheckResultDto {
  configured: boolean;
  message: string | null;
  newDocumentsFound: number;
  checkedAt: string;
}

export interface SecDocumentWatchDto {
  id: number;
  url: string;
  fileName: string | null;
  firstSeenAt: string;
}

// ---- Press release search (Tender Readiness) ----

export interface PressReleaseResultDto {
  title: string;
  snippet: string;
  url: string;
}

export interface PressReleaseSearchResponseDto {
  configured: boolean;
  message: string | null;
  hits: PressReleaseResultDto[];
}

// ---- Auth ----

export interface UserDto {
  id: number;
  email: string;
  fullName: string;
  mobile: string | null;
  address: string | null;
  department: string | null;
  isAdmin: boolean;
}

export interface LoginResponseDto {
  token: string;
  user: UserDto;
}

export interface GenericMessageDto {
  message: string;
}

// ---- Profile ----

export interface NotificationSubscriptionDto {
  id: number;
  subscriptionType: 'MaterialCategory' | 'MaterialSection' | 'Technology' | 'Supplier' | string;
  targetCode: string;
  targetLabel: string;
  createdAt: string;
  recipients: SubscriptionRecipientDto[];
}

export interface SubscriptionRecipientDto {
  id: number;
  subscriptionId: number;
  email: string;
  name: string | null;
  createdAt: string;
}

export interface SubscribableOptionsDto {
  materialCategories: string[];
  technologies: TechnologyDto[];
  materialSections: SubscribableMaterialDto[];
}

export interface SubscribableMaterialDto {
  code: string;
  label: string;
}

export interface SendDigestResultDto {
  queued: number;
}

// ---- Export (format choice + optional email delivery) ----

export type ExportFormat = 'pdf' | 'html';

export interface ExportSendResultDto {
  sent: boolean;
  message: string;
}

// ---- Admin (Profile page "Admin" tab, admins only) ----

export interface AdminUserDto {
  id: number;
  email: string;
  fullName: string;
  department: string | null;
  isActive: boolean;
  isAdmin: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface InviteUserResultDto {
  user: AdminUserDto;
  emailSent: boolean;
}
