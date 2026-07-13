import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';
import { ManufacturingDepartmentsPageComponent } from './manufacturing-departments-page.component';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';
import { ManufacturingCompensationPageComponent } from './manufacturing-compensation-page.component';
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
      {
        path: 'employees',
        loadChildren: () => import('../workers-page/workers-page.module').then((module) => module.WorkersPageModule),
        canMatch: [PermissionCanMatchGuard],
        canActivate: [PermissionCanActivateGuard],
        data: manufacturingRouteData(employees)
      },
      { path: 'departments', component: ManufacturingDepartmentsPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(departments) },
      { path: 'factory-structure', component: FactoryStructureFoundationPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(factoryStructure) },
      { path: 'stages', component: ManufacturingMasterDataPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(stages) },
      { path: 'models', component: ManufacturingMasterDataPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(models) },
      { path: 'compensation', component: ManufacturingCompensationPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: manufacturingRouteData(compensation) },
      { path: 'orders', component: ProductionCostRecordingPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: { title: 'أوامر الإنتاج', breadcrumb: 'أوامر الإنتاج', permission: 'production.view' } },
      { path: 'production-recording', component: ProductionCostRecordingPageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: { title: 'تسجيل تكلفة الإنتاج', breadcrumb: 'تسجيل تكلفة الإنتاج', permission: 'production.view' } },
      { path: '**', redirectTo: 'dashboard' }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(MANUFACTURING_WORKSPACE_ROUTES)],
  exports: [RouterModule]
})
export class ManufacturingWorkspaceRoutingModule {}
