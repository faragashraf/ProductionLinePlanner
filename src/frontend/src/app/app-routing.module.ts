import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { DashboardPageComponent } from './pages/dashboard-page/dashboard-page.component';
import { FactoryMapPageComponent } from './pages/factory-map-page/factory-map-page.component';
import { NotificationsPageComponent } from './pages/notifications-page/notifications-page.component';
import { LoginPageComponent } from './pages/login-page/login-page.component';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { AccessDeniedPageComponent } from './pages/access-denied-page/access-denied-page.component';
import { AuthGuard } from './core/guards/auth.guard';
import { PermissionCanActivateGuard } from './core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from './core/guards/permission-can-match.guard';
import { PERMISSIONS } from './core/config/permission-identifiers';
import { MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS } from './core/config/manufacturing-workspace.config';

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
        loadChildren: () => import('./pages/manufacturing-workspace/manufacturing-workspace.module').then((module) => module.ManufacturingWorkspaceModule),
        canMatch: [PermissionCanMatchGuard],
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'مساحة التصنيع',
          breadcrumb: 'مساحة التصنيع',
          requireAny: [...MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS]
        }
      },
      {
        path: 'factory-map',
        component: FactoryMapPageComponent,
        canActivate: [PermissionCanActivateGuard],
        data: {
          title: 'خريطة المصنع',
          breadcrumb: 'خريطة المصنع',
          requireAll: [PERMISSIONS.factoryStructure.view, PERMISSIONS.stages.view]
        }
      },
      {
        path: 'workers',
        loadChildren: () => import('./pages/workers-page/workers-page.module').then((module) => module.WorkersPageModule),
        canMatch: [PermissionCanMatchGuard],
        canActivate: [PermissionCanActivateGuard],
        data: { title: 'إدارة العاملين', breadcrumb: 'إدارة العاملين', permission: PERMISSIONS.workers.view }
      },
      {
        path: 'attendance/workforce',
        loadChildren: () => import('./pages/attendance-workforce-page/attendance-workforce-page.module').then((module) => module.AttendanceWorkforcePageModule),
        canMatch: [PermissionCanMatchGuard],
        canActivate: [PermissionCanActivateGuard],
        data: { title: 'الحضور والتسكين اليومي', breadcrumb: 'الحضور والتسكين اليومي', requireAll: [PERMISSIONS.attendance.view, PERMISSIONS.assignments.view] }
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
