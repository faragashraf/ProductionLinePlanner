import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { SharedModule } from '../../shared/shared.module';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';
import { ManufacturingWorkspaceRoutingModule } from './manufacturing-workspace-routing.module';

@NgModule({
  declarations: [
    ManufacturingWorkspaceLayoutComponent,
    ManufacturingPlaceholderPageComponent,
    ProductionCostRecordingPageComponent,
    ManufacturingMasterDataPageComponent
  ],
  imports: [
    CommonModule,
    SharedModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    TableModule,
    FormsModule,
    ReactiveFormsModule,
    ManufacturingWorkspaceRoutingModule
  ]
})
export class ManufacturingWorkspaceModule {}
