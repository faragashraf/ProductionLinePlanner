import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { AUTH_STORAGE_KEYS } from '../config/auth-storage.config';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService]
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('uses the centralized relative login URL and never duplicates the API prefix', () => {
    service.login('  FACTORY.MANAGER  ', 'correct-horse').subscribe();

    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    expect(login.request.body.email).toBe('factory.manager');
    expect(login.request.url).not.toContain('/api/api/');
    login.flush({
      success: true,
      data: {
        accessToken: 'access-token',
        refreshToken: 'refresh-token',
        userId: 'user-1',
        expiresAt: '2026-07-16T12:00:00Z',
        roles: [],
        permissions: []
      }
    });

    const currentUser = http.expectOne('/api/auth/me');
    expect(currentUser.request.url).not.toContain('/api/api/');
    currentUser.flush({
      success: true,
      data: {
        id: 'user-1',
        fullName: 'Operator',
        email: 'factory.manager',
        roles: [],
        permissions: []
      }
    });

    expect(localStorage.getItem(AUTH_STORAGE_KEYS.accessToken)).toBe('access-token');
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.refreshToken)).toBe('refresh-token');
  });

  it('stores a rotated token pair and preserves the current user permissions from the refresh response', () => {
    service.login('operator@example.com', 'correct-horse').subscribe();
    http.expectOne('/api/auth/login').flush({
      success: true,
      data: {
        accessToken: 'expired-access-token', refreshToken: 'refresh-token', userId: 'user-1',
        expiresAt: '2026-07-16T12:00:00Z', roles: ['Viewer'], permissions: ['old.permission']
      }
    });
    http.expectOne('/api/auth/me').flush({
      success: true,
      data: {
        id: 'user-1', fullName: 'Operator', email: 'operator@example.com', roles: ['Viewer'], permissions: ['old.permission']
      }
    });

    service.refreshAccessToken().subscribe();

    const refresh = http.expectOne('/api/auth/refresh');
    expect(refresh.request.method).toBe('POST');
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-token' });
    refresh.flush({
      success: true,
      data: {
        accessToken: 'new-access-token',
        refreshToken: 'new-refresh-token',
        userId: 'user-1',
        expiresAt: '2026-07-16T12:00:00Z',
        roles: ['Admin'],
        permissions: ['models.manage']
      }
    });

    expect(localStorage.getItem(AUTH_STORAGE_KEYS.accessToken)).toBe('new-access-token');
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.refreshToken)).toBe('new-refresh-token');
    expect(JSON.parse(localStorage.getItem(AUTH_STORAGE_KEYS.currentUser) ?? '{}')).toEqual(jasmine.objectContaining({
      fullName: 'Operator', email: 'operator@example.com', roles: ['Admin'], permissions: ['models.manage']
    }));
  });

  it('requests server revocation and clears the local session when logging out', () => {
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'access-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.refreshToken, 'refresh-token');
    localStorage.setItem(AUTH_STORAGE_KEYS.currentUser, JSON.stringify({ id: 'user-1' }));

    service.logout();

    const logout = http.expectOne('/api/auth/logout');
    expect(logout.request.method).toBe('POST');
    expect(logout.request.body).toEqual({ refreshToken: 'refresh-token' });
    logout.flush({ success: true, data: { revoked: true } });
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.accessToken)).toBeNull();
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.refreshToken)).toBeNull();
    expect(localStorage.getItem(AUTH_STORAGE_KEYS.currentUser)).toBeNull();
  });
});
