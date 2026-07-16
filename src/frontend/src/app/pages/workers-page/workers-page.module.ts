import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule, Routes } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { RippleModule } from 'primeng/ripple';
import { TableModule } from 'primeng/table';
import { PermissionCanActivateGuard } from '../../core/guards/permission-can-activate.guard';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { SharedModule } from '../../shared/shared.module';
import { WorkersPageComponent } from './workers-page.component';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpTablePaginationDirective } from '../../shared/product/plp-table-pagination.directive';
import { PlpFormSheetComponent } from '../../shared/product/plp-form-sheet.component';
import { PlpFormComponent } from '../../shared/product/plp-form.component';

const WORKERS_ROUTES: Routes = [
  {
    path: '',
    component: WorkersPageComponent,
    canActivate: [PermissionCanActivateGuard],
    data: {
      title: 'العاملون',
      breadcrumb: 'العاملون',
      permission: PERMISSIONS.workers.view
    }
  }
];

@NgModule({
  declarations: [WorkersPageComponent],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    SharedModule,
    ButtonModule,
    RippleModule,
    TableModule,
    PlpResponsiveTableDirective,
    PlpTablePaginationDirective,
    PlpFormSheetComponent,
    PlpFormComponent,
    RouterModule.forChild(WORKERS_ROUTES)
  ]
})
export class WorkersPageModule {}
