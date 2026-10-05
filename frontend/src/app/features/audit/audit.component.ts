import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import {
  AuditReleaseSummaryDto, AuditSectionHistoryDto, AuditSectionOptionDto,
  SecDocumentWatchDto, SecMonitorCheckResultDto,
} from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent],
  template: `
    <div class="page">
      <h1>Release Audit</h1>
      <p class="subtitle">
        Every imported release, whether SEC has published something new, and a material's own
        history across every release it's appeared in - without running the mismatch query by hand.
      </p>

      <div class="card" style="margin-bottom: 16px;">
        <div class="row" style="justify-content: space-between; align-items: center;">
          <h2 style="margin: 0;">SEC monitoring</h2>
          <button class="primary" (click)="checkNow()" [disabled]="checking">
            {{ checking ? 'Checking...' : 'Check for new LOTAM release now' }}
          </button>
        </div>
        <p class="muted" *ngIf="lastCheckResult && !lastCheckResult.configured" style="margin-top: 10px;">
          ⚠ {{ lastCheckResult.message }}
        </p>
        <p class="muted" *ngIf="lastCheckResult && lastCheckResult.configured && lastCheckResult.message" style="margin-top: 10px;">
          ⚠ {{ lastCheckResult.message }}
        </p>
        <p class="muted" *ngIf="lastCheckResult && lastCheckResult.configured && !lastCheckResult.message" style="margin-top: 10px;">
          Checked at {{ lastCheckResult.checkedAt | date: 'short' }} - {{ lastCheckResult.newDocumentsFound }} new document(s) found.
        </p>

        <div class="pending-list" *ngIf="pendingDocs.length > 0">
          <h3 style="margin-top: 16px;">Discovered, not yet handled</h3>
          <div class="pending-item" *ngFor="let d of pendingDocs">
            <a [href]="d.url" target="_blank" rel="noopener">{{ d.fileName || d.url }}</a>
            <span class="muted">found {{ d.firstSeenAt | date: 'short' }}</span>
            <button (click)="dismiss(d)">Mark handled</button>
            <button class="btn-danger" (click)="deleteDoc(d)" title="Permanently remove - use for a wrong match, not a legitimately handled one">Delete</button>
          </div>
          <p class="muted" style="margin-top: 8px;">
            Click a link to download it yourself, then import it as usual on Version Comparison.
            "Mark handled" hides it here; "Delete" removes it entirely (it can reappear if found again on a later check).
          </p>
        </div>
      </div>

      <div class="card" style="margin-bottom: 16px;">
        <h2>Imported releases</h2>
        <table class="data-table">
          <thead><tr><th>Date</th><th>Source file</th><th>Sections</th><th>Flagged Mismatch</th><th>Imported</th></tr></thead>
          <tbody>
            <tr *ngFor="let r of releases">
              <td>{{ r.releaseDate }}</td>
              <td>{{ r.sourceFilename }}</td>
              <td>{{ r.sectionCount }}</td>
              <td [class.warn]="r.mismatchCount > 0">{{ r.mismatchCount }}</td>
              <td>{{ r.importedAt | date: 'short' }}</td>
            </tr>
            <tr *ngIf="releases.length === 0">
              <td colspan="5" class="muted">No releases imported yet.</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="card combobox-card">
        <label class="combo-label">Look up a material's history across every release</label>
        <div class="combobox">
          <input
            type="text"
            placeholder="Search by category, description, or code..."
            [(ngModel)]="search"
            (ngModelChange)="onSearch()"
            (focus)="dropdownOpen = true"
          />
          <div class="combo-list" *ngIf="dropdownOpen && options.length > 0">
            <div class="combo-item" *ngFor="let o of options" (click)="selectSection(o)">
              <span class="combo-code">{{ o.code }}</span>
              <span class="combo-label-text">{{ o.label }}</span>
            </div>
          </div>
        </div>
        <div class="backdrop" *ngIf="dropdownOpen" (click)="dropdownOpen = false"></div>
      </div>

      <div class="card" style="margin-top: 16px;" *ngIf="history">
        <h2>{{ history.code }} — history across {{ history.snapshots.length }} release(s)</h2>
        <div class="snapshot" *ngFor="let snap of history.snapshots">
          <div class="snapshot-header">
            <strong>{{ snap.releaseDate }}</strong>
            <span class="muted">{{ snap.categoryName }} — {{ snap.rawDescription }}</span>
            <span class="muted">{{ snap.extractedEntryCount }} of {{ snap.printedTotal }} shown</span>
            <span class="badge" [class.badge-rose]="snap.validationStatus === 'Mismatch'" [class.badge-emerald]="snap.validationStatus === 'Matched'">
              {{ snap.validationStatus }}
            </span>
          </div>
          <div class="snapshot-manufacturers">
            <span class="manu-chip" *ngFor="let m of snap.manufacturers">
              {{ m.supplierName }}
              <app-severity-badge [severity]="m.status" [statusMode]="true"></app-severity-badge>
            </span>
            <span class="muted" *ngIf="snap.manufacturers.length === 0">No manufacturers recorded.</span>
          </div>
        </div>
        <p class="muted" *ngIf="history.snapshots.length === 0">This code hasn't appeared in any imported release.</p>
      </div>
    </div>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 820px; margin-bottom: 16px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .warn { color: var(--hue-amber); font-weight: 600; }
      .row { display: flex; gap: 10px; flex-wrap: wrap; }
      .pending-list { display: flex; flex-direction: column; gap: 6px; }
      .pending-item { display: flex; align-items: center; gap: 12px; padding: 8px 0; border-top: 1px solid var(--border); font-size: 13px; }
      .pending-item a { flex: 1; }
      .btn-danger { background: var(--hue-rose-bg, rgba(190,40,64,0.12)); color: var(--hue-rose, #be2840); border: 1px solid var(--hue-rose-border, rgba(190,40,64,0.35)); }
      .btn-danger:hover { background: var(--hue-rose, #be2840); color: #fff; }
      .combobox-card { position: relative; overflow: visible; z-index: 30; }
      .combo-label { display: block; font-size: 11px; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 6px; }
      .combobox { position: relative; }
      .combobox input { width: 100%; }
      .combo-list {
        position: absolute; top: calc(100% + 6px); left: 0; right: 0; z-index: 20;
        background: rgba(19,28,46,0.96); backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px);
        border: 1px solid var(--border-strong); border-radius: var(--radius-sm);
        max-height: 360px; overflow-y: auto; box-shadow: var(--shadow);
      }
      .combo-item { display: flex; gap: 10px; align-items: baseline; padding: 9px 12px; cursor: pointer; font-size: 13px; }
      .combo-item:hover { background: rgba(255,255,255,0.06); }
      .combo-code { font-family: var(--font-display); font-weight: 600; color: var(--accent); flex-shrink: 0; }
      .backdrop { position: fixed; inset: 0; z-index: 10; }
      .snapshot { padding: 14px 0; border-top: 1px solid var(--border); }
      .snapshot:first-of-type { border-top: none; }
      .snapshot-header { display: flex; gap: 14px; align-items: center; flex-wrap: wrap; margin-bottom: 8px; }
      .snapshot-manufacturers { display: flex; gap: 8px; flex-wrap: wrap; }
      .manu-chip {
        display: inline-flex; align-items: center; gap: 6px; font-size: 12px;
        background: var(--surface-2); padding: 4px 10px; border-radius: 999px;
      }
    `,
  ],
})
export class AuditComponent implements OnInit {
  releases: AuditReleaseSummaryDto[] = [];
  pendingDocs: SecDocumentWatchDto[] = [];
  checking = false;
  lastCheckResult: SecMonitorCheckResultDto | null = null;

  search = '';
  options: AuditSectionOptionDto[] = [];
  dropdownOpen = false;
  history: AuditSectionHistoryDto | null = null;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getAuditReleases().subscribe((r) => (this.releases = r));
    this.loadPending();
  }

  private loadPending() {
    this.api.getPendingSecDocuments().subscribe((d) => (this.pendingDocs = d));
  }

  checkNow() {
    this.checking = true;
    this.api.checkForNewSecReleases().subscribe({
      next: (r) => { this.lastCheckResult = r; this.checking = false; this.loadPending(); },
      error: () => { this.checking = false; },
    });
  }

  dismiss(d: SecDocumentWatchDto) {
    this.api.dismissSecDocument(d.id).subscribe(() => this.loadPending());
  }

  deleteDoc(d: SecDocumentWatchDto) {
    if (!confirm(`Permanently remove "${d.fileName || d.url}"? This can't be undone.`)) return;
    this.api.deleteSecDocument(d.id).subscribe(() => this.loadPending());
  }

  onSearch() {
    this.dropdownOpen = true;
    this.api.getAuditSectionOptions(this.search || undefined).subscribe((o) => (this.options = o));
  }

  selectSection(o: AuditSectionOptionDto) {
    this.search = `${o.code} ${o.label}`;
    this.dropdownOpen = false;
    this.api.getAuditSectionHistory(o.code).subscribe((h) => (this.history = h));
  }
}
