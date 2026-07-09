import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus, resolveFactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-worker-avatar',
  templateUrl: './worker-avatar.component.html',
  styleUrls: ['./worker-avatar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerAvatarComponent {
  @Input() fullName: unknown = '';
  @Input() code: unknown = '';
  @Input() status: FactoryStatus | string = 'info';
  @Input() size: 'sm' | 'md' | 'lg' = 'md';
  @Input() imageUrl?: unknown;

  get initials(): string {
    const parts = this.safeFullName
      .trim()
      .split(' ')
      .map((item) => item.trim())
      .filter(Boolean);
    if (!parts.length) {
      return this.safeCode ? this.safeCode.slice(0, 2) : '؟';
    }
    if (parts.length === 1) {
      return parts[0].slice(0, 2).toUpperCase();
    }
    return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
  }

  get safeFullName(): string {
    return this.coerceLabel(this.fullName);
  }

  get safeCode(): string {
    return this.coerceLabel(this.code);
  }

  get safeImageUrl(): string {
    return typeof this.imageUrl === 'string' ? this.imageUrl : '';
  }

  get statusTone(): string {
    return `plp-worker-avatar--${resolveFactoryStatus(this.status).toneClass}`;
  }

  get avatarClasses(): string {
    return `plp-worker-avatar ${this.statusTone} plp-worker-avatar--${this.size}`;
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
