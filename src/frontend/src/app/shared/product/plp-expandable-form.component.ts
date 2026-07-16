import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { PlpFormSheetComponent } from './plp-form-sheet.component';

/** Reusable progressive-disclosure shell for long operational add/edit forms. */
@Component({
  selector: 'plp-expandable-form',
  standalone: true,
  imports: [CommonModule, ButtonModule, PlpFormSheetComponent],
  template: `
    <section class="plp-expandable-form" [class.plp-expandable-form--expanded]="expanded">
      <header class="plp-expandable-form__header">
        <div>
          <h2>{{ title }}</h2>
          <p *ngIf="summary">{{ summary }}</p>
        </div>
        <button
          *ngIf="canExpand"
          pButton
          type="button"
          class="p-button-sm"
          [icon]="expanded ? 'pi pi-minus' : 'pi pi-plus'"
          [label]="expanded ? closeLabel : openLabel"
          [disabled]="saving"
          [attr.aria-expanded]="expanded"
          (click)="toggle()"
        ></button>
      </header>
      <plp-form-sheet
        [visible]="expanded"
        [title]="title"
        [subtitle]="summary"
        [showSave]="false"
        [showCancel]="false"
        [saving]="saving"
        [error]="error"
        [focusOnShow]="true"
        (visibleChange)="onSheetVisibleChange($event)"
      ><ng-content></ng-content></plp-form-sheet>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpExpandableFormComponent {
  @Input() title = '';
  @Input() summary = '';
  @Input() openLabel = 'إضافة';
  @Input() closeLabel = 'إغلاق النموذج';
  @Input() expanded = false;
  @Input() canExpand = true;
  @Input() saving = false;
  @Input() error = '';
  @Output() expandedChange = new EventEmitter<boolean>();

  toggle(): void {
    if (this.saving) return;
    this.expandedChange.emit(!this.expanded);
  }

  onSheetVisibleChange(visible: boolean): void {
    if (!visible && !this.saving) this.expandedChange.emit(false);
  }
}
