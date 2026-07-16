import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { IamAdminService, AdminUserAuthorization } from '../../../core/services/iam-admin.service';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';
import { PermissionService } from '../../../core/services/permission.service';
import { IamAdminModule } from '../iam-admin.module';
import { UserAuthorizationPageComponent } from './user-authorization-page.component';

describe('UserAuthorizationPageComponent', () => {
  const authorization: AdminUserAuthorization = {
    id: 'user-1',
    fullName: 'Ashraf Farag',
    email: 'ashraf',
    isActive: true,
    permissionsVersion: '2026-07-17T09:00:00Z',
    roles: ['Admin'],
    directGrants: [],
    directDenies: [],
    effectivePermissions: [{
      permission: 'assignments.view',
      granted: true,
      sources: ['Role Grant:Admin'],
      isCritical: false,
      descriptionAr: 'عرض التعيينات'
    }]
  };
  const catalog = [{
    capability: 'assignments',
    permissions: [{
      name: 'assignments.view', capability: 'assignments', descriptionAr: 'عرض التعيينات',
      descriptionEn: 'View assignments', isCritical: false, isActive: true
    }]
  }];
  const roles = [{
    id: 'role-1', role: 'Admin', name: 'Admin', description: null,
    isSystemRole: true, isActive: true, assignedUsers: 1, permissions: ['assignments.view']
  }];

  function createComponent(returnUrl: string | null = '/admin/users?q=ashraf', stateReturnUrl?: string): { component: UserAuthorizationPageComponent; router: any } {
    const route = {
      snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) },
      paramMap: of(convertToParamMap({ id: 'user-1' }))
    };
    const router = {
      getCurrentNavigation: () => stateReturnUrl ? { extras: { state: { returnUrl: stateReturnUrl } } } : null,
      navigateByUrl: jasmine.createSpy('navigateByUrl')
    };
    const admin = {
      getUserAuthorization: () => of(authorization),
      getRoles: () => of(roles),
      getPermissionCatalog: () => of(catalog),
      replaceUserAuthorization: jasmine.createSpy('replaceUserAuthorization').and.returnValue(of(undefined))
    };
    const permissions = { has: () => true };
    const component = new UserAuthorizationPageComponent(
      route as any,
      router as any,
      admin as any,
      permissions as any,
      { confirm: () => true } as any
    );
    component.ngOnInit();
    return { component, router };
  }

  it('returns to the preserved users-list context and uses the safe fallback when absent', () => {
    const preserved = createComponent();
    preserved.component.backToUsers();
    expect(preserved.router.navigateByUrl).toHaveBeenCalledOnceWith('/admin/users?q=ashraf');

    const fallback = createComponent(null);
    fallback.component.backToUsers();
    expect(fallback.router.navigateByUrl).toHaveBeenCalledOnceWith('/admin/users');
  });

  it('accepts navigation state as the return source when no query parameter is present', () => {
    const { component, router } = createComponent(null, '/admin/users?q=factory');
    component.backToUsers();
    expect(router.navigateByUrl).toHaveBeenCalledOnceWith('/admin/users?q=factory');
  });

  it('does not select a direct grant for an inherited role permission or count it as a change', () => {
    const { component } = createComponent();
    const permission = component.permissionGroups[0].permissions[0];

    expect(permission.inheritedRoles).toEqual(['Admin']);
    expect(component.isDirectGrantSelected(permission.permission)).toBeFalse();
    expect(component.previewGranted(permission)).toBeTrue();
    expect(component.pendingChangeCount).toBe(0);
  });

  it('keeps direct grant and direct deny mutually exclusive and updates the preview immediately', () => {
    const { component } = createComponent();
    const permission = component.permissionGroups[0].permissions[0];

    component.toggleDirectDeny(permission.permission, true);
    expect(component.isDirectDenySelected(permission.permission)).toBeTrue();
    expect(component.isDirectGrantSelected(permission.permission)).toBeFalse();
    expect(component.previewGranted(permission)).toBeFalse();

    component.toggleDirectGrant(permission.permission, true);
    expect(component.isDirectGrantSelected(permission.permission)).toBeTrue();
    expect(component.isDirectDenySelected(permission.permission)).toBeFalse();
    expect(component.previewGranted(permission)).toBeTrue();
  });

  it('updates inherited-role preview immediately without creating a direct override', () => {
    const { component } = createComponent();
    component.toggleRole('Admin', false);
    const permission = component.permissionGroups[0].permissions[0];

    expect(permission.inheritedRoles).toEqual([]);
    expect(component.previewGranted(permission)).toBeFalse();
    expect(component.selectedDirectOverrideCount).toBe(0);
    expect(component.pendingChangeCount).toBe(1);
  });

  it('uses centralized Arabic capability labels instead of a raw technical heading', () => {
    const { component } = createComponent();
    expect(component.permissionGroups[0].label).toBe('التعيينات');
    expect(component.permissionGroups[0].label).not.toBe('assignments');
  });
});

describe('UserAuthorizationPageComponent DOM', () => {
  let fixture: ComponentFixture<UserAuthorizationPageComponent>;
  const authorization: AdminUserAuthorization = {
    id: 'user-1', fullName: 'Ashraf Farag', email: 'ashraf', isActive: true,
    permissionsVersion: '2026-07-17T09:00:00Z', roles: ['Admin'], directGrants: [], directDenies: [],
    effectivePermissions: [{
      permission: 'assignments.view', granted: true, sources: ['Role Grant:Admin'],
      isCritical: false, descriptionAr: 'عرض التعيينات'
    }]
  };
  const catalog = [{
    capability: 'assignments',
    permissions: [{
      name: 'assignments.view', capability: 'assignments', descriptionAr: 'عرض التعيينات',
      descriptionEn: 'View assignments', isCritical: false, isActive: true
    }]
  }];
  const roles = [{
    id: 'role-1', role: 'Admin', name: 'Admin', description: null,
    isSystemRole: true, isActive: true, assignedUsers: 1, permissions: ['assignments.view']
  }];

  beforeEach(() => {
    const route = {
      snapshot: { queryParamMap: convertToParamMap({ returnUrl: '/admin/users?q=ashraf' }) },
      paramMap: of(convertToParamMap({ id: 'user-1' }))
    };
    const admin = {
      getUserAuthorization: () => of(authorization),
      getRoles: () => of(roles),
      getPermissionCatalog: () => of(catalog)
    };
    const permissions = {
      permissions$: of(['users.manage', 'permissions.assign']),
      hydrationState$: of('ready'),
      hydrationState: 'ready',
      has: () => true,
      hasAccess: () => true
    };

    TestBed.configureTestingModule({
      imports: [IamAdminModule, RouterTestingModule, NoopAnimationsModule],
      providers: [
        { provide: ActivatedRoute, useValue: route },
        { provide: IamAdminService, useValue: admin },
        { provide: PermissionService, useValue: permissions },
        { provide: IamConfirmationService, useValue: { confirm: () => true } }
      ]
    });
    fixture = TestBed.createComponent(UserAuthorizationPageComponent);
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('renders a clear return action, usable breadcrumb, inheritance explanation, and role source', () => {
    const element: HTMLElement = fixture.nativeElement;
    const returnButton = Array.from(element.querySelectorAll('button')).find((button) => button.textContent?.includes('العودة إلى المستخدمين'));
    const breadcrumb = element.querySelector('.page-breadcrumb a') as HTMLAnchorElement;

    expect(returnButton).toBeDefined();
    expect(breadcrumb.textContent).toContain('إدارة المستخدمين');
    expect(breadcrumb.getAttribute('href')).toContain('/admin/users?q=ashraf');
    expect(element.textContent).toContain('الصلاحيات الموروثة من الأدوار لا تظهر كمنح مباشر');
    expect(element.textContent).toContain('من الدور: مدير');
    expect(element.textContent).toContain('لا توجد استثناءات مباشرة');
    expect(element.querySelector('.permission-group__header h4')?.textContent).toContain('التعيينات');
  });
});
