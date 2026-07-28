import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
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
  @Input() icon = 'pi pi-bell';
  @Input() workerName: string | null = null;
  @Input() employeeCode: string | null = null;
  @Input() attendanceTime: string | null = null;
  @Input() assignmentLabel: string | null = null;
  @Input() createdAtLabel = '';
  @Input() actionable = false;
  @Output() activate = new EventEmitter<void>();

  get readStateClass(): string {
    return this.isRead ? 'plp-notification-card--read' : 'plp-notification-card--unread';
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.actionable || (event.key !== 'Enter' && event.key !== ' ')) return;
    event.preventDefault();
    this.activate.emit();
  }
}
