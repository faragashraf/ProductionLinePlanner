import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { clampPercent } from '../../utils/number.utils';
import { FactoryStatus, deriveStatusFromReadiness, resolveFactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-readiness-ring',
  templateUrl: './readiness-ring.component.html',
  styleUrls: ['./readiness-ring.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReadinessRingComponent {
  @Input() value = 0;
  @Input() status: FactoryStatus | string = 'info';
  @Input() size = 86;
  @Input() showLabel = true;
  @Input() showIcon = false;

  get normalized(): number {
    return clampPercent(this.value);
  }

  get statusMeta() {
    return resolveFactoryStatus(this.status || deriveStatusFromReadiness(this.normalized));
  }

  get toneClass(): string {
    return `plp-readiness-ring--${this.statusMeta.toneClass}`;
  }
}
