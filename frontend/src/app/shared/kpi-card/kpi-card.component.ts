import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PALETTE } from '../../core/theme';

@Component({
  selector: 'app-kpi-card',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card kpi">
      <div class="accent-bar" [style.background]="barColor()"></div>
      <h3>{{ label }}</h3>
      <div class="value" [style.color]="valueColor()">{{ value }}</div>
      <div class="sub" *ngIf="sublabel">{{ sublabel }}</div>
    </div>
  `,
  styles: [
    `
      .kpi { display: flex; flex-direction: column; gap: 4px; height: 128px; overflow: hidden; }
      .kpi:hover { transform: translateY(-2px); }
      .kpi h3 { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      .accent-bar {
        position: absolute; top: 0; left: 0; right: 0; height: 3px;
        border-radius: var(--radius) var(--radius) 0 0;
      }
      .value { font-size: 32px; font-weight: 700; letter-spacing: -0.02em; font-family: var(--font-display); margin-top: 2px; }
      .sub { font-size: 12px; color: var(--text-dim); }
    `,
  ],
})
export class KpiCardComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: string | number;
  @Input() sublabel?: string;

  /** Preferred: a named hue from the shared palette ('blue', 'teal', 'pink',
   * 'amber', 'emerald', 'rose', 'purple', 'indigo', 'cyan'). Give each KPI on
   * a dashboard a different hue so the row reads as colorful, not five blue
   * cards in a row. */
  @Input() hue?: string;

  /** Escape hatch for a raw CSS color/gradient when a hue doesn't fit
   * (e.g. a severity-driven color already computed elsewhere). */
  @Input() accentColor?: string;

  private resolved() {
    if (this.hue) {
      const entry = PALETTE.find((p) => p.name === this.hue);
      if (entry) return entry.color;
    }
    return this.accentColor ?? null;
  }

  barColor(): string {
    return this.resolved() ?? 'linear-gradient(90deg, var(--accent), var(--accent-2))';
  }

  valueColor(): string {
    return this.resolved() ?? 'var(--text)';
  }
}
