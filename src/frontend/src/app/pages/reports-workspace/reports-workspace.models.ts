import { QuantitiesReportRow, QuantitiesReportSortBy, QuantitiesReportSortDirection, QuantitiesReportStatus, QuantitiesReportView } from '../../core/services/production-quantities-report-api.service';

export type ReportPresentationMode = 'QuantitiesOnly' | 'QuantitiesAndFinancials';

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

export interface ReportsWorkspaceSummary {
  totalPhysicalProducedQuantity: number;
  totalPhysicalAcceptedQuantity: number;
  totalPhysicalRejectedQuantity: number;
  recordCount: number;
  stageCount: number;
  workerCount: number;
}

export interface ReportsWorkspaceResult {
  summary: ReportsWorkspaceSummary;
  rows: QuantitiesReportRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  appliedStatus: string;
  view: QuantitiesReportView;
  sortBy: QuantitiesReportSortBy;
  sortDirection: QuantitiesReportSortDirection;
}
