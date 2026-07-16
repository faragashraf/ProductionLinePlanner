import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus, deriveStatusFromReadiness } from '../../models/factory-status.model';
import { clampPercent } from '../../utils/number.utils';

@Component({
  selector: 'plp-factory-card',
  templateUrl: './factory-card.component.html',
  styleUrls: ['./factory-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FactoryCardComponent {
  @Input() title = '';
  @Input() subtitle = '';
  @Input() readinessPercent = 0;
  @Input() workersCurrent = 0;
  @Input() workersRequired = 0;
  @Input() metadataLabel = 'العاملون';
  @Input() metadataValue: string | number | null = null;
  @Input() status: FactoryStatus | string = 'info';

  get clampedReadiness(): number {
    return clampPercent(this.readinessPercent);
  }

  get resolvedStatus(): FactoryStatus | string {
    return this.status || deriveStatusFromReadiness(this.clampedReadiness);
  }

  get resolvedMetadataValue(): string | number {
    return this.metadataValue ?? `${this.workersCurrent} / ${this.workersRequired}`;
  }
}
