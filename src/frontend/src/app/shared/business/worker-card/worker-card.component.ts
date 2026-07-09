import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-worker-card',
  templateUrl: './worker-card.component.html',
  styleUrls: ['./worker-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerCardComponent {
  @Input() fullName = '';
  @Input() code = '';
  @Input() status: FactoryStatus | string = 'info';
  @Input() assignmentType = 'غير محدد';
  @Input() lastActivity = '';
}
