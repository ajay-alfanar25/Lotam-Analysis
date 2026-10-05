import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { paletteColorForKey } from '../../core/theme';

export interface BarDatum {
  label: string;
  value: number;
  color?: string; // explicit override; otherwise derived from the label
}

@Component({
  selector: 'app-bar-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="bar-chart">
      <div class="row" *ngFor="let d of data">
        <div class="label">{{ d.label }}</div>
        <div class="track">
          <div
            class="fill"
            [style.width.%]="maxValue() ? (d.value / maxValue()) * 100 : 0"
            [style.background]="d.color || colorFor(d.label)"
          ></div>
        </div>
        <div class="value">{{ d.value }}</div>
      </div>
      <div class="empty" *ngIf="!data || data.length === 0">No data for this selection.</div>
    </div>
  `,
  styles: [
    `
      .bar-chart { display: flex; flex-direction: column; gap: 10px; }
      .row { display: grid; grid-template-columns: 140px 1fr 40px; align-items: center; gap: 10px; }
      .label { font-size: 12px; color: var(--text-dim); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      .track { background: var(--surface-2); border-radius: 6px; height: 10px; overflow: hidden; }
      .fill { height: 100%; border-radius: 6px; transition: width 300ms ease, background 300ms ease; }
      .value { font-size: 12px; text-align: right; font-variant-numeric: tabular-nums; }
      .empty { color: var(--text-dim); font-size: 13px; padding: 12px 0; }
    `,
  ],
})
export class BarChartComponent {
  @Input() data: BarDatum[] = [];

  /** True (default) gives every bar a distinct hue keyed to its label -
   * reads as varied/colorful. Set false for the rare case where a single
   * consistent color across all bars is actually what you want. */
  @Input() varied = true;

  maxValue(): number {
    return Math.max(1, ...this.data.map((d) => d.value));
  }

  colorFor(label: string): string {
    return this.varied ? paletteColorForKey(label).color : 'var(--accent)';
  }
}
