import { Component, ElementRef, EventEmitter, HostListener, Input, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExportFormat } from '../../core/models';

export interface ExportDownloadEvent {
  format: ExportFormat;
}

export interface ExportEmailEvent {
  format: ExportFormat;
  email: string;
}

/// <summary>Replaces a plain "Export PDF" button with a small popover
/// offering: download as PDF, download as HTML, or send either format to
/// an email address. Used identically on Material Explorer and Change
/// Explorer - the host component still owns the actual export call and its
/// own loading state; this component only decides *what* was asked for and
/// emits it.</summary>
@Component({
  selector: 'app-export-menu',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="export-menu" #root>
      <button class="primary" [disabled]="disabled" [title]="disabledTitle || 'Export'" (click)="toggle()">
        Export ▾
      </button>

      <div class="panel" *ngIf="open">
        <button class="option" (click)="download('pdf')">Download as PDF</button>
        <button class="option" (click)="download('html')">Download as HTML</button>
        <div class="divider"></div>
        <div class="email-row">
          <input
            type="email"
            placeholder="name@company.com"
            [(ngModel)]="email"
            (keydown.enter)="sendEmail('pdf')"
          />
          <div class="email-btns">
            <button class="linklike" [disabled]="!isValidEmail()" (click)="sendEmail('pdf')">Email PDF</button>
            <button class="linklike" [disabled]="!isValidEmail()" (click)="sendEmail('html')">Email HTML</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .export-menu { position: relative; display: inline-block; }
      .panel {
        position: absolute; right: 0; top: calc(100% + 6px); z-index: 40; width: 260px;
        background: rgba(19, 28, 46, 0.97); backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px);
        border: 1px solid var(--border-strong); border-radius: var(--radius-sm); box-shadow: var(--shadow);
        padding: 8px;
        display: flex; flex-direction: column; gap: 2px;
      }
      .option {
        background: none; border: none; text-align: left; padding: 9px 10px; font-size: 13px;
        border-radius: 8px; color: var(--text); cursor: pointer;
      }
      .option:hover { background: rgba(255, 255, 255, 0.06); }
      .divider { height: 1px; background: var(--border); margin: 6px 2px; }
      .email-row { display: flex; flex-direction: column; gap: 8px; padding: 4px 4px 2px; }
      .email-row input { width: 100%; font-size: 12.5px; padding: 8px 10px; }
      .email-btns { display: flex; gap: 8px; }
      .email-btns button { flex: 1; text-align: center; }
      button.linklike {
        background: rgba(255, 255, 255, 0.05); border: 1px solid var(--border-strong); color: var(--text);
        padding: 7px 10px; border-radius: 8px; font-size: 12px; cursor: pointer;
      }
      button.linklike:disabled { opacity: 0.4; cursor: not-allowed; }
      button.linklike:not(:disabled):hover { border-color: var(--accent); }
    `,
  ],
})
export class ExportMenuComponent {
  @Input() disabled = false;
  @Input() disabledTitle: string | null = null;
  @Output() downloadRequested = new EventEmitter<ExportDownloadEvent>();
  @Output() emailRequested = new EventEmitter<ExportEmailEvent>();

  @ViewChild('root') rootRef?: ElementRef<HTMLElement>;

  open = false;
  email = '';

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.open && this.rootRef && !this.rootRef.nativeElement.contains(event.target as Node)) {
      this.open = false;
    }
  }

  toggle(): void {
    if (this.disabled) return;
    this.open = !this.open;
  }

  isValidEmail(): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.email.trim());
  }

  download(format: ExportFormat): void {
    this.open = false;
    this.downloadRequested.emit({ format });
  }

  sendEmail(format: ExportFormat): void {
    if (!this.isValidEmail()) return;
    this.open = false;
    this.emailRequested.emit({ format, email: this.email.trim() });
    this.email = '';
  }
}
