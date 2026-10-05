import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/// <summary>Blocks every route except /login when there's no token. A
/// missing/expired token still lets the request reach the API (the
/// interceptor just won't attach a header) - the API's own [Authorize]
/// checks are the real enforcement; this guard only avoids flashing app
/// screens full of 401s before redirecting.</summary>
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn()) return true;

  router.navigate(['/login']);
  return false;
};
