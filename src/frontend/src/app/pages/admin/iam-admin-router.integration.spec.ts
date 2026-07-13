import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, Routes } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';
import { APP_ROUTES } from '../../app-routing.module';
import { PermissionRouteAccessService } from '../../core/authorization/permission-route-access.service';
import { AuthGuard } from '../../core/guards/auth.guard';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from '../../core/guards/permission-can-match.guard';
import { AuthService } from '../../core/services/auth.service';
import { IamAdminService } from '../../core/services/iam-admin.service';
import { IamConfirmationService } from '../../core/services/iam-confirmation.service';
import { PermissionService } from '../../core/services/permission.service';

@Component({ selector: 'app-router-test-shell', template: '<router-outlet></router-outlet>' })
class RouterTestShellComponent {}

@Component({ selector: 'app-router-test-access-denied', template: 'Access denied' })
class RouterTestAccessDeniedComponent {}

describe('IAM admin router integration', () => {
  const iamRoute = APP_ROUTES
    .find((route) => route.path === '')
    ?.children
    ?.find((route) => route.path === 'admin');

  if (!iamRoute) {
    throw new Error('The IAM lazy route must be available to router integration tests.');
  }

  const routes: Routes = [
    { path: '403', component: RouterTestAccessDeniedComponent },
    {
      path: '',
      component: RouterTestShellComponent,
      canActivate: [AuthGuard],
      children: [iamRoute]
    }
  ];

  let router: Router;
  let fixture: ReturnType<typeof TestBed.createComponent<RouterTestShellComponent>>;
  let grantedPermissions = new Set<string>();

  const authenticatedUser = {
    id: 'smoke-admin',
    fullName: 'Smoke Admin',
    email: 'smoke.admin@example.com',
    roles: ['SuperAdmin'],
    permissions: [] as string[]
  };

  const hasAccess = (requirement: { permission?: string; requireAny?: string | string[]; requireAll?: string | string[] }) => {
    const toArray = (value: string | string[] | undefined) => value === undefined ? [] : Array.isArray(value) ? value : [value];
    const includes = (permission: string) => grantedPermissions.has(permission.toLowerCase());

    if (requirement.permission) {
      return includes(requirement.permission);
    }

    if (requirement.requireAll) {
      return toArray(requirement.requireAll).every(includes);
    }

    return requirement.requireAny ? toArray(requirement.requireAny).some(includes) : false;
  };

  beforeEach(() => {
    grantedPermissions = new Set<string>();

    TestBed.configureTestingModule({
      imports: [RouterTestingModule.withRoutes(routes, { initialNavigation: 'disabled' })],
      declarations: [RouterTestShellComponent, RouterTestAccessDeniedComponent],
      providers: [
        PermissionRouteAccessService,
        PermissionCanMatchGuard,
        PermissionCanActivateGuard,
        AuthGuard,
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => true,
            getCurrentUser: () => of(authenticatedUser),
            logout: jasmine.createSpy('logout')
          }
        },
        {
          provide: PermissionService,
          useValue: {
            ensureHydrated: () => of([...grantedPermissions]),
            hasAccess
          }
        },
        {
          provide: IamAdminService,
          useValue: {
            getUsers: () => of([]),
            getRoles: () => of([]),
            getPermissionCatalog: () => of([])
          }
        },
        { provide: IamConfirmationService, useValue: { confirm: () => true } }
      ]
    });

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(RouterTestShellComponent);
    fixture.detectChanges();
  });

  async function navigateWith(permissions: string[], url: string): Promise<void> {
    grantedPermissions = new Set(permissions.map((permission) => permission.toLowerCase()));

    await router.navigateByUrl(url);
    await fixture.whenStable();
  }

  it('allows every navigation URL for the effective SuperAdmin permission set', async () => {
    const superAdminPermissions = ['users.view', 'users.manage', 'roles.view', 'roles.manage', 'permissions.assign'];

    for (const url of ['/admin/users', '/admin/roles', '/admin/permissions']) {
      await navigateWith(superAdminPermissions, url);
      expect(router.url).toBe(url);
    }
  });

  it('allows users.view only for Users and redirects the other IAM screens to 403', async () => {
    await navigateWith(['users.view'], '/admin/users');
    expect(router.url).toBe('/admin/users');

    await navigateWith(['users.view'], '/admin/roles');
    expect(router.url).toBe('/403');

    await navigateWith(['users.view'], '/admin/permissions');
    expect(router.url).toBe('/403');
  });

  it('allows roles.view only for Roles and redirects Users to 403', async () => {
    await navigateWith(['roles.view'], '/admin/roles');
    expect(router.url).toBe('/admin/roles');

    await navigateWith(['roles.view'], '/admin/users');
    expect(router.url).toBe('/403');
  });

  it('allows permissions.assign for Permission Catalog', async () => {
    await navigateWith(['permissions.assign'], '/admin/permissions');

    expect(router.url).toBe('/admin/permissions');
  });

  it('redirects every IAM URL to 403 when no IAM permissions are available', async () => {
    for (const url of ['/admin/users', '/admin/roles', '/admin/permissions']) {
      await navigateWith([], url);
      expect(router.url).toBe('/403');
    }
  });

  it('supports direct child-route navigation after guard hydration', async () => {
    for (const { permission, url } of [
      { permission: 'users.view', url: '/admin/users' },
      { permission: 'roles.view', url: '/admin/roles' },
      { permission: 'permissions.assign', url: '/admin/permissions' }
    ]) {
      await navigateWith([permission], url);
      expect(router.url).toBe(url);
    }
  });
});
