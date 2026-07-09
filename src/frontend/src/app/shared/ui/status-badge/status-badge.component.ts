import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus, resolveFactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-status-badge',
  templateUrl: './status-badge.component.html',
  styleUrls: ['./status-badge.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StatusBadgeComponent {
  @Input() status: FactoryStatus | string = 'info';
  @Input() label = '';
  @Input() compact = false;

  get metadata() {
    return resolveFactoryStatus(this.status);
  }

  get resolvedLabel(): string {
    return this.label || this.metadata.labelAr;
  }

  get toneClass(): string {
    return `plp-status-badge--${this.metadata.toneClass}`;
  }
}
