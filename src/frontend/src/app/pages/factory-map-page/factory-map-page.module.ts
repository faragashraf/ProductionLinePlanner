import { ScrollingModule } from '@angular/cdk/scrolling';
import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterModule, Routes } from '@angular/router';
import { MultiSelectModule } from 'primeng/multiselect';
import { AttendanceSyncStatusComponent } from './components/attendance-sync-status/attendance-sync-status.component';
import { FactoryReadinessMapComponent } from './components/factory-readiness-map/factory-readiness-map.component';
import { ReadinessMetricsComponent } from './components/readiness-metrics/readiness-metrics.component';
import { ReadinessModelSelectorComponent } from './components/readiness-model-selector/readiness-model-selector.component';
import { ReadinessNodeCardComponent } from './components/readiness-node-card/readiness-node-card.component';
import { ReadinessStageFilterComponent } from './components/readiness-stage-filter/readiness-stage-filter.component';
import { ReadinessWorkerPanelComponent } from './components/readiness-worker-panel/readiness-worker-panel.component';
import { WorkerAttendanceStatusComponent } from './components/worker-attendance-status/worker-attendance-status.component';
import { FactoryMapPageComponent } from './factory-map-page.component';

export const FACTORY_MAP_ROUTES: Routes = [{ path: '', component: FactoryMapPageComponent }];

@NgModule({
  declarations: [
    FactoryMapPageComponent,
    FactoryReadinessMapComponent,
    ReadinessNodeCardComponent,
    ReadinessStageFilterComponent,
    ReadinessMetricsComponent,
    ReadinessModelSelectorComponent,
    ReadinessWorkerPanelComponent,
    WorkerAttendanceStatusComponent,
    AttendanceSyncStatusComponent
  ],
  imports: [CommonModule, FormsModule, MultiSelectModule, ScrollingModule, RouterModule.forChild(FACTORY_MAP_ROUTES)]
})
export class FactoryMapPageModule {}
