import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule, Routes } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { RippleModule } from 'primeng/ripple';
import { TableModule } from 'primeng/table';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from '../../core/guards/permission-can-match.guard';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { SharedModule } from '../../shared/shared.module';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpTablePaginationDirective } from '../../shared/product/plp-table-pagination.directive';
import { PlpProductPageHeaderComponent } from '../../shared/product/plp-page-header.component';
import { PlpProductLoadingStateComponent } from '../../shared/product/plp-loading-state.component';
import { PlpProductErrorStateComponent } from '../../shared/product/plp-error-state.component';
import { PlpProductEmptyStateComponent } from '../../shared/product/plp-empty-state.component';
import { AttendanceWorkforcePageComponent } from './attendance-workforce-page.component';

const routes: Routes = [{ path: '', component: AttendanceWorkforcePageComponent, canMatch: [PermissionCanMatchGuard], canActivate: [PermissionCanActivateGuard], data: { title: 'الحضور والتسكين اليومي', breadcrumb: 'الحضور والتسكين اليومي', requireAll: [PERMISSIONS.attendance.view, PERMISSIONS.assignments.view] } }];

@NgModule({ declarations: [AttendanceWorkforcePageComponent], imports: [CommonModule, FormsModule, RouterModule.forChild(routes), SharedModule, ButtonModule, InputTextModule, RippleModule, TableModule, PlpResponsiveTableDirective, PlpTablePaginationDirective, PlpProductPageHeaderComponent, PlpProductLoadingStateComponent, PlpProductErrorStateComponent, PlpProductEmptyStateComponent] })
export class AttendanceWorkforcePageModule {}
