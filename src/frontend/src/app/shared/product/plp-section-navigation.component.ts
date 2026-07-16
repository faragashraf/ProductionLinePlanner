import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';

export interface PlpSectionNavigationItem {
  id: string;
  label: string;
  icon?: string;
}

/**
 * Presentational half of internal section navigation. Pair with
 * `PlpSectionNavigationDirective` on a bounded scroll container so route
 * fragments and section visibility remain independent from document scrolling.
 */
@Component({
  selector: 'plp-section-navigation',
  standalone: true,
  imports: [CommonModule, ButtonModule],
  template: `
    <nav class="plp-section-navigation" aria-label="التنقل بين أقسام الصفحة">
      <button
        *ngFor="let section of sections; trackBy: trackById"
        pButton
        type="button"
        class="p-button-sm p-button-text plp-section-navigation__item"
        [class.plp-section-navigation__item--active]="section.id === activeId"
        [icon]="section.icon || ''"
        [label]="section.label"
        [attr.aria-current]="section.id === activeId ? 'location' : null"
        (click)="requested.emit(section.id)"
      ></button>
    </nav>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpSectionNavigationComponent {
  @Input() sections: readonly PlpSectionNavigationItem[] = [];
  @Input() activeId = '';
  @Output() requested = new EventEmitter<string>();

  trackById(_: number, section: PlpSectionNavigationItem): string {
    return section.id;
  }
}
