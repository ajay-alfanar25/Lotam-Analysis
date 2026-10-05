import { Component, Input, OnChanges, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-export-loading',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="overlay" *ngIf="active">
      <div class="card">
        <div class="spinner"></div>
        <p class="message">{{ currentMessage }}</p>
      </div>
    </div>
  `,
  styles: [
    `
      .overlay {
        position: fixed; inset: 0; z-index: 100;
        background: rgba(5, 8, 16, 0.65); backdrop-filter: blur(6px);
        display: flex; align-items: center; justify-content: center;
      }
      .card {
        display: flex; flex-direction: column; align-items: center; gap: 18px;
        padding: 36px 44px; border-radius: var(--radius);
        background: rgba(19, 27, 48, 0.92); border: 1px solid var(--border-strong);
        box-shadow: var(--shadow);
      }
      .spinner {
        width: 40px; height: 40px; border-radius: 50%;
        border: 3px solid rgba(255, 255, 255, 0.12);
        border-top-color: var(--accent);
        animation: spin 0.8s linear infinite;
      }
      @keyframes spin { to { transform: rotate(360deg); } }
      .message { font-size: 13.5px; color: var(--text-dim); min-width: 220px; text-align: center; }
    `,
  ],
})
export class ExportLoadingComponent implements OnChanges, OnDestroy {
  @Input() active = false;
  @Input() messages: string[] = [
    'Gathering matching records...',
    'Laying out the report table...',
    'Applying colors and highlights...',
    'Finalizing your file...',
  ];

  currentMessage = '';
  private index = 0;
  private timer: ReturnType<typeof setInterval> | null = null;

  ngOnChanges(): void {
    if (this.active) {
      this.index = 0;
      this.currentMessage = this.messages[0] ?? 'Working...';
      this.timer ??= setInterval(() => {
        this.index = (this.index + 1) % this.messages.length;
        this.currentMessage = this.messages[this.index];
      }, 1500);
    } else {
      this.stop();
    }
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
