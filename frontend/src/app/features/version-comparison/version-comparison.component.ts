import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';

@Component({
  selector: 'app-version-comparison',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <h1>Version Comparison</h1>
      <p class="subtitle">
        Import the two LOTAM PDFs already provided for this project, then run the deterministic
        comparator against a chosen release pair. Re-running a pair is safe - it clears and
        replaces any prior result for that exact pair.
      </p>

      <div class="card" style="margin-bottom: 16px;">
        <h2>1. Import a LOTAM PDF</h2>
        <p class="muted">Choose a LOTAM PDF from your computer to upload and import.</p>
        <div class="row">
          <input type="file" accept="application/pdf" (change)="onFileSelected($event)" #fileInput />
          <button class="primary" (click)="doUpload()" [disabled]="importing || !selectedFile">
            {{ importing ? 'Importing…' : 'Upload && Import' }}
          </button>
        </div>
        <p class="muted" *ngIf="selectedFile" style="margin-top: 6px;">Selected: {{ selectedFile.name }}</p>
        <p class="result" *ngIf="importMessage">{{ importMessage }}</p>
      </div>

      <div class="card" style="margin-bottom: 16px;">
        <h2>2. Choose the release pair</h2>
        <div class="row">
          <label>
            Previous release
            <select [ngModel]="selection.previousReleaseId()" (ngModelChange)="selection.setPrevious($event)">
              <option *ngFor="let r of selection.releases()" [ngValue]="r.id">
                {{ r.releaseDate }} ({{ r.sourceFilename }})
              </option>
            </select>
          </label>
          <label>
            Current release
            <select [ngModel]="selection.currentReleaseId()" (ngModelChange)="selection.setCurrent($event)">
              <option *ngFor="let r of selection.releases()" [ngValue]="r.id">
                {{ r.releaseDate }} ({{ r.sourceFilename }})
              </option>
            </select>
          </label>
        </div>
      </div>

      <div class="card">
        <h2>3. Run the comparison</h2>
        <button class="primary" (click)="runComparison()" [disabled]="!selection.hasSelection() || running">
          {{ running ? 'Running…' : 'Run Comparison' }}
        </button>
        <p class="result" *ngIf="comparisonMessage">{{ comparisonMessage }}</p>
      </div>
    </div>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 700px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; margin-top: 10px; }
      .row label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: var(--text-dim); }
      input[type='file'] {
        color: var(--text-dim); font-size: 13px; font-family: var(--font);
      }
      input[type='file']::file-selector-button {
        background: rgba(255,255,255,0.07); color: var(--text); border: 1px solid var(--border);
        border-radius: var(--radius-sm); padding: 8px 14px; margin-right: 10px; cursor: pointer;
        font-family: var(--font); font-size: 13px;
      }
      input[type='file']::file-selector-button:hover { border-color: var(--border-strong); }
      .result { margin-top: 10px; font-size: 13px; }
    `,
  ],
})
export class VersionComparisonComponent {
  selectedFile: File | null = null;
  importing = false;
  importMessage = '';

  running = false;
  comparisonMessage = '';

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
  ) {}

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.importMessage = '';
  }

  doUpload() {
    if (!this.selectedFile) return;
    this.importing = true;
    this.importMessage = '';
    this.api.importByUpload(this.selectedFile).subscribe({
      next: (result) => {
        this.importing = false;
        this.importMessage = result.error
          ? `Import failed: ${result.error}`
          : `Imported release #${result.releaseId} (status: ${result.processingStatus}).`;
        this.selectedFile = null;
        this.api.getReleases().subscribe((releases) => this.selection.setReleases(releases));
      },
      error: (err) => {
        this.importing = false;
        this.importMessage = `Import failed: ${err.error ?? err.message ?? 'unknown error - check the Network tab and the backend console.'}`;
      },
    });
  }

  runComparison() {
    const prev = this.selection.previousReleaseId();
    const curr = this.selection.currentReleaseId();
    if (prev === null || curr === null) return;

    this.running = true;
    this.comparisonMessage = '';
    this.api.runComparison(prev, curr).subscribe({
      next: (result) => {
        this.running = false;
        this.comparisonMessage = `Comparison complete: ${result.changeCount} changes detected. Visit the Dashboard or Change Explorer to review them.`;
      },
      error: (err) => {
        this.running = false;
        this.comparisonMessage = `Comparison failed: ${err.error ?? err.message}`;
      },
    });
  }
}
