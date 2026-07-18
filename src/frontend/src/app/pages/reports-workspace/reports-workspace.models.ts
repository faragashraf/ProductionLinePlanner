import { QuantitiesReportSortBy, QuantitiesReportSortDirection, QuantitiesReportStatus, QuantitiesReportView } from '../../core/services/production-quantities-report-api.service';

export interface ReportsWorkspaceFilters {
  from: string;
  to: string;
  factoryId: string;
  productionLineId: string;
  productModelId: string;
  productionOrderId: string;
  productModelStageId: string;
  workerId: string;
  status: QuantitiesReportStatus;
  view: QuantitiesReportView;
  page: number;
  pageSize: number;
  sortBy?: QuantitiesReportSortBy;
  sortDirection: QuantitiesReportSortDirection;
}

export interface ReportsWorkspaceViewOption {
  value: QuantitiesReportView;
  label: string;
  description: string;
  icon: string;
}
