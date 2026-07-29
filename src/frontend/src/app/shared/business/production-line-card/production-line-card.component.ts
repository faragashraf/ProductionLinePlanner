import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-production-line-card',
  templateUrl: './production-line-card.component.html',
  styleUrls: ['./production-line-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProductionLineCardComponent {
  @Input() lineName = '';
  @Input() statusText = '';
  @Input() readinessPercent = 0;
  @Input() totalStages = 0;
  @Input() activeStage = '';
  @Input() status: FactoryStatus | string = 'info';
  @Input() progressLabel = 'جاهزية';
}
