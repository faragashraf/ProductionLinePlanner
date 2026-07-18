import { Injectable } from '@angular/core';
import { QUANTITIES_REPORT_VIEWS, QuantitiesReportStatus } from '../../core/services/production-quantities-report-api.service';
import { ReportsWorkspaceFilters } from './reports-workspace.models';

@Injectable({ providedIn: 'root' })
export class ReportsWorkspaceStateService {
  private readonly storageKey = 'plp.reports-workspace.filters.v1';

  restore(defaults: ReportsWorkspaceFilters): ReportsWorkspaceFilters {
    try {
      const raw = localStorage.getItem(this.storageKey);
      if (!raw) return defaults;
      const value = JSON.parse(raw) as Partial<ReportsWorkspaceFilters>;
      const status = this.isStatus(value.status) ? value.status : defaults.status;
      const view = QUANTITIES_REPORT_VIEWS.includes(value.view ?? defaults.view) ? value.view ?? defaults.view : defaults.view;
      return {
        ...defaults,
        from: typeof value.from === 'string' ? value.from : defaults.from,
        to: typeof value.to === 'string' ? value.to : defaults.to,
        factoryId: typeof value.factoryId === 'string' ? value.factoryId : '',
        productionLineId: typeof value.productionLineId === 'string' ? value.productionLineId : '',
        productModelId: typeof value.productModelId === 'string' ? value.productModelId : '',
        productionOrderId: typeof value.productionOrderId === 'string' ? value.productionOrderId : '',
        productModelStageId: typeof value.productModelStageId === 'string' ? value.productModelStageId : '',
        workerId: typeof value.workerId === 'string' ? value.workerId : '',
        status,
        view
      };
    } catch {
      return defaults;
    }
  }

  save(filters: ReportsWorkspaceFilters): void {
    const persisted = {
      from: filters.from,
      to: filters.to,
      factoryId: filters.factoryId,
      productionLineId: filters.productionLineId,
      productModelId: filters.productModelId,
      productionOrderId: filters.productionOrderId,
      productModelStageId: filters.productModelStageId,
      workerId: filters.workerId,
      status: filters.status,
      view: filters.view
    };
    localStorage.setItem(this.storageKey, JSON.stringify(persisted));
  }

  clear(): void {
    localStorage.removeItem(this.storageKey);
  }

  private isStatus(value: unknown): value is QuantitiesReportStatus {
    return value === 'Approved' || value === 'Draft' || value === 'Cancelled';
  }
}
