import { HTTP_INTERCEPTORS, HttpClient } from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { AUTH_STORAGE_KEYS } from '../config/auth-storage.config';
import { AuthService } from '../services/auth.service';
import { AuthTokenInterceptor } from './auth-token.interceptor';

describe('AuthTokenInterceptor', () => {
  let httpClient: HttpClient;
  let http: HttpTestingController;
  let authService: AuthService;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule],
      providers: [
        AuthService,
        { provide: HTTP_INTERCEPTORS, useClass: AuthTokenInterceptor, multi: true }
      ]
    });
    httpClient = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    authService = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('adds the current access token to API requests', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'test-token');

    httpClient.post('/api/production/records/preview', { acceptedQuantity: 500 }).subscribe();

    const request = http.expectOne('/api/production/records/preview');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-token');
    request.flush({ success: true, data: {} });
  });

  it('refreshes once for concurrent 401 responses then retries each original request with the rotated token', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'expired-access-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, 'refresh-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.currentUser, JSON.stringify({ id: 'user-1', fullName: 'Operator', email: 'operator@example.com', roles: ['Viewer'], permissions: ['models.view'] }));

    const firstResult: unknown[] = [];
    const secondResult: unknown[] = [];
    httpClient.get('/api/models/one').subscribe(value => firstResult.push(value));
    httpClient.get('/api/models/two').subscribe(value => secondResult.push(value));

    const initial = http.match(request => request.url === '/api/models/one' || request.url === '/api/models/two');
    expect(initial).toHaveSize(2);
    initial.forEach(request => request.flush({ success: false }, { status: 401, statusText: 'Unauthorized' }));

    const refresh = http.expectOne('/api/auth/refresh');
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-token' });
    refresh.flush({
      success: true,
      data: {
        accessToken: 'new-access-token', refreshToken: 'new-refresh-token', userId: 'user-1',
        expiresAt: '2026-07-16T12:00:00Z', roles: ['Admin'], permissions: ['models.manage']
      }
    });

    const retried = http.match(request => request.url === '/api/models/one' || request.url === '/api/models/two');
    expect(retried).toHaveSize(2);
    retried.forEach(request => {
      expect(request.request.headers.get('Authorization')).toBe('Bearer new-access-token');
      request.flush({ success: true, data: { id: request.request.url } });
    });

    expect(firstResult).toHaveSize(1);
    expect(secondResult).toHaveSize(1);
    expect(JSON.parse(localStorage.getItem(AUTH_STORAGE_KEYS.currentUser) ?? '{}')).toEqual(jasmine.objectContaining({ permissions: ['models.manage'] }));
  });

  it('retries a late concurrent 401 with the already-rotated token instead of starting a second refresh', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'expired-access-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, 'refresh-token');

    httpClient.get('/api/models/one').subscribe();
    httpClient.get('/api/models/two').subscribe();
    const initial = http.match(request => request.url === '/api/models/one' || request.url === '/api/models/two');
    const first = initial.find(request => request.request.url === '/api/models/one')!;
    const second = initial.find(request => request.request.url === '/api/models/two')!;

    first.flush({ success: false }, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush({
      success: true,
      data: {
        accessToken: 'new-access-token', refreshToken: 'new-refresh-token', userId: 'user-1',
        expiresAt: '2026-07-16T12:00:00Z', roles: [], permissions: []
      }
    });

    second.flush({ success: false }, { status: 401, statusText: 'Unauthorized' });
    expect(http.match('/api/auth/refresh')).toEqual([]);
    const retried = http.match(request => request.url === '/api/models/one' || request.url === '/api/models/two');
    expect(retried).toHaveSize(2);
    retried.forEach(request => {
      expect(request.request.headers.get('Authorization')).toBe('Bearer new-access-token');
      request.flush({ success: true, data: {} });
    });
  });

  it('does not refresh login, refresh, logout, or public API requests', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'expired-access-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, 'refresh-token');

    ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout', '/api/health'].forEach(url => {
      httpClient.post(url, {}).subscribe({ error: () => undefined });
      const request = http.expectOne(url);
      expect(request.request.headers.has('Authorization')).toBeFalse();
      request.flush({ success: false }, { status: 401, statusText: 'Unauthorized' });
    });

    expect(http.match('/api/auth/refresh')).toEqual([]);
  });

  it('keeps the renewed session when the one permitted retry fails for a non-authentication reason', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'expired-access-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, 'refresh-token');
    const navigate = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const errors: unknown[] = [];

    httpClient.get('/api/models/one').subscribe({ error: error => errors.push(error) });
    http.expectOne('/api/models/one').flush({ success: false }, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush({
      success: true,
      data: {
        accessToken: 'new-access-token', refreshToken: 'new-refresh-token', userId: 'user-1',
        expiresAt: '2026-07-16T12:00:00Z', roles: [], permissions: []
      }
    });
    http.expectOne('/api/models/one').flush({ success: false }, { status: 500, statusText: 'Server Error' });

    expect(errors).toHaveSize(1);
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.accessToken)).toBe('new-access-token');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('clears and redirects once when the shared refresh request fails without retrying again', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'expired-access-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, 'refresh-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.currentUser, JSON.stringify({ id: 'user-1' }));
    const navigate = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const errors: unknown[] = [];

    httpClient.get('/api/models/one').subscribe({ error: error => errors.push(error) });
    httpClient.get('/api/models/two').subscribe({ error: error => errors.push(error) });
    http.match(request => request.url === '/api/models/one' || request.url === '/api/models/two')
      .forEach(request => request.flush({ success: false }, { status: 401, statusText: 'Unauthorized' }));

    http.expectOne('/api/auth/refresh').flush({ success: false }, { status: 401, statusText: 'Unauthorized' });

    expect(http.match('/api/auth/refresh')).toHaveSize(0);
    expect(errors).toHaveSize(2);
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.accessToken)).toBeNull();
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.refreshToken)).toBeNull();
    expect(navigate).toHaveBeenCalledOnceWith('/login');
    expect(authService.isAuthenticated()).toBeFalse();
  });
});
