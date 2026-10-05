import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile/profile.component').then((m) => m.ProfileComponent),
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
  },
  {
    path: 'changes',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/change-explorer/change-explorer.component').then((m) => m.ChangeExplorerComponent),
  },
  {
    path: 'materials',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/material-explorer/material-explorer.component').then(
        (m) => m.MaterialExplorerComponent,
      ),
  },
  {
    path: 'suppliers',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/supplier-view/supplier-view.component').then((m) => m.SupplierViewComponent),
  },
  {
    path: 'compare',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/version-comparison/version-comparison.component').then(
        (m) => m.VersionComparisonComponent,
      ),
  },
  {
    path: 'supply-chain-map',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/supply-chain-map/supply-chain-map.component').then((m) => m.SupplyChainMapComponent),
  },
  {
    path: 'tender-readiness',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/tender-readiness/tender-readiness.component').then((m) => m.TenderReadinessComponent),
  },
  {
    path: 'watchlist',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/watchlist/watchlist.component').then((m) => m.WatchlistComponent),
  },
  {
    path: 'alias-review',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/alias-review/alias-review.component').then((m) => m.AliasReviewComponent),
  },
  {
    path: 'audit',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/audit/audit.component').then((m) => m.AuditComponent),
  },
  {
    path: 'remap',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/material-remap/material-remap.component').then((m) => m.MaterialRemapComponent),
  },
  { path: '**', redirectTo: 'dashboard' },
];
