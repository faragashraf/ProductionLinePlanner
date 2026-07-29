import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { OperationalReadinessWorker } from '../../../../shared/models/operational-readiness.model';

@Component({
  selector: 'app-worker-attendance-status',
  templateUrl: './worker-attendance-status.component.html',
  styleUrls: ['./worker-attendance-status.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerAttendanceStatusComponent {
  @Input({ required: true }) worker!: OperationalReadinessWorker;

  get icon(): string {
    return ({ Present: 'pi-check-circle', Late: 'pi-clock', Absent: 'pi-times-circle', NotCheckedIn: 'pi-times-circle', CheckedOut: 'pi-sign-out', Unknown: 'pi-question-circle' } as const)[this.worker.attendanceState];
  }
}
