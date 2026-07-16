import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DialogModule } from 'primeng/dialog';
import { PlpActionButtonComponent } from './plp-action-button.component';
import { PLP_DIALOG_SIZE_CLASS, PlpDialogSize } from './product-responsive';

/** Canonical responsive dialog shell for operational forms and confirmations. */
@Component({
  selector: 'plp-dialog',
  standalone: true,
  imports: [CommonModule, DialogModule, PlpActionButtonComponent],
  template: `
    <p-dialog
      [visible]="visible"
      [modal]="true"
      [draggable]="false"
      [resizable]="false"
      [closable]="!saving"
      [dismissableMask]="!saving"
      [styleClass]="dialogClass"
      [appendTo]="'body'"
      [focusOnShow]="focusOnShow"
      (visibleChange)="onVisibleChange($event)"
      (onHide)="onHide.emit()"
    >
      <ng-template pTemplate="header">
        <div class="plp-product-dialog__header">
          <div>
            <h2 class="plp-text-section-title">{{ title }}</h2>
            <p *ngIf="subtitle" class="plp-text-supporting">{{ subtitle }}</p>
          </div>
          <ng-content select="[plp-dialog-header-actions]"></ng-content>
        </div>
      </ng-template>

      <div class="plp-product-dialog__body" [attr.aria-busy]="saving || null">
        <p *ngIf="error" class="plp-product-dialog__error" role="alert">{{ error }}</p>
        <ng-content></ng-content>
      </div>

      <ng-template pTemplate="footer">
        <div class="plp-product-dialog__footer">
          <ng-content select="[plp-dialog-footer-start]"></ng-content>
          <div class="plp-action-group">
            <plp-action-button
              *ngIf="showCancel"
              action="cancel"
              [label]="cancelLabel"
              [disabled]="saving"
              (triggered)="cancel.emit()"
            ></plp-action-button>
            <plp-action-button
              *ngIf="showSave"
              action="save"
              [label]="saveLabel"
              [disabled]="saveDisabled"
              [loading]="saving"
              (triggered)="save.emit()"
            ></plp-action-button>
            <ng-content select="[plp-dialog-footer-actions]"></ng-content>
          </div>
        </div>
      </ng-template>
    </p-dialog>
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
  /** Opt out when a long workspace must preserve its own scroll position. */
  @Input() focusOnShow = true;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() save = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();
  @Output() onHide = new EventEmitter<void>();

  get dialogClass(): string {
    return ['plp-product-dialog', PLP_DIALOG_SIZE_CLASS[this.size]].join(' ');
  }

  onVisibleChange(visible: boolean): void {
    this.visibleChange.emit(visible);
  }
}
