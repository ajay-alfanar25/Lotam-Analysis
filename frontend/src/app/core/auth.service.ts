import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { GenericMessageDto, LoginResponseDto, UserDto } from './models';

const TOKEN_KEY = 'lotam_sentinel_token';
const USER_KEY = 'lotam_sentinel_user';

/// <summary>Owns the JWT and current-user state client-side. The token is
/// kept in localStorage (not a cookie) so the JWT interceptor can attach it
/// as a plain Authorization header - simplest approach for a single-page
/// app talking to its own API with no cross-site cookie concerns.</summary>
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly base = `${environment.apiBaseUrl}/api/auth`;

  // Signal so the sidebar/topbar can reactively show "logged in as X" /
  // hide nav entirely without every component re-checking localStorage.
  currentUser = signal<UserDto | null>(this.readStoredUser());

  constructor(private http: HttpClient) {}

  login(email: string, password: string): Observable<LoginResponseDto> {
    return this.http.post<LoginResponseDto>(`${this.base}/login`, { email, password }).pipe(
      tap((res) => {
        localStorage.setItem(TOKEN_KEY, res.token);
        localStorage.setItem(USER_KEY, JSON.stringify(res.user));
        this.currentUser.set(res.user);
      }),
    );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this.currentUser.set(null);
  }

  forgotPassword(email: string): Observable<GenericMessageDto> {
    return this.http.post<GenericMessageDto>(`${this.base}/forgot-password`, { email });
  }

  resetPassword(email: string, otp: string, newPassword: string): Observable<GenericMessageDto> {
    return this.http.post<GenericMessageDto>(`${this.base}/reset-password`, { email, otp, newPassword });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<GenericMessageDto> {
    return this.http.post<GenericMessageDto>(`${this.base}/change-password`, { currentPassword, newPassword });
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  /// <summary>Lets the Profile page's "save" refresh the cached name shown
  /// in the sidebar without a full re-login.</summary>
  updateCachedUser(user: UserDto): void {
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this.currentUser.set(user);
  }

  private readStoredUser(): UserDto | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? (JSON.parse(raw) as UserDto) : null;
    } catch {
      return null;
    }
  }
}
