import { Injectable, Optional } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { BehaviorSubject, EMPTY, Observable, catchError, finalize, map, of, shareReplay, switchMap, tap, throwError, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { AUTH_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { AUTH_STORAGE_KEYS } from '../config/auth-storage.config';
import { ApiResponse } from '../models/api-response.model';
import {
  AuthLoginResponse,
  AuthRole,
  AuthUser,
  CurrentUserResponse,
  LoginRequest
} from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly currentUserSubject = new BehaviorSubject<AuthUser | null>(this.readStoredUser());
  private readonly authWarningSubject = new BehaviorSubject<string | null>(null);
  private refreshInFlight$?: Observable<AuthLoginResponse>;
  private sessionExpired = false;

  readonly currentUser$ = this.currentUserSubject.asObservable();
  readonly authWarning$ = this.authWarningSubject.asObservable();

  constructor(
    private readonly http: HttpClient,
    @Optional() private readonly router?: Router
  ) {}

  get accessToken(): string | null {
    return localStorage.getItem(AUTH_STORAGE_KEYS.accessToken);
  }

  get refreshToken(): string | null {
    return localStorage.getItem(AUTH_STORAGE_KEYS.refreshToken);
  }

  isAuthenticated(): boolean {
    return Boolean(this.accessToken || this.refreshToken);
  }

  hasRole(role: AuthRole): boolean {
    return this.getRoles().includes(role);
  }

  getRoles(): AuthRole[] {
    return this.currentUserSubject.value?.roles ?? [];
  }

  get userName(): string {
    const user = this.currentUserSubject.value;
    return user?.fullName || user?.email || '';
  }

  login(email: string, password: string): Observable<AuthLoginResponse> {
    const request: LoginRequest = {
      email: email.trim().toLowerCase(),
      password
    };

    this.authWarningSubject.next(null);

    return this.http.post<ApiResponse<AuthLoginResponse>>(buildApiUrl('/api/auth/login'), request).pipe(
      timeout(AUTH_API_TIMEOUT_MS),
      map(response => this.extractData(response)),
      tap(response => this.storeSessionResponse(response, request.email)),
      switchMap(response =>
        this.getCurrentUser().pipe(
          map(() => response),
          catchError((error: HttpErrorResponse) => {
            if (error.status === 401) {
              this.expireSession();
              return throwError(() => error);
            }

            this.authWarningSubject.next('تم تسجيل الدخول، لكن تعذر تحميل بيانات المستخدم حالياً.');
            return of(response);
          })
        )
      )
    );
  }

  getCurrentUser(): Observable<AuthUser> {
    return this.http.get<ApiResponse<CurrentUserResponse>>(buildApiUrl('/api/auth/me')).pipe(
      timeout(AUTH_API_TIMEOUT_MS),
      map(response => this.extractData(response)),
      map(response => this.toAuthUser(response)),
      tap(user => this.storeCurrentUser(user))
    );
  }

  refreshAccessToken(): Observable<AuthLoginResponse> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token is available.'));
    }

    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.http.post<ApiResponse<AuthLoginResponse>>(
        buildApiUrl('/api/auth/refresh'),
        { refreshToken }
      ).pipe(
        timeout(AUTH_API_TIMEOUT_MS),
        map(response => this.extractData(response)),
        tap(response => this.storeSessionResponse(response)),
        finalize(() => this.refreshInFlight$ = undefined),
        shareReplay({ bufferSize: 1, refCount: false })
      );
    }

    return this.refreshInFlight$;
  }

  logout(): void {
    const refreshToken = this.refreshToken;
    this.clearSession();

    if (!refreshToken) {
      return;
    }

    this.http.post<ApiResponse<unknown>>(
      buildApiUrl('/api/auth/logout'),
      { refreshToken }
    ).pipe(
      timeout(AUTH_API_TIMEOUT_MS),
      catchError(() => EMPTY)
    ).subscribe();
  }

  expireSession(): void {
    if (this.sessionExpired) {
      return;
    }

    this.sessionExpired = true;
    this.clearSession();
    if (this.router?.url !== '/login') {
      void this.router?.navigateByUrl('/login');
    }
  }

  private extractData<T>(response: ApiResponse<T>): T {
    if (!response.success || !response.data) {
      throw new Error(response.error?.message || 'Unexpected API response.');
    }

    return response.data;
  }

  private storeSessionResponse(response: AuthLoginResponse, fallbackEmail = ''): void {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, response.accessToken);

    if (response.refreshToken) {
      localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, response.refreshToken);
    } else {
      localStorage.removeItem(AUTH_STORAGE_KEYS.refreshToken);
    }

    const currentUser = this.currentUserSubject.value;
    const user = currentUser?.id === response.userId ? currentUser : null;
    this.storeCurrentUser({
      id: response.userId,
      fullName: user?.fullName ?? '',
      email: user?.email ?? fallbackEmail,
      roles: response.roles ?? [],
      permissions: response.permissions ?? []
    });
    this.sessionExpired = false;
  }

  private storeCurrentUser(user: AuthUser): void {
    localStorage.setItem(AUTH_STORAGE_KEYS.currentUser, JSON.stringify(user));
    this.currentUserSubject.next(user);
  }

  private toAuthUser(response: CurrentUserResponse): AuthUser {
    return {
      id: response.id,
      fullName: response.fullName,
      email: response.email,
      roles: response.roles ?? [],
      permissions: response.permissions ?? []
    };
  }

  private clearSession(): void {
    this.refreshInFlight$ = undefined;
    localStorage.removeItem(AUTH_STORAGE_KEYS.accessToken);
    localStorage.removeItem(AUTH_STORAGE_KEYS.refreshToken);
    localStorage.removeItem(AUTH_STORAGE_KEYS.currentUser);
    this.currentUserSubject.next(null);
    this.authWarningSubject.next(null);
  }

  private readStoredUser(): AuthUser | null {
    const storedUser = localStorage.getItem(AUTH_STORAGE_KEYS.currentUser);

    if (!storedUser) {
      return null;
    }

    try {
      return JSON.parse(storedUser) as AuthUser;
    } catch {
      localStorage.removeItem(AUTH_STORAGE_KEYS.currentUser);
      return null;
    }
  }
}
