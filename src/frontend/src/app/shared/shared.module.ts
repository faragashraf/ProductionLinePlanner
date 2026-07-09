import { CommonModule } from '@angular/common';
import { NgModule } from '@angular/core';
import { EmptyStateComponent } from './ui/empty-state/empty-state.component';
import { ErrorStateComponent } from './ui/error-state/error-state.component';
import { WorkerAvatarComponent } from './ui/worker-avatar/worker-avatar.component';
import { PageHeaderComponent } from './ui/page-header/page-header.component';
import { KpiCardComponent } from './ui/kpi-card/kpi-card.component';
import { StatisticCardComponent } from './ui/statistic-card/statistic-card.component';
import { CompletionBarComponent } from './ui/completion-bar/completion-bar.component';
import { StatusBadgeComponent } from './ui/status-badge/status-badge.component';
import { ReadinessRingComponent } from './ui/readiness-ring/readiness-ring.component';
import { LoadingSkeletonComponent } from './ui/loading-skeleton/loading-skeleton.component';
import { ResponsiveGridComponent } from './ui/responsive-grid/responsive-grid.component';
import { ToolbarShellComponent } from './ui/toolbar-shell/toolbar-shell.component';
import { TimelineCardComponent } from './ui/timeline-card/timeline-card.component';
import { FactoryCardComponent } from './business/factory-card/factory-card.component';
import { ProductionLineCardComponent } from './business/production-line-card/production-line-card.component';
import { MainStageCardComponent } from './business/main-stage-card/main-stage-card.component';
import { SubStageCardComponent } from './business/sub-stage-card/sub-stage-card.component';
import { WorkerCardComponent } from './business/worker-card/worker-card.component';
import { AssignmentCardComponent } from './business/assignment-card/assignment-card.component';
import { NotificationCardComponent } from './business/notification-card/notification-card.component';

@NgModule({
  declarations: [
    EmptyStateComponent,
    ErrorStateComponent,
    WorkerAvatarComponent,
    PageHeaderComponent,
    KpiCardComponent,
    StatisticCardComponent,
    CompletionBarComponent,
    StatusBadgeComponent,
    ReadinessRingComponent,
    LoadingSkeletonComponent,
    ResponsiveGridComponent,
    ToolbarShellComponent,
    TimelineCardComponent,
    FactoryCardComponent,
    ProductionLineCardComponent,
    MainStageCardComponent,
    SubStageCardComponent,
    WorkerCardComponent,
    AssignmentCardComponent,
    NotificationCardComponent
  ],
  imports: [CommonModule],
  exports: [
    EmptyStateComponent,
    ErrorStateComponent,
    WorkerAvatarComponent,
    PageHeaderComponent,
    KpiCardComponent,
    StatisticCardComponent,
    CompletionBarComponent,
    StatusBadgeComponent,
    ReadinessRingComponent,
    LoadingSkeletonComponent,
    ResponsiveGridComponent,
    ToolbarShellComponent,
    TimelineCardComponent,
    FactoryCardComponent,
    ProductionLineCardComponent,
    MainStageCardComponent,
    SubStageCardComponent,
    WorkerCardComponent,
    AssignmentCardComponent,
    NotificationCardComponent
  ]
})
export class SharedModule {}
