import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { ReadinessStageFilter, ReadinessStageFilterOption } from '../../stage-readiness-filter';

@Component({
  selector: 'app-readiness-stage-filter',
  templateUrl: './readiness-stage-filter.component.html',
  styleUrls: ['./readiness-stage-filter.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReadinessStageFilterComponent {
  @Input({ required: true }) options: ReadinessStageFilterOption[] = [];
  @Input({ required: true })
  set selectedFilters(value: ReadinessStageFilter[]) {
    const next = value ?? [];
    if (next.length === this.selection.length && next.every((filter, index) => filter === this.selection[index])) return;
    this.selection = [...next];
  }
  get selectedFilters(): ReadinessStageFilter[] { return this.selection; }
  @Input() visibleCount = 0;
  @Input() totalCount = 0;
  @Output() filtersChanged = new EventEmitter<ReadinessStageFilter[]>();
  @Output() filtersCleared = new EventEmitter<void>();

  private selection: ReadinessStageFilter[] = [];

  get hasSelection(): boolean { return this.selectedFilters.length > 0; }

  updateSelection(value: ReadinessStageFilter[] | null): void {
    this.selection = [...(value ?? [])];
    this.filtersChanged.emit(this.selection);
  }
}
