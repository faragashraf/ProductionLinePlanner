import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PlpProductMetadataItemComponent, PlpProductMetadataRowComponent } from '../../product/plp-metadata-row.component';
import { PlpResponsiveEntityRowComponent } from '../../product/plp-responsive-entity-row.component';

export interface WorkerAssignmentDisplayItem {
  productionLineId: string;
  productionLineName: string;
  subStageId: string;
  subStageName: string;
}

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
      <plp-product-metadata-row
        *ngIf="assignmentDetails !== null; else legacyAssignmentDetails"
        plp-entity-metadata
        label="التسكين الفعلي"
      >
        <plp-product-metadata-item
          class="plp-worker-assignment-details__metadata plp-worker-assignment-details__metadata--assignment-state"
          [value]="actualAssignmentStatusLabel"
          icon="pi pi-briefcase"
        ></plp-product-metadata-item>
        <small
          class="plp-worker-assignment-details__unassigned"
          *ngIf="!visibleAssignmentDetails.length"
        >لا يوجد تسكين حالي</small>
        <span class="plp-worker-assignment-details__actual-list" *ngIf="visibleAssignmentDetails.length">
          <ng-container *ngFor="let assignment of visibleAssignmentDetails; trackBy: trackByAssignment">
            <plp-product-metadata-item
              class="plp-worker-assignment-details__metadata plp-worker-assignment-details__metadata--actual-line"
              label="الخط"
              [value]="assignment.productionLineName"
              icon="pi pi-sitemap"
            ></plp-product-metadata-item>
            <plp-product-metadata-item
              class="plp-worker-assignment-details__metadata plp-worker-assignment-details__metadata--actual-stage"
              label="المرحلة"
              [value]="assignment.subStageName"
            ></plp-product-metadata-item>
          </ng-container>
          <plp-product-metadata-item
            *ngIf="hiddenAssignmentCount"
            class="plp-worker-assignment-details__metadata plp-worker-assignment-details__metadata--more"
            [value]="'+' + hiddenAssignmentCount + ' تسكينات أخرى'"
          ></plp-product-metadata-item>
        </span>
      </plp-product-metadata-row>
      <ng-template #legacyAssignmentDetails>
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
      </ng-template>
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

    .plp-worker-assignment-details__actual-list {
      display: flex;
      flex: 1 1 100%;
      flex-wrap: wrap;
      gap: .3rem;
      min-width: 0;
    }

    .plp-worker-assignment-details__actual-list plp-product-metadata-item {
      min-width: 0;
      max-width: 100%;
    }

    .plp-worker-assignment-details__unassigned {
      color: var(--plp-color-text-subtle);
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

    :host ::ng-deep .plp-worker-assignment-details__metadata--assignment-state .p-tag {
      background: var(--plp-color-ready-soft);
      border-color: color-mix(in oklab, var(--plp-color-ready) 35%, var(--plp-color-border-muted));
      color: var(--plp-color-ready-strong);
    }

    :host ::ng-deep .plp-worker-assignment-details__metadata--actual-line .p-tag {
      background: var(--plp-color-info-soft);
      border-color: var(--plp-color-selected-border);
      color: var(--plp-color-info-strong);
    }

    :host ::ng-deep .plp-worker-assignment-details__metadata--actual-stage .p-tag {
      background: var(--plp-color-surface-soft);
      border-color: var(--plp-color-border-muted);
      color: var(--plp-color-text);
    }

    :host ::ng-deep .plp-worker-assignment-details__metadata--more .p-tag {
      background: var(--plp-color-info-soft);
      border-color: var(--plp-color-info);
      color: var(--plp-color-info-strong);
    }

    :host ::ng-deep .plp-worker-assignment-details__actual-list .p-tag-value {
      white-space: normal;
      word-break: normal;
      overflow-wrap: normal;
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
  @Input() assignmentDetails: readonly WorkerAssignmentDisplayItem[] | null = null;

  get normalizedStageNames(): string[] {
    return this.stageNames.map(name => name.trim()).filter(Boolean);
  }

  get stageCountLabel(): string {
    return `عدد المراحل: ${this.normalizedStageNames.length}`;
  }

  get normalizedAssignmentDetails(): WorkerAssignmentDisplayItem[] {
    return (this.assignmentDetails ?? [])
      .map(assignment => ({
        ...assignment,
        productionLineName: assignment.productionLineName.trim(),
        subStageName: assignment.subStageName.trim(),
      }))
      .filter(assignment => assignment.productionLineName && assignment.subStageName);
  }

  get visibleAssignmentDetails(): WorkerAssignmentDisplayItem[] {
    return this.normalizedAssignmentDetails.slice(0, 1);
  }

  get hiddenAssignmentCount(): number {
    return Math.max(0, this.normalizedAssignmentDetails.length - this.visibleAssignmentDetails.length);
  }

  get actualAssignmentStatusLabel(): string {
    const count = this.normalizedAssignmentDetails.length;
    return count === 0 ? 'غير مسكن' : 'مسكن';
  }

  trackByStageName(index: number): number {
    return index;
  }

  trackByAssignment(_index: number, assignment: WorkerAssignmentDisplayItem): string {
    return `${assignment.productionLineId}:${assignment.subStageId}`;
  }
}
