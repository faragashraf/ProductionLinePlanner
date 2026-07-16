import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';

/** Reusable progressive-disclosure shell for long operational add/edit forms. */
@Component({
  selector: 'plp-expandable-form',
  standalone: true,
  imports: [CommonModule, ButtonModule],
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
          [attr.aria-expanded]="expanded"
          (click)="toggle()"
        ></button>
      </header>
      <div class="plp-expandable-form__body" *ngIf="expanded">
        <ng-content></ng-content>
      </div>
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
  @Output() expandedChange = new EventEmitter<boolean>();

  toggle(): void {
    this.expandedChange.emit(!this.expanded);
  }
}
