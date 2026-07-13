import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

/** Presentation wrapper for a projected PrimeNG or native form control. */
@Component({
  selector: 'plp-form-field',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div
      class="plp-product-form-field"
      [class.plp-product-form-field--disabled]="disabled"
      [class.plp-product-form-field--readonly]="readonly"
    >
      <label *ngIf="label" class="plp-product-form-field__label" [attr.for]="controlId || null">
        {{ label }}
        <span *ngIf="required" class="plp-product-form-field__required" aria-hidden="true">*</span>
        <span *ngIf="optional" class="plp-product-form-field__optional">اختياري</span>
      </label>
      <ng-content></ng-content>
      <small *ngIf="help && !error" class="plp-product-form-field__help">{{ help }}</small>
      <small *ngIf="error" class="plp-product-form-field__error" role="alert">{{ error }}</small>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpFormFieldComponent {
  @Input() label = '';
  @Input() controlId = '';
  @Input() required = false;
  @Input() optional = false;
  @Input() readonly = false;
  @Input() disabled = false;
  @Input() help = '';
  @Input() error = '';
}
