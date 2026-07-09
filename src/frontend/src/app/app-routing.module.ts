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
import { AuthGuard } from './core/guards/auth.guard';
import { RoleGuard } from './core/guards/role.guard';

const routes: Routes = [
  { path: 'login', component: LoginPageComponent, data: { title: 'تسجيل الدخول', breadcrumb: 'تسجيل الدخول' } },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [AuthGuard],
    data: { title: 'Main Shell', breadcrumb: 'لوحة التحكم' },
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'dashboard', component: DashboardPageComponent, data: { title: 'لوحة التحكم', breadcrumb: 'لوحة التحكم' } },
      { path: 'factory-map', component: FactoryMapPageComponent, data: { title: 'خريطة المصنع', breadcrumb: 'خريطة المصنع' } },
      { path: 'production-lines', component: ProductionLinesPageComponent, data: { title: 'خطوط الإنتاج', breadcrumb: 'خطوط الإنتاج', roles: ['Admin', 'SuperAdmin'] } },
      { path: 'stages', component: StagesPageComponent, data: { title: 'المراحل', breadcrumb: 'المراحل' } },
      { path: 'workers', component: WorkersPageComponent, data: { title: 'العاملون', breadcrumb: 'العاملون', roles: ['Admin', 'SuperAdmin'] } },
      { path: 'assignments', component: AssignmentsPageComponent, data: { title: 'التعيينات', breadcrumb: 'التعيينات', roles: ['Admin', 'SuperAdmin'] }, canActivate: [RoleGuard] },
      { path: 'notifications', component: NotificationsPageComponent, data: { title: 'الإشعارات', breadcrumb: 'الإشعارات' } },
      { path: '**', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: 'login' }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
