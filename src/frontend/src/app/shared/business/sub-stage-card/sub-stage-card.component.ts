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
  @Input() status: FactoryStatus | string = 'info';

  get percentage(): number {
    if (this.workersRequired === 0) {
      return 0;
    }
    return Math.round((this.workersCurrent / this.workersRequired) * 100);
  }
}
