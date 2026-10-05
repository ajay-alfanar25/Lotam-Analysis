import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SEVERITY_COLORS, STATUS_COLORS } from '../../core/theme';

@Component({
  selector: 'app-severity-badge',
  standalone: true,
  imports: [CommonModule],
  template: `<span class="pill" [ngStyle]="style()">{{ severity }}</span>`,
  styles: [
    `
      .pill {
        display: inline-flex; align-items: center; gap: 6px;
        padding: 4px 11px; border-radius: 999px; font-size: 11.5px; font-weight: 600;
        letter-spacing: 0.02em; border: 1px solid transparent;
      }
    `,
  ],
})
export class SeverityBadgeComponent {
  /** Either a change severity (Critical/High/Medium/Low) or, when
   * statusMode is true, an approval status (Qualified/Conditional/
   * Suspended/Disqualified/Blacklisted/Reactivated/Unknown). Both maps
   * live in core/theme.ts - this component has no color logic of its own. */
  @Input({ required: true }) severity!: string;
  @Input() statusMode = false;

  style(): Record<string, string> {
    const table = this.statusMode ? STATUS_COLORS : SEVERITY_COLORS;
    const entry = table[this.severity] ?? STATUS_COLORS['Unknown'];
    return {
      background: entry.bg,
      color: entry.color,
      'box-shadow': `0 0 0 1px ${entry.border}, 0 0 12px -2px ${entry.border}`,
    };
  }
}
