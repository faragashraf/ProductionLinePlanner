import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-sub-stage-card',
  templateUrl: './sub-stage-card.component.html',
  styleUrls: ['./sub-stage-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SubStageCardComponent {
  @Input() name = '';
  @Input() workersCurrent = 0;
  @Input() workersRequired = 0;
  @Input() workerRequirementDefined = true;
  @Input() presentAssignedWorkers = 0;
  @Input() attendanceStatus = 'Unavailable';
  @Input() status: FactoryStatus | string = 'info';

  get percentage(): number {
    if (!this.workerRequirementDefined || this.workersRequired === 0) {
      return 0;
    }
    return Math.round((this.workersCurrent / this.workersRequired) * 100);
  }

  get workersSummary(): string {
    return this.workerRequirementDefined
      ? `${this.workersCurrent} / ${this.workersRequired}`
      : `${this.workersCurrent} مسكن - الاحتياج غير محدد`;
  }

  get staffingStatusLabel(): string {
    if (!this.workerRequirementDefined) return 'الاحتياج غير محدد';
    if (this.workersCurrent === 0) return 'دون تسكين';
    return this.workersCurrent >= this.workersRequired ? 'التسكين مكتمل' : 'التسكين ناقص';
  }

  get attendanceSummary(): string {
    if (this.attendanceStatus === 'FullyPresent') return `${this.presentAssignedWorkers} من ${this.workersCurrent} - حاضر بالكامل`;
    if (this.attendanceStatus === 'PartiallyPresent') return `${this.presentAssignedWorkers} من ${this.workersCurrent} - حضور جزئي`;
    if (this.attendanceStatus === 'AllAbsent') return `0 من ${this.workersCurrent} - جميع المسكنين غائبون`;
    if (this.attendanceStatus === 'NeedsSync') return 'تحتاج مزامنة حضور اليوم';
    if (this.attendanceStatus === 'NoAssignments') return 'لا يوجد عمال مسكنون';
    if (this.attendanceStatus === 'NotAuthorized') return 'غير متاح بالصلاحية';
    return 'بيانات الحضور غير متاحة';
  }

  get attendancePercentage(): number | null {
    if (!this.hasAttendancePercentage) {
      return null;
    }

    return Math.round((this.presentAssignedWorkers / this.workersCurrent) * 100);
  }

  get hasAttendancePercentage(): boolean {
    return this.workersCurrent > 0
      && !['NeedsSync', 'NoAssignments', 'NotAuthorized', 'Unavailable'].includes(this.attendanceStatus);
  }

  get attendanceStatusTone(): FactoryStatus {
    if (this.attendanceStatus === 'FullyPresent') return 'present';
    if (this.attendanceStatus === 'PartiallyPresent') return 'late';
    if (this.attendanceStatus === 'AllAbsent') return 'absent';
    return 'info';
  }
}
