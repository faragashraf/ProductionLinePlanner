import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TagModule } from 'primeng/tag';
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
    TagModule,
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
      <p-tag
        *ngIf="assignmentDetails !== null"
        plp-entity-status
        [value]="actualAssignmentStatusLabel"
        [severity]="actualAssignmentStatusSeverity"
        [icon]="actualAssignmentStatusIcon"
        [rounded]="true"
        styleClass="plp-worker-assignment-details__assignment-status"
      ></p-tag>
      <plp-product-metadata-row
        *ngIf="assignmentDetails === null"
        plp-entity-metadata
        label="بيانات العامل الحالية"
      >
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

    :host ::ng-deep .p-tag.plp-worker-assignment-details__assignment-status {
      border: var(--plp-border-width) solid currentColor;
      font-size: var(--plp-type-caption);
      font-weight: var(--plp-font-weight-bold);
      min-block-size: 1.75rem;
      padding-inline: var(--plp-space-8);
    }

    :host ::ng-deep .p-tag.plp-worker-assignment-details__assignment-status.p-tag-success {
      background: var(--plp-color-success-soft);
      border-color: color-mix(in oklab, var(--plp-color-success) 42%, var(--plp-color-border-muted));
      color: var(--plp-color-success-strong);
    }

    :host ::ng-deep .p-tag.plp-worker-assignment-details__assignment-status.p-tag-danger {
      background: var(--plp-color-danger-soft);
      border-color: color-mix(in oklab, var(--plp-color-danger) 38%, var(--plp-color-border-muted));
      color: var(--plp-color-danger-strong);
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

  get actualAssignmentStatusLabel(): string {
    const count = this.normalizedAssignmentDetails.length;
    return count === 0 ? 'غير مسكن' : 'مسكن';
  }

  get actualAssignmentStatusSeverity(): 'success' | 'danger' {
    return this.normalizedAssignmentDetails.length ? 'success' : 'danger';
  }

  get actualAssignmentStatusIcon(): string {
    return this.normalizedAssignmentDetails.length
      ? 'pi pi-check-circle'
      : 'pi pi-exclamation-triangle';
  }

  trackByStageName(index: number): number {
    return index;
  }

}
