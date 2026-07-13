import { APP_ROUTES } from './app-routing.module';
import { PermissionCanActivateGuard } from './core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from './core/guards/permission-can-match.guard';

describe('IAM routing', () => {
  it('protects the lazy IAM boundary with canMatch and its direct routes with canActivate', () => {
    const shell = APP_ROUTES.find((route) => route.path === '');
    const admin = shell?.children?.find((route) => route.path === 'admin');
    const assignments = shell?.children?.find((route) => route.path === 'assignments');

    expect(admin?.loadChildren).toBeDefined();
    expect(admin?.canMatch).toContain(PermissionCanMatchGuard);
    expect(admin?.canActivate).toContain(PermissionCanActivateGuard);
    expect(assignments?.canActivate).toContain(PermissionCanActivateGuard);
  });
});
