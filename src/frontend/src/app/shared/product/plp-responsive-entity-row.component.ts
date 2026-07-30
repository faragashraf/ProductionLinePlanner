import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TagModule } from 'primeng/tag';
import { normalizePrimeIconClass } from '../design-system/icons/production-icon-map';

/**
 * Shared responsive identity row for operational lists and structured previews.
 * The entity name remains primary while metadata, state, value, and actions use
 * explicit projection regions that stay readable in RTL and mixed-script data.
 */
@Component({
  selector: 'plp-responsive-entity-row',
  standalone: true,
  imports: [CommonModule, TagModule],
  host: { class: 'plp-responsive-entity-row' },
  template: `
    <span class="plp-responsive-entity-row__identity">
      <span class="plp-responsive-entity-row__title-line">
        <i *ngIf="iconClass" [class]="iconClass" aria-hidden="true"></i>
        <strong class="plp-responsive-entity-row__title" [attr.dir]="titleDirection" [attr.title]="title">{{ title }}</strong>
        <p-tag *ngIf="code" [value]="code" [rounded]="true" styleClass="plp-responsive-entity-row__code"></p-tag>
      </span>
      <ng-content select="[plp-entity-description]"></ng-content>
    </span>
    <span class="plp-responsive-entity-row__metadata"><ng-content select="[plp-entity-metadata]"></ng-content></span>
    <span class="plp-responsive-entity-row__status"><ng-content select="[plp-entity-status]"></ng-content></span>
    <span class="plp-responsive-entity-row__value"><ng-content select="[plp-entity-value]"></ng-content></span>
    <span class="plp-responsive-entity-row__actions"><ng-content select="[plp-entity-actions]"></ng-content></span>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpResponsiveEntityRowComponent {
  @Input() title = '';
  @Input() code = '';
  @Input() icon = '';
  @Input() titleDirection: 'auto' | 'rtl' | 'ltr' = 'auto';

  get iconClass(): string | undefined {
    return this.icon ? normalizePrimeIconClass(this.icon) : undefined;
  }
}
