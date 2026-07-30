import { IAM_ADMIN_ROUTES } from './pages/admin/iam-admin.module';
import { APP_ROUTES } from './app-routing.module';
import { PERMISSIONS } from './core/config/permission-identifiers';
import { PermissionCanActivateGuard } from './core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from './core/guards/permission-can-match.guard';

describe('IAM routing', () => {
  it('protects the lazy IAM boundary with canMatch and canActivate and allows any IAM permission', () => {
    const shell = APP_ROUTES.find((route) => route.path === '');
    const admin = shell?.children?.find((route) => route.path === 'admin');

    expect(admin?.loadChildren).toBeDefined();
    expect(admin?.canMatch).toContain(PermissionCanMatchGuard);
    expect(admin?.canActivate).toContain(PermissionCanActivateGuard);
    expect(admin?.data?.['requireAny']).toEqual([
      PERMISSIONS.users.view,
      PERMISSIONS.roles.view,
      PERMISSIONS.permissions.assign,
      PERMISSIONS.notifications.policiesManage
    ]);
  });

  it('requires users.view for users route and related editor', () => {
    const users = IAM_ADMIN_ROUTES.find((route) => route.path === 'users');
    const userAuth = IAM_ADMIN_ROUTES.find((route) => route.path === 'users/:id');

    expect(users?.canActivate).toContain(PermissionCanActivateGuard);
    expect(users?.data?.['permission']).toBe(PERMISSIONS.users.view);
    expect(userAuth?.canActivate).toContain(PermissionCanActivateGuard);
    expect(userAuth?.data?.['permission']).toBe(PERMISSIONS.users.view);
  });

  it('requires roles.view for roles route', () => {
    const roles = IAM_ADMIN_ROUTES.find((route) => route.path === 'roles');

    expect(roles?.canActivate).toContain(PermissionCanActivateGuard);
    expect(roles?.data?.['permission']).toBe(PERMISSIONS.roles.view);
  });

  it('requires permissions.assign for permissions catalog route', () => {
    const permissions = IAM_ADMIN_ROUTES.find((route) => route.path === 'permissions');

    expect(permissions?.canActivate).toContain(PermissionCanActivateGuard);
    expect(permissions?.data?.['permission']).toBe(PERMISSIONS.permissions.assign);
  });

  it('requires notification policy permission for the policy studio route', () => {
    const policies = IAM_ADMIN_ROUTES.find((route) => route.path === 'notification-policies');

    expect(policies?.canActivate).toContain(PermissionCanActivateGuard);
    expect(policies?.data?.['permission']).toBe(PERMISSIONS.notifications.policiesManage);
  });

  it('contains exact navigation routes for IAM screens', () => {
    const adminUsersRoute = IAM_ADMIN_ROUTES.find((route) => route.path === 'users');
    const rolesRoute = IAM_ADMIN_ROUTES.find((route) => route.path === 'roles');
    const permissionsRoute = IAM_ADMIN_ROUTES.find((route) => route.path === 'permissions');

    expect(adminUsersRoute).toBeDefined();
    expect(rolesRoute).toBeDefined();
    expect(permissionsRoute).toBeDefined();
  });

  it('keeps manufacturing workspace lazy', () => {
    const shell = APP_ROUTES.find((route) => route.path === '');
    const manufacturing = shell?.children?.find((route) => route.path === 'manufacturing');

    expect(manufacturing?.loadChildren).toBeDefined();
    expect(manufacturing?.component).toBeUndefined();
    expect(manufacturing?.canMatch).toContain(PermissionCanMatchGuard);
    expect(manufacturing?.canActivate).toContain(PermissionCanActivateGuard);
  });

  it('keeps workers route lazy', () => {
    const shell = APP_ROUTES.find((route) => route.path === '');
    const workers = shell?.children?.find((route) => route.path === 'workers');

    expect(workers?.loadChildren).toBeDefined();
    expect(workers?.component).toBeUndefined();
    expect(workers?.canMatch).toContain(PermissionCanMatchGuard);
    expect(workers?.canActivate).toContain(PermissionCanActivateGuard);
    expect(workers?.data?.['permission']).toBe(PERMISSIONS.workers.view);
  });

  it('keeps Factory Map lazy and requires structure, stages, assignments, and attendance access', () => {
    const shell = APP_ROUTES.find((route) => route.path === '');
    const factoryMap = shell?.children?.find((route) => route.path === 'factory-map');

    expect(factoryMap?.loadChildren).toBeDefined();
    expect(factoryMap?.component).toBeUndefined();
    expect(factoryMap?.canMatch).toContain(PermissionCanMatchGuard);
    expect(factoryMap?.canActivate).toContain(PermissionCanActivateGuard);
    expect(factoryMap?.data?.['requireAll']).toEqual([
      PERMISSIONS.factoryStructure.view,
      PERMISSIONS.stages.view,
      PERMISSIONS.assignments.view,
      PERMISSIONS.attendance.view
    ]);
  });

  it('removes legacy stages, production-lines, and assignments routes', () => {
    const shell = APP_ROUTES.find((route) => route.path === '');
    const legacyPaths = ['stages', 'production-lines', 'assignments'];

    legacyPaths.forEach((path) => {
      expect(shell?.children?.some((route) => route.path === path)).toBeFalse();
    });
  });
});
