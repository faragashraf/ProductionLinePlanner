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
  @Input() hasPhoto = false;
  @Input() photoReference: string | null = null;
  @Input() photoVersion: string | null = null;
  @Input() statusMessage = '';
  @Input() unavailableMessage = '';

  @Output() selectionChange = new EventEmitter<boolean>();

  onCheckboxChange(event: Event): void {
    this.selectionChange.emit((event.target as HTMLInputElement).checked);
  }

  selectSingle(): void {
    if (!this.disabled) this.selectionChange.emit(true);
  }
}
