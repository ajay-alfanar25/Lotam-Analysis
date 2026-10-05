import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '../../core/toast.service';

/// <summary>Renders the app-wide toast queue (ToastService) as a fixed
/// bottom-right stack. Mounted exactly once, in AppComponent's template, so
/// any page/component can report a background result (export emailed, save
/// failed, etc.) without owning its own notification UI.</summary>
@Component({
  selector: 'app-toast-host',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="toast-stack" *ngIf="toasts.toasts().length > 0">
      <div
        class="toast"
        *ngFor="let t of toasts.toasts()"
        [class.success]="t.kind === 'success'"
        [class.error]="t.kind === 'error'"
        [class.info]="t.kind === 'info'"
      >
        <span class="icon">{{ t.kind === 'success' ? '✓' : t.kind === 'error' ? '✕' : 'ℹ' }}</span>
        <span class="msg">{{ t.message }}</span>
        <button class="close" (click)="toasts.dismiss(t.id)" aria-label="Dismiss">✕</button>
      </div>
    </div>
  `,
  styles: [
    `
      .toast-stack {
        position: fixed; right: 20px; bottom: 20px; z-index: 200;
        display: flex; flex-direction: column; gap: 10px; max-width: 360px;
      }
      .toast {
        display: flex; align-items: flex-start; gap: 10px;
        padding: 12px 14px; border-radius: var(--radius-sm, 8px);
        background: rgba(19, 27, 48, 0.96); backdrop-filter: blur(16px); -webkit-backdrop-filter: blur(16px);
        border: 1px solid var(--border-strong, rgba(255, 255, 255, 0.14));
        box-shadow: var(--shadow, 0 12px 30px rgba(0, 0, 0, 0.35));
        font-size: 13px; color: var(--text, #f2f4f8);
        animation: toast-in 180ms ease-out;
      }
      @keyframes toast-in { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
      .toast .icon {
        flex-shrink: 0; width: 20px; height: 20px; border-radius: 50%;
        display: flex; align-items: center; justify-content: center;
        font-size: 11px; font-weight: 700; margin-top: 1px;
      }
      .toast.success .icon { background: rgba(26, 180, 110, 0.18); color: #3ddc97; }
      .toast.error .icon { background: rgba(220, 60, 70, 0.18); color: #ff6b76; }
      .toast.info .icon { background: rgba(91, 140, 255, 0.18); color: #7ea2ff; }
      .toast .msg { flex: 1; line-height: 1.45; }
      .toast .close {
        flex-shrink: 0; background: none; border: none; color: var(--text-dim, #9aa3b5);
        cursor: pointer; font-size: 11px; padding: 2px; line-height: 1;
      }
      .toast .close:hover { color: var(--text, #f2f4f8); }
    `,
  ],
})
export class ToastHostComponent {
  constructor(protected toasts: ToastService) {}
}
