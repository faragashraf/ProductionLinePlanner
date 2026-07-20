import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from '../../core/guards/permission-can-match.guard';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';
import { ManufacturingDepartmentsPageComponent } from './manufacturing-departments-page.component';
import { LineStaffingWorkspacePageComponent } from './line-staffing-workspace-page.component';
import { DailyProductionOperationsPageComponent } from './daily-production-operations-page.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';
import { ReportsWorkspacePageComponent } from '../reports-workspace/reports-workspace-page.component';
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

  it('redirects the removed compensation screen to model setup without a dead route', () => {
    const route = childRoutes.find(item => item.path === 'compensation');

    expect(route?.redirectTo).toBe('models');
    expect(route?.pathMatch).toBe('full');
    expect(route?.component).toBeUndefined();
  });

  it('requires production view and recording permission for the Production Recording route', () => {
    const route = childRoutes.find(item => item.path === 'production-recording');

    expect(route?.component).toBe(ProductionCostRecordingPageComponent);
    expect(route?.canMatch).toContain(PermissionCanMatchGuard);
    expect(route?.canActivate).toContain(PermissionCanActivateGuard);
    expect(route?.data?.['requireAll']).toEqual([PERMISSIONS.production.view, PERMISSIONS.production.record]);
  });

  it('routes line staffing to its dedicated attendance-free workspace with the required planning permissions', () => {
    const route = childRoutes.find(item => item.path === 'line-staffing');

    expect(route?.component).toBe(LineStaffingWorkspacePageComponent);
    expect(route?.canMatch).toContain(PermissionCanMatchGuard);
    expect(route?.canActivate).toContain(PermissionCanActivateGuard);
    expect(route?.data?.['requireAll']).toEqual([
      PERMISSIONS.factoryStructure.view,
      PERMISSIONS.models.view,
      PERMISSIONS.workers.view,
      PERMISSIONS.assignments.view
    ]);
  });

  it('routes daily production to the focused multi-stage workspace with production view and record access', () => {
    const route = childRoutes.find(item => item.path === 'daily-production-operations');

    expect(route?.component).toBe(DailyProductionOperationsPageComponent);
    expect(route?.canMatch).toContain(PermissionCanMatchGuard);
    expect(route?.canActivate).toContain(PermissionCanActivateGuard);
    expect(route?.data?.['requireAll']).toEqual([PERMISSIONS.production.view, PERMISSIONS.production.record]);
  });

  it('routes reports to the quantities workspace with the dedicated report permission', () => {
    const route = childRoutes.find(item => item.path === 'reports');

    expect(route?.component).toBe(ReportsWorkspacePageComponent);
    expect(route?.canMatch).toContain(PermissionCanMatchGuard);
    expect(route?.canActivate).toContain(PermissionCanActivateGuard);
    expect(route?.data?.['permission']).toBe(PERMISSIONS.reports.productionView);
  });
});
