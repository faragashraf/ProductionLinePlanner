import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { PaginatorModule } from 'primeng/paginator';
import { SharedModule } from '../../shared/shared.module';
import { PlpProductEmptyStateComponent } from '../../shared/product/plp-empty-state.component';
import { PlpProductErrorStateComponent } from '../../shared/product/plp-error-state.component';
import { PlpProductLoadingStateComponent } from '../../shared/product/plp-loading-state.component';
import { PlpProductPageHeaderComponent } from '../../shared/product/plp-page-header.component';
import { NotificationsPageComponent } from './notifications-page.component';

const routes: Routes = [
  { path: '', component: NotificationsPageComponent }
];

@NgModule({
  declarations: [NotificationsPageComponent],
  imports: [
    CommonModule,
    RouterModule.forChild(routes),
    ButtonModule,
    PaginatorModule,
    SharedModule,
    PlpProductEmptyStateComponent,
    PlpProductErrorStateComponent,
    PlpProductLoadingStateComponent,
    PlpProductPageHeaderComponent
  ]
})
export class NotificationsPageModule {}
