import { Injectable } from '@angular/core';
import {
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { AUTH_STORAGE_KEYS } from '../config/auth-storage.config';

@Injectable()
export class AuthTokenInterceptor implements HttpInterceptor {
  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const token = localStorage.getItem(AUTH_STORAGE_KEYS.accessToken);
    const isApiRequest = request.url.startsWith(API_BASE_URL) || request.url.startsWith('/api/');

    if (!token || !isApiRequest) {
      return next.handle(request);
    }

    return next.handle(
      request.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      })
    );
  }
}
