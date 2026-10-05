import { Component, OnInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import { ChangeRecordDetailDto, ChangeRecordDto, TechnologyDto } from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';
import { KpiCardComponent } from '../../shared/kpi-card/kpi-card.component';

@Component({
  selector: 'app-technology-view',
  standalone: true,
  imports: [CommonModule, SeverityBadgeComponent, KpiCardComponent],
  template: `
    <div class="page">
      <h1>Technology View</h1>

      <div class="tech-tabs">
        <button
          *ngFor="let t of technologies"
          [class.active]="t.code === selectedCode"
          (click)="select(t.code)"
        >
          {{ t.name }}
        </button>
      </div>

      <ng-container *ngIf="selectedCode">
        <div class="grid" style="grid-template-columns: repeat(4, 1fr); margin: 20px 0;">
          <app-kpi-card label="Total Changes" [value]="changes.length"></app-kpi-card>
          <app-kpi-card
            label="Critical"
            [value]="countBySeverity('Critical')"
            accentColor="var(--critical)"
          ></app-kpi-card>
          <app-kpi-card label="High" [value]="countBySeverity('High')" accentColor="var(--high)"></app-kpi-card>
          <app-kpi-card
            label="Distinct Suppliers"
            [value]="distinctSupplierCount()"
          ></app-kpi-card>
        </div>

        <div class="card">
          <table class="data-table">
            <thead>
              <tr>
                <th>Supplier</th>
                <th>Material</th>
                <th>Change Type</th>
                <th>Previous</th>
                <th>Current</th>
                <th>Severity</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let c of changes" (click)="openDetail(c)">
                <td>{{ c.supplierName }}</td>
                <td>{{ c.materialCode }} — {{ c.materialDescription }}</td>
                <td>{{ c.changeType }}</td>
                <td class="truncate">{{ c.previousValue || '—' }}</td>
                <td class="truncate">{{ c.currentValue || '—' }}</td>
                <td><app-severity-badge [severity]="c.severity"></app-severity-badge></td>
              </tr>
              <tr *ngIf="changes.length === 0">
                <td colspan="6" style="color: var(--text-dim);">No changes for this technology in this release pair.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </ng-container>
    </div>

    <div class="overlay" *ngIf="selected" (click)="closeDetail()">
      <div class="side-panel" (click)="$event.stopPropagation()">
        <button class="close" (click)="closeDetail()">✕</button>
        <ng-container *ngIf="detail as d">
          <h2>{{ d.change.supplierName }}</h2>
          <p class="muted">{{ d.change.materialCode }} — {{ d.change.materialDescription }}</p>
          <div class="row">
            <app-severity-badge [severity]="d.change.severity"></app-severity-badge>
            <span class="pill" style="background: rgba(255,255,255,0.06); color: var(--text-dim);">
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
      .tech-tabs { display: flex; gap: 8px; flex-wrap: wrap; }
      .tech-tabs button.active { background: var(--accent); border-color: var(--accent); color: white; }
      .truncate { max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .overlay { position: fixed; inset: 0; background: rgba(5,8,16,0.55); backdrop-filter: blur(4px); display: flex; justify-content: flex-end; z-index: 50; }
      .side-panel { width: 480px; max-width: 90vw; background: rgba(19, 28, 46, 0.85); backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px); border-left: 1px solid rgba(255,255,255,0.08); padding: 28px; overflow-y: auto; position: relative; }
      .close { position: absolute; top: 20px; right: 20px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .row { display: flex; gap: 8px; margin-top: 8px; }
      .compare-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
      .value-box { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08); border-radius: 10px; padding: 10px 12px; font-size: 13px; margin-top: 4px; white-space: pre-wrap; }
      .value-box.small { font-size: 11px; max-height: 200px; overflow-y: auto; }
    `,
  ],
})
export class TechnologyViewComponent implements OnInit {
  technologies: TechnologyDto[] = [];
  selectedCode: string | null = null;
  changes: ChangeRecordDto[] = [];

  selected: ChangeRecordDto | null = null;
  detail: ChangeRecordDetailDto | null = null;

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
  ) {
    effect(() => {
      this.selection.previousReleaseId();
      this.selection.currentReleaseId();
      if (this.selectedCode) this.loadChanges();
    });
  }

  ngOnInit(): void {
    this.api.getTechnologies().subscribe((t) => {
      this.technologies = t;
      if (t.length > 0) this.select(t[0].code);
    });
  }

  select(code: string) {
    this.selectedCode = code;
    this.loadChanges();
  }

  private loadChanges() {
    const prev = this.selection.previousReleaseId();
    const curr = this.selection.currentReleaseId();
    if (prev === null || curr === null || !this.selectedCode) return;

    this.api.getTechnologyChanges(this.selectedCode, prev, curr).subscribe((c) => (this.changes = c));
  }

  countBySeverity(severity: string): number {
    return this.changes.filter((c) => c.severity === severity).length;
  }

  distinctSupplierCount(): number {
    return new Set(this.changes.map((c) => c.supplierName)).size;
  }

  openDetail(c: ChangeRecordDto) {
    this.selected = c;
    this.api.getChangeDetail(c.id).subscribe((d) => (this.detail = d));
  }

  closeDetail() {
    this.selected = null;
    this.detail = null;
  }
}
