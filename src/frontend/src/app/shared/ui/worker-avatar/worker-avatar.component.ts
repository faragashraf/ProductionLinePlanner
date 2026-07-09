import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus, resolveFactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-worker-avatar',
  templateUrl: './worker-avatar.component.html',
  styleUrls: ['./worker-avatar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerAvatarComponent {
  @Input() fullName = '';
  @Input() code = '';
  @Input() status: FactoryStatus | string = 'info';
  @Input() size: 'sm' | 'md' | 'lg' = 'md';
  @Input() imageUrl?: string;

  get initials(): string {
    const parts = this.fullName
      .trim()
      .split(' ')
      .map((item) => item.trim())
      .filter(Boolean);
    if (!parts.length) {
      return this.code ? this.code.slice(0, 2) : '؟';
    }
    if (parts.length === 1) {
      return parts[0].slice(0, 2).toUpperCase();
    }
    return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
  }

  get statusTone(): string {
    return `plp-worker-avatar--${resolveFactoryStatus(this.status).toneClass}`;
  }
}
