import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-assignment-card',
  templateUrl: './assignment-card.component.html',
  styleUrls: ['./assignment-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AssignmentCardComponent {
  @Input() worker = '';
  @Input() fromStage = '';
  @Input() toStage = '';
  @Input() assignmentType = 'ثابت';
  @Input() status: FactoryStatus | string = 'ready';
}
