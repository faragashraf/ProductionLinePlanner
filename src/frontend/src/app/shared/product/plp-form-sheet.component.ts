import { CommonModule, isPlatformBrowser } from '@angular/common';
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, EventEmitter, Inject, Input, OnChanges, OnDestroy, Optional, Output, PLATFORM_ID, SimpleChanges } from '@angular/core';
import { MessageService } from 'primeng/api';
import { DialogModule } from 'primeng/dialog';
import { PlpActionButtonComponent } from './plp-action-button.component';
import { PLP_DIALOG_SIZE_CLASS, PlpDialogSize } from './product-responsive';

/**
 * The single responsive CRUD surface for the product.
 *
 * It is a centered, constrained dialog on desktop and a viewport-safe bottom
 * sheet on touch devices. Its content is the only scroll owner; the header
 * and footer remain visible for long forms and keyboard-driven entry.
 */
@Component({
  selector: 'plp-form-sheet',
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
      [closeOnEscape]="!saving"
      [blockScroll]="true"
      [rtl]="rtl"
      [styleClass]="sheetClass"
      [maskStyleClass]="sheetMaskClass"
      [contentStyleClass]="'plp-form-sheet__content'"
      [position]="dialogPosition"
      [transitionOptions]="transitionOptions"
      [closeAriaLabel]="closeAriaLabel"
      [appendTo]="'body'"
      [focusOnShow]="focusOnShow"
      (visibleChange)="handleVisibleChange($event)"
      (onShow)="handleShow()"
      (onHide)="handleHide()"
    >
      <ng-template pTemplate="header">
        <div class="plp-form-sheet__header">
          <div>
            <h2 class="plp-text-section-title">{{ title }}</h2>
            <p *ngIf="subtitle" class="plp-text-supporting">{{ subtitle }}</p>
          </div>
          <ng-content select="[plp-dialog-header-actions]"></ng-content>
        </div>
      </ng-template>

      <div class="plp-form-sheet__body" [attr.aria-busy]="saving || null">
        <p *ngIf="error" class="plp-product-dialog__error" role="alert">{{ error }}</p>
        <ng-content></ng-content>
      </div>

      <ng-template pTemplate="footer">
        <div class="plp-form-sheet__footer">
          <ng-content select="[plp-dialog-footer-start]"></ng-content>
          <div class="plp-action-group">
            <plp-action-button
              *ngIf="showCancel"
              action="cancel"
              [label]="effectiveCancelLabel"
              [disabled]="saving"
              (triggered)="handleCancel()"
            ></plp-action-button>
            <plp-action-button
              *ngIf="effectiveShowSave"
              action="save"
              [label]="saveLabel"
              [disabled]="saveDisabled"
              [loading]="saving"
              (triggered)="requestSave()"
            ></plp-action-button>
            <ng-content select="[plp-dialog-footer-actions]"></ng-content>
          </div>
        </div>
      </ng-template>
    </p-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpFormSheetComponent implements OnChanges, OnDestroy {
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
  /** Details use the same surface and footer, with a single close action. */
  @Input() readOnly = false;
  @Input() focusOnShow = true;
  /** Operational screens are RTL-first; callers can opt out for an LTR-only surface. */
  @Input() rtl = true;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() save = new EventEmitter<void>();
  @Output() cancel = new EventEmitter<void>();
  @Output() onShow = new EventEmitter<void>();
  @Output() onHide = new EventEmitter<void>();

  private readonly isBrowser: boolean;
  private layoutMediaQuery: MediaQueryList | null = null;
  private reducedMotionMediaQuery: MediaQueryList | null = null;
  private restoreFocusTarget: HTMLElement | null = null;
  private saveRequested = false;
  private isBottomSheet = false;
  private reducedMotion = false;
  private readonly onResponsivePreferenceChange = (): void => this.updateResponsivePreferences();

  constructor(
    @Inject(PLATFORM_ID) platformId: object,
    private readonly changeDetectorRef: ChangeDetectorRef,
    @Optional() private readonly messageService: MessageService | null
  ) {
    this.isBrowser = isPlatformBrowser(platformId);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible']?.currentValue === true && changes['visible']?.previousValue !== true) {
      this.captureRestoreFocusTarget();
      this.saveRequested = false;
    }

    if (changes['visible']?.previousValue === true && changes['visible'].currentValue === false) {
      this.notifySuccessfulSave();
    }

    if (changes['saving'] && !this.saving && changes['saving'].previousValue === true) {
      this.saveRequested = false;
    }
  }

  ngOnDestroy(): void {
    this.layoutMediaQuery?.removeEventListener?.('change', this.onResponsivePreferenceChange);
    this.reducedMotionMediaQuery?.removeEventListener?.('change', this.onResponsivePreferenceChange);
    this.layoutMediaQuery?.removeListener?.(this.onResponsivePreferenceChange);
    this.reducedMotionMediaQuery?.removeListener?.(this.onResponsivePreferenceChange);
  }

  get sheetClass(): string {
    return [
      'plp-form-sheet',
      PLP_DIALOG_SIZE_CLASS[this.size],
      this.isBottomSheet ? 'plp-form-sheet--bottom' : 'plp-form-sheet--desktop'
    ].join(' ');
  }

  get sheetMaskClass(): string {
    return ['plp-form-sheet-mask', this.isBottomSheet ? 'plp-form-sheet-mask--bottom' : 'plp-form-sheet-mask--desktop'].join(' ');
  }

  get dialogPosition(): 'bottom' | 'center' {
    return this.isBottomSheet ? 'bottom' : 'center';
  }

  get transitionOptions(): string {
    if (this.reducedMotion) {
      return '0ms';
    }
    return this.isBottomSheet ? '180ms cubic-bezier(0.2, 0, 0, 1)' : '150ms cubic-bezier(0.2, 0, 0, 1)';
  }

  get effectiveShowSave(): boolean {
    return this.showSave && !this.readOnly;
  }

  get effectiveCancelLabel(): string {
    return this.readOnly ? this.closeLabel : this.cancelLabel;
  }

  /** Hosts may call this after custom validation to keep focus inside the sheet. */
  focusFirstInvalidControl(): void {
    if (!this.isBrowser) return;
    window.setTimeout(() => {
      const invalid = document.body.querySelector<HTMLElement>(
        '.p-dialog.plp-form-sheet [aria-invalid="true"], .p-dialog.plp-form-sheet .ng-invalid:not(form):not([disabled])'
      );
      invalid?.focus({ preventScroll: true });
    });
  }

  requestSave(): void {
    if (this.saving || this.saveDisabled || this.saveRequested) {
      if (this.saveDisabled) this.focusFirstInvalidControl();
      return;
    }

    this.saveRequested = true;
    this.save.emit();
    // Validation may reject a save synchronously. Keep that failure retryable
    // while still suppressing duplicate taps during an actual request.
    queueMicrotask(() => {
      if (!this.saving && this.visible) this.saveRequested = false;
    });
  }

  handleCancel(): void {
    if (!this.saving) this.cancel.emit();
  }

  handleVisibleChange(visible: boolean): void {
    this.visibleChange.emit(visible);
  }

  handleShow(): void {
    this.ensureResponsivePreferenceListeners();
    this.onShow.emit();
  }

  handleHide(): void {
    this.restoreFocus();
    this.onHide.emit();
  }

  private ensureResponsivePreferenceListeners(): void {
    if (!this.isBrowser || this.layoutMediaQuery) return;
    this.layoutMediaQuery = window.matchMedia('(max-width: 1023px)');
    this.reducedMotionMediaQuery = window.matchMedia('(prefers-reduced-motion: reduce)');
    this.layoutMediaQuery.addEventListener?.('change', this.onResponsivePreferenceChange);
    this.reducedMotionMediaQuery.addEventListener?.('change', this.onResponsivePreferenceChange);
    this.layoutMediaQuery.addListener?.(this.onResponsivePreferenceChange);
    this.reducedMotionMediaQuery.addListener?.(this.onResponsivePreferenceChange);
    this.updateResponsivePreferences();
  }

  private updateResponsivePreferences(): void {
    const isBottomSheet = this.layoutMediaQuery?.matches ?? false;
    const reducedMotion = this.reducedMotionMediaQuery?.matches ?? false;
    if (this.isBottomSheet === isBottomSheet && this.reducedMotion === reducedMotion) return;
    this.isBottomSheet = isBottomSheet;
    this.reducedMotion = reducedMotion;
    this.changeDetectorRef.markForCheck();
  }

  private captureRestoreFocusTarget(): void {
    if (!this.isBrowser) return;
    this.ensureResponsivePreferenceListeners();
    const activeElement = document.activeElement;
    this.restoreFocusTarget = activeElement instanceof HTMLElement ? activeElement : null;
  }

  private restoreFocus(): void {
    const target = this.restoreFocusTarget;
    this.restoreFocusTarget = null;
    if (!target?.isConnected) return;
    window.setTimeout(() => target.focus({ preventScroll: true }));
  }

  private notifySuccessfulSave(): void {
    if (!this.saveRequested || this.error || !this.successMessage) return;
    this.messageService?.add({
      severity: 'success',
      summary: 'تم الحفظ',
      detail: this.successMessage,
      life: 3600
    });
    this.saveRequested = false;
  }
}
