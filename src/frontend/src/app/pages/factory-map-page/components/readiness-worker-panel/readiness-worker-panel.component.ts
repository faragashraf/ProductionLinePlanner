import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryReadinessStore } from '../../factory-readiness.store';
import { OperationalReadinessStage, OperationalReadinessWorker, ReadinessWorkerFilter } from '../../../../shared/models/operational-readiness.model';

@Component({
  selector: 'app-readiness-worker-panel',
  templateUrl: './readiness-worker-panel.component.html',
  styleUrls: ['./readiness-worker-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReadinessWorkerPanelComponent {
  @Input({ required: true }) stage!: OperationalReadinessStage;
  readonly filters: { value: ReadinessWorkerFilter; label: string; icon: string }[] = [
    { value: 'all', label: 'الكل', icon: 'pi-users' },
    { value: 'present', label: 'حاضر', icon: 'pi-check-circle' },
    { value: 'late', label: 'متأخر', icon: 'pi-clock' },
    { value: 'absent', label: 'غائب', icon: 'pi-times-circle' },
    { value: 'checkedOut', label: 'منصرف', icon: 'pi-sign-out' }
  ];

  constructor(readonly store: FactoryReadinessStore) {}
  trackWorker(_: number, worker: OperationalReadinessWorker): string { return worker.workerId; }
}
