import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { InputTextModule } from 'primeng/inputtext';
import { productionIconFor } from '../design-system/icons/production-icon-map';

@Component({
  selector: 'plp-product-toolbar',
  standalone: true,
  imports: [CommonModule, InputTextModule],
  template: `
    <section class="plp-product-toolbar" [class.plp-density-compact]="density === 'compact'">
      <div class="plp-product-toolbar__search" *ngIf="searchEnabled">
        <i [ngClass]="['pi', searchIcon]" aria-hidden="true"></i>
        <label class="plp-sr-only" [attr.for]="searchInputId">{{ searchLabel }}</label>
        <input
          pInputText
          class="plp-control"
          [id]="searchInputId"
          [value]="searchValue"
          [placeholder]="searchPlaceholder"
          type="search"
          (input)="onSearch($event)"
        />
      </div>
      <div class="plp-product-toolbar__filters"><ng-content select="[plp-toolbar-filters]"></ng-content></div>
      <div class="plp-product-toolbar__actions plp-action-group"><ng-content select="[plp-toolbar-actions]"></ng-content></div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductToolbarComponent {
  @Input() searchEnabled = true;
  @Input() searchValue = '';
  @Input() searchPlaceholder = 'بحث';
  @Input() searchLabel = 'بحث في النتائج';
  @Input() searchInputId = 'plp-toolbar-search';
  @Input() density: 'standard' | 'compact' = 'standard';
  @Output() searchValueChange = new EventEmitter<string>();

  readonly searchIcon = productionIconFor('search');

  onSearch(event: Event): void {
    this.searchValueChange.emit((event.target as HTMLInputElement).value);
  }
}
