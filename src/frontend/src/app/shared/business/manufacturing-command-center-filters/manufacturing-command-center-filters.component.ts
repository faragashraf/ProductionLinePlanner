import { Component, EventEmitter, Input, Output } from '@angular/core';
import {
  CommandCenterFilters,
  CommandCenterOperationStatus,
  CommandCenterStructureCatalog,
  defaultCommandCenterFilters
} from '../../models/manufacturing-command-center.model';

@Component({
  selector: 'app-manufacturing-command-center-filters',
  templateUrl: './manufacturing-command-center-filters.component.html',
  styleUrls: ['./manufacturing-command-center-filters.component.scss']
})
export class ManufacturingCommandCenterFiltersComponent {
  @Input() catalog: CommandCenterStructureCatalog | null = null;
  @Input() filters: CommandCenterFilters = defaultCommandCenterFilters();
  @Input() loading = false;
  @Output() filtersChange = new EventEmitter<CommandCenterFilters>();

  readonly operationStatuses: ReadonlyArray<{ value: CommandCenterOperationStatus; label: string }> = [
    { value: 'All', label: 'كل حالات التشغيل' },
    { value: 'None', label: 'لا يوجد تشغيل' },
    { value: 'Draft', label: 'مسودة' },
    { value: 'Approved', label: 'معتمد' },
    { value: 'ApprovalCancelled', label: 'ملغي الاعتماد' },
    { value: 'Cancelled', label: 'ملغى' }
  ];

  onDateChange(operationDate: string): void {
    if (!operationDate) return;
    this.emit({ ...this.filters, operationDate });
  }

  onStatusChange(operationStatus: CommandCenterOperationStatus): void {
    this.emit({ ...this.filters, operationStatus });
  }

  onFactoryChange(factoryId: string | null): void {
    this.emit({ ...this.filters, factoryId, departmentId: null, productionLineId: null });
  }

  onDepartmentChange(departmentId: string | null): void {
    const department = this.catalog?.departments.find(item => item.id === departmentId);
    this.emit({
      ...this.filters,
      factoryId: department?.factoryId ?? this.filters.factoryId,
      departmentId,
      productionLineId: null
    });
  }

  onLineChange(productionLineId: string | null): void {
    const line = this.catalog?.lines.find(item => item.id === productionLineId);
    this.emit({
      ...this.filters,
      factoryId: line?.factoryId ?? this.filters.factoryId,
      departmentId: line ? line.departmentId : this.filters.departmentId,
      productionLineId
    });
  }

  reset(): void {
    this.emit(defaultCommandCenterFilters());
  }

  private emit(filters: CommandCenterFilters): void {
    this.filtersChange.emit(filters);
  }
}
