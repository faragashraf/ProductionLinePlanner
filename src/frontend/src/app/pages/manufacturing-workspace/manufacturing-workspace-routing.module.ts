import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from '../../core/guards/permission-can-match.guard';
import { MANUFACTURING_WORKSPACE_ITEMS } from '../../core/config/manufacturing-workspace.config';

const [manufacturingDashboard, employees, departments, factoryStructure, stages, models, compensation] = MANUFACTURING_WORKSPACE_ITEMS;

function manufacturingRouteData(item: (typeof MANUFACTURING_WORKSPACE_ITEMS)[number]): Record<string, unknown> {
  const routeData: Record<string, unknown> = {
    title: item.label,
    breadcrumb: item.label,
    workspaceItem: item
  };

  if (item.permission !== undefined) {
    routeData['permission'] = item.permission;
  }

  if (item.requireAny !== undefined) {
    routeData['requireAny'] = item.requireAny;
  }

  return routeData;
}

export const MANUFACTURING_WORKSPACE_ROUTES: Routes = [
  {
    path: '',
    component: ManufacturingWorkspaceLayoutComponent,
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'dashboard', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(manufacturingDashboard) },
      { path: 'employees', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(employees) },
      { path: 'departments', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(departments) },
      { path: 'factory-structure', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(factoryStructure) },
      { path: 'stages', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(stages) },
      { path: 'models', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(models) },
      { path: 'compensation', component: ManufacturingPlaceholderPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(compensation) },
      { path: '**', redirectTo: 'dashboard' }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(MANUFACTURING_WORKSPACE_ROUTES)],
  exports: [RouterModule]
})
export class ManufacturingWorkspaceRoutingModule {}
