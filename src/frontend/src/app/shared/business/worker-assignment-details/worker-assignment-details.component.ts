import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlpProductMetadataItemComponent, PlpProductMetadataRowComponent } from '../../product/plp-metadata-row.component';
import { PlpResponsiveEntityRowComponent } from '../../product/plp-responsive-entity-row.component';

/**
 * One compact worker identity and current-participation pattern for assignment
 * pickers. The containing row owns selection and interaction behavior.
 */
@Component({
  selector: 'plp-worker-assignment-details',
  standalone: true,
  imports: [
    CommonModule,
    PlpProductMetadataItemComponent,
    PlpProductMetadataRowComponent,
    PlpResponsiveEntityRowComponent
  ],
  host: {
    class: 'plp-worker-assignment-details',
    dir: 'rtl'
  },
  template: `
    <plp-responsive-entity-row [title]="fullName" [code]="employeeCode">
      <plp-product-metadata-row plp-entity-metadata label="بيانات العامل الحالية">
        <plp-product-metadata-item
          class="plp-worker-assignment-details__metadata plp-worker-assignment-details__metadata--line"
          label="الخط"
          [value]="productionLineName || 'غير محدد'"
          icon="pi pi-sitemap"
        ></plp-product-metadata-item>
        <plp-product-metadata-item
          class="plp-worker-assignment-details__metadata plp-worker-assignment-details__metadata--stage-count"
          [value]="stageCountLabel"
          icon="pi pi-list"
        ></plp-product-metadata-item>
        <span class="plp-worker-assignment-details__stages" *ngIf="normalizedStageNames.length">
          <plp-product-metadata-item
            class="plp-worker-assignment-details__stage-chip"
            *ngFor="let stageName of normalizedStageNames; trackBy: trackByStageName"
            [value]="stageName"
          ></plp-product-metadata-item>
        </span>
      </plp-product-metadata-row>
    </plp-responsive-entity-row>
  `,
  styles: [`
    :host {
      display: block;
      min-width: 0;
      width: 100%;
      text-align: start;
    }

    .plp-worker-assignment-details__stages {
      display: flex;
      flex-wrap: wrap;
      flex-basis: 100%;
      gap: .3rem;
      min-width: 0;
      overflow-wrap: anywhere;
    }

    .plp-worker-assignment-details__stages plp-product-metadata-item {
      min-width: 0;
      max-width: 100%;
    }

    :host ::ng-deep .plp-responsive-entity-row__code {
      display: inline-flex;
      direction: ltr;
      flex: 0 0 auto;
      inline-size: auto;
      max-inline-size: max-content;
      unicode-bidi: isolate;
    }

    :host ::ng-deep .plp-responsive-entity-row__code .p-tag-value {
      display: inline;
      white-space: nowrap;
    }

    :host ::ng-deep .plp-worker-assignment-details__metadata--line .p-tag {
      background: var(--plp-color-info-soft);
      border-color: var(--plp-color-selected-border);
      color: var(--plp-color-info-strong);
    }

    :host ::ng-deep .plp-worker-assignment-details__metadata--stage-count .p-tag {
      background: var(--plp-color-ready-soft);
      border-color: color-mix(in oklab, var(--plp-color-ready) 35%, var(--plp-color-border-muted));
      color: var(--plp-color-ready-strong);
    }

    :host ::ng-deep .plp-worker-assignment-details__stage-chip .p-tag {
      background: var(--plp-color-surface-soft);
      border-color: var(--plp-color-border-muted);
      color: var(--plp-color-text);
      font-weight: var(--plp-font-weight-semibold);
    }

    :host ::ng-deep .plp-worker-assignment-details__stage-chip .p-tag-value {
      display: inline;
      min-width: 0;
      white-space: normal;
      overflow-wrap: anywhere;
    }

    :host ::ng-deep .plp-product-metadata__tag {
      font-size: .75rem;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerAssignmentDetailsComponent {
  @Input() fullName = '';
  @Input() employeeCode = '';
  @Input() productionLineName = '';
  @Input() isOnActiveService = true;
  @Input() stageNames: readonly string[] = [];

  get normalizedStageNames(): string[] {
    return this.stageNames.map(name => name.trim()).filter(Boolean);
  }

  get stageCountLabel(): string {
    return `عدد المراحل: ${this.normalizedStageNames.length}`;
  }

  trackByStageName(index: number): number {
    return index;
  }
}
