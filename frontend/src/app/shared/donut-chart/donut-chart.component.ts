import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface DonutDatum {
  label: string;
  value: number;
  color: string;
}

interface Segment extends DonutDatum {
  dashArray: string;
  dashOffset: number;
}

@Component({
  selector: 'app-donut-chart',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="donut-wrap">
      <svg viewBox="0 0 120 120" width="140" height="140">
        <circle cx="60" cy="60" r="50" fill="none" stroke="var(--surface-2)" stroke-width="16" />
        <circle
          *ngFor="let seg of segments()"
          cx="60"
          cy="60"
          r="50"
          fill="none"
          [attr.stroke]="seg.color"
          stroke-width="16"
          [attr.stroke-dasharray]="seg.dashArray"
          [attr.stroke-dashoffset]="seg.dashOffset"
          transform="rotate(-90 60 60)"
        />
        <text x="60" y="56" text-anchor="middle" fill="var(--text)" font-size="20" font-weight="700">
          {{ total() }}
        </text>
        <text x="60" y="72" text-anchor="middle" fill="var(--text-dim)" font-size="9">total</text>
      </svg>
      <div class="legend">
        <div class="item" *ngFor="let d of data">
          <span class="dot" [style.background]="d.color"></span>
          <span class="l">{{ d.label }}</span>
          <span class="v">{{ d.value }}</span>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .donut-wrap { display: flex; align-items: center; gap: 20px; }
      .legend { display: flex; flex-direction: column; gap: 8px; }
      .item { display: flex; align-items: center; gap: 8px; font-size: 13px; }
      .dot { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; }
      .l { color: var(--text-dim); min-width: 70px; }
      .v { font-weight: 600; font-variant-numeric: tabular-nums; }
    `,
  ],
})
export class DonutChartComponent {
  @Input() data: DonutDatum[] = [];

  private readonly circumference = 2 * Math.PI * 50;

  total(): number {
    return this.data.reduce((sum, d) => sum + d.value, 0);
  }

  segments(): Segment[] {
    const total = this.total() || 1;
    let offsetAccum = 0;
    return this.data
      .filter((d) => d.value > 0)
      .map((d) => {
        const fraction = d.value / total;
        const length = fraction * this.circumference;
        const seg: Segment = {
          ...d,
          dashArray: `${length} ${this.circumference - length}`,
          dashOffset: -offsetAccum,
        };
        offsetAccum += length;
        return seg;
      });
  }
}
