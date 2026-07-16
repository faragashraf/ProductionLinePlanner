import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, map, of, switchMap, tap, throwError, timeout } from 'rxjs';
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

  readonly currentUser$ = this.currentUserSubject.asObservable();
  readonly authWarning$ = this.authWarningSubject.asObservable();

  constructor(private readonly http: HttpClient) {}

  get accessToken(): string | null {
    return localStorage.getItem(AUTH_STORAGE_KEYS.accessToken);
  }

  isAuthenticated(): boolean {
    return Boolean(this.accessToken);
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
      tap(response => this.storeLoginResponse(response, request.email)),
      switchMap(response =>
        this.getCurrentUser().pipe(
          map(() => response),
          catchError((error: HttpErrorResponse) => {
            if (error.status === 401) {
              this.logout();
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

  logout(): void {
    localStorage.removeItem(AUTH_STORAGE_KEYS.accessToken);
    localStorage.removeItem(AUTH_STORAGE_KEYS.refreshToken);
    localStorage.removeItem(AUTH_STORAGE_KEYS.currentUser);
    this.currentUserSubject.next(null);
    this.authWarningSubject.next(null);
  }

  private extractData<T>(response: ApiResponse<T>): T {
    if (!response.success || !response.data) {
      throw new Error(response.error?.message || 'Unexpected API response.');
    }

    return response.data;
  }

  private storeLoginResponse(response: AuthLoginResponse, email: string): void {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, response.accessToken);

    if (response.refreshToken) {
      localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, response.refreshToken);
    } else {
      localStorage.removeItem(AUTH_STORAGE_KEYS.refreshToken);
    }

    this.storeCurrentUser({
      id: response.userId,
      fullName: '',
      email,
      roles: response.roles ?? [],
      permissions: response.permissions ?? []
    });
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
