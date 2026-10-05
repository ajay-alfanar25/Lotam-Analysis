import { Component, OnInit, effect, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { filter } from 'rxjs';
import { ApiService } from './core/api.service';
import { AuthService } from './core/auth.service';
import { ReleaseSelectionService } from './core/release-selection.service';
import { paletteColorForKey } from './core/theme';
import { ToastHostComponent } from './shared/toast/toast-host.component';

// Pages where the global previous/current picker is redundant or actively
// confusing: Supplier View now always shows the latest release only (no
// pair needed), Material Explorer has its own Previous/Current tabs that
// already represent this same selection in a more direct way, and Profile
// has nothing on it that's ever scoped to a comparison pair - its
// Notification Settings material picker always reflects the single latest
// release (see ProfileController.GetSubscribableOptions), so showing a
// picker that implies "this page is comparing two versions" would be
// actively misleading there.
const HIDE_TOPBAR_ROUTES = ['/suppliers', '/materials', '/watchlist', '/supply-chain-map', '/tender-readiness', '/profile'];

// The login screen renders its own full-page layout - no sidebar, no
// topbar, nothing from the app shell around it.
const NO_SHELL_ROUTES = ['/login'];

interface NavItem {
  path: string;
  label: string;
}

const NAV_ITEMS: NavItem[] = [
  { path: '/dashboard', label: 'Dashboard' },
  { path: '/changes', label: 'Change Explorer' },
  { path: '/materials', label: 'Material Explorer' },
  { path: '/suppliers', label: 'Supplier View' },
  { path: '/compare', label: 'Version Comparison' },
  { path: '/supply-chain-map', label: 'Supply Chain Map' },
  { path: '/tender-readiness', label: 'Tender Readiness' },
  { path: '/watchlist', label: 'My Suppliers' },
  { path: '/audit', label: 'Release Audit' },
  // Alias Review and Material Remap hidden from nav for now (per request) -
  // routes and pages are untouched, just not linked here. Restore by
  // adding these two lines back:
  // { path: '/alias-review', label: 'Alias Review' },
  // { path: '/remap', label: 'Material Remap' },
];

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterOutlet, RouterLink, RouterLinkActive, ToastHostComponent],
  template: `
    <app-toast-host></app-toast-host>

    <router-outlet *ngIf="noShell()"></router-outlet>

    <div class="shell" *ngIf="!noShell()">
      <aside class="sidebar">
        <div class="brand">
          <div class="logo">LS</div>
          <div>
            <div class="name">LOTAM Sentinel</div>
            <div class="tagline">V1</div>
          </div>
        </div>
        <nav>
          <a
            *ngFor="let item of navItems"
            [routerLink]="item.path"
            routerLinkActive="active"
            [style.--nav-dot]="dotColor(item.path)"
          >
            <span class="dot" [style.background]="dotColor(item.path)"></span>
            {{ item.label }}
          </a>
        </nav>

        <div class="user-box" *ngIf="auth.currentUser() as u">
          <a routerLink="/profile" routerLinkActive="active" class="user-link">
            <span class="user-avatar">{{ initials(u.fullName) }}</span>
            <span class="user-name">{{ u.fullName }}</span>
          </a>
          <button class="logout-btn" (click)="logout()" title="Log out">Log out</button>
        </div>
      </aside>

      <div class="main">
        <header class="topbar" *ngIf="!hideTopbar()">
          <div class="release-picker" *ngIf="selection.releases().length > 0">
            <label>
              Previous
              <select
                [ngModel]="selection.previousReleaseId()"
                (ngModelChange)="selection.setPrevious($event)"
                name="previousRelease"
              >
                <option *ngFor="let r of selection.releases()" [ngValue]="r.id">
                  {{ r.releaseDate }} ({{ r.sourceFilename }})
                </option>
              </select>
            </label>
            <span class="arrow">→</span>
            <label>
              Current
              <select
                [ngModel]="selection.currentReleaseId()"
                (ngModelChange)="selection.setCurrent($event)"
                name="currentRelease"
              >
                <option *ngFor="let r of selection.releases()" [ngValue]="r.id">
                  {{ r.releaseDate }} ({{ r.sourceFilename }})
                </option>
              </select>
            </label>
          </div>
          <div class="release-picker" *ngIf="selection.releases().length === 0">
            No releases imported yet. Use the Version Comparison page to import the two LOTAM PDFs.
          </div>
        </header>

        <main>
          <router-outlet></router-outlet>
        </main>
      </div>
    </div>
  `,
  styles: [
    `
      .shell { display: flex; min-height: 100vh; }
      .sidebar {
        width: 232px;
        flex-shrink: 0;
        background: rgba(255, 255, 255, 0.035);
        backdrop-filter: blur(24px);
        -webkit-backdrop-filter: blur(24px);
        border-right: 1px solid var(--border);
        padding: 22px 16px;
        display: flex;
        flex-direction: column;
        gap: 28px;
        position: sticky;
        top: 0;
        height: 100vh;
      }
      .brand { display: flex; align-items: center; gap: 11px; padding: 0 8px; }
      .logo {
        width: 38px; height: 38px; border-radius: 11px;
        background: linear-gradient(135deg, var(--hue-blue), var(--hue-purple), var(--hue-pink));
        display: flex; align-items: center; justify-content: center;
        font-weight: 700; font-size: 13px; color: white;
        font-family: var(--font-display);
        box-shadow: 0 4px 16px -2px rgba(91, 140, 255, 0.5);
      }
      .name { font-weight: 700; font-size: 14px; font-family: var(--font-display); }
      .tagline { font-size: 11px; color: var(--text-dim); }
      nav { display: flex; flex-direction: column; gap: 3px; }
      nav a {
        display: flex; align-items: center; gap: 10px;
        color: var(--text-dim);
        padding: 10px 13px;
        border-radius: var(--radius-sm);
        font-size: 13px;
        font-weight: 500;
        transition: background 150ms ease, color 150ms ease;
      }
      .dot { width: 7px; height: 7px; border-radius: 50%; flex-shrink: 0; opacity: 0.55; transition: opacity 150ms ease, box-shadow 150ms ease; }
      nav a:hover { background: rgba(255, 255, 255, 0.06); color: var(--text); }
      nav a:hover .dot { opacity: 1; }
      nav a.active {
        background: rgba(255, 255, 255, 0.07);
        color: var(--text);
        box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.1);
      }
      nav a.active .dot { opacity: 1; box-shadow: 0 0 8px 1px var(--nav-dot, var(--accent)); }
      .user-box { margin-top: auto; display: flex; flex-direction: column; gap: 8px; padding-top: 12px; border-top: 1px solid var(--border); }
      .user-link { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: var(--radius-sm); }
      .user-link.active, .user-link:hover { background: rgba(255, 255, 255, 0.06); }
      .user-avatar {
        width: 26px; height: 26px; border-radius: 50%; flex-shrink: 0;
        background: linear-gradient(135deg, var(--hue-blue), var(--hue-purple));
        display: flex; align-items: center; justify-content: center;
        font-size: 10.5px; font-weight: 700; color: white;
      }
      .user-name { font-size: 12.5px; color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .logout-btn {
        background: none; border: 1px solid var(--border-strong); color: var(--text-dim);
        font-size: 11.5px; padding: 6px 10px; border-radius: var(--radius-sm); cursor: pointer;
      }
      .logout-btn:hover { color: var(--text); border-color: var(--accent); }
      .main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
      .topbar {
        border-bottom: 1px solid var(--border);
        padding: 14px 32px;
        display: flex;
        align-items: center;
        background: rgba(255, 255, 255, 0.02);
        backdrop-filter: blur(16px);
        -webkit-backdrop-filter: blur(16px);
        position: sticky;
        top: 0;
        z-index: 10;
      }
      .release-picker { display: flex; align-items: center; gap: 12px; font-size: 12px; color: var(--text-dim); }
      .release-picker label { display: flex; flex-direction: column; gap: 4px; }
      .arrow { color: var(--accent); font-weight: 600; }
      main { flex: 1; overflow-y: auto; }
    `,
  ],
})
export class AppComponent implements OnInit {
  hideTopbar = signal(false);
  noShell = signal(false);
  navItems = NAV_ITEMS;

  constructor(
    private api: ApiService,
    private router: Router,
    protected selection: ReleaseSelectionService,
    protected auth: AuthService,
  ) {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe((e) => {
      const url = (e as NavigationEnd).urlAfterRedirects;
      this.hideTopbar.set(HIDE_TOPBAR_ROUTES.some((r) => url.startsWith(r)));
      this.noShell.set(NO_SHELL_ROUTES.some((r) => url.startsWith(r)));
    });

    // Reacts to auth.currentUser (a signal) rather than checking
    // isLoggedIn() once in ngOnInit. AppComponent is a singleton created
    // once when the SPA first boots - on a brand new session that boot
    // happens on the *login* page, before any token exists, so a one-time
    // "if isLoggedIn() then load releases" in ngOnInit always saw
    // isLoggedIn()===false and never re-ran once login() actually set the
    // token a moment later (ngOnInit only fires once, ever). Releases then
    // stayed empty - dashboards/pickers reading ReleaseSelectionService
    // had nothing to show - until the next full page reload re-ran
    // ngOnInit with a token already in localStorage. That was the exact
    // "works after refresh, not on first launch/login" symptom reported.
    // Keying off the currentUser signal instead means this fires the
    // moment login() resolves, with no reload needed, and also clears
    // releases back out on logout so a next login doesn't start from a
    // stale previous user's selection.
    effect(() => {
      if (this.auth.currentUser()) {
        this.api.getReleases().subscribe((releases) => this.selection.setReleases(releases));
      } else {
        this.selection.setReleases([]);
      }
    });
  }

  ngOnInit(): void {}

  dotColor(path: string): string {
    return paletteColorForKey(path).color;
  }

  initials(fullName: string): string {
    return fullName
      .split(/\s+/)
      .filter((p) => p.length > 0)
      .slice(0, 2)
      .map((p) => p[0]!.toUpperCase())
      .join('');
  }

  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }
}
