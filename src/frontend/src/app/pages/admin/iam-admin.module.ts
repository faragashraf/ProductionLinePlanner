import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { RippleModule } from 'primeng/ripple';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { CheckboxModule } from 'primeng/checkbox';
import { MultiSelectModule } from 'primeng/multiselect';
import { TagModule } from 'primeng/tag';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { SharedModule } from '../../shared/shared.module';
import { PermissionCatalogPageComponent } from './permission-catalog-page/permission-catalog-page.component';
import { AdminRolesPageComponent } from './roles-page/admin-roles-page.component';
import { UserAuthorizationPageComponent } from './user-authorization-page/user-authorization-page.component';
import { AdminUsersPageComponent } from './users-page/admin-users-page.component';
import { PlpDialogComponent } from '../../shared/product/plp-dialog.component';
import { PlpFormFieldComponent } from '../../shared/product/plp-form-field.component';
import { PlpProductEmptyStateComponent } from '../../shared/product/plp-empty-state.component';
import { PlpProductErrorStateComponent } from '../../shared/product/plp-error-state.component';
import { PlpProductPageHeaderComponent } from '../../shared/product/plp-page-header.component';

export const IAM_ADMIN_ROUTES: Routes = [
  { path: 'users', component: AdminUsersPageComponent, canActivate: [PermissionCanActivateGuard], data: { title: 'إدارة المستخدمين', breadcrumb: 'إدارة المستخدمين', permission: PERMISSIONS.users.view } },
  { path: 'users/:id', component: UserAuthorizationPageComponent, canActivate: [PermissionCanActivateGuard], data: { title: 'صلاحيات المستخدم', breadcrumb: 'صلاحيات المستخدم', permission: PERMISSIONS.users.view } },
  { path: 'roles', component: AdminRolesPageComponent, canActivate: [PermissionCanActivateGuard], data: { title: 'إدارة الأدوار', breadcrumb: 'إدارة الأدوار', permission: PERMISSIONS.roles.view } },
  { path: 'permissions', component: PermissionCatalogPageComponent, canActivate: [PermissionCanActivateGuard], data: { title: 'كتالوج الصلاحيات', breadcrumb: 'كتالوج الصلاحيات', permission: PERMISSIONS.permissions.assign } },
  { path: '', pathMatch: 'full', redirectTo: 'users' }
];

@NgModule({
  declarations: [AdminUsersPageComponent, UserAuthorizationPageComponent, AdminRolesPageComponent, PermissionCatalogPageComponent],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    RouterModule.forChild(IAM_ADMIN_ROUTES),
    ButtonModule,
    CheckboxModule,
    InputTextModule,
    MultiSelectModule,
    RippleModule,
    TagModule,
    SharedModule,
    PlpDialogComponent,
    PlpFormFieldComponent,
    PlpProductEmptyStateComponent,
    PlpProductErrorStateComponent,
    PlpProductPageHeaderComponent
  ]
})
export class IamAdminModule {}
