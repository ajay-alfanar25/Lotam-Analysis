import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/// <summary>Attaches the JWT to every /api request and, on a 401, clears
/// the (presumably expired/invalid) token and bounces to /login rather than
/// leaving the app stuck showing empty data with a silent failed
/// request.
///
/// isApiRequest/isLoginRequest match on the "/api/..." *path*, not a
/// leading string on req.url - in dev, req.url is relative ("/api/...",
/// same origin as the app) but in production it's the full deployed API
/// origin (environment.apiBaseUrl + "/api/...", a different origin than
/// the static frontend - see ApiService/AuthService), so a plain
/// req.url.startsWith('/api') would stop matching anything the moment
/// apiBaseUrl is non-empty and silently stop attaching the token on every
/// production request.</summary>
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.getToken();

  const isLoginRequest = req.url.includes('/api/auth/login');
  const isApiRequest = req.url.includes('/api/');

  const authedReq = token && isApiRequest && !isLoginRequest
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authedReq).pipe(
    catchError((err) => {
      if (err?.status === 401 && !isLoginRequest) {
        auth.logout();
        router.navigate(['/login']);
      }
      return throwError(() => err);
    }),
  );
};
