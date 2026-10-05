import { Component, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import { MaterialCodeRemapDto, UnmatchedMaterialSectionDto, UnmatchedSectionsDto } from '../../core/models';

@Component({
  selector: 'app-material-remap',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page" *ngIf="selection.hasSelection(); else noSelection">
      <h1>Material Code Remap</h1>
      <p class="subtitle">
        By default, a material code disappearing from one release and a different code appearing in
        the next are treated as REMOVED + NEW - never assumed to be the same material. If SEC has
        genuinely renumbered a material, confirm it here. This requires a rationale and is fully
        reversible in the database, but takes effect only after re-running the comparison.
      </p>

      <div class="grid two-col" style="margin-bottom: 16px;">
        <div class="card">
          <h2>Removed from {{ selection.previousRelease()?.releaseDate }}</h2>
          <div class="section-list">
            <div
              class="section-item"
              *ngFor="let s of unmatched?.removedFromPrevious"
              [class.selected]="fromId === s.materialSectionId"
              (click)="fromId = s.materialSectionId"
            >
              <div class="code">{{ s.code }}</div>
              <div class="desc">{{ s.description }}</div>
              <div class="muted">{{ s.entryCount }} manufacturer(s)</div>
            </div>
            <div class="muted" *ngIf="unmatched && unmatched.removedFromPrevious.length === 0" style="padding: 8px;">
              Nothing removed between these releases.
            </div>
          </div>
        </div>

        <div class="card">
          <h2>New in {{ selection.currentRelease()?.releaseDate }}</h2>
          <div class="section-list">
            <div
              class="section-item"
              *ngFor="let s of unmatched?.newInCurrent"
              [class.selected]="toId === s.materialSectionId"
              (click)="toId = s.materialSectionId"
            >
              <div class="code">{{ s.code }}</div>
              <div class="desc">{{ s.description }}</div>
              <div class="muted">{{ s.entryCount }} manufacturer(s)</div>
            </div>
            <div class="muted" *ngIf="unmatched && unmatched.newInCurrent.length === 0" style="padding: 8px;">
              Nothing new between these releases.
            </div>
          </div>
        </div>
      </div>

      <div class="card" style="margin-bottom: 16px;">
        <h2>Confirm remap</h2>
        <div class="row">
          <input type="text" placeholder="Rationale (required)" [(ngModel)]="rationale" style="flex: 1; min-width: 300px;" />
          <input type="text" placeholder="Confirmed by" [(ngModel)]="confirmedBy" style="width: 200px;" />
          <button class="primary" (click)="createRemap()" [disabled]="!fromId || !toId || !rationale.trim()">
            Confirm remap
          </button>
        </div>
        <p class="result" *ngIf="message">{{ message }}</p>
      </div>

      <div class="card">
        <h2>Existing remaps for this release pair</h2>
        <table class="data-table">
          <thead><tr><th>From</th><th>To</th><th>Rationale</th><th>Confirmed by</th><th>When</th></tr></thead>
          <tbody>
            <tr *ngFor="let r of remaps">
              <td>{{ r.fromCode }}</td>
              <td>{{ r.toCode }}</td>
              <td>{{ r.rationale }}</td>
              <td>{{ r.confirmedBy }}</td>
              <td>{{ r.confirmedAt | date: 'short' }}</td>
            </tr>
            <tr *ngIf="remaps.length === 0">
              <td colspan="5" class="muted">No confirmed remaps for this release pair yet.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <ng-template #noSelection>
      <div class="page">
        <h1>Material Code Remap</h1>
        <div class="card">Select a release pair from the top bar first.</div>
      </div>
    </ng-template>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 800px; margin-bottom: 20px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .two-col { grid-template-columns: 1fr 1fr; }
      .section-list { display: flex; flex-direction: column; gap: 4px; max-height: 320px; overflow-y: auto; }
      .section-item { padding: 10px; border-radius: var(--radius-sm); cursor: pointer; border: 1px solid transparent; }
      .section-item:hover { background: rgba(255,255,255,0.05); }
      .section-item.selected { background: rgba(91,140,255,0.12); border-color: var(--accent); }
      .code { font-weight: 600; font-family: var(--font-display); font-size: 13px; }
      .desc { font-size: 12px; color: var(--text-dim); margin: 2px 0; }
      .row { display: flex; gap: 10px; flex-wrap: wrap; align-items: center; }
      .result { margin-top: 10px; font-size: 13px; }
    `,
  ],
})
export class MaterialRemapComponent {
  unmatched: UnmatchedSectionsDto | null = null;
  remaps: MaterialCodeRemapDto[] = [];
  fromId: number | null = null;
  toId: number | null = null;
  rationale = '';
  confirmedBy = '';
  message = '';

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
  ) {
    effect(() => {
      const prev = this.selection.previousReleaseId();
      const curr = this.selection.currentReleaseId();
      if (prev !== null && curr !== null) this.load(prev, curr);
    });
  }

  private load(prev: number, curr: number) {
    this.fromId = null;
    this.toId = null;
    this.api.getUnmatchedSections(prev, curr).subscribe((u) => (this.unmatched = u));
    this.api.getRemaps(prev, curr).subscribe((r) => (this.remaps = r));
  }

  createRemap() {
    if (!this.fromId || !this.toId || !this.rationale.trim()) return;
    const confirmer = this.confirmedBy.trim() || 'unspecified';
    this.api.createRemap(this.fromId, this.toId, this.rationale.trim(), confirmer).subscribe({
      next: () => {
        this.message = 'Remap confirmed. Re-run the comparison for this release pair to apply it.';
        this.rationale = '';
        this.fromId = null;
        this.toId = null;
        const prev = this.selection.previousReleaseId();
        const curr = this.selection.currentReleaseId();
        if (prev !== null && curr !== null) this.load(prev, curr);
      },
      error: (err) => (this.message = `Failed: ${err.error ?? err.message}`),
    });
  }
}
