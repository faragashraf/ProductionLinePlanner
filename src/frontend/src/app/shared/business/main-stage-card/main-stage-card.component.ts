import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-main-stage-card',
  templateUrl: './main-stage-card.component.html',
  styleUrls: ['./main-stage-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MainStageCardComponent {
  @Input() name = '';
  @Input() workersCurrent = 0;
  @Input() workersRequired = 0;
  @Input() status: FactoryStatus | string = 'info';
  @Input() note = '';

  get readinessPercent(): number {
    if (!this.workersRequired) {
      return 0;
    }
    return Math.round((this.workersCurrent / this.workersRequired) * 100);
  }
}
