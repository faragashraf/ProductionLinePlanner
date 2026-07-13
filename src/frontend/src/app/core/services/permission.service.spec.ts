import { BehaviorSubject, firstValueFrom, of, throwError } from 'rxjs';
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

  function createService(authenticated = true, initialUser: (typeof user) | null = user) {
    const currentUser$ = new BehaviorSubject<(typeof user) | null>(initialUser);
    let isAuthenticated = authenticated;
    const getCurrentUser = jasmine.createSpy('getCurrentUser');

    const auth = {
      currentUser$,
      isAuthenticated: () => isAuthenticated,
      getCurrentUser
    } as unknown as AuthService & {
      getCurrentUser: jasmine.Spy;
      currentUser$: BehaviorSubject<(typeof user) | null>;
    };

    const service = new PermissionService(auth);

    return {
      service,
      currentUser$,
      auth,
      setAuthenticated: (value: boolean) => {
        isAuthenticated = value;
      },
      getCurrentUser
    };
  }

  it('initializes unauthenticated with empty permissions and not hydrated', () => {
    const { service, currentUser$ } = createService(false, null);

    currentUser$.next(null);

    expect(service.hasHydrated).toBeFalse();
    expect(service.hydrationState).toBe('idle');
    expect(service.has('workers.view')).toBeFalse();
  });

  it('keeps unauthenticated ensureHydrated non-blocking and non-authoritative', () => {
    const { service, getCurrentUser } = createService(false, null);

    getCurrentUser.and.returnValue(of(user));
    service.ensureHydrated().subscribe();

    expect(getCurrentUser).not.toHaveBeenCalled();
    expect(service.hydrationState).toBe('idle');
    expect(service.hasHydrated).toBeFalse();
  });

  it('hydrates after login using /api/auth/me and marks hydrated', () => {
    const { service, setAuthenticated, currentUser$, auth } = createService(false, null);
    const hydratedUser = {
      ...user,
      permissions: ['users.view', 'roles.view', 'permissions.assign', 'workers.view', 'departments.view']
    };
    setAuthenticated(true);
    currentUser$.next({ ...user, permissions: [] });
    (auth.getCurrentUser as jasmine.Spy).and.returnValue(of(hydratedUser));

    service.ensureHydrated().subscribe();

    expect(auth.getCurrentUser).toHaveBeenCalledTimes(1);
    expect(service.hasHydrated).toBeTrue();
    expect(service.hydrationState).toBe('ready');
    expect(service.has('users.view')).toBeTrue();
    expect(service.has('roles.view')).toBeTrue();
  });

  it('treats cached user permissions as UX hint and replaces them after authoritative hydrate', () => {
    const { service, setAuthenticated, currentUser$, getCurrentUser } = createService(true, { ...user, permissions: ['workers.view'] });

    expect(service.has('workers.view')).toBeTrue();
    setAuthenticated(true);
    getCurrentUser.and.returnValue(of({ ...user, permissions: ['stages.view'] }));

    service.ensureHydrated().subscribe();

    expect(service.has('workers.view')).toBeFalse();
    expect(service.has('stages.view')).toBeTrue();
  });

  it('normalizes multiple simultaneous hydration calls to one request', () => {
    const { service, setAuthenticated, getCurrentUser } = createService(true, { ...user, permissions: [] });

    setAuthenticated(true);
    getCurrentUser.and.returnValue(of({ ...user, permissions: ['workers.view'] }));

    service.ensureHydrated().subscribe();
    service.ensureHydrated().subscribe();
    service.ensureHydrated().subscribe();

    expect(getCurrentUser).toHaveBeenCalledTimes(1);
    expect(service.hydrationState).toBe('ready');
    expect(service.hasHydrated).toBeTrue();
  });

  it('clears permissions and hydration state on user logout', () => {
    const { service, currentUser$ } = createService(true, user);

    currentUser$.next(null);

    expect(service.hydrationState).toBe('idle');
    expect(service.hasHydrated).toBeFalse();
    expect(service.has('workers.view')).toBeFalse();
  });

  it('executes a fresh hydration after logout/login cycle', () => {
    const { service, setAuthenticated, currentUser$, getCurrentUser } = createService(true, user);

    getCurrentUser.and.returnValue(of({ ...user, permissions: ['workers.view'] }));
    service.ensureHydrated().subscribe();

    currentUser$.next(null);
    setAuthenticated(false);
    expect(service.hasHydrated).toBeFalse();

    setAuthenticated(true);
    currentUser$.next({ ...user, permissions: ['users.view'] });
    getCurrentUser.and.returnValue(of({ ...user, permissions: ['roles.view'] }));

    service.ensureHydrated().subscribe();

    expect(service.hasHydrated).toBeTrue();
    expect(service.hydrationState).toBe('ready');
    expect(service.has('roles.view')).toBeTrue();
  });

  it('enters error state and allows retry after failure', async () => {
    const { service, setAuthenticated, getCurrentUser } = createService(true, {
      ...user,
      permissions: []
    });

    setAuthenticated(true);
    getCurrentUser.and.returnValue(
      throwError(() => ({ status: 500 }))
    );

    await expectAsync(
      firstValueFrom(service.ensureHydrated())
    ).toBeRejected();

    expect(service.hydrationState).toBe('error');
    expect(service.hasHydrated).toBeFalse();
    expect(service.has('workers.view')).toBeFalse();

    getCurrentUser.and.returnValue(
      of({ ...user, permissions: ['workers.view'] })
    );

    const permissions = await firstValueFrom(
      service.ensureHydrated()
    );

    expect(permissions).toEqual(['workers.view']);
    expect(service.hydrationState).toBe('ready');
    expect(service.hasHydrated).toBeTrue();
    expect(service.has('workers.view')).toBeTrue();
  });

  it('evaluates has, hasAny and hasAll against normalized permissions', () => {
    const { service } = createService(true, user);

    expect(service.has('WORKERS.VIEW')).toBeTrue();
    expect(service.hasAny(['workers.manage', 'assignments.manage'])).toBeTrue();
    expect(service.hasAll(['workers.view', 'assignments.manage'])).toBeTrue();
    expect(service.hasAll(['workers.view', 'workers.manage'])).toBeFalse();
  });

  it('treats an empty requirement consistently', () => {
    const { service } = createService(true, user);

    expect(service.hasAny([])).toBeTrue();
    expect(service.hasAll([])).toBeTrue();
  });
});
