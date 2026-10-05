import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { LotamEntryDetailDto, SupplierDto, SupplierTimelineDto, SupplierTimelinePointDto } from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';
import { STATUS_COLORS } from '../../core/theme';

interface MaterialRow {
  materialCode: string;
  materialDescription: string;
  segments: { leftPct: number; widthPct: number; status: string; startDate: string; endDate: string }[];
}

@Component({
  selector: 'app-supplier-timeline',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent],
  template: `
    <div class="page">
      <h1>Supplier Timeline</h1>
      <p class="subtitle">
        Full qualification history for one supplier across every imported release - not just the
        latest-vs-previous comparison. Built from the same lotam_entry records as every other page;
        with only two releases loaded so far this will look sparse, but it's designed to keep
        growing correctly as more releases are imported over time.
      </p>

      <div class="layout">
        <div class="card list-panel">
          <input type="text" placeholder="Search suppliers..." [(ngModel)]="search" (ngModelChange)="onSearch()" />
          <div class="supplier-list">
            <div
              *ngFor="let s of suppliers"
              class="supplier-item"
              [class.active]="s.id === selectedId"
              (click)="select(s)"
            >
              <div class="name">{{ s.canonicalName }}</div>
              <div class="meta">{{ s.canonicalCountry || 'Unknown country' }}</div>
            </div>
            <div *ngIf="suppliers.length === 0" class="meta" style="padding: 12px;">No suppliers found.</div>
          </div>
        </div>

        <div class="detail-panel" *ngIf="timeline">
          <div class="card" style="margin-bottom: 16px;">
            <h2>{{ timeline.canonicalName }}</h2>
            <p class="muted" *ngIf="releaseDates.length > 0">
              Releases covered: {{ releaseDates.join(' → ') }}
            </p>

            <div class="gantt" *ngFor="let row of materialRows()">
              <div class="gantt-label">{{ row.materialCode }} — {{ row.materialDescription }}</div>
              <div class="gantt-track">
                <div
                  class="gantt-segment"
                  *ngFor="let seg of row.segments"
                  [style.left.%]="seg.leftPct"
                  [style.width.%]="seg.widthPct"
                  [style.background]="statusColor(seg.status)"
                  [title]="seg.status + ': ' + seg.startDate + ' to ' + seg.endDate"
                ></div>
              </div>
            </div>
            <p class="muted" *ngIf="materialRows().length === 0">No qualification records found for this supplier.</p>
          </div>

          <div class="card">
            <h2>Full audit trail</h2>
            <table class="data-table">
              <thead>
                <tr><th>Release</th><th>Material</th><th>Status</th></tr>
              </thead>
              <tbody>
                <tr *ngFor="let p of timeline.points" (click)="openEntry(p)">
                  <td>{{ p.releaseDate }} ({{ p.sourceFilename }})</td>
                  <td>{{ p.materialCode }} — {{ p.materialDescription }}</td>
                  <td><app-severity-badge [severity]="p.status" [statusMode]="true"></app-severity-badge></td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
        <div class="card detail-panel" *ngIf="!timeline">
          <p class="muted">Select a supplier to see its full history.</p>
        </div>
      </div>
    </div>

    <div class="glass-overlay" *ngIf="entryDetail" (click)="entryDetail = null">
      <div class="glass-panel" (click)="$event.stopPropagation()">
        <button class="close" (click)="entryDetail = null">✕</button>
        <h2>{{ entryDetail!.supplierCanonicalName }}</h2>
        <p class="muted">{{ entryDetail!.materialCode }} — {{ entryDetail!.materialDescription }}</p>
        <app-severity-badge [severity]="entryDetail!.status" [statusMode]="true"></app-severity-badge>
        <h3 style="margin-top: 16px;">Manufacturer (raw, as printed)</h3>
        <div class="value-box">{{ entryDetail!.rawManufacturerBlock }}</div>
        <h3 style="margin-top: 16px;">Remarks</h3>
        <div class="value-box">{{ entryDetail!.remarksRaw || '—' }}</div>
      </div>
    </div>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 800px; margin-bottom: 16px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .layout { display: grid; grid-template-columns: 300px 1fr; gap: 16px; align-items: start; }
      .list-panel input { width: 100%; margin-bottom: 12px; }
      .supplier-list { display: flex; flex-direction: column; gap: 4px; max-height: 65vh; overflow-y: auto; }
      .supplier-item { padding: 10px; border-radius: 10px; cursor: pointer; }
      .supplier-item:hover { background: var(--surface-2); }
      .supplier-item.active { background: var(--surface-2); border: 1px solid var(--accent); }
      .name { font-size: 13px; font-weight: 600; }
      .meta { font-size: 11px; color: var(--text-dim); }
      .gantt { margin-bottom: 16px; }
      .gantt-label { font-size: 12px; margin-bottom: 4px; }
      .gantt-track { position: relative; height: 16px; background: var(--surface-2); border-radius: 6px; overflow: hidden; }
      .gantt-segment { position: absolute; top: 0; bottom: 0; }
    `,
  ],
})
export class SupplierTimelineComponent {
  suppliers: SupplierDto[] = [];
  search = '';
  selectedId: number | null = null;
  timeline: SupplierTimelineDto | null = null;
  entryDetail: LotamEntryDetailDto | null = null;

  constructor(private api: ApiService) {
    this.loadSuppliers();
  }

  onSearch() {
    this.loadSuppliers();
  }

  private loadSuppliers() {
    this.api.getSuppliers(this.search || undefined).subscribe((s) => (this.suppliers = s));
  }

  select(s: SupplierDto) {
    this.selectedId = s.id;
    this.api.getSupplierTimeline(s.id).subscribe((t) => (this.timeline = t));
  }

  get releaseDates(): string[] {
    if (!this.timeline) return [];
    return [...new Set(this.timeline.points.map((p) => p.releaseDate))].sort();
  }

  materialRows(): MaterialRow[] {
    if (!this.timeline || this.timeline.points.length === 0) return [];

    const dates = this.timeline.points.map((p) => new Date(p.releaseDate).getTime());
    const minDate = Math.min(...dates);
    const maxDate = Math.max(...dates);
    const span = Math.max(1, maxDate - minDate);
    const pct = (t: number) => ((t - minDate) / span) * 100;

    const byMaterial = new Map<string, SupplierTimelinePointDto[]>();
    for (const p of this.timeline.points) {
      const key = `${p.materialCode}|${p.materialDescription}`;
      if (!byMaterial.has(key)) byMaterial.set(key, []);
      byMaterial.get(key)!.push(p);
    }

    const rows: MaterialRow[] = [];
    for (const [key, points] of byMaterial) {
      const [materialCode, materialDescription] = key.split('|');
      const sorted = [...points].sort((a, b) => a.releaseDate.localeCompare(b.releaseDate));
      const segments: MaterialRow['segments'] = [];

      for (let i = 0; i < sorted.length; i++) {
        const startT = new Date(sorted[i].releaseDate).getTime();
        const isLast = i === sorted.length - 1;
        const endT = isLast ? maxDate + span * 0.06 : new Date(sorted[i + 1].releaseDate).getTime();
        const leftPct = pct(startT);
        const widthPct = Math.max(1.5, pct(endT) - leftPct);
        segments.push({
          leftPct,
          widthPct: Math.min(widthPct, 100 - leftPct),
          status: sorted[i].status,
          startDate: sorted[i].releaseDate,
          endDate: isLast ? 'latest' : sorted[i + 1].releaseDate,
        });
      }

      rows.push({ materialCode, materialDescription, segments });
    }
    return rows.sort((a, b) => a.materialCode.localeCompare(b.materialCode, undefined, { numeric: true }));
  }

  statusColor(status: string): string {
    return (STATUS_COLORS[status] ?? STATUS_COLORS['Unknown']).color;
  }

  openEntry(p: SupplierTimelinePointDto) {
    this.api.getLotamEntryDetail(p.entryId).subscribe((d) => (this.entryDetail = d));
  }
}
