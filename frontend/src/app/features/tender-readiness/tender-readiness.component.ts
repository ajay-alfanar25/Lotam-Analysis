import { Component, ElementRef, HostListener, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import {
  MaterialSectionDetailDto, MaterialSectionEntryDto, MaterialSectionOptionDto,
  PressReleaseSearchResponseDto,
} from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

const READY_STATUSES = ['Qualified', 'Reactivated'];

@Component({
  selector: 'app-tender-readiness',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent],
  template: `
    <div class="page" *ngIf="selection.hasSelection(); else noSelection">
      <h1>Tender Readiness</h1>
      <p class="subtitle">
        What are you bidding on? Pick the exact material, and get a direct answer: who can you
        safely name today, and who needs a second look before you commit to them in a proposal.
        This reads the exact same data as Material Explorer for the current release
        ({{ selection.currentRelease()?.releaseDate }}). To change which release pair this compares
        against, use the picker on Dashboard or Change Explorer - it's shared across the app.
      </p>

      <div class="card combobox-card" #comboboxCard>
        <label class="combo-label">What material are you bidding on?</label>
        <div class="combobox">
          <input
            type="text"
            placeholder="Search by category, description, or standard (as of the current release)..."
            [(ngModel)]="searchTerm"
            (focus)="dropdownOpen = true"
            (input)="dropdownOpen = true"
          />
          <div class="combo-list" *ngIf="dropdownOpen && filteredOptions().length > 0">
            <div class="combo-item" *ngFor="let o of filteredOptions()" (click)="selectMaterial(o)">
              <span class="combo-code">{{ o.code }}</span>
              <span class="combo-label-text">{{ o.label }}</span>
            </div>
          </div>
        </div>
      </div>

      <ng-container *ngIf="currentDetail">
        <div class="card" style="margin-top: 16px;">
          <h2>{{ currentDetail.code }} — {{ currentDetail.categoryName }} {{ currentDetail.rawDescription }}</h2>
          <p class="muted" *ngIf="currentDetail.applicableStandard">Applicable Standards: {{ currentDetail.applicableStandard }}</p>
        </div>

        <div class="card ready-card" style="margin-top: 16px;">
          <h2>✅ Ready to name in a bid</h2>
          <p class="muted">Currently Qualified (or Reactivated), no recent change to double-check.</p>
          <table class="data-table" *ngIf="readyEntries().length > 0">
            <thead><tr><th>Manufacturer</th><th>Local Representative</th><th>Status</th></tr></thead>
            <tbody>
              <tr *ngFor="let e of readyEntries()">
                <td class="raw-block">{{ firstLine(e.rawManufacturerBlock) }}</td>
                <td class="raw-block">{{ e.localRepresentativeRaw || '— (contact manufacturer directly)' }}</td>
                <td>
                  <app-severity-badge [severity]="e.status" [statusMode]="true"></app-severity-badge>
                  <span class="badge badge-emerald" *ngIf="e.badge === 'New'" style="margin-left: 6px;">NEW</span>
                </td>
              </tr>
            </tbody>
          </table>
          <p class="muted" *ngIf="readyEntries().length === 0">No suppliers are ready without review for this material right now.</p>
        </div>

        <div class="card review-card" style="margin-top: 16px;">
          <h2>⚠ Review before bidding</h2>
          <p class="muted">Recently changed, or not currently in an unconditional Qualified status.</p>
          <table class="data-table" *ngIf="reviewEntries().length > 0">
            <thead><tr><th>Manufacturer</th><th>Status</th><th>Why review it</th></tr></thead>
            <tbody>
              <tr *ngFor="let e of reviewEntries()">
                <td class="raw-block">{{ firstLine(e.rawManufacturerBlock) }}</td>
                <td><app-severity-badge [severity]="e.status" [statusMode]="true"></app-severity-badge></td>
                <td class="muted">
                  <span *ngIf="e.badge === 'Updated'">Something about this qualification changed in the latest release - check the details before relying on it.</span>
                  <span *ngIf="e.badge !== 'Updated'">Not currently in an unconditional Qualified status.</span>
                </td>
              </tr>
            </tbody>
          </table>
          <p class="muted" *ngIf="reviewEntries().length === 0">Nothing flagged for review.</p>
        </div>

        <div class="card removed-card" style="margin-top: 16px;" *ngIf="removedEntries().length > 0">
          <h2>🚫 No longer qualified</h2>
          <p class="muted">
            Was listed for this material in the previous release ({{ selection.previousRelease()?.releaseDate }})
            but is not in the current one - do not name these in a new bid without confirming directly with SEC.
          </p>
          <table class="data-table">
            <thead><tr><th>Manufacturer</th><th>Previous status</th></tr></thead>
            <tbody>
              <tr *ngFor="let e of removedEntries()">
                <td class="raw-block">{{ firstLine(e.rawManufacturerBlock) }}</td>
                <td><app-severity-badge [severity]="e.status" [statusMode]="true"></app-severity-badge></td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="card press-card" style="margin-top: 16px;">
          <h2>🔎 Recent press mentions</h2>
          <p class="muted warn-text">
            ⚠ Unofficial and not from the LOTAM document - a real-time web search for recent SEC/National
            Grid SA announcements about this material, so you don't miss a supplier that got approval
            after the current LOTAM release was published. <strong>Never include a supplier in a bid based
            on this alone</strong> - always confirm officially before relying on it.
          </p>

          <div *ngIf="pressLoading" class="muted">Searching...</div>

          <div *ngIf="!pressLoading && pressResult && !pressResult.configured" class="press-empty">
            {{ pressResult.message }}
          </div>

          <div *ngIf="!pressLoading && pressResult && pressResult.configured && pressResult.message" class="press-empty">
            {{ pressResult.message }}
          </div>

          <div *ngIf="!pressLoading && pressResult && pressResult.configured && !pressResult.message && pressResult.hits.length === 0" class="press-empty">
            No recent press mentions found for this material.
          </div>

          <div class="press-hits" *ngIf="!pressLoading && pressResult && pressResult.hits.length > 0">
            <a class="press-hit" *ngFor="let hit of pressResult.hits" [href]="hit.url" target="_blank" rel="noopener">
              <div class="press-hit-title">{{ hit.title }}</div>
              <div class="press-hit-snippet">{{ hit.snippet }}</div>
              <div class="press-hit-url">{{ hit.url }}</div>
            </a>
          </div>
        </div>
      </ng-container>
    </div>

    <ng-template #noSelection>
      <div class="page">
        <h1>Tender Readiness</h1>
        <div class="card">Select a release pair from the top bar first.</div>
      </div>
    </ng-template>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 820px; margin-bottom: 16px; }
      .muted { color: var(--text-dim); font-size: 12px; }
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
      .combo-label-text { flex: 1; color: var(--text); }
      .ready-card { box-shadow: inset 0 0 0 1px var(--hue-emerald-border); }
      .review-card { box-shadow: inset 0 0 0 1px var(--hue-amber-border); }
      .removed-card { box-shadow: inset 0 0 0 1px var(--hue-rose-border); }
      .raw-block { white-space: pre-line; }
      .raw-block { max-width: 320px; }
      .press-card { box-shadow: inset 0 0 0 1px var(--hue-indigo-border); background: var(--hue-indigo-bg); }
      .warn-text { color: var(--hue-amber); }
      .press-empty { color: var(--text-dim); font-size: 13px; padding: 10px 0; }
      .press-hits { display: flex; flex-direction: column; gap: 10px; margin-top: 8px; }
      .press-hit {
        display: block; padding: 12px 14px; border-radius: var(--radius-sm);
        background: rgba(255,255,255,0.04); border: 1px solid var(--border);
        text-decoration: none; color: var(--text); transition: border-color 150ms ease, background 150ms ease;
      }
      .press-hit:hover { border-color: var(--hue-indigo-border); background: rgba(255,255,255,0.07); }
      .press-hit-title { font-weight: 600; font-size: 13px; color: var(--hue-indigo); margin-bottom: 4px; }
      .press-hit-snippet { font-size: 12.5px; color: var(--text-dim); line-height: 1.5; margin-bottom: 6px; }
      .press-hit-url { font-size: 11px; color: var(--text-dim); opacity: 0.7; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    `,
  ],
})
export class TenderReadinessComponent implements OnInit {
  @ViewChild('comboboxCard') comboboxCardRef?: ElementRef<HTMLElement>;

  options: MaterialSectionOptionDto[] = [];
  previousOptions: MaterialSectionOptionDto[] = [];
  searchTerm = '';
  dropdownOpen = false;

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.dropdownOpen && this.comboboxCardRef && !this.comboboxCardRef.nativeElement.contains(event.target as Node)) {
      this.dropdownOpen = false;
    }
  }

  currentDetail: MaterialSectionDetailDto | null = null;
  previousDetail: MaterialSectionDetailDto | null = null;

  pressResult: PressReleaseSearchResponseDto | null = null;
  pressLoading = false;

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
  ) {}

  ngOnInit(): void {
    const curr = this.selection.currentReleaseId();
    const prev = this.selection.previousReleaseId();
    if (curr !== null) this.api.getMaterialSectionsForRelease(curr).subscribe((o) => (this.options = o));
    if (prev !== null) this.api.getMaterialSectionsForRelease(prev).subscribe((o) => (this.previousOptions = o));
  }

  filteredOptions(): MaterialSectionOptionDto[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) return this.options.slice(0, 40);
    return this.options
      .filter((o) => o.label.toLowerCase().includes(term) || o.code.toLowerCase().includes(term))
      .slice(0, 40);
  }

  selectMaterial(option: MaterialSectionOptionDto) {
    this.searchTerm = `${option.code} ${option.label}`;
    this.dropdownOpen = false;
    const prev = this.selection.previousReleaseId() ?? undefined;
    const curr = this.selection.currentReleaseId() ?? undefined;

    this.api.getMaterialSectionDetail(option.materialSectionId, prev, curr).subscribe((d) => (this.currentDetail = d));

    this.pressLoading = true;
    this.pressResult = null;
    this.api.searchPressReleases(option.materialSectionId).subscribe({
      next: (r) => { this.pressResult = r; this.pressLoading = false; },
      error: () => { this.pressLoading = false; },
    });

    // Find the SAME material (by code) in the previous release, so we can
    // show anyone who dropped off entirely - only meaningful if that exact
    // code existed there too (a renumbered code needs a confirmed remap to
    // line up, same rule as everywhere else in this app).
    const previousMatch = this.previousOptions.find((o) => o.code === option.code);
    if (previousMatch) {
      this.api.getMaterialSectionDetail(previousMatch.materialSectionId, prev, curr)
        .subscribe((d) => (this.previousDetail = d));
    } else {
      this.previousDetail = null;
    }
  }

  readyEntries(): MaterialSectionEntryDto[] {
    if (!this.currentDetail) return [];
    return this.currentDetail.entries.filter(
      (e) => READY_STATUSES.includes(e.status) && e.badge !== 'Updated',
    );
  }

  reviewEntries(): MaterialSectionEntryDto[] {
    if (!this.currentDetail) return [];
    return this.currentDetail.entries.filter(
      (e) => e.badge === 'Updated' || !READY_STATUSES.includes(e.status),
    );
  }

  removedEntries(): MaterialSectionEntryDto[] {
    if (!this.previousDetail) return [];
    return this.previousDetail.entries.filter((e) => e.badge === 'Removed');
  }

  firstLine(block: string): string {
    return block.split('\n')[0]?.trim() ?? block;
  }
}
