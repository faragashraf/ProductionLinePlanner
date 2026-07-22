import { Injectable } from '@angular/core';
import {
  HttpEvent,
  HttpHandler,
  HttpContextToken,
  HttpInterceptor,
  HttpRequest,
  HttpErrorResponse
} from '@angular/common/http';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { AuthService } from '../services/auth.service';

export const SKIP_AUTH_REFRESH = new HttpContextToken<boolean>(() => false);
export const AUTH_REFRESH_RETRIED = new HttpContextToken<boolean>(() => false);

const NON_REFRESHABLE_PATHS = [
  '/api/auth/login',
  '/api/auth/refresh',
  '/api/auth/logout',
  '/api/health',
  '/api/identity/placeholder',
  '/api/error',
  '/api/admin/bootstrap'
] as const;

@Injectable()
export class AuthTokenInterceptor implements HttpInterceptor {
  constructor(private readonly authService: AuthService) {}

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const isApiRequest = request.url.startsWith(API_BASE_URL) || request.url.startsWith('/api/');

    if (!isApiRequest || this.isNonRefreshablePath(request.url)) {
      return next.handle(request);
    }

    const requestAccessToken = this.authService.accessToken;
    return next.handle(this.withAccessToken(request, requestAccessToken)).pipe(
      catchError((error: unknown) => {
        if (!this.canRefresh(request, error)) {
          return throwError(() => error);
        }

        if (requestAccessToken !== this.authService.accessToken) {
          return this.retryOnce(request, next);
        }

        return this.authService.refreshAccessToken().pipe(
          catchError(refreshError => this.expireAfterFailure(refreshError)),
          switchMap(() => this.retryOnce(request, next))
        );
      })
    );
  }

  private retryOnce(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const retried = request.clone({ context: request.context.set(AUTH_REFRESH_RETRIED, true) });
    return next.handle(this.withAccessToken(retried));
  }

  private expireAfterFailure(error: unknown): Observable<never> {
    this.authService.expireSession();
    return throwError(() => error);
  }

  private withAccessToken(request: HttpRequest<unknown>, token = this.authService.accessToken): HttpRequest<unknown> {
    return token
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;
  }

  private canRefresh(request: HttpRequest<unknown>, error: unknown): boolean {
    return error instanceof HttpErrorResponse
      && error.status === 401
      && !request.context.get(SKIP_AUTH_REFRESH)
      && !request.context.get(AUTH_REFRESH_RETRIED)
      && !this.isNonRefreshablePath(request.url);
  }

  private isNonRefreshablePath(url: string): boolean {
    const path = url.split('?')[0];
    return NON_REFRESHABLE_PATHS.some(candidate => path === candidate || path.endsWith(candidate));
  }
}
