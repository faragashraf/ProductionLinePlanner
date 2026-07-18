import { Injectable } from '@angular/core';
import { QUANTITIES_REPORT_VIEWS, QuantitiesReportStatus } from '../../core/services/production-quantities-report-api.service';
import { ReportPresentationMode, ReportsWorkspaceFilters } from './reports-workspace.models';

interface ReportsWorkspaceStoredState {
  filters: ReportsWorkspaceFilters;
  presentationMode: ReportPresentationMode;
}

@Injectable({ providedIn: 'root' })
export class ReportsWorkspaceStateService {
  private readonly storageKey = 'plp.reports-workspace.filters.v1';

  restore(defaults: ReportsWorkspaceFilters, canUseFinancialMode: boolean): ReportsWorkspaceStoredState {
    try {
      const raw = localStorage.getItem(this.storageKey);
      if (!raw) return { filters: defaults, presentationMode: 'QuantitiesOnly' };
      const value = JSON.parse(raw) as Partial<ReportsWorkspaceFilters> & { presentationMode?: ReportPresentationMode };
      const status = this.isStatus(value.status) ? value.status : defaults.status;
      const view = QUANTITIES_REPORT_VIEWS.includes(value.view ?? defaults.view) ? value.view ?? defaults.view : defaults.view;
      return {
        filters: {
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
        },
        presentationMode: canUseFinancialMode && value.presentationMode === 'QuantitiesAndFinancials'
          ? 'QuantitiesAndFinancials'
          : 'QuantitiesOnly'
      };
    } catch {
      return { filters: defaults, presentationMode: 'QuantitiesOnly' };
    }
  }

  save(filters: ReportsWorkspaceFilters, presentationMode: ReportPresentationMode, canUseFinancialMode: boolean): void {
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
      view: filters.view,
      ...(canUseFinancialMode ? { presentationMode } : {})
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
