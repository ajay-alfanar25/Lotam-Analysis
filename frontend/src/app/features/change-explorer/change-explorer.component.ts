import { Component, OnInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, downloadBlob } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import { ChangeRecordDetailDto, ChangeRecordDto, PagedResult, TechnologyDto } from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';
import { ExportLoadingComponent } from '../../shared/export-loading/export-loading.component';
import { ExportDownloadEvent, ExportEmailEvent, ExportMenuComponent } from '../../shared/export-menu/export-menu.component';
import { paletteColorForKey } from '../../core/theme';
import { ToastService } from '../../core/toast.service';

const SEVERITIES = ['Critical', 'High', 'Medium', 'Low'];
const CHANGE_TYPES = [
  'NewRecord', 'RemovedRecord', 'StatusChanged', 'ModelAdded', 'ModelRemoved',
  'RatingChanged', 'VoltageChanged', 'SpecificationChanged', 'ConditionChanged',
  'RemarkChanged', 'AgentChanged', 'CountryChanged', 'ScopeChanged', 'OtherMaterialChange',
];

@Component({
  selector: 'app-change-explorer',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent, ExportLoadingComponent, ExportMenuComponent],
  template: `
    <div class="page">
      <h1>Change Explorer</h1>

      <div class="filters card">
        <input
          type="text"
          placeholder="Search supplier, material, explanation..."
          [(ngModel)]="search"
          (ngModelChange)="onFilterChange()"
        />
        <select [(ngModel)]="technology" (ngModelChange)="onFilterChange()">
          <option [ngValue]="undefined">All technologies</option>
          <option *ngFor="let t of technologies" [ngValue]="t.code">{{ t.name }}</option>
        </select>
        <select [(ngModel)]="severity" (ngModelChange)="onFilterChange()">
          <option [ngValue]="undefined">All severities</option>
          <option *ngFor="let s of severities" [ngValue]="s">{{ s }}</option>
        </select>
        <select [(ngModel)]="changeType" (ngModelChange)="onFilterChange()">
          <option [ngValue]="undefined">All change types</option>
          <option *ngFor="let ct of changeTypes" [ngValue]="ct">{{ ct }}</option>
        </select>
        <input
          type="text"
          placeholder="Filter by supplier name..."
          [(ngModel)]="supplier"
          (ngModelChange)="onFilterChange()"
        />
        <button (click)="clearFilters()">Clear filter</button>
        <app-export-menu
          [disabled]="!canExport()"
          [disabledTitle]="canExport() ? 'Export the currently filtered changes' : 'Select a Technology or Change Type filter to enable export'"
          (downloadRequested)="onExportDownload($event)"
          (emailRequested)="onExportEmail($event)"
        ></app-export-menu>
      </div>

      <div class="card" style="margin-top: 16px;">
        <table class="data-table" *ngIf="result">
          <thead>
            <tr>
              <th>Supplier</th>
              <th>Technology</th>
              <th>Material</th>
              <th>Change Type</th>
              <th>Previous</th>
              <th>Current</th>
              <th>Severity</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let c of result.items" (click)="openDetail(c)">
              <td>{{ c.supplierName }}</td>
              <td>
                <span *ngFor="let t of c.technologyCodes" class="tech-tag" [style.background]="techBg(t)" [style.color]="techColor(t)">{{ t }}</span>
                <span *ngIf="c.technologyCodes.length === 0">—</span>
              </td>
              <td>{{ c.materialCode }}</td>
              <td>{{ c.changeType }}</td>
              <td class="truncate">{{ c.previousValue || '—' }}</td>
              <td class="truncate">{{ c.currentValue || '—' }}</td>
              <td><app-severity-badge [severity]="c.severity"></app-severity-badge></td>
            </tr>
            <tr *ngIf="result.items.length === 0">
              <td colspan="7" style="color: var(--text-dim);">No changes match these filters.</td>
            </tr>
          </tbody>
        </table>

        <div class="pagination" *ngIf="result && result.totalCount > 0">
          <button (click)="prevPage()" [disabled]="page === 1">← Previous</button>
          <span>Page {{ page }} of {{ totalPages() }} ({{ result.totalCount }} changes)</span>
          <button (click)="nextPage()" [disabled]="page >= totalPages()">Next →</button>
        </div>
      </div>
    </div>

    <app-export-loading [active]="exporting"></app-export-loading>

    <div class="glass-overlay" *ngIf="selected" (click)="closeDetail()">
      <div class="glass-panel" (click)="$event.stopPropagation()">
        <button class="close" (click)="closeDetail()">✕</button>
        <ng-container *ngIf="detail as d">
          <h2>{{ d.change.supplierName }}</h2>
          <p class="muted">{{ d.change.materialCode }} — {{ d.change.materialDescription }}</p>
          <div class="row">
            <app-severity-badge [severity]="d.change.severity"></app-severity-badge>
            <span class="pill" [style.background]="techBg(d.change.changeType)" [style.color]="techColor(d.change.changeType)">
              {{ d.change.changeType }}
            </span>
          </div>

          <h3 style="margin-top: 20px;">Explanation</h3>
          <p>{{ d.change.explanation }}</p>

          <h3 style="margin-top: 20px;">Previous → Current</h3>
          <div class="compare-grid">
            <div>
              <div class="muted">Previous ({{ d.change.previousReleaseDate }})</div>
              <div class="value-box">{{ d.change.previousValue || '—' }}</div>
            </div>
            <div>
              <div class="muted">Current ({{ d.change.currentReleaseDate }})</div>
              <div class="value-box">{{ d.change.currentValue || '—' }}</div>
            </div>
          </div>

          <h3 style="margin-top: 20px;">Source Evidence</h3>
          <div class="compare-grid">
            <div>
              <div class="muted">{{ d.previousPageEvidence || 'No previous-release record' }}</div>
              <div class="value-box small" *ngIf="d.previousRawExcerpt">{{ d.previousRawExcerpt }}</div>
            </div>
            <div>
              <div class="muted">{{ d.currentPageEvidence || 'No current-release record' }}</div>
              <div class="value-box small" *ngIf="d.currentRawExcerpt">{{ d.currentRawExcerpt }}</div>
            </div>
          </div>
        </ng-container>
      </div>
    </div>
  `,
  styles: [
    `
      .filters { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
      .filters input, .filters select { flex: 1; min-width: 160px; }
      .filters button { flex-shrink: 0; }
      .truncate { max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .pagination { display: flex; align-items: center; gap: 16px; justify-content: center; padding-top: 16px; font-size: 13px; color: var(--text-dim); }
      .tech-tag { display: inline-block; font-size: 10px; font-weight: 700; padding: 2px 7px; border-radius: 6px; margin-right: 4px; letter-spacing: 0.03em; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .row { display: flex; gap: 8px; margin-top: 8px; }
      .compare-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    `,
  ],
})
export class ChangeExplorerComponent implements OnInit {
  result: PagedResult<ChangeRecordDto> | null = null;
  technologies: TechnologyDto[] = [];
  severities = SEVERITIES;
  changeTypes = CHANGE_TYPES;

  search?: string;
  technology?: string;
  severity?: string;
  changeType?: string;
  supplier?: string;
  page = 1;
  pageSize = 25;

  selected: ChangeRecordDto | null = null;
  detail: ChangeRecordDetailDto | null = null;
  exporting = false;

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
    private toast: ToastService,
  ) {
    effect(() => {
      const prev = this.selection.previousReleaseId();
      const curr = this.selection.currentReleaseId();
      if (prev !== null && curr !== null) {
        this.page = 1;
        this.load();
      }
    });
  }

  ngOnInit(): void {
    this.api.getTechnologies().subscribe((t) => (this.technologies = t));
  }

  onFilterChange() {
    this.page = 1;
    this.load();
  }

  clearFilters() {
    this.search = undefined;
    this.technology = undefined;
    this.severity = undefined;
    this.changeType = undefined;
    this.supplier = undefined;
    this.onFilterChange();
  }

  // Export requires Technology OR Change Type - the two filters specific
  // enough to keep an export meaningfully scoped. Other filters may
  // additionally narrow it further once that gate is met.
  canExport(): boolean {
    return !!this.technology || !!this.changeType;
  }

  onExportDownload(event: ExportDownloadEvent) {
    if (!this.canExport()) return;
    const prev = this.selection.previousReleaseId();
    const curr = this.selection.currentReleaseId();
    if (prev === null || curr === null) return;

    this.exporting = true;
    this.api
      .exportChangesPdf(
        {
          previousReleaseId: prev,
          currentReleaseId: curr,
          technology: this.technology,
          severity: this.severity,
          changeType: this.changeType,
          supplier: this.supplier,
          search: this.search,
        },
        event.format,
      )
      .subscribe({
        next: (blob) => {
          const ext = event.format === 'html' ? 'html' : 'pdf';
          downloadBlob(blob, `LOTAM-Sentinel-Changes-${new Date().toISOString().slice(0, 10)}.${ext}`);
          this.exporting = false;
        },
        error: () => {
          this.exporting = false;
          this.toast.error('Could not generate the export. Please try again.');
        },
      });
  }

  onExportEmail(event: ExportEmailEvent) {
    if (!this.canExport()) return;
    const prev = this.selection.previousReleaseId();
    const curr = this.selection.currentReleaseId();
    if (prev === null || curr === null) return;

    this.exporting = true;
    this.api
      .emailChangesExport(
        {
          previousReleaseId: prev,
          currentReleaseId: curr,
          technology: this.technology,
          severity: this.severity,
          changeType: this.changeType,
          supplier: this.supplier,
          search: this.search,
        },
        event.format,
        event.email,
      )
      .subscribe({
        // The API call succeeding (HTTP 200) only means the request was
        // accepted - ExportSendResultDto.sent is the actual outcome (the
        // email provider can still fail, e.g. SMTP timeout - see
        // DEPLOYMENT.md "Email delivery"), so both branches are checked,
        // not just the HTTP success/error split.
        next: (result) => {
          this.exporting = false;
          if (result.sent) this.toast.success(result.message || `Emailed to ${event.email}.`);
          else this.toast.error(result.message || `Could not send the email to ${event.email}.`);
        },
        error: (err) => {
          this.exporting = false;
          this.toast.error(err?.error?.message || `Could not send the email to ${event.email}.`);
        },
      });
  }

  load() {
    const prev = this.selection.previousReleaseId();
    const curr = this.selection.currentReleaseId();
    if (prev === null || curr === null) return;

    this.api
      .getChanges({
        previousReleaseId: prev,
        currentReleaseId: curr,
        technology: this.technology,
        severity: this.severity,
        changeType: this.changeType,
        supplier: this.supplier,
        search: this.search,
        page: this.page,
        pageSize: this.pageSize,
      })
      .subscribe((r) => (this.result = r));
  }

  totalPages(): number {
    if (!this.result) return 1;
    return Math.max(1, Math.ceil(this.result.totalCount / this.result.pageSize));
  }

  prevPage() {
    if (this.page > 1) {
      this.page--;
      this.load();
    }
  }

  nextPage() {
    if (this.page < this.totalPages()) {
      this.page++;
      this.load();
    }
  }

  openDetail(c: ChangeRecordDto) {
    this.selected = c;
    this.api.getChangeDetail(c.id).subscribe((d) => (this.detail = d));
  }

  closeDetail() {
    this.selected = null;
    this.detail = null;
  }

  techBg(code: string): string {
    return paletteColorForKey(code).bg;
  }

  techColor(code: string): string {
    return paletteColorForKey(code).color;
  }
}
