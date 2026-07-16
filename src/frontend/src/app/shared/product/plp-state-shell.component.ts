import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlpActionButtonComponent } from './plp-action-button.component';
import { PlpActionKind } from './product-action';
import { normalizePrimeIconClass } from '../design-system/icons/production-icon-map';

/** Shared visual shell used by empty, error, and unauthorized states. */
@Component({
  selector: 'plp-state-shell',
  standalone: true,
  imports: [CommonModule, PlpActionButtonComponent],
  template: `
    <section class="plp-product-state" [class]="'plp-product-state plp-product-state--' + tone" [attr.role]="role">
      <i class="plp-product-state__icon" [ngClass]="iconClass" aria-hidden="true"></i>
      <div class="plp-product-state__content">
        <h2 class="plp-text-section-title">{{ title }}</h2>
        <p *ngIf="description" class="plp-text-supporting">{{ description }}</p>
        <plp-action-button
          *ngIf="actionLabel"
          [action]="actionKind"
          [label]="actionLabel"
          (triggered)="action.emit()"
        ></plp-action-button>
      </div>
      <ng-content></ng-content>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpStateShellComponent {
  @Input() title = '';
  @Input() description = '';
  @Input() icon = 'pi-info-circle';
  @Input() tone: 'empty' | 'error' | 'unauthorized' = 'empty';
  @Input() role: 'status' | 'alert' = 'status';
  @Input() actionLabel = '';
  @Input() actionKind: PlpActionKind = 'refresh';
  @Output() action = new EventEmitter<void>();

  get iconClass(): string {
    return normalizePrimeIconClass(this.icon);
  }
}
