import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { AttendanceSyncFreshness } from '../../models/operational-readiness.model';

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
    if (this.sync.status === 'Failed' && this.sync.isTrusted) return 'فشلت آخر محاولة؛ بيانات الحضور السابقة ما زالت حديثة';
    return ({ Fresh: 'مزامنة الحضور حديثة', RecordsAvailable: 'سجلات حضور اليوم متاحة', Stale: 'مزامنة الحضور قديمة', Failed: 'فشلت آخر مزامنة', NeverSynced: 'لم تتم مزامنة الحضور' } as const)[this.sync.status];
  }
}
