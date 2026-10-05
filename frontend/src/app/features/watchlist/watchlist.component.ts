import { Component, OnInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import { ChangeRecordDto, SupplierDto, WatchlistItemDto } from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

@Component({
  selector: 'app-watchlist',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent],
  template: `
    <div class="page">
      <h1>My Suppliers</h1>
      <p class="subtitle">
        A shared watchlist for suppliers your team currently relies on. There's no login in this
        build, so this list is shared across everyone using the app rather than per-person - the
        "Added by" field is just attribution, not an account. To change which release pair the
        "changes affecting your watchlist" section below compares, use the picker on Dashboard or
        Change Explorer - it's shared across the app.
      </p>

      <div class="grid" style="grid-template-columns: repeat(2, 1fr); margin-bottom: 20px;">
        <div class="card kpi-plain">
          <h3>Watchlisted Suppliers</h3>
          <div class="big-num">{{ watchlist.length }}</div>
        </div>
        <div class="card kpi-plain" [class.alert]="watchlistChanges.length > 0">
          <h3>Changed In This Release Pair</h3>
          <div class="big-num">{{ watchlistChanges.length }} of {{ watchlist.length }}</div>
        </div>
      </div>

      <div class="card" style="margin-bottom: 16px;">
        <h2>Add a supplier</h2>
        <div class="row">
          <input type="text" placeholder="Search suppliers by name..." [(ngModel)]="search" (ngModelChange)="onSearch()" />
          <input type="text" placeholder="Your name (for attribution)" [(ngModel)]="addedBy" style="width: 200px;" />
        </div>
        <div class="search-results" *ngIf="searchResults.length > 0">
          <div class="search-result" *ngFor="let s of searchResults" (click)="add(s)">
            {{ s.canonicalName }} <span class="muted" *ngIf="s.canonicalCountry">({{ s.canonicalCountry }})</span>
            <span class="muted" *ngIf="isWatched(s.id)"> · already on list</span>
          </div>
        </div>
      </div>

      <div class="card" style="margin-bottom: 16px;" *ngIf="watchlistChanges.length > 0">
        <h2>⚠ Changes affecting your watchlist</h2>
        <p class="muted">
          Comparing {{ selection.previousRelease()?.releaseDate }} → {{ selection.currentRelease()?.releaseDate }}.
          Same change_record data as Change Explorer, filtered to just your watchlisted suppliers.
        </p>
        <table class="data-table">
          <thead><tr><th>Supplier</th><th>Material</th><th>Change</th><th>Severity</th></tr></thead>
          <tbody>
            <tr *ngFor="let c of watchlistChanges">
              <td>{{ c.supplierName }}</td>
              <td>{{ c.materialCode }} — {{ c.materialDescription }}</td>
              <td>{{ c.changeType }}</td>
              <td><app-severity-badge [severity]="c.severity"></app-severity-badge></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="card">
        <h2>Watchlist</h2>
        <table class="data-table">
          <thead><tr><th>Supplier</th><th>Added by</th><th>Notes</th><th>Added</th><th></th></tr></thead>
          <tbody>
            <tr *ngFor="let w of watchlist">
              <td>{{ w.canonicalName }} <span class="muted" *ngIf="w.canonicalCountry">({{ w.canonicalCountry }})</span></td>
              <td>{{ w.addedBy }}</td>
              <td>{{ w.notes || '—' }}</td>
              <td>{{ w.addedAt | date: 'short' }}</td>
              <td><button (click)="remove(w)">Remove</button></td>
            </tr>
            <tr *ngIf="watchlist.length === 0">
              <td colspan="5" class="muted">No suppliers watchlisted yet - search above to add one.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 800px; margin-bottom: 16px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .kpi-plain { display: flex; flex-direction: column; gap: 4px; }
      .kpi-plain.alert { box-shadow: inset 0 0 0 1px var(--hue-amber-border); }
      .big-num { font-size: 32px; font-weight: 700; font-family: var(--font-display); }
      .row { display: flex; gap: 10px; flex-wrap: wrap; }
      .row input { flex: 1; min-width: 200px; }
      .search-results { margin-top: 10px; display: flex; flex-direction: column; gap: 2px; max-height: 220px; overflow-y: auto; }
      .search-result { padding: 8px 12px; font-size: 13px; cursor: pointer; border-radius: 8px; }
      .search-result:hover { background: rgba(255,255,255,0.06); }
    `,
  ],
})
export class WatchlistComponent implements OnInit {
  watchlist: WatchlistItemDto[] = [];
  watchlistChanges: ChangeRecordDto[] = [];
  search = '';
  addedBy = '';
  searchResults: SupplierDto[] = [];

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
  ) {
    effect(() => {
      const prev = this.selection.previousReleaseId();
      const curr = this.selection.currentReleaseId();
      if (prev !== null && curr !== null) this.loadChanges(prev, curr);
    });
  }

  ngOnInit(): void {
    this.loadWatchlist();
  }

  private loadWatchlist() {
    this.api.getWatchlist().subscribe((w) => (this.watchlist = w));
  }

  private loadChanges(prev: number, curr: number) {
    this.api.getWatchlistChanges(prev, curr).subscribe((c) => (this.watchlistChanges = c));
  }

  onSearch() {
    if (!this.search || this.search.length < 2) {
      this.searchResults = [];
      return;
    }
    this.api.getSuppliers(this.search).subscribe((s) => (this.searchResults = s));
  }

  isWatched(supplierId: number): boolean {
    return this.watchlist.some((w) => w.supplierId === supplierId);
  }

  add(s: SupplierDto) {
    if (this.isWatched(s.id)) return;
    this.api.addToWatchlist(s.id, this.addedBy.trim() || 'unspecified').subscribe(() => {
      this.loadWatchlist();
      const prev = this.selection.previousReleaseId();
      const curr = this.selection.currentReleaseId();
      if (prev !== null && curr !== null) this.loadChanges(prev, curr);
    });
  }

  remove(w: WatchlistItemDto) {
    this.api.removeFromWatchlist(w.watchlistId).subscribe(() => this.loadWatchlist());
  }
}
