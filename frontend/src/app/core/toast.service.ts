import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

/// <summary>Tiny app-wide toast/snackbar queue - a signal<Toast[]> that
/// ToastHostComponent renders (mounted once, globally, in AppComponent) and
/// any component can push to via success()/error()/info(). Exists mainly so
/// an action with no other visible result - like "Email PDF" in the Export
/// menu, which used to resolve or fail completely silently - gets some
/// on-screen confirmation. Each toast auto-dismisses after a few seconds;
/// error toasts stay up longer since they're more likely to need reading
/// carefully (e.g. an SMTP failure message).</summary>
@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;

  success(message: string, durationMs = 4000): void {
    this.push('success', message, durationMs);
  }

  error(message: string, durationMs = 7000): void {
    this.push('error', message, durationMs);
  }

  info(message: string, durationMs = 4000): void {
    this.push('info', message, durationMs);
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }

  private push(kind: ToastKind, message: string, durationMs: number): void {
    const id = this.nextId++;
    this.toasts.update((list) => [...list, { id, kind, message }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }
}
