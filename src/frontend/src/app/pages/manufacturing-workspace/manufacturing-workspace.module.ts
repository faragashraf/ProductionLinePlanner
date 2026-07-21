import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { DialogModule } from 'primeng/dialog';
import { CalendarModule } from 'primeng/calendar';
import { DropdownModule } from 'primeng/dropdown';
import { TreeModule } from 'primeng/tree';
import { ContextMenuModule } from 'primeng/contextmenu';
import { OverlayPanelModule } from 'primeng/overlaypanel';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { SharedModule } from '../../shared/shared.module';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';
import { ManufacturingPlaceholderPageComponent } from './manufacturing-placeholder-page.component';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';
import { ManufacturingDepartmentsPageComponent } from './manufacturing-departments-page.component';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';
import { FactoryStructureTreeViewComponent } from './factory-structure-tree-view.component';
import { LineStaffingWorkspacePageComponent } from './line-staffing-workspace-page.component';
import { DailyProductionOperationsPageComponent } from './daily-production-operations-page.component';
import { ManufacturingWorkspaceRoutingModule } from './manufacturing-workspace-routing.module';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpTablePaginationDirective } from '../../shared/product/plp-table-pagination.directive';
import { PlpOverflowRailDirective } from '../../shared/product/plp-horizontal-overflow';
import { PlpExpandableFormComponent } from '../../shared/product/plp-expandable-form.component';
import { PlpDialogComponent } from '../../shared/product/plp-dialog.component';
import { PlpFormSheetComponent } from '../../shared/product/plp-form-sheet.component';
import { PlpSectionNavigationComponent } from '../../shared/product/plp-section-navigation.component';
import { PlpFormComponent } from '../../shared/product/plp-form.component';
import { PlpProductToolbarComponent } from '../../shared/product/plp-toolbar.component';
import { ReportsWorkspacePageComponent } from '../reports-workspace/reports-workspace-page.component';
import { ReportsFilterBarComponent } from '../reports-workspace/reports-filter-bar.component';
import { ReportsSummaryCardsComponent } from '../reports-workspace/reports-summary-cards.component';
import { ReportsResultsToolbarComponent } from '../reports-workspace/reports-results-toolbar.component';

@NgModule({
  declarations: [
    ManufacturingWorkspaceLayoutComponent,
    ManufacturingPlaceholderPageComponent,
    ProductionCostRecordingPageComponent,
    ManufacturingMasterDataPageComponent,
    ManufacturingDepartmentsPageComponent,
    FactoryStructureFoundationPageComponent,
    FactoryStructureTreeViewComponent,
    LineStaffingWorkspacePageComponent,
    DailyProductionOperationsPageComponent,
    ReportsWorkspacePageComponent,
    ReportsFilterBarComponent,
    ReportsSummaryCardsComponent,
    ReportsResultsToolbarComponent
  ],
  imports: [
    CommonModule,
    SharedModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    TableModule,
    DialogModule,
    CalendarModule,
    DropdownModule,
    TreeModule,
    ContextMenuModule,
    OverlayPanelModule,
    FormsModule,
    ReactiveFormsModule,
    PlpResponsiveTableDirective,
    PlpTablePaginationDirective,
    PlpOverflowRailDirective,
    PlpExpandableFormComponent,
    PlpDialogComponent,
    PlpFormSheetComponent,
    PlpSectionNavigationComponent,
    PlpFormComponent,
    PlpProductToolbarComponent,
    ManufacturingWorkspaceRoutingModule
  ]
})
export class ManufacturingWorkspaceModule {}
