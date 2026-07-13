import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { CardModule } from 'primeng/card';
import { SharedModule } from '../../shared/shared.module';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { ManufacturingWorkspaceRoutingModule } from './manufacturing-workspace-routing.module';

@NgModule({
  declarations: [
    ManufacturingWorkspaceLayoutComponent,
    ManufacturingPlaceholderPageComponent
  ],
  imports: [
    CommonModule,
    SharedModule,
    CardModule,
    ManufacturingWorkspaceRoutingModule
  ]
})
export class ManufacturingWorkspaceModule {}
