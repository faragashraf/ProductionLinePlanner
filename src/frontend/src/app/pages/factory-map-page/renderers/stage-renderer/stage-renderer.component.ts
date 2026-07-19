import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { ProductionLineLayout, SubStageLayout, MainStageLayout, WorkerLayout } from '../../../../shared/models/factory-visualization.model';
import { productionNavigationIconFor } from '../../../../shared/design-system/icons/production-icon-map';

type StageRenderMode = 'stage' | 'worker';
type StageRendererBack = 'line' | 'stage';
type AttendanceFilter =
  | 'all'
  | 'has-absence'
  | 'fully-present'
  | 'partially-present'
  | 'all-absent'
  | 'needs-sync'
  | 'no-assignments';

@Component({
  selector: 'plp-stage-renderer',
  templateUrl: './stage-renderer.component.html',
  styleUrls: ['./stage-renderer.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StageRendererComponent {
  readonly backIcon = productionNavigationIconFor('back', 'rtl');
  readonly forwardIcon = productionNavigationIconFor('forward', 'rtl');

  @Input() line!: ProductionLineLayout;
  @Input() mainStage!: MainStageLayout;
  @Input() mode: StageRenderMode = 'stage';
  @Input() subStage?: SubStageLayout;

  @Output() back = new EventEmitter<StageRendererBack>();
  @Output() subStageSelected = new EventEmitter<string>();

  attendanceFilter: AttendanceFilter = 'all';

  get filteredSubStages(): SubStageLayout[] {
    return this.mainStage.subStages.filter((subStage) => this.matchesAttendanceFilter(subStage));
  }

  get hasFilteredSubStages(): boolean {
    return this.filteredSubStages.length > 0;
  }

  setAttendanceFilter(value: string): void {
    this.attendanceFilter = this.isAttendanceFilter(value) ? value : 'all';
  }

  clearAttendanceFilter(): void {
    this.attendanceFilter = 'all';
  }

  onBackToLine(): void {
    this.back.emit('line');
  }

  onBackToStage(): void {
    this.back.emit('stage');
  }

  onSubStageSelected(subStageId: string): void {
    this.subStageSelected.emit(subStageId);
  }

  trackBySubStage(_index: number, subStage: SubStageLayout): string {
    return subStage.id;
  }

  trackByWorker(_index: number, worker: WorkerLayout): string {
    return worker.id;
  }

  private matchesAttendanceFilter(subStage: SubStageLayout): boolean {
    const assignedWorkersCount = subStage.workersCurrent ?? 0;
    const status = subStage.attendanceStatus ?? 'Unavailable';

    switch (this.attendanceFilter) {
      case 'has-absence':
        return this.hasAvailableAttendance(subStage) && assignedWorkersCount > 0
          && status !== 'NeedsSync'
          && (subStage.presentAssignedWorkers ?? 0) < assignedWorkersCount;
      case 'fully-present':
        return status === 'FullyPresent';
      case 'partially-present':
        return status === 'PartiallyPresent';
      case 'all-absent':
        return status === 'AllAbsent';
      case 'needs-sync':
        return status === 'NeedsSync';
      case 'no-assignments':
        return status === 'NoAssignments' || assignedWorkersCount === 0;
      default:
        return true;
    }
  }

  private hasAvailableAttendance(subStage: SubStageLayout): boolean {
    return subStage.attendanceSummaryAvailable === true
      && subStage.attendanceStatus !== 'NotAuthorized'
      && subStage.attendanceStatus !== 'Unavailable';
  }

  private isAttendanceFilter(value: string): value is AttendanceFilter {
    return [
      'all',
      'has-absence',
      'fully-present',
      'partially-present',
      'all-absent',
      'needs-sync',
      'no-assignments'
    ].includes(value as AttendanceFilter);
  }
}
