import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from '../../core/guards/permission-can-match.guard';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';
import { ManufacturingDepartmentsPageComponent } from './manufacturing-departments-page.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { MANUFACTURING_WORKSPACE_ROUTES } from './manufacturing-workspace-routing.module';

describe('ManufacturingWorkspaceRoutingModule', () => {
  const childRoutes = MANUFACTURING_WORKSPACE_ROUTES[0].children ?? [];

  it('routes departments to the real departments page with departments.view permission', () => {
    const route = childRoutes.find(item => item.path === 'departments');

    expect(route?.component).toBe(ManufacturingDepartmentsPageComponent);
    expect(route?.component).not.toBe(ManufacturingPlaceholderPageComponent);
    expect(route?.canMatch).toContain(PermissionCanMatchGuard);
    expect(route?.canActivate).toContain(PermissionCanActivateGuard);
    expect(route?.data?.['permission']).toBe(PERMISSIONS.departments.view);
  });

  it('keeps the workers route lazy-loaded and unchanged', () => {
    const route = childRoutes.find(item => item.path === 'employees');

    expect(route?.component).toBeUndefined();
    expect(route?.loadChildren).toBeDefined();
    expect(route?.data?.['permission']).toBe(PERMISSIONS.workers.view);
  });

  it('routes factory structure to the operational foundation page with factory-structure.view permission', () => {
    const route = childRoutes.find(item => item.path === 'factory-structure');

    expect(route?.component).toBe(FactoryStructureFoundationPageComponent);
    expect(route?.component).not.toBe(ManufacturingPlaceholderPageComponent);
    expect(route?.canMatch).toContain(PermissionCanMatchGuard);
    expect(route?.canActivate).toContain(PermissionCanActivateGuard);
    expect(route?.data?.['permission']).toBe(PERMISSIONS.factoryStructure.view);
  });
});
