import { Component, OnInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import { DashboardSummaryDto } from '../../core/models';
import { KpiCardComponent } from '../../shared/kpi-card/kpi-card.component';
import { BarChartComponent, BarDatum } from '../../shared/bar-chart/bar-chart.component';
import { DonutChartComponent, DonutDatum } from '../../shared/donut-chart/donut-chart.component';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, KpiCardComponent, BarChartComponent, DonutChartComponent, SeverityBadgeComponent],
  template: `
    <div class="page" *ngIf="selection.hasSelection(); else noSelection">
      <h1>Dashboard</h1>
      <p class="subtitle" *ngIf="summary">
        Comparing <strong>{{ summary.previousRelease?.releaseDate }}</strong> →
        <strong>{{ summary.currentRelease?.releaseDate }}</strong>
      </p>

      <ng-container *ngIf="summary">
        <div class="card watchlist-callout" *ngIf="watchlistTotal > 0" [class.hot]="watchlistChangedCount > 0">
          <span>
            <strong>{{ watchlistChangedCount }} of your {{ watchlistTotal }}</strong> watchlisted suppliers changed
            in this release pair.
          </span>
          <a routerLink="/watchlist">Review on My Suppliers →</a>
        </div>

        <div class="grid" style="grid-template-columns: repeat(6, 1fr); margin: 20px 0;">
          <app-kpi-card label="Records (current)" [value]="summary.totalRecordsCurrent" hue="indigo"></app-kpi-card>
          <app-kpi-card label="Total Changes" [value]="summary.totalChanges" hue="cyan"></app-kpi-card>
          <app-kpi-card label="Removed / Disqualified" sublabel="Critical" [value]="summary.critical" accentColor="var(--critical)"></app-kpi-card>
          <app-kpi-card label="Downgraded" sublabel="High" [value]="summary.high" accentColor="var(--high)"></app-kpi-card>
          <app-kpi-card label="New / Expanded" sublabel="Medium" [value]="summary.medium" accentColor="var(--medium)"></app-kpi-card>
          <app-kpi-card label="Minor Changes" sublabel="Low" [value]="summary.low" accentColor="var(--low)"></app-kpi-card>
        </div>

        <div class="grid" style="grid-template-columns: 1fr 1fr; margin-bottom: 20px;">
          <div class="card">
            <h2>Changes by Severity</h2>
            <app-donut-chart [data]="severityDonutData()"></app-donut-chart>
          </div>
          <div class="card">
            <h2>Changes by Technology</h2>
            <app-bar-chart [data]="technologyBarData()"></app-bar-chart>
          </div>
        </div>

        <div class="card" style="margin-bottom: 20px;">
          <h2>Change Types</h2>
          <app-bar-chart [data]="changeTypeBarData()"></app-bar-chart>
        </div>

        <div class="card">
          <h2>Recent Important Changes</h2>
          <table class="data-table">
            <thead>
              <tr>
                <th>Supplier</th>
                <th>Material</th>
                <th>Change</th>
                <th>Severity</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let c of summary.recentImportantChanges" [routerLink]="['/changes']">
                <td>{{ c.supplierName }}</td>
                <td>{{ c.materialCode }} — {{ c.materialDescription }}</td>
                <td>{{ c.changeType }}</td>
                <td><app-severity-badge [severity]="c.severity"></app-severity-badge></td>
                <td><a routerLink="/changes">View →</a></td>
              </tr>
              <tr *ngIf="summary.recentImportantChanges.length === 0">
                <td colspan="5" style="color: var(--text-dim);">
                  No critical/high changes for this release pair. Run the comparison on the
                  <a routerLink="/compare">Version Comparison</a> page if you haven't yet.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </ng-container>
    </div>

    <ng-template #noSelection>
      <div class="page">
        <h1>Dashboard</h1>
        <div class="card">
          No LOTAM releases imported yet. Go to
          <a routerLink="/compare">Version Comparison</a> to import the two supplied PDFs and run the comparison.
        </div>
      </div>
    </ng-template>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); margin-bottom: 8px; }
      .watchlist-callout {
        display: flex; justify-content: space-between; align-items: center;
        margin-bottom: 16px; padding: 14px 18px; font-size: 13px;
      }
      .watchlist-callout.hot { box-shadow: inset 0 0 0 1px var(--hue-amber-border); }
    `,
  ],
})
export class DashboardComponent implements OnInit {
  watchlistTotal = 0;
  watchlistChangedCount = 0;
  summary: DashboardSummaryDto | null = null;

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
  ) {
    effect(() => {
      const prev = this.selection.previousReleaseId();
      const curr = this.selection.currentReleaseId();
      if (prev !== null && curr !== null) {
        this.load(prev, curr);
        this.loadWatchlistCallout(prev, curr);
      }
    });
  }

  ngOnInit(): void {}

  private loadWatchlistCallout(previousReleaseId: number, currentReleaseId: number) {
    this.api.getWatchlist().subscribe((w) => (this.watchlistTotal = w.length));
    this.api.getWatchlistChanges(previousReleaseId, currentReleaseId)
      .subscribe((c) => (this.watchlistChangedCount = new Set(c.map((x) => x.supplierName)).size));
  }

  private load(previousReleaseId: number, currentReleaseId: number) {
    this.api.getDashboard(previousReleaseId, currentReleaseId).subscribe((s) => (this.summary = s));
  }

  severityDonutData(): DonutDatum[] {
    if (!this.summary) return [];
    return [
      { label: 'Removed / Disqualified', value: this.summary.critical, color: 'var(--critical)' },
      { label: 'Downgraded', value: this.summary.high, color: 'var(--high)' },
      { label: 'New / Expanded', value: this.summary.medium, color: 'var(--medium)' },
      { label: 'Minor Changes', value: this.summary.low, color: 'var(--low)' },
    ];
  }

  technologyBarData(): BarDatum[] {
    if (!this.summary) return [];
    return this.summary.changesByTechnology.map((t) => ({ label: t.technologyName, value: t.count }));
  }

  changeTypeBarData(): BarDatum[] {
    if (!this.summary) return [];
    // No explicit color here anymore - BarChartComponent now assigns each
    // bar a distinct, stable color from the shared palette by default.
    return this.summary.changesByType.map((t) => ({ label: t.changeType, value: t.count }));
  }
}
