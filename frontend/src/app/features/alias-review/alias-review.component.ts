import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { SupplierAliasReviewDto, SupplierOptionDto } from '../../core/models';

@Component({
  selector: 'app-alias-review',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <h1>Alias Review</h1>
      <p class="subtitle">
        Manufacturer names the system could not confidently match to an existing supplier, or
        matched only tentatively. Nothing here was auto-merged - confirm, reject, or create a new
        canonical supplier for each. Genuinely distinct entities that happen to share a brand name
        (e.g. two different regional subsidiaries) should be rejected, not merged.
      </p>

      <div class="card" style="margin-bottom: 16px;">
        <label class="reviewer-label">
          Reviewing as
          <input type="text" placeholder="Your name" [(ngModel)]="reviewedBy" style="width: 240px;" />
        </label>
      </div>

      <div class="card" *ngFor="let a of aliases" style="margin-bottom: 12px;">
        <div class="alias-row">
          <div class="alias-info">
            <div class="raw-name">{{ a.rawName }}</div>
            <div class="muted">
              {{ a.rawCountry || 'Unknown country' }} · first seen {{ a.firstSeenReleaseDate }} ·
              {{ a.entryCount }} qualification record(s)
            </div>
            <div class="muted" *ngIf="a.suggestedSupplierName">
              Suggested match: <strong>{{ a.suggestedSupplierName }}</strong>
              <span *ngIf="a.confidence"> ({{ (a.confidence * 100).toFixed(0) }}% confidence)</span>
            </div>
          </div>

          <div class="actions" *ngIf="!busy[a.aliasId]">
            <div class="search-row">
              <input
                type="text"
                placeholder="Search suppliers to link..."
                [(ngModel)]="searchTerms[a.aliasId]"
                (ngModelChange)="onSearch(a.aliasId, $event)"
              />
              <div class="search-results" *ngIf="searchResults[a.aliasId]?.length">
                <div
                  class="search-result"
                  *ngFor="let s of searchResults[a.aliasId]"
                  (click)="confirm(a, s.id)"
                >
                  {{ s.canonicalName }} <span class="muted" *ngIf="s.canonicalCountry">({{ s.canonicalCountry }})</span>
                </div>
              </div>
            </div>
            <button *ngIf="a.suggestedSupplierId as sid" (click)="confirm(a, sid)">
              Confirm suggested match
            </button>
            <button (click)="reject(a)">Reject</button>
            <button (click)="startNewSupplier(a)">New supplier…</button>
          </div>

          <div class="new-supplier-form" *ngIf="creatingFor === a.aliasId">
            <input type="text" placeholder="Canonical name" [(ngModel)]="newSupplierName" />
            <input type="text" placeholder="Country (optional)" [(ngModel)]="newSupplierCountry" />
            <button class="primary" (click)="createSupplier(a)">Create & confirm</button>
            <button (click)="creatingFor = null">Cancel</button>
          </div>
        </div>
      </div>

      <div class="card" *ngIf="aliases.length === 0">
        <p class="muted">Nothing needs review right now.</p>
      </div>
    </div>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 800px; margin-bottom: 20px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .reviewer-label { display: flex; align-items: center; gap: 10px; font-size: 12px; color: var(--text-dim); }
      .alias-row { display: flex; justify-content: space-between; align-items: flex-start; gap: 20px; flex-wrap: wrap; }
      .alias-info { min-width: 260px; }
      .raw-name { font-size: 15px; font-weight: 600; font-family: var(--font-display); }
      .actions { display: flex; align-items: flex-start; gap: 8px; flex-wrap: wrap; position: relative; }
      .search-row { position: relative; }
      .search-row input { width: 220px; }
      .search-results {
        position: absolute; top: calc(100% + 4px); left: 0; width: 260px; z-index: 5;
        background: rgba(19,28,46,0.95); backdrop-filter: blur(16px); border: 1px solid var(--border);
        border-radius: var(--radius-sm); max-height: 220px; overflow-y: auto; box-shadow: var(--shadow);
      }
      .search-result { padding: 8px 12px; font-size: 13px; cursor: pointer; }
      .search-result:hover { background: rgba(255,255,255,0.06); }
      .new-supplier-form { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; width: 100%; margin-top: 8px; }
      .new-supplier-form input { width: 200px; }
    `,
  ],
})
export class AliasReviewComponent implements OnInit {
  aliases: SupplierAliasReviewDto[] = [];
  reviewedBy = '';
  busy: Record<number, boolean> = {};
  searchTerms: Record<number, string> = {};
  searchResults: Record<number, SupplierOptionDto[]> = {};
  creatingFor: number | null = null;
  newSupplierName = '';
  newSupplierCountry = '';

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.load();
  }

  private load() {
    this.api.getAliasesNeedingReview().subscribe((a) => (this.aliases = a));
  }

  onSearch(aliasId: number, term: string) {
    if (!term || term.length < 2) {
      this.searchResults[aliasId] = [];
      return;
    }
    this.api.searchSuppliers(term).subscribe((results) => (this.searchResults[aliasId] = results));
  }

  confirm(alias: SupplierAliasReviewDto, supplierId: number) {
    const reviewer = this.reviewedBy.trim() || 'unspecified';
    this.busy[alias.aliasId] = true;
    this.api.confirmAlias(alias.aliasId, supplierId, reviewer).subscribe(() => this.load());
  }

  reject(alias: SupplierAliasReviewDto) {
    const reviewer = this.reviewedBy.trim() || 'unspecified';
    this.busy[alias.aliasId] = true;
    this.api.rejectAlias(alias.aliasId, reviewer).subscribe(() => this.load());
  }

  startNewSupplier(alias: SupplierAliasReviewDto) {
    this.creatingFor = alias.aliasId;
    this.newSupplierName = alias.rawName;
    this.newSupplierCountry = alias.rawCountry ?? '';
  }

  createSupplier(alias: SupplierAliasReviewDto) {
    const reviewer = this.reviewedBy.trim() || 'unspecified';
    this.api
      .createSupplierForAlias(alias.aliasId, this.newSupplierName.trim(), this.newSupplierCountry.trim() || null, reviewer)
      .subscribe(() => {
        this.creatingFor = null;
        this.load();
      });
  }
}
