import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { DashboardPageComponent } from './pages/dashboard-page/dashboard-page.component';
import { FactoryMapPageComponent } from './pages/factory-map-page/factory-map-page.component';
import { ProductionLinesPageComponent } from './pages/production-lines-page/production-lines-page.component';
import { StagesPageComponent } from './pages/stages-page/stages-page.component';
import { WorkersPageComponent } from './pages/workers-page/workers-page.component';
import { AssignmentsPageComponent } from './pages/assignments-page/assignments-page.component';
import { NotificationsPageComponent } from './pages/notifications-page/notifications-page.component';
import { LoginPageComponent } from './pages/login-page/login-page.component';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { AccessDeniedPageComponent } from './pages/access-denied-page/access-denied-page.component';
import { AuthGuard } from './core/guards/auth.guard';
import { PermissionCanActivateGuard } from './core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from './core/guards/permission-can-match.guard';
import { PERMISSIONS } from './core/config/permission-identifiers';
import { ManufacturingWorkspaceLayoutComponent } from './pages/manufacturing-workspace/manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './pages/manufacturing-workspace/manufacturing-placeholder-page.component';
import { MANUFACTURING_WORKSPACE_ITEMS, MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS } from './core/config/manufacturing-workspace.config';

const [manufacturingDashboard, employees, departments, factoryStructure, stages, models, compensation] = MANUFACTURING_WORKSPACE_ITEMS;

function manufacturingRouteData(item: typeof manufacturingDashboard): Record<string, unknown> {
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

export const APP_ROUTES: Routes = [
  { path: 'login', component: LoginPageComponent, data: { title: 'تسجيل الدخول', breadcrumb: 'تسجيل الدخول' } },
  { path: '403', component: AccessDeniedPageComponent, data: { title: 'غير مصرح', breadcrumb: '403' } },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [AuthGuard],
    data: { title: 'Main Shell', breadcrumb: 'لوحة التحكم' },
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'dashboard', component: DashboardPageComponent, data: { title: 'لوحة التحكم', breadcrumb: 'لوحة التحكم' } },
      {
        path: 'manufacturing',
        component: ManufacturingWorkspaceLayoutComponent,
        canMatch: [PermissionCanMatchGuard],
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'مساحة التصنيع',
          breadcrumb: 'مساحة التصنيع',
          requireAny: [...MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS]
        },
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
      },
      {
        path: 'factory-map',
        component: FactoryMapPageComponent,
        canActivate: [PermissionCanActivateGuard],
        data: { title: 'خريطة المصنع', breadcrumb: 'خريطة المصنع', permission: PERMISSIONS.factoryStructure.view }
      },
      {
        path: 'production-lines',
        component: ProductionLinesPageComponent,
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'خطوط الإنتاج',
          breadcrumb: 'خطوط الإنتاج',
          permission: PERMISSIONS.factoryStructure.view
        }
      },
      {
        path: 'stages',
        component: StagesPageComponent,
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'المراحل',
          breadcrumb: 'المراحل',
          permission: PERMISSIONS.stages.view
        }
      },
      {
        path: 'workers',
        component: WorkersPageComponent,
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'العاملون',
          breadcrumb: 'العاملون',
          permission: PERMISSIONS.workers.view
        }
      },
      {
        path: 'assignments',
        component: AssignmentsPageComponent,
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'التعيينات',
          breadcrumb: 'التعيينات',
          permission: PERMISSIONS.assignments.view
        }
      },
      {
        path: 'admin',
        loadChildren: () => import('./pages/admin/iam-admin.module').then((module) => module.IamAdminModule),
        canMatch: [PermissionCanMatchGuard],
        canActivate: [PermissionCanActivateGuard],
        data: {
          requireAny: [PERMISSIONS.users.view, PERMISSIONS.roles.view, PERMISSIONS.permissions.assign]
        }
      },
      { path: 'notifications', component: NotificationsPageComponent, data: { title: 'الإشعارات', breadcrumb: 'الإشعارات' } },
      { path: '**', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: 'login' }
];

@NgModule({
  imports: [RouterModule.forRoot(APP_ROUTES)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
