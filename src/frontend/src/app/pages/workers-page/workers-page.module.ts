import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { RouterModule, Routes } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PaginatorModule } from 'primeng/paginator';
import { RippleModule } from 'primeng/ripple';
import { TableModule } from 'primeng/table';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PermissionCanMatchGuard } from '../../core/guards/permission-can-match.guard';
import { SharedModule } from '../../shared/shared.module';
import { PlpProductEmptyStateComponent } from '../../shared/product/plp-empty-state.component';
import { PlpProductErrorStateComponent } from '../../shared/product/plp-error-state.component';
import { PlpProductLoadingStateComponent } from '../../shared/product/plp-loading-state.component';
import { PlpProductPageHeaderComponent } from '../../shared/product/plp-page-header.component';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpSectionNavigationComponent } from '../../shared/product/plp-section-navigation.component';
import { PlpProductToolbarComponent } from '../../shared/product/plp-toolbar.component';
import { WORKER_MANAGEMENT_DATA_SOURCE } from './worker-management.data-source';
import { WorkerManagementApiDataSource } from './worker-management-api-data-source';
import { WorkerManagementFacade } from './worker-management.facade';
import { WorkerProfileWorkspaceComponent } from './worker-profile-workspace.component';
import { WorkersPageComponent } from './workers-page.component';

export const WORKERS_ROUTES: Routes = [
  {
    path: '',
    component: WorkersPageComponent,
    canMatch: [PermissionCanMatchGuard],
    canActivate: [PermissionCanActivateGuard],
    data: {
      title: 'إدارة العاملين',
      breadcrumb: 'إدارة العاملين',
      permission: PERMISSIONS.workers.view
    }
  }
];

@NgModule({
  declarations: [WorkersPageComponent, WorkerProfileWorkspaceComponent],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule.forChild(WORKERS_ROUTES),
    SharedModule,
    ButtonModule,
    InputTextModule,
    PaginatorModule,
    RippleModule,
    TableModule,
    PlpProductEmptyStateComponent,
    PlpProductErrorStateComponent,
    PlpProductLoadingStateComponent,
    PlpProductPageHeaderComponent,
    PlpProductToolbarComponent,
    PlpResponsiveTableDirective,
    PlpSectionNavigationComponent
  ],
  providers: [
    WorkerManagementFacade,
    WorkerManagementApiDataSource,
    { provide: WORKER_MANAGEMENT_DATA_SOURCE, useExisting: WorkerManagementApiDataSource }
  ]
})
export class WorkersPageModule {}
