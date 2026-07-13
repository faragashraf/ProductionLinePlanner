import { DefaultUrlSerializer, Router, UrlTree } from '@angular/router';
import { Observable, firstValueFrom, isObservable, of, Subject, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { PermissionService } from '../services/permission.service';
import { PermissionRouteAccessService } from './permission-route-access.service';
import { PermissionRequirementDescriptor as PermissionRequirement } from './permission-requirement';

describe('PermissionRouteAccessService', () => {
  const urlSerializer = new DefaultUrlSerializer();
  const router = {
    parseUrl: (url: string) => urlSerializer.parse(url),
    serializeUrl: (urlTree: UrlTree) => urlSerializer.serialize(urlTree)
  };

  type EvaluateResult = boolean | UrlTree | Observable<boolean | UrlTree>;

  async function resolveEvaluation(result: EvaluateResult): Promise<boolean | UrlTree> {
    return isObservable(result) ? firstValueFrom(result) : Promise.resolve(result);
  }

  function assertUrlTree(value: boolean | UrlTree): UrlTree {
    if (typeof value === 'boolean') {
      throw new Error(`Expected UrlTree but received boolean ${value}`);
    }

    return value;
  }

  interface CreateOptions {
    authenticated?: boolean;
    allowed?: boolean;
    permissions?: string[];
    error?: {
      status?: number;
    };
    hydration$?: Observable<string[]>;
    ensureHydrated?: () => Observable<string[]>;
  }

  function create(options: CreateOptions = {}) {
    let authenticated = options.authenticated ?? true;

    const auth = {
      isAuthenticated: () => authenticated,
      logout: jasmine.createSpy('logout')
    };

    const permission = {
      ensureHydrated:
        options.ensureHydrated ??
        (() => options.hydration$ ?? (options.error ? throwError(() => options.error) : of([]))),
      hasAccess: (requirement: PermissionRequirement) =>
        options.permissions !== undefined
          ? hasAccessByPermissionSet(options.permissions, requirement)
          : (options.allowed ?? true)
    };

    return {
      service: new PermissionRouteAccessService(
        auth as unknown as AuthService,
        permission as unknown as PermissionService,
        router as unknown as Router
      ),
      auth,
      permission,
      setAuthenticated: (value: boolean) => {
        authenticated = value;
      }
    };
  }

  it('returns true when no permission metadata is available', async () => {
    const result = await resolveEvaluation(create().service.evaluate(undefined));

    expect(result).toBeTrue();
  });

  it('redirects unauthenticated users to login', async () => {
    const result = await resolveEvaluation(create({ authenticated: false }).service.evaluate({ permission: 'users.view' }));

    expect(
      router.serializeUrl(
        assertUrlTree(result)
      )
    ).toBe('/login');
  });

  it('waits for hydration before allowing a protected route', async () => {
    const hydration$ = new Subject<string[]>();
    const hydrationSpy = jasmine.createSpy('ensureHydrated').and.returnValue(hydration$);
    const { service, permission } = create({
      allowed: true,
      ensureHydrated: () => hydrationSpy()
    });

    const evaluation = resolveEvaluation(service.evaluate({ requireAll: ['users.view', 'roles.view'] }));

    hydration$.next(['users.view', 'roles.view']);
    hydration$.complete();

    const result = await evaluation;
    expect(result).toBeTrue();
    expect(hydrationSpy).toHaveBeenCalledTimes(1);
    expect(permission.hasAccess({ permission: 'users.view' })).toBeTrue();
  });

  it('redirects malformed metadata to 403', async () => {
    const result = await resolveEvaluation(
      create().service.evaluate({ permission: ['users.view'] } as Record<string, unknown>)
    );

    expect(
      router.serializeUrl(
        assertUrlTree(result)
      )
    ).toBe('/403');
  });

  it('allows when permission is authorized', async () => {
    const result = await resolveEvaluation(create({ allowed: true }).service.evaluate({ permission: 'users.view' }));

    expect(result).toBeTrue();
  });

  it('forbidden routes return 403 UrlTree', async () => {
    const result = await resolveEvaluation(create({ allowed: false }).service.evaluate({ permission: 'users.view' }));

    expect(
      router.serializeUrl(
        assertUrlTree(result)
      )
    ).toBe('/403');
  });

  it('clears a 401 session and returns login', async () => {
    const { service, auth } = create({ error: { status: 401 } });
    const result = await resolveEvaluation(service.evaluate({ permission: 'users.view' }));

    expect(auth.logout).toHaveBeenCalled();
    expect(
      router.serializeUrl(
        assertUrlTree(result)
      )
    ).toBe('/login');
  });

  it('returns 403 for non-401 hydration errors', async () => {
    const result = await resolveEvaluation(create({ error: { status: 500 } }).service.evaluate({ permission: 'users.view' }));

    expect(
      router.serializeUrl(
        assertUrlTree(result)
      )
    ).toBe('/403');
  });

  it('re-evaluates hydration after an unauthenticated attempt', async () => {
    const hydrationSpy = jasmine.createSpy('ensureHydrated').and.returnValue(of([]));
    const { service, setAuthenticated, auth } = create({
      authenticated: false,
      ensureHydrated: () => hydrationSpy()
    });

    const firstResult = await resolveEvaluation(service.evaluate({ permission: 'users.view' }));
    expect(
      router.serializeUrl(
        assertUrlTree(firstResult)
      )
    ).toBe('/login');
    expect(hydrationSpy).not.toHaveBeenCalled();

    setAuthenticated(true);
    const secondResult = await resolveEvaluation(service.evaluate({ permission: 'users.view' }));
    expect(hydrationSpy).toHaveBeenCalledTimes(1);
    expect(secondResult).toBeTrue();
    expect(auth.logout).not.toHaveBeenCalled();
  });

  it('allows users.view for users-only and denies roles/permissions screens', async () => {
    const usersOnly = create({ permissions: ['users.view'] });

    expect(await resolveEvaluation(usersOnly.service.evaluate({ permission: 'users.view' }))).toBeTrue();
    expect(
      router.serializeUrl(
        assertUrlTree(
          await resolveEvaluation(usersOnly.service.evaluate({ permission: 'roles.view' }))
        )
      )
    ).toBe('/403');
    expect(
      router.serializeUrl(
        assertUrlTree(
          await resolveEvaluation(usersOnly.service.evaluate({ permission: 'permissions.assign' }))
        )
      )
    ).toBe('/403');
  });

  it('allows roles-only for roles route and denies users route', async () => {
    const rolesOnly = create({ permissions: ['roles.view'] });

    expect(await resolveEvaluation(rolesOnly.service.evaluate({ permission: 'roles.view' }))).toBeTrue();
    expect(
      router.serializeUrl(
        assertUrlTree(
          await resolveEvaluation(rolesOnly.service.evaluate({ permission: 'users.view' }))
        )
      )
    ).toBe('/403');
  });

  it('allows permissions.assign for permission catalog only', async () => {
    const permissionsManager = create({ permissions: ['permissions.assign'] });

    expect(await resolveEvaluation(permissionsManager.service.evaluate({ permission: 'permissions.assign' }))).toBeTrue();
    expect(
      router.serializeUrl(
        assertUrlTree(
          await resolveEvaluation(permissionsManager.service.evaluate({ permission: 'users.view' }))
        )
      )
    ).toBe('/403');
  });

  it('treats SuperAdmin effective permissions as allow-all for IAM routes', async () => {
    const superAdmin = create({ permissions: ['users.view', 'roles.view', 'permissions.assign'] });

    expect(await resolveEvaluation(superAdmin.service.evaluate({ permission: 'users.view' }))).toBeTrue();
    expect(await resolveEvaluation(superAdmin.service.evaluate({ permission: 'roles.view' }))).toBeTrue();
    expect(await resolveEvaluation(superAdmin.service.evaluate({ permission: 'permissions.assign' }))).toBeTrue();
  });

  it('rejects IAM parent boundary when no permissions are available', async () => {
    const denied = create({ permissions: [] });

    expect(
      router.serializeUrl(
        assertUrlTree(
          await resolveEvaluation(denied.service.evaluate({ requireAny: ['users.view', 'roles.view', 'permissions.assign'] }))
        )
      )
    ).toBe('/403');
  });

  it('allows IAM parent boundary when exactly one required permission exists', async () => {
    const usersOnly = create({ permissions: ['users.view'] });
    const rolesOnly = create({ permissions: ['roles.view'] });
    const permissionsOnly = create({ permissions: ['permissions.assign'] });

    expect(await resolveEvaluation(usersOnly.service.evaluate({ requireAny: ['users.view', 'roles.view', 'permissions.assign'] }))).toBeTrue();
    expect(await resolveEvaluation(rolesOnly.service.evaluate({ requireAny: ['users.view', 'roles.view', 'permissions.assign'] }))).toBeTrue();
    expect(await resolveEvaluation(permissionsOnly.service.evaluate({ requireAny: ['users.view', 'roles.view', 'permissions.assign'] }))).toBeTrue();
  });
});

function hasAccessByPermissionSet(
  permissions: string[],
  requirement: PermissionRequirement
): boolean {
  const normalizedPermissions = permissions
    .map((permission) => permission.trim().toLowerCase())
    .filter(Boolean);

  const toArray = (value: string | string[]) => Array.isArray(value) ? value : [value];
  const includes = (permission: string | undefined) => {
    if (!permission) {
      return false;
    }

    return normalizedPermissions.includes(permission.trim().toLowerCase());
  };

  if (!requirement || (!requirement.permission && !requirement.requireAny && !requirement.requireAll)) {
    return true;
  }

  if (requirement.permission) {
    return includes(requirement.permission);
  }

  if (requirement.requireAll) {
    return toArray(requirement.requireAll).every(includes);
  }

  if (requirement.requireAny) {
    return toArray(requirement.requireAny).some(includes);
  }

  return true;
}
