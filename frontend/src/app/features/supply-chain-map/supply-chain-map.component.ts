import { Component, ElementRef, HostListener, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import {
  GeoConcentrationDto, GeoConcentrationSummaryDto, GeoConcentrationSupplierDto, LotamEntryDetailDto,
} from '../../core/models';
import { coordsForCountry } from '../../core/geo';
import { WorldMapComponent, MapMarker } from '../../shared/world-map/world-map.component';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

@Component({
  selector: 'app-supply-chain-map',
  standalone: true,
  imports: [CommonModule, FormsModule, WorldMapComponent, SeverityBadgeComponent],
  template: `
    <div class="page">
      <h1>Supply Chain Map</h1>
      <p class="subtitle">
        Distinct qualified suppliers by country, for the latest release ({{ summary?.releaseDate }}).
        Badge number is supplier count. This reads the same manufacturer-country and technology data
        as Material Explorer and Change Explorer - nothing here is a separate estimate.
      </p>

      <div class="card combobox-card" #comboboxCard>
        <label class="combo-label">Material group / technology</label>
        <div class="combobox">
          <input
            type="text"
            placeholder="Search technologies, or leave blank to see everything..."
            [(ngModel)]="techSearchTerm"
            (focus)="dropdownOpen = true"
            (input)="dropdownOpen = true"
          />
          <div class="combo-list" *ngIf="dropdownOpen">
            <div class="combo-item" (click)="selectTech(null)">
              <span class="combo-label-text"><strong>All technologies</strong></span>
            </div>
            <div class="combo-item" *ngFor="let t of filteredTechOptions()" (click)="selectTech(t)">
              <span class="combo-label-text">{{ t }}</span>
            </div>
          </div>
        </div>
        <p class="muted" *ngIf="summary && summary.unmappedEntryCount > 0" style="margin-top: 10px;">
          ⚠ {{ summary.unmappedEntryCount }} entries in the latest release have no extracted
          manufacturer country and are excluded from this map - not silently ignored, just not
          guessable. See Material Explorer for those specific records.
        </p>
      </div>

      <div class="grid two-col">
        <div class="card map-card">
          <h2>Map</h2>
          <app-world-map [markers]="markers" (countrySelected)="onCountrySelected($event)"></app-world-map>
        </div>
        <div class="card rank-card">
          <h2>Ranked by supplier count</h2>
          <div class="rank-scroll">
            <table class="data-table">
              <thead><tr><th>Country</th><th>Suppliers</th></tr></thead>
              <tbody>
                <tr *ngFor="let c of countryTotals()" (click)="onCountrySelected(c.country)" [class.active]="c.country === selectedCountry">
                  <td>{{ c.country }}</td>
                  <td>{{ c.count }}</td>
                </tr>
                <tr *ngIf="countryTotals().length === 0">
                  <td colspan="2" class="muted">No data for this selection.</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div class="card detail-card" *ngIf="selectedCountry" style="margin-top: 16px;">
        <h2>{{ selectedCountry }} — suppliers{{ selectedTech ? ' (' + selectedTech + ')' : '' }}</h2>
        <div *ngIf="detailLoading" class="muted">Loading...</div>
        <table class="data-table" *ngIf="!detailLoading && countryDetails.length > 0">
          <thead><tr><th>Supplier</th><th>Local Representative</th><th>Material</th><th>Status</th></tr></thead>
          <tbody>
            <tr *ngFor="let s of countryDetails" (click)="openEntry(s)">
              <td>{{ s.supplierName }}</td>
              <td>{{ s.localRepresentative || '—' }}</td>
              <td>{{ s.materialCode }} — {{ s.materialDescription }}</td>
              <td><app-severity-badge [severity]="s.status" [statusMode]="true"></app-severity-badge></td>
            </tr>
          </tbody>
        </table>
        <p class="muted" *ngIf="!detailLoading && countryDetails.length === 0">No suppliers found for this selection.</p>
      </div>
    </div>

    <div class="glass-overlay" *ngIf="entryDetail" (click)="entryDetail = null">
      <div class="glass-panel" (click)="$event.stopPropagation()">
        <button class="close" (click)="entryDetail = null">✕</button>
        <h2>{{ entryDetail!.supplierCanonicalName }}</h2>
        <p class="muted">{{ entryDetail!.materialCode }} — {{ entryDetail!.materialDescription }}</p>
        <div class="row">
          <app-severity-badge [severity]="entryDetail!.status" [statusMode]="true"></app-severity-badge>
          <span class="pill" style="background: rgba(255,255,255,0.06); color: var(--text-dim);">
            {{ entryDetail!.releaseDate }} · {{ entryDetail!.sourceFilename }}
            <ng-container *ngIf="entryDetail!.pageNumber as p">, page {{ p }}</ng-container>
          </span>
        </div>

        <h3 style="margin-top: 20px;">Manufacturer (raw, as printed)</h3>
        <div class="value-box">{{ entryDetail!.rawManufacturerBlock }}</div>

        <div class="two-col-detail">
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
      .subtitle { color: var(--text-dim); max-width: 800px; margin-bottom: 16px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .combobox-card { position: relative; overflow: visible; z-index: 30; }
      .combo-label { display: block; font-size: 11px; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 6px; }
      .combobox { position: relative; }
      .combobox input { width: 100%; }
      .combo-list {
        position: absolute; top: calc(100% + 6px); left: 0; right: 0; z-index: 20;
        background: rgba(19,28,46,0.96); backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px);
        border: 1px solid var(--border-strong); border-radius: var(--radius-sm);
        max-height: 300px; overflow-y: auto; box-shadow: var(--shadow);
      }
      .combo-item { padding: 9px 12px; cursor: pointer; font-size: 13px; }
      .combo-item:hover { background: rgba(255,255,255,0.06); }
      .combo-label-text { color: var(--text); }

      /* Explicit, equal, fixed height on both sides - the earlier
         flex-stretch approach had nothing concrete to stretch against
         (neither card had a real height to share), so the list just grew
         to fit its own content instead of matching the map. */
      .two-col { grid-template-columns: 1fr 1fr; align-items: stretch; }
      .map-card, .rank-card { display: flex; flex-direction: column; height: 560px; }
      .rank-scroll { overflow-y: auto; flex: 1; min-height: 0; }
      table.data-table tbody tr { cursor: pointer; }
      table.data-table tbody tr.active { background: rgba(255,255,255,0.07); }
      .detail-card { box-shadow: inset 0 0 0 1px var(--hue-indigo-border); }

      .glass-overlay { position: fixed; inset: 0; background: rgba(5,8,16,0.55); backdrop-filter: blur(4px); display: flex; justify-content: flex-end; z-index: 50; }
      .glass-panel {
        width: 520px; max-width: 92vw; overflow-y: auto; position: relative; padding: 28px;
        background: rgba(19, 27, 48, 0.88); backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px);
        border-left: 1px solid rgba(255,255,255,0.08);
      }
      .close { position: absolute; top: 20px; right: 20px; }
      .row { display: flex; gap: 8px; margin-top: 8px; flex-wrap: wrap; align-items: center; }
      .two-col-detail { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 16px; }
      .value-box {
        background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08);
        border-radius: 10px; padding: 10px 12px; font-size: 13px; margin-top: 6px; white-space: pre-wrap;
      }
    `,
  ],
})
export class SupplyChainMapComponent implements OnInit {
  @ViewChild('comboboxCard') comboboxCardRef?: ElementRef<HTMLElement>;

  summary: GeoConcentrationSummaryDto | null = null;
  selectedTech: string | null = null;
  techSearchTerm = '';
  dropdownOpen = false;
  markers: MapMarker[] = [];

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.dropdownOpen && this.comboboxCardRef && !this.comboboxCardRef.nativeElement.contains(event.target as Node)) {
      this.dropdownOpen = false;
    }
  }

  selectedCountry: string | null = null;
  countryDetails: GeoConcentrationSupplierDto[] = [];
  detailLoading = false;
  entryDetail: LotamEntryDetailDto | null = null;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.getGeoConcentration().subscribe((s) => {
      this.summary = s;
      this.rebuild();
    });
  }

  technologyOptions(): string[] {
    if (!this.summary) return [];
    return [...new Set(this.summary.rows.map((r) => r.technologyName))].sort();
  }

  filteredTechOptions(): string[] {
    const term = this.techSearchTerm.trim().toLowerCase();
    const all = this.technologyOptions();
    return term ? all.filter((t) => t.toLowerCase().includes(term)) : all;
  }

  selectTech(tech: string | null) {
    this.selectedTech = tech;
    this.techSearchTerm = tech ?? '';
    this.dropdownOpen = false;
    this.rebuild();
    if (this.selectedCountry) this.loadCountryDetails(this.selectedCountry);
  }

  private filteredRows(): GeoConcentrationDto[] {
    if (!this.summary) return [];
    return this.selectedTech
      ? this.summary.rows.filter((r) => r.technologyName === this.selectedTech)
      : this.summary.rows;
  }

  countryTotals(): { country: string; count: number }[] {
    const totals = new Map<string, number>();
    for (const row of this.filteredRows()) {
      totals.set(row.country, (totals.get(row.country) ?? 0) + row.supplierCount);
    }
    return [...totals.entries()]
      .map(([country, count]) => ({ country, count }))
      .sort((a, b) => b.count - a.count);
  }

  rebuild(): void {
    this.markers = this.countryTotals()
      .map((c) => {
        const coords = coordsForCountry(c.country);
        if (!coords) return null;
        return {
          country: c.country,
          lat: coords.lat,
          lon: coords.lon,
          value: c.count,
          color: paletteColorForKeyRaw(c.country),
          label: `${c.country}: ${c.count} supplier${c.count === 1 ? '' : 's'}`,
        } as MapMarker;
      })
      .filter((m): m is MapMarker => m !== null);
  }

  onCountrySelected(country: string) {
    this.selectedCountry = country;
    this.loadCountryDetails(country);
  }

  private loadCountryDetails(country: string) {
    this.detailLoading = true;
    this.api.getGeoConcentrationDetails(country, this.selectedTech).subscribe({
      next: (d) => { this.countryDetails = d; this.detailLoading = false; },
      error: () => { this.detailLoading = false; },
    });
  }

  openEntry(s: GeoConcentrationSupplierDto) {
    this.api.getLotamEntryDetail(s.entryId).subscribe((d) => (this.entryDetail = d));
  }
}

// paletteColorForKey returns a CSS var() reference, which Leaflet's color
// options can't resolve directly - resolve to a concrete hex value here.
const HEX_PALETTE = ['#5b8cff', '#a25bff', '#2dd4bf', '#f472b6', '#ffb84f', '#34d399', '#ff5c72', '#818cf8', '#22d3ee'];
function paletteColorForKeyRaw(key: string): string {
  let hash = 0;
  for (let i = 0; i < key.length; i++) {
    hash = (hash << 5) - hash + key.charCodeAt(i);
    hash |= 0;
  }
  return HEX_PALETTE[Math.abs(hash) % HEX_PALETTE.length];
}
