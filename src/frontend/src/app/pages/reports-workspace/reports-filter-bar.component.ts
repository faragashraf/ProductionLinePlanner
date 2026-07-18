import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FactoryItem, ModelStageItem, ProductModelItem, ProductionLineOption } from '../../core/services/manufacturing-master-data-api.service';
import { ProductionOrder, WorkerOption } from '../../core/services/production-cost-recording-api.service';
import { ReportsWorkspaceFilters } from './reports-workspace.models';

@Component({
  selector: 'app-reports-filter-bar',
  templateUrl: './reports-filter-bar.component.html',
  styleUrls: ['./reports-filter-bar.component.scss']
})
export class ReportsFilterBarComponent {
  @Input({ required: true }) filters!: ReportsWorkspaceFilters;
  @Input() factories: FactoryItem[] = [];
  @Input() productionLines: ProductionLineOption[] = [];
  @Input() models: ProductModelItem[] = [];
  @Input() stages: ModelStageItem[] = [];
  @Input() workers: WorkerOption[] = [];
  @Input() orders: ProductionOrder[] = [];
  @Input() loading = false;
  @Input() stageLoading = false;
  @Output() filtersChange = new EventEmitter<ReportsWorkspaceFilters>();
  @Output() apply = new EventEmitter<void>();
  @Output() reset = new EventEmitter<void>();

  readonly statuses = [
    { label: 'المعتمدة', value: 'Approved' },
    { label: 'المسودات', value: 'Draft' },
    { label: 'الملغاة', value: 'Cancelled' }
  ];

  get visibleLines(): ProductionLineOption[] {
    return this.productionLines.filter(line => !this.filters.factoryId || line.factoryId === this.filters.factoryId);
  }

  update(patch: Partial<ReportsWorkspaceFilters>): void {
    this.filtersChange.emit({ ...this.filters, ...patch });
  }

  changeFactory(value: string | null): void {
    this.update({ factoryId: value ?? '', productionLineId: '', productModelId: '', productModelStageId: '' });
  }

  changeLine(value: string | null): void {
    this.update({ productionLineId: value ?? '', productModelId: '', productModelStageId: '' });
  }

  changeModel(value: string | null): void {
    this.update({ productModelId: value ?? '', productModelStageId: '' });
  }

  get selectedOrderTitle(): string {
    return this.orders.find(order => order.id === this.filters.productionOrderId)?.orderNumber ?? '';
  }

  trackById(_: number, item: { id: string }): string {
    return item.id;
  }
}
