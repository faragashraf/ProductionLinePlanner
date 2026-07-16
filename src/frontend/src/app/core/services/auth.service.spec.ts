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
  });
});
