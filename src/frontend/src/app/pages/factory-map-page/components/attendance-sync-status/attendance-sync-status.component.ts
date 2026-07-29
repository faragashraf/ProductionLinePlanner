import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { AttendanceSyncFreshness } from '../../../../shared/models/operational-readiness.model';

@Component({
  selector: 'app-attendance-sync-status',
  templateUrl: './attendance-sync-status.component.html',
  styleUrls: ['./attendance-sync-status.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AttendanceSyncStatusComponent {
  @Input({ required: true }) sync!: AttendanceSyncFreshness;
  @Input() realtimeDisconnected = false;

  get label(): string {
    if (this.realtimeDisconnected) return 'الاتصال اللحظي منقطع';
    return ({ Fresh: 'مزامنة الحضور حديثة', Stale: 'مزامنة الحضور قديمة', Failed: 'فشلت آخر مزامنة', NeverSynced: 'لم تتم مزامنة الحضور' } as const)[this.sync.status];
  }
}
