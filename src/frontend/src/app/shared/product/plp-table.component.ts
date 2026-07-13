import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { PlpProductEmptyStateComponent } from './plp-empty-state.component';
import { PlpProductLoadingStateComponent } from './plp-loading-state.component';

/**
 * Operational table composition shell. Callers project PrimeNG pTemplate
 * header/body templates and may project row actions inside their body template.
 */
@Component({
  selector: 'plp-table',
  standalone: true,
  imports: [CommonModule, TableModule, PlpProductEmptyStateComponent, PlpProductLoadingStateComponent],
  template: `
    <section class="plp-product-table plp-operational-table" [class.plp-density-compact]="density === 'compact'">
      <div class="plp-product-table__toolbar"><ng-content select="[plp-table-toolbar]"></ng-content></div>

      <plp-product-loading-state *ngIf="loading; else loaded" [lines]="skeletonRows"></plp-product-loading-state>

      <ng-template #loaded>
        <p-table
          *ngIf="items.length > 0; else empty"
          [value]="items"
          [dataKey]="dataKey"
          [responsiveLayout]="responsiveLayout"
          [breakpoint]="breakpoint"
          styleClass="plp-product-table__table"
        >
          <ng-content></ng-content>
        </p-table>

        <ng-template #empty>
          <plp-product-empty-state [title]="emptyTitle" [description]="emptyDescription"></plp-product-empty-state>
        </ng-template>
      </ng-template>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpTableComponent<T> {
  @Input() items: readonly T[] = [];
  @Input() dataKey = 'id';
  @Input() loading = false;
  @Input() skeletonRows = 5;
  @Input() density: 'standard' | 'compact' = 'standard';
  @Input() responsiveLayout: 'scroll' | 'stack' = 'scroll';
  @Input() breakpoint = '768px';
  @Input() emptyTitle = 'لا توجد بيانات';
  @Input() emptyDescription = 'لا توجد بيانات متاحة للعرض حالياً.';
}
