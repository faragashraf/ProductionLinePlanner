import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { DialogModule } from 'primeng/dialog';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { SharedModule } from '../../shared/shared.module';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';
import { ManufacturingDepartmentsPageComponent } from './manufacturing-departments-page.component';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';
import { ManufacturingCompensationPageComponent } from './manufacturing-compensation-page.component';
import { ManufacturingWorkspaceRoutingModule } from './manufacturing-workspace-routing.module';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpTablePaginationDirective } from '../../shared/product/plp-table-pagination.directive';
import { PlpOverflowRailDirective } from '../../shared/product/plp-horizontal-overflow';
import { PlpExpandableFormComponent } from '../../shared/product/plp-expandable-form.component';

@NgModule({
  declarations: [
    ManufacturingWorkspaceLayoutComponent,
    ManufacturingPlaceholderPageComponent,
    ProductionCostRecordingPageComponent,
    ManufacturingMasterDataPageComponent,
    ManufacturingDepartmentsPageComponent,
    FactoryStructureFoundationPageComponent,
    ManufacturingCompensationPageComponent
  ],
  imports: [
    CommonModule,
    SharedModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    TableModule,
    DialogModule,
    FormsModule,
    ReactiveFormsModule,
    PlpResponsiveTableDirective,
    PlpTablePaginationDirective,
    PlpOverflowRailDirective,
    PlpExpandableFormComponent,
    ManufacturingWorkspaceRoutingModule
  ]
})
export class ManufacturingWorkspaceModule {}
