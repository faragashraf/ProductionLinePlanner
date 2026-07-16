import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TagModule } from 'primeng/tag';
import { normalizePrimeIconClass } from '../design-system/icons/production-icon-map';

/** Groups concise secondary facts below a primary entity name. */
@Component({
  selector: 'plp-product-metadata-row',
  standalone: true,
  template: `
    <div class="plp-product-metadata" role="list" [attr.aria-label]="label">
      <ng-content></ng-content>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductMetadataRowComponent {
  @Input() label = 'معلومات إضافية';
}

/** PrimeNG-backed metadata item with one consistent label/value hierarchy. */
@Component({
  selector: 'plp-product-metadata-item',
  standalone: true,
  imports: [CommonModule, TagModule],
  host: { role: 'listitem' },
  template: `
    <p-tag
      [value]="displayValue"
      [icon]="iconClass"
      [rounded]="true"
      styleClass="plp-product-metadata__tag"
    ></p-tag>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductMetadataItemComponent {
  @Input() label = '';
  @Input() value: unknown = '';
  @Input() icon = '';

  get displayValue(): string {
    const value = this.coerceValue(this.value) || 'غير محدد';
    return this.label ? `${this.label}: ${value}` : value;
  }

  get iconClass(): string | undefined {
    return this.icon ? normalizePrimeIconClass(this.icon) : undefined;
  }

  private coerceValue(value: unknown): string {
    return typeof value === 'string' || typeof value === 'number' ? String(value) : '';
  }
}
