import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'plp-product-page-header',
  standalone: true,
  imports: [CommonModule],
  template: `
    <header class="plp-product-page-header" [class.plp-product-page-header--compact]="compact">
      <div class="plp-product-page-header__content">
        <nav *ngIf="showBreadcrumb" class="plp-product-page-header__breadcrumb" aria-label="مسار الصفحة">
          <ng-content select="[plp-breadcrumb]"></ng-content>
        </nav>
        <h1 class="plp-text-page-title">{{ title }}</h1>
        <p *ngIf="subtitle" class="plp-text-supporting">{{ subtitle }}</p>
        <div class="plp-product-page-header__metadata">
          <ng-content select="[plp-page-metadata]"></ng-content>
          <ng-content></ng-content>
        </div>
      </div>
      <div class="plp-product-page-header__actions">
        <div class="plp-action-group"><ng-content select="[plp-secondary-actions]"></ng-content></div>
        <div class="plp-action-group"><ng-content select="[plp-primary-action]"></ng-content></div>
      </div>
    </header>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductPageHeaderComponent {
  @Input() title = '';
  @Input() subtitle = '';
  @Input() showBreadcrumb = true;
  @Input() compact = false;
}
