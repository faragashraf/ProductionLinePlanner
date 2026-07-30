import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { WorkerAssignmentDisplayItem } from '../worker-assignment-details/worker-assignment-details.component';

export type WorkerAssignmentCardSelectionMode = 'single' | 'multiple';

/** Shared interactive worker card for permanent and temporary assignment pickers. */
@Component({
  selector: 'plp-worker-assignment-card',
  templateUrl: './worker-assignment-card.component.html',
  styleUrls: ['./worker-assignment-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WorkerAssignmentCardComponent {
  @Input() selectionMode: WorkerAssignmentCardSelectionMode = 'single';
  @Input() selected = false;
  @Input() disabled = false;
  @Input() fullName = '';
  @Input() employeeCode = '';
  @Input() productionLineName = '';
  @Input() isOnActiveService = true;
  @Input() stageNames: readonly string[] = [];
  @Input() assignmentDetails: readonly WorkerAssignmentDisplayItem[] | null = null;
  @Input() expanded = false;
  @Input() hasPhoto = false;
  @Input() photoReference: string | null = null;
  @Input() photoVersion: string | null = null;
  @Input() statusMessage = '';
  @Input() unavailableMessage = '';

  @Output() selectionChange = new EventEmitter<boolean>();
  @Output() expandedChange = new EventEmitter<boolean>();

  get hasAssignmentDetails(): boolean {
    return (this.assignmentDetails?.length ?? 0) > 0;
  }

  get assignmentExpansionLabel(): string {
    const count = this.assignmentDetails?.length ?? 0;
    return count > 1 ? `التسكينات (${count})` : 'التسكينات';
  }

  onCheckboxChange(event: Event): void {
    this.selectionChange.emit((event.target as HTMLInputElement).checked);
  }

  selectSingle(): void {
    if (!this.disabled) this.selectionChange.emit(true);
  }

  toggleExpanded(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.hasAssignmentDetails) this.expandedChange.emit(!this.expanded);
  }

  trackByAssignment(_index: number, assignment: WorkerAssignmentDisplayItem): string {
    return `${assignment.productionLineId}:${assignment.subStageId}`;
  }
}
