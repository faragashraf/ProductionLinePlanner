import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-notification-card',
  templateUrl: './notification-card.component.html',
  styleUrls: ['./notification-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NotificationCardComponent {
  @Input() title = '';
  @Input() message = '';
  @Input() severity: FactoryStatus | string = 'info';
  @Input() isRead = false;

  get readStateClass(): string {
    return this.isRead ? 'plp-notification-card--read' : 'plp-notification-card--unread';
  }
}
