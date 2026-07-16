import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

/** Shared heading and action boundary for cards and operational page sections. */
@Component({
  selector: 'plp-product-section-header',
  standalone: true,
  imports: [CommonModule],
  template: `
    <header class="plp-section-header">
      <div class="plp-section-header__copy">
        <h2 *ngIf="level === 2; else tertiaryTitle" class="plp-text-section-title">{{ title }}</h2>
        <ng-template #tertiaryTitle><h3 class="plp-text-section-title">{{ title }}</h3></ng-template>
        <p *ngIf="description" class="plp-text-supporting">{{ description }}</p>
      </div>
      <div class="plp-section-header__actions"><ng-content></ng-content></div>
    </header>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductSectionHeaderComponent {
  @Input() title = '';
  @Input() description = '';
  @Input() level: 2 | 3 = 2;
}
