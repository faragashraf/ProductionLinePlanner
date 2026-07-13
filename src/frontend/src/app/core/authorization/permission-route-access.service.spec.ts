import { of, throwError } from 'rxjs';
import { PermissionRouteAccessService } from './permission-route-access.service';

describe('PermissionRouteAccessService', () => {
  const router = { parseUrl: (url: string) => ({ url }) };

  function create(options: { authenticated?: boolean; allowed?: boolean; error?: { status?: number } } = {}) {
    const auth = {
      isAuthenticated: () => options.authenticated ?? true,
      logout: jasmine.createSpy('logout')
    };
    const permission = {
      ensureHydrated: () => options.error ? throwError(() => options.error) : of([]),
      hasAccess: () => options.allowed ?? true
    };
    return { service: new PermissionRouteAccessService(auth as any, permission as any, router as any), auth };
  }

  it('redirects unauthenticated users to login', () => {
    const result = create({ authenticated: false }).service.evaluate({ permission: 'users.view' }) as unknown as { url: string };
    expect(result.url).toBe('/login');
  });

  it('waits for hydration before allowing a protected route', (done) => {
    const result = create({ allowed: true }).service.evaluate({ requireAll: ['users.view', 'roles.view'] }) as any;
    result.subscribe((value: boolean) => {
      expect(value).toBeTrue();
      done();
    });
  });

  it('fails closed for denied or malformed metadata', (done) => {
    const denied = create({ allowed: false }).service.evaluate({ permission: 'users.view' }) as any;
    denied.subscribe((value: { url: string }) => {
      expect(value.url).toBe('/403');
      expect(create().service.evaluate({ permission: ['users.view'] } as any) as unknown).toEqual({ url: '/403' });
      done();
    });
  });

  it('clears a 401 session and returns login', (done) => {
    const { service, auth } = create({ error: { status: 401 } });
    (service.evaluate({ permission: 'users.view' }) as any).subscribe((value: { url: string }) => {
      expect(auth.logout).toHaveBeenCalled();
      expect(value.url).toBe('/login');
      done();
    });
  });
});
