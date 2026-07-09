import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-worker-card',
  templateUrl: './worker-card.component.html',
  styleUrls: ['./worker-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerCardComponent {
  @Input() fullName: unknown = '';
  @Input() code: unknown = '';
  @Input() status: FactoryStatus | string = 'info';
  @Input() assignmentType: unknown = 'غير محدد';
  @Input() lastActivity: unknown = '';

  get safeFullName(): string {
    return this.coerceLabel(this.fullName);
  }

  get safeCode(): string {
    return this.coerceLabel(this.code);
  }

  get safeAssignmentType(): string {
    return this.coerceLabel(this.assignmentType);
  }

  get safeLastActivity(): string {
    return this.coerceLabel(this.lastActivity);
  }

  private coerceLabel(value: unknown): string {
    if (typeof value === 'string') {
      return value;
    }
    if (typeof value === 'number') {
      return String(value);
    }
    return '';
  }
}
