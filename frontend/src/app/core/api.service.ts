import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AdminUserDto,
  ChangeFilters,
  ChangeRecordDetailDto,
  DashboardSummaryDto,
  AuditReleaseSummaryDto,
  AuditSectionHistoryDto,
  AuditSectionOptionDto,
  EntrySummaryDto,
  ExportFormat,
  ExportSendResultDto,
  GeoConcentrationSummaryDto,
  GeoConcentrationSupplierDto,
  ImportResultDto,
  InviteUserResultDto,
  NotificationSubscriptionDto,
  PagedResult,
  ChangeRecordDto,
  LotamEntryDetailDto,
  MaterialCodeRemapDto,
  MaterialSectionDetailDto,
  MaterialSectionOptionDto,
  PressReleaseSearchResponseDto,
  ReleaseDto,
  RunComparisonResultDto,
  SecDocumentWatchDto,
  SecMonitorCheckResultDto,
  SendDigestResultDto,
  SubscribableOptionsDto,
  SubscriptionRecipientDto,
  SupplierAliasReviewDto,
  SupplierDto,
  SupplierHistoryDto,
  SupplierOptionDto,
  UserDto,
  WatchlistItemDto,
  TechnologyDto,
  UnmatchedSectionsDto,
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  // apiBaseUrl is '' in dev (proxy.conf.json forwards /api same-origin) and
  // the deployed API's real origin in production, since a static host
  // (Cloudflare Pages) can't reverse-proxy /api to a different domain -
  // see environment.prod.ts.
  private readonly base = `${environment.apiBaseUrl}/api`;

  constructor(private http: HttpClient) {}

  getReleases(): Observable<ReleaseDto[]> {
    return this.http.get<ReleaseDto[]>(`${this.base}/releases`);
  }

  importByPath(filePath: string): Observable<ImportResultDto> {
    return this.http.post<ImportResultDto>(`${this.base}/import/path`, { filePath });
  }

  importByUpload(file: File): Observable<ImportResultDto> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<ImportResultDto>(`${this.base}/import/upload`, formData);
  }

  runComparison(previousReleaseId: number, currentReleaseId: number): Observable<RunComparisonResultDto> {
    return this.http.post<RunComparisonResultDto>(`${this.base}/comparisons/run`, {
      previousReleaseId,
      currentReleaseId,
    });
  }

  getDashboard(previousReleaseId: number, currentReleaseId: number): Observable<DashboardSummaryDto> {
    return this.http.get<DashboardSummaryDto>(`${this.base}/dashboard`, {
      params: { previousReleaseId, currentReleaseId },
    });
  }

  getChanges(filters: ChangeFilters): Observable<PagedResult<ChangeRecordDto>> {
    const params: Record<string, string | number> = {
      previousReleaseId: filters.previousReleaseId,
      currentReleaseId: filters.currentReleaseId,
      page: filters.page ?? 1,
      pageSize: filters.pageSize ?? 25,
    };
    if (filters.technology) params['technology'] = filters.technology;
    if (filters.supplier) params['supplier'] = filters.supplier;
    if (filters.severity) params['severity'] = filters.severity;
    if (filters.changeType) params['changeType'] = filters.changeType;
    if (filters.search) params['search'] = filters.search;

    return this.http.get<PagedResult<ChangeRecordDto>>(`${this.base}/changes`, { params });
  }

  getChangeDetail(id: number): Observable<ChangeRecordDetailDto> {
    return this.http.get<ChangeRecordDetailDto>(`${this.base}/changes/${id}`);
  }

  getTechnologies(): Observable<TechnologyDto[]> {
    return this.http.get<TechnologyDto[]>(`${this.base}/technologies`);
  }

  getTechnologyChanges(
    code: string,
    previousReleaseId: number,
    currentReleaseId: number,
  ): Observable<ChangeRecordDto[]> {
    return this.http.get<ChangeRecordDto[]>(`${this.base}/technologies/${code}/changes`, {
      params: { previousReleaseId, currentReleaseId },
    });
  }

  getSuppliers(search?: string, status?: string | null): Observable<SupplierDto[]> {
    const params: Record<string, string> = {};
    if (search) params['search'] = search;
    if (status) params['status'] = status;
    return this.http.get<SupplierDto[]>(`${this.base}/suppliers`, { params });
  }

  getSupplierHistory(id: number): Observable<SupplierHistoryDto> {
    return this.http.get<SupplierHistoryDto>(`${this.base}/suppliers/${id}/history`);
  }

  getLotamEntryDetail(id: number): Observable<LotamEntryDetailDto> {
    return this.http.get<LotamEntryDetailDto>(`${this.base}/lotam-entries/${id}`);
  }

  getEntryChangeTypes(id: number, previousReleaseId?: number, currentReleaseId?: number): Observable<string[]> {
    const params: Record<string, number> = {};
    if (previousReleaseId !== undefined) params['previousReleaseId'] = previousReleaseId;
    if (currentReleaseId !== undefined) params['currentReleaseId'] = currentReleaseId;
    return this.http.get<string[]>(`${this.base}/lotam-entries/${id}/change-types`, { params });
  }

  // ---- Data quality: alias review ----

  getAliasesNeedingReview(): Observable<SupplierAliasReviewDto[]> {
    return this.http.get<SupplierAliasReviewDto[]>(`${this.base}/data-quality/aliases/needs-review`);
  }

  searchSuppliers(q: string): Observable<SupplierOptionDto[]> {
    return this.http.get<SupplierOptionDto[]>(`${this.base}/data-quality/suppliers/search`, { params: { q } });
  }

  confirmAlias(aliasId: number, supplierId: number, reviewedBy: string): Observable<void> {
    return this.http.post<void>(`${this.base}/data-quality/aliases/${aliasId}/confirm`, { supplierId, reviewedBy });
  }

  rejectAlias(aliasId: number, reviewedBy: string): Observable<void> {
    return this.http.post<void>(`${this.base}/data-quality/aliases/${aliasId}/reject`, { reviewedBy });
  }

  createSupplierForAlias(
    aliasId: number, canonicalName: string, canonicalCountry: string | null, reviewedBy: string,
  ): Observable<void> {
    return this.http.post<void>(`${this.base}/data-quality/aliases/${aliasId}/create-supplier`, {
      canonicalName, canonicalCountry, reviewedBy,
    });
  }

  // ---- Data quality: material code remap ----

  getUnmatchedSections(previousReleaseId: number, currentReleaseId: number): Observable<UnmatchedSectionsDto> {
    return this.http.get<UnmatchedSectionsDto>(`${this.base}/data-quality/material-sections/unmatched`, {
      params: { previousReleaseId, currentReleaseId },
    });
  }

  getRemaps(previousReleaseId: number, currentReleaseId: number): Observable<MaterialCodeRemapDto[]> {
    return this.http.get<MaterialCodeRemapDto[]>(`${this.base}/data-quality/material-code-remaps`, {
      params: { previousReleaseId, currentReleaseId },
    });
  }

  createRemap(
    fromMaterialSectionId: number, toMaterialSectionId: number, rationale: string, confirmedBy: string,
  ): Observable<MaterialCodeRemapDto> {
    return this.http.post<MaterialCodeRemapDto>(`${this.base}/data-quality/material-code-remaps`, {
      fromMaterialSectionId, toMaterialSectionId, rationale, confirmedBy,
    });
  }

  // ---- Material Explorer ----

  getMaterialSectionsForRelease(releaseId: number): Observable<MaterialSectionOptionDto[]> {
    return this.http.get<MaterialSectionOptionDto[]>(`${this.base}/releases/${releaseId}/material-sections`);
  }

  getEntriesSummary(
    releaseId: number, materialCodes?: string[], previousReleaseId?: number, currentReleaseId?: number,
  ): Observable<EntrySummaryDto[]> {
    const params: Record<string, string | number> = {};
    if (materialCodes && materialCodes.length > 0) params['materialCodes'] = materialCodes.join(',');
    if (previousReleaseId !== undefined) params['previousReleaseId'] = previousReleaseId;
    if (currentReleaseId !== undefined) params['currentReleaseId'] = currentReleaseId;
    return this.http.get<EntrySummaryDto[]>(`${this.base}/releases/${releaseId}/entries-summary`, { params });
  }

  getMaterialSectionDetail(
    materialSectionId: number, previousReleaseId?: number, currentReleaseId?: number,
  ): Observable<MaterialSectionDetailDto> {
    const params: Record<string, number> = {};
    if (previousReleaseId !== undefined) params['previousReleaseId'] = previousReleaseId;
    if (currentReleaseId !== undefined) params['currentReleaseId'] = currentReleaseId;
    return this.http.get<MaterialSectionDetailDto>(`${this.base}/material-sections/${materialSectionId}`, { params });
  }

  // ---- Supply Chain Map ----

  getGeoConcentration(): Observable<GeoConcentrationSummaryDto> {
    return this.http.get<GeoConcentrationSummaryDto>(`${this.base}/analytics/geo-concentration`);
  }

  getGeoConcentrationDetails(country: string, technology?: string | null): Observable<GeoConcentrationSupplierDto[]> {
    const params: Record<string, string> = { country };
    if (technology) params['technology'] = technology;
    return this.http.get<GeoConcentrationSupplierDto[]>(`${this.base}/analytics/geo-concentration/details`, { params });
  }

  // ---- My Suppliers watchlist ----

  getWatchlist(): Observable<WatchlistItemDto[]> {
    return this.http.get<WatchlistItemDto[]>(`${this.base}/watchlist`);
  }

  addToWatchlist(supplierId: number, addedBy: string, notes?: string): Observable<WatchlistItemDto> {
    return this.http.post<WatchlistItemDto>(`${this.base}/watchlist`, { supplierId, addedBy, notes });
  }

  removeFromWatchlist(watchlistId: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/watchlist/${watchlistId}`);
  }

  getWatchlistChanges(previousReleaseId: number, currentReleaseId: number): Observable<ChangeRecordDto[]> {
    return this.http.get<ChangeRecordDto[]>(`${this.base}/watchlist/changes`, {
      params: { previousReleaseId, currentReleaseId },
    });
  }

  // ---- Press release search ----

  searchPressReleases(materialSectionId: number): Observable<PressReleaseSearchResponseDto> {
    return this.http.get<PressReleaseSearchResponseDto>(`${this.base}/press-releases/search`, {
      params: { materialSectionId },
    });
  }

  // ---- Release Audit ----

  getAuditReleases(): Observable<AuditReleaseSummaryDto[]> {
    return this.http.get<AuditReleaseSummaryDto[]>(`${this.base}/audit/releases`);
  }

  getAuditSectionOptions(search?: string): Observable<AuditSectionOptionDto[]> {
    return this.http.get<AuditSectionOptionDto[]>(`${this.base}/audit/sections`, {
      params: search ? { search } : {},
    });
  }

  getAuditSectionHistory(code: string): Observable<AuditSectionHistoryDto> {
    return this.http.get<AuditSectionHistoryDto>(`${this.base}/audit/sections/${encodeURIComponent(code)}/history`);
  }

  // ---- SEC monitoring ----

  checkForNewSecReleases(): Observable<SecMonitorCheckResultDto> {
    return this.http.post<SecMonitorCheckResultDto>(`${this.base}/sec-monitor/check-now`, {});
  }

  getPendingSecDocuments(): Observable<SecDocumentWatchDto[]> {
    return this.http.get<SecDocumentWatchDto[]>(`${this.base}/sec-monitor/pending`);
  }

  dismissSecDocument(id: number): Observable<void> {
    return this.http.post<void>(`${this.base}/sec-monitor/${id}/dismiss`, {});
  }

  /** Permanently removes a discovered-link row (a genuine wrong match),
   * unlike dismissSecDocument which just hides an otherwise-legitimate one. */
  deleteSecDocument(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/sec-monitor/${id}`);
  }

  // ---- Report exports ----
  // Download variants return a raw Blob (the actual file bytes); the send-
  // to-email variants return a small JSON confirmation instead, since
  // there's no file to hand the browser in that case. Both format=pdf and
  // format=html are handled by the same backend endpoint - see
  // IReportRendererFactory. Triggering the browser download itself is
  // handled by the calling component via downloadBlob() below.

  exportChangesPdf(filters: ChangeFilters, format: ExportFormat = 'pdf'): Observable<Blob> {
    const params: Record<string, string | number> = {
      previousReleaseId: filters.previousReleaseId,
      currentReleaseId: filters.currentReleaseId,
      format,
    };
    if (filters.technology) params['technology'] = filters.technology;
    if (filters.supplier) params['supplier'] = filters.supplier;
    if (filters.severity) params['severity'] = filters.severity;
    if (filters.changeType) params['changeType'] = filters.changeType;
    if (filters.search) params['search'] = filters.search;
    return this.http.get(`${this.base}/changes/export`, { params, responseType: 'blob' });
  }

  emailChangesExport(filters: ChangeFilters, format: ExportFormat, emailTo: string): Observable<ExportSendResultDto> {
    const params: Record<string, string | number> = {
      previousReleaseId: filters.previousReleaseId,
      currentReleaseId: filters.currentReleaseId,
      format,
      emailTo,
    };
    if (filters.technology) params['technology'] = filters.technology;
    if (filters.supplier) params['supplier'] = filters.supplier;
    if (filters.severity) params['severity'] = filters.severity;
    if (filters.changeType) params['changeType'] = filters.changeType;
    if (filters.search) params['search'] = filters.search;
    return this.http.get<ExportSendResultDto>(`${this.base}/changes/export`, { params });
  }

  exportMaterialsPdf(
    releaseId: number, materialCodes: string[], previousReleaseId?: number, currentReleaseId?: number,
    format: ExportFormat = 'pdf',
  ): Observable<Blob> {
    const params: Record<string, string | number> = { materialCodes: materialCodes.join(','), format };
    if (previousReleaseId !== undefined) params['previousReleaseId'] = previousReleaseId;
    if (currentReleaseId !== undefined) params['currentReleaseId'] = currentReleaseId;
    return this.http.get(`${this.base}/releases/${releaseId}/entries-export`, { params, responseType: 'blob' });
  }

  emailMaterialsExport(
    releaseId: number, materialCodes: string[], format: ExportFormat, emailTo: string,
    previousReleaseId?: number, currentReleaseId?: number,
  ): Observable<ExportSendResultDto> {
    const params: Record<string, string | number> = { materialCodes: materialCodes.join(','), format, emailTo };
    if (previousReleaseId !== undefined) params['previousReleaseId'] = previousReleaseId;
    if (currentReleaseId !== undefined) params['currentReleaseId'] = currentReleaseId;
    return this.http.get<ExportSendResultDto>(`${this.base}/releases/${releaseId}/entries-export`, { params });
  }

  // ---- Profile ----

  getProfile(): Observable<UserDto> {
    return this.http.get<UserDto>(`${this.base}/profile`);
  }

  updateProfile(
    fullName: string, mobile: string | null, address: string | null, department: string | null,
  ): Observable<UserDto> {
    return this.http.put<UserDto>(`${this.base}/profile`, { fullName, mobile, address, department });
  }

  getSubscribableOptions(): Observable<SubscribableOptionsDto> {
    return this.http.get<SubscribableOptionsDto>(`${this.base}/profile/subscribable-options`);
  }

  getNotificationSubscriptions(): Observable<NotificationSubscriptionDto[]> {
    return this.http.get<NotificationSubscriptionDto[]>(`${this.base}/profile/notification-subscriptions`);
  }

  addNotificationSubscription(
    subscriptionType: string, targetCode: string, targetLabel: string,
  ): Observable<NotificationSubscriptionDto> {
    return this.http.post<NotificationSubscriptionDto>(`${this.base}/profile/notification-subscriptions`, {
      subscriptionType, targetCode, targetLabel,
    });
  }

  removeNotificationSubscription(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/profile/notification-subscriptions/${id}`);
  }

  addSubscriptionRecipient(subscriptionId: number, email: string, name: string | null): Observable<SubscriptionRecipientDto> {
    return this.http.post<SubscriptionRecipientDto>(
      `${this.base}/profile/notification-subscriptions/${subscriptionId}/recipients`, { email, name },
    );
  }

  removeSubscriptionRecipient(subscriptionId: number, recipientId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.base}/profile/notification-subscriptions/${subscriptionId}/recipients/${recipientId}`,
    );
  }

  sendSubscriptionDigest(): Observable<SendDigestResultDto> {
    return this.http.post<SendDigestResultDto>(`${this.base}/profile/notification-subscriptions/send-digest`, {});
  }

  // ---- Admin (Profile page "Admin" tab, admins only) ----

  getAdminUsers(): Observable<AdminUserDto[]> {
    return this.http.get<AdminUserDto[]>(`${this.base}/admin/users`);
  }

  inviteUser(
    email: string, fullName: string, department: string | null, isAdmin: boolean,
  ): Observable<InviteUserResultDto> {
    return this.http.post<InviteUserResultDto>(`${this.base}/admin/users/invite`, {
      email, fullName, department, isAdmin,
    });
  }

  setUserActive(userId: number, isActive: boolean): Observable<AdminUserDto> {
    return this.http.put<AdminUserDto>(`${this.base}/admin/users/${userId}/active`, { isActive });
  }
}

/// Triggers a browser file download from a Blob response - shared by any
/// export button, regardless of format.
export function downloadBlob(blob: Blob, filename: string): void {
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();
  window.URL.revokeObjectURL(url);
}
