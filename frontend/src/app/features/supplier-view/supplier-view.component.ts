import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { LotamEntryDetailDto, SupplierDto, SupplierHistoryDto, SupplierMaterialStatusDto } from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

// Maps our actual derived-status vocabulary to display labels. Kept
// distinct from the raw enum value (used for filtering/matching) so the
// underlying data stays accurate to what the document actually supports.
const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: 'Qualified', label: 'Qualified' },
  { value: 'Conditional', label: 'Conditional Approval' },
  { value: 'Suspended', label: 'Suspended (On Hold)' },
  { value: 'Disqualified', label: 'Disqualified' },
  { value: 'Blacklisted', label: 'Blacklisted (Blocked)' },
  { value: 'Reactivated', label: 'Reactivated' },
  { value: 'Unknown', label: 'Unknown' },
];

@Component({
  selector: 'app-supplier-view',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent],
  template: `
    <div class="page">
      <h1>Supplier View</h1>

      <div class="layout">
        <div class="card list-panel">
          <div class="search-row">
            <input
              type="text"
              placeholder="Search suppliers..."
              [(ngModel)]="search"
              (ngModelChange)="onFilterChange()"
            />
            <select [(ngModel)]="statusFilter" (ngModelChange)="onFilterChange()">
              <option [ngValue]="null">All statuses</option>
              <option *ngFor="let opt of statusOptions" [ngValue]="opt.value">{{ opt.label }}</option>
            </select>
          </div>
          <div class="supplier-list">
            <div
              *ngFor="let s of suppliers"
              class="supplier-item"
              [class.active]="s.id === selectedId"
              (click)="select(s)"
            >
              <div class="name">{{ s.canonicalName }}</div>
              <div class="meta">{{ s.canonicalCountry || 'Unknown country' }} · {{ s.aliasCount }} alias(es)</div>
            </div>
            <div *ngIf="suppliers.length === 0" class="meta" style="padding: 12px;">
              No suppliers match this search/filter.
            </div>
          </div>
        </div>

        <div class="card detail-panel" *ngIf="history">
          <h2>{{ history.canonicalName }}</h2>
          <p class="muted" *ngIf="latestReleaseDate">As of the latest release ({{ latestReleaseDate }})</p>

          <table class="data-table">
            <thead>
              <tr>
                <th>Material</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let m of history.materialStatuses" (click)="openEntry(m)">
                <td>{{ formatMaterial(m.materialCode, m.categoryName, m.materialDescription, m.applicableStandard) }}</td>
                <td><app-severity-badge [severity]="m.status" [statusMode]="true"></app-severity-badge></td>
              </tr>
              <tr *ngIf="history.materialStatuses.length === 0">
                <td colspan="2" style="color: var(--text-dim);">No qualification records found in the latest release.</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="card detail-panel" *ngIf="!history">
          <p style="color: var(--text-dim);">Select a supplier to see its current qualification status.</p>
        </div>
      </div>
    </div>

    <div class="glass-overlay" *ngIf="entryDetail" (click)="closeEntry()">
      <div class="glass-panel" (click)="$event.stopPropagation()">
        <button class="close" (click)="closeEntry()">✕</button>
        <h2>{{ entryDetail!.supplierCanonicalName }}</h2>
        <p class="muted">
          {{ formatMaterial(entryDetail!.materialCode, entryDetail!.categoryName, entryDetail!.materialDescription, entryDetail!.applicableStandard) }}
        </p>
        <div class="row">
          <app-severity-badge [severity]="entryDetail!.status" [statusMode]="true"></app-severity-badge>
          <span class="pill" style="background: rgba(255,255,255,0.06); color: var(--text-dim);">
            {{ entryDetail!.releaseDate }} · {{ entryDetail!.sourceFilename }}
            <ng-container *ngIf="entryDetail!.pageNumber as p">, page {{ p }}</ng-container>
          </span>
        </div>

        <h3 style="margin-top: 20px;">Manufacturer (raw, as printed)</h3>
        <div class="value-box">{{ entryDetail!.rawManufacturerBlock }}</div>

        <div class="two-col">
          <div>
            <h3>Local Representative</h3>
            <div class="value-box">{{ entryDetail!.localRepresentativeRaw || '— (not specified in source)' }}</div>
          </div>
          <div>
            <h3>Country</h3>
            <div class="value-box">{{ entryDetail!.manufacturerCountryRaw || '—' }}</div>
          </div>
        </div>

        <h3 style="margin-top: 16px;">Model / Rating</h3>
        <div class="value-box">{{ entryDetail!.modelRatingRaw || '—' }}</div>

        <h3 style="margin-top: 16px;">Type Tests</h3>
        <div class="value-box">{{ entryDetail!.typeTestsRaw || '—' }}</div>

        <h3 style="margin-top: 16px;">Remarks / Observations</h3>
        <div class="value-box">{{ entryDetail!.remarksRaw || '—' }}</div>

        <p class="muted" style="margin-top: 16px;" *ngIf="entryDetail!.statusConfidence as c">
          Status derived with {{ (c * 100).toFixed(0) }}% confidence from the remarks text above.
        </p>
      </div>
    </div>
  `,
  styles: [
    `
      .layout { display: grid; grid-template-columns: 340px 1fr; gap: 16px; margin-top: 16px; align-items: start; }
      .search-row { display: flex; flex-direction: column; gap: 8px; margin-bottom: 12px; }
      .search-row input, .search-row select { width: 100%; }
      .supplier-list { display: flex; flex-direction: column; gap: 4px; max-height: 65vh; overflow-y: auto; }
      .supplier-item { padding: 10px; border-radius: 10px; cursor: pointer; transition: background 120ms ease; }
      .supplier-item:hover { background: var(--surface-2); }
      .supplier-item.active { background: var(--surface-2); border: 1px solid var(--accent); }
      .name { font-size: 13px; font-weight: 600; }
      .meta { font-size: 11px; color: var(--text-dim); }
      .muted { color: var(--text-dim); font-size: 12px; margin-bottom: 12px; }
      .row { display: flex; gap: 8px; margin-top: 8px; flex-wrap: wrap; align-items: center; }
      .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 16px; }
    `,
  ],
})
export class SupplierViewComponent implements OnInit {
  suppliers: SupplierDto[] = [];
  search = '';
  selectedId: number | null = null;
  history: SupplierHistoryDto | null = null;
  entryDetail: LotamEntryDetailDto | null = null;

  statusOptions = STATUS_OPTIONS;
  statusFilter: string | null = null;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadSuppliers();
  }

  onFilterChange() {
    this.loadSuppliers();
  }

  private loadSuppliers() {
    this.api.getSuppliers(this.search || undefined, this.statusFilter).subscribe((s) => (this.suppliers = s));
  }

  select(s: SupplierDto) {
    this.selectedId = s.id;
    this.api.getSupplierHistory(s.id).subscribe((h) => (this.history = h));
  }

  get latestReleaseDate(): string | null {
    return this.history?.materialStatuses[0]?.releaseDate ?? null;
  }

  // "51.1 Telecommunication Integration facilities - Telecommunication
  // Integration facilities, Applicable Standards: ..." - code, then category,
  // then the material's own description, standard appended when present.
  formatMaterial(code: string, categoryName: string, description: string, standard: string | null): string {
    const base = `${code} ${categoryName} - ${description}`;
    return standard ? `${base}, Applicable Standards: ${standard}` : base;
  }

  openEntry(m: SupplierMaterialStatusDto) {
    this.api.getLotamEntryDetail(m.lotamEntryId).subscribe((d) => (this.entryDetail = d));
  }

  closeEntry() {
    this.entryDetail = null;
  }
}
