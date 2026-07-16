import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { PLP_DIALOG_SIZE_CLASS, PlpDialogSize } from './product-responsive';
import { PlpFormSheetComponent } from './plp-form-sheet.component';

/**
 * Compatibility facade for existing operational dialogs.
 * New CRUD work should use `plp-form-sheet`; this facade intentionally routes
 * legacy dialog markup through that same responsive implementation.
 */
@Component({
  selector: 'plp-dialog',
  standalone: true,
  imports: [PlpFormSheetComponent],
  template: `
    <plp-form-sheet
      [visible]="visible"
      [title]="title"
      [subtitle]="subtitle"
      [size]="size"
      [saving]="saving"
      [saveDisabled]="saveDisabled"
      [error]="error"
      [showSave]="showSave"
      [showCancel]="showCancel"
      [saveLabel]="saveLabel"
      [cancelLabel]="cancelLabel"
      [closeLabel]="closeLabel"
      [closeAriaLabel]="closeAriaLabel"
      [successMessage]="successMessage"
      [readOnly]="readOnly"
      [focusOnShow]="focusOnShow"
      [rtl]="rtl"
      (visibleChange)="visibleChange.emit($event)"
      (onShow)="onShow.emit()"
      (onHide)="onHide.emit()"
      (save)="save.emit()"
      (cancel)="cancel.emit()"
    >
      <ng-content></ng-content>
    </plp-form-sheet>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpDialogComponent {
  @Input() visible = false;
  @Input() title = '';
  @Input() subtitle = '';
  @Input() size: PlpDialogSize = 'standard';
  @Input() saving = false;
  @Input() saveDisabled = false;
  @Input() error = '';
  @Input() showSave = true;
  @Input() showCancel = true;
  @Input() saveLabel = 'حفظ';
  @Input() cancelLabel = 'إلغاء';
  @Input() closeLabel = 'إغلاق';
  @Input() closeAriaLabel = 'إغلاق النافذة';
  @Input() successMessage = 'تم الحفظ بنجاح.';
  @Input() readOnly = false;
  /** Opt out when a long workspace must preserve its own scroll position. */
  @Input() focusOnShow = true;
  @Input() rtl = true;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() save = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();
  @Output() onShow = new EventEmitter<void>();
  @Output() onHide = new EventEmitter<void>();

  /** Preserves the legacy public surface while delegating rendering to form-sheet. */
  get dialogClass(): string {
    return ['plp-form-sheet', PLP_DIALOG_SIZE_CLASS[this.size]].join(' ');
  }
}
