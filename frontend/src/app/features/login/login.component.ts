import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

type Stage = 'login' | 'forgot' | 'reset';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="screen">
      <div class="card">
        <div class="brand">
          <div class="logo">LS</div>
          <div>
            <div class="name">LOTAM Sentinel</div>
            <div class="tagline">Regulatory Intelligence Platform</div>
          </div>
        </div>

        <!-- ---- Login ---- -->
        <form *ngIf="stage === 'login'" (ngSubmit)="submitLogin()">
          <label>Email
            <input type="email" name="email" [(ngModel)]="email" required autocomplete="username" />
          </label>
          <label>Password
            <input type="password" name="password" [(ngModel)]="password" required autocomplete="current-password" />
          </label>
          <p class="error" *ngIf="error">{{ error }}</p>
          <button type="submit" class="primary" [disabled]="loading">{{ loading ? 'Signing in...' : 'Sign in' }}</button>
          <button type="button" class="linklike" (click)="goToForgot()">Forgot password?</button>
        </form>

        <!-- ---- Forgot: request OTP ---- -->
        <form *ngIf="stage === 'forgot'" (ngSubmit)="submitForgot()">
          <p class="hint">Enter your account email - if it exists, we'll send a 6-digit reset code to it.</p>
          <label>Email
            <input type="email" name="email" [(ngModel)]="email" required autocomplete="username" />
          </label>
          <p class="error" *ngIf="error">{{ error }}</p>
          <p class="success" *ngIf="message">{{ message }}</p>
          <button type="submit" class="primary" [disabled]="loading">{{ loading ? 'Sending...' : 'Send reset code' }}</button>
          <button type="button" class="linklike" (click)="stage = 'reset'" *ngIf="message">I already have a code</button>
          <button type="button" class="linklike" (click)="goToLogin()">Back to sign in</button>
        </form>

        <!-- ---- Reset: enter OTP + new password ---- -->
        <form *ngIf="stage === 'reset'" (ngSubmit)="submitReset()">
          <label>Email
            <input type="email" name="email" [(ngModel)]="email" required autocomplete="username" />
          </label>
          <label>6-digit code
            <input type="text" name="otp" [(ngModel)]="otp" required maxlength="6" inputmode="numeric" />
          </label>
          <label>New password
            <input type="password" name="newPassword" [(ngModel)]="newPassword" required autocomplete="new-password" />
          </label>
          <p class="error" *ngIf="error">{{ error }}</p>
          <p class="success" *ngIf="message">{{ message }}</p>
          <button type="submit" class="primary" [disabled]="loading">{{ loading ? 'Updating...' : 'Set new password' }}</button>
          <button type="button" class="linklike" (click)="goToLogin()">Back to sign in</button>
        </form>
      </div>
    </div>
  `,
  styles: [
    `
      .screen { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 20px; }
      .card {
        width: 360px; max-width: 100%; padding: 32px 28px; border-radius: var(--radius);
        background: rgba(19, 27, 48, 0.6); border: 1px solid var(--border-strong); box-shadow: var(--shadow);
        backdrop-filter: blur(20px);
      }
      .brand { display: flex; align-items: center; gap: 12px; margin-bottom: 28px; }
      .logo {
        width: 42px; height: 42px; border-radius: 12px;
        background: linear-gradient(135deg, var(--hue-blue), var(--hue-purple), var(--hue-pink));
        display: flex; align-items: center; justify-content: center;
        font-weight: 700; font-size: 14px; color: white; font-family: var(--font-display);
      }
      .name { font-weight: 700; font-size: 16px; font-family: var(--font-display); }
      .tagline { font-size: 11.5px; color: var(--text-dim); }
      form { display: flex; flex-direction: column; gap: 14px; }
      label { display: flex; flex-direction: column; gap: 6px; font-size: 12.5px; color: var(--text-dim); }
      input { padding: 10px 12px; font-size: 14px; }
      .hint { font-size: 12.5px; color: var(--text-dim); margin: 0 0 4px; line-height: 1.5; }
      .error { color: var(--hue-rose, #f04438); font-size: 12.5px; margin: 0; }
      .success { color: var(--hue-emerald, #22c55e); font-size: 12.5px; margin: 0; }
      button.primary { padding: 10px; font-size: 14px; }
      button.linklike {
        background: none; border: none; color: var(--accent); font-size: 12.5px; cursor: pointer; padding: 2px 0;
        text-align: left;
      }
      button.linklike:hover { text-decoration: underline; }
    `,
  ],
})
export class LoginComponent {
  stage: Stage = 'login';
  email = '';
  password = '';
  otp = '';
  newPassword = '';
  loading = false;
  error = '';
  message = '';

  constructor(
    private auth: AuthService,
    private router: Router,
  ) {}

  submitLogin(): void {
    this.error = '';
    this.loading = true;
    this.auth.login(this.email.trim(), this.password).subscribe({
      next: () => {
        this.loading = false;
        this.router.navigateByUrl('/dashboard');
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'Invalid email or password.';
      },
    });
  }

  submitForgot(): void {
    this.error = '';
    this.message = '';
    this.loading = true;
    this.auth.forgotPassword(this.email.trim()).subscribe({
      next: (res) => {
        this.loading = false;
        this.message = res.message;
      },
      error: () => {
        this.loading = false;
        this.error = 'Something went wrong - please try again.';
      },
    });
  }

  submitReset(): void {
    this.error = '';
    this.message = '';
    this.loading = true;
    this.auth.resetPassword(this.email.trim(), this.otp.trim(), this.newPassword).subscribe({
      next: (res) => {
        this.loading = false;
        this.message = res.message;
        setTimeout(() => this.goToLogin(), 1200);
      },
      error: (err) => {
        this.loading = false;
        this.error = err?.error?.message || 'That code is invalid or has expired.';
      },
    });
  }

  goToForgot(): void {
    this.stage = 'forgot';
    this.error = '';
    this.message = '';
  }

  goToLogin(): void {
    this.stage = 'login';
    this.error = '';
    this.message = '';
    this.password = '';
    this.otp = '';
    this.newPassword = '';
  }
}
