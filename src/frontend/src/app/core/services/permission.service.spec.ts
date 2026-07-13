import { BehaviorSubject, of } from 'rxjs';
import { AuthService } from './auth.service';
import { PermissionService } from './permission.service';

describe('PermissionService', () => {
  const user = {
    id: '1',
    fullName: 'Test User',
    email: 'test@example.com',
    roles: [],
    permissions: ['workers.view', 'assignments.manage']
  };

  function createService(currentUser = user): { service: PermissionService; currentUser$: BehaviorSubject<typeof user | null> } {
    const currentUser$ = new BehaviorSubject<typeof user | null>(currentUser);
    const auth = {
      currentUser$,
      isAuthenticated: () => currentUser !== null,
      getCurrentUser: () => of(currentUser!)
    } as unknown as AuthService;

    return { service: new PermissionService(auth), currentUser$ };
  }

  it('evaluates has, hasAny and hasAll against normalized permissions', () => {
    const { service } = createService();

    expect(service.has('WORKERS.VIEW')).toBeTrue();
    expect(service.hasAny(['workers.manage', 'assignments.manage'])).toBeTrue();
    expect(service.hasAll(['workers.view', 'assignments.manage'])).toBeTrue();
    expect(service.hasAll(['workers.view', 'workers.manage'])).toBeFalse();
  });

  it('hydrates the token session before allowing protected content', () => {
    const { service } = createService();

    expect(service.hydrationState).toBe('idle');
    service.ensureHydrated().subscribe();
    expect(service.hydrationState).toBe('ready');
  });

  it('clears permission state after logout', () => {
    const { service, currentUser$ } = createService();

    currentUser$.next(null);
    expect(service.has('workers.view')).toBeFalse();
    expect(service.hydrationState).toBe('idle');
  });

  it('updates permissions when the authenticated user changes', () => {
    const { service, currentUser$ } = createService();

    currentUser$.next({ ...user, permissions: ['roles.view'] });

    expect(service.has('workers.view')).toBeFalse();
    expect(service.has('roles.view')).toBeTrue();
  });

  it('treats an empty requirement consistently', () => {
    const { service } = createService();

    expect(service.hasAny([])).toBeTrue();
    expect(service.hasAll([])).toBeTrue();
  });
});
