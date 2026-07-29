import { FinancialReportResult, FinancialReportRow, FinancialReportSummary } from '../../core/services/production-financial-report-api.service';
import { QuantitiesReportResult, QuantitiesReportRow, QuantitiesReportSortBy, QuantitiesReportSortDirection, QuantitiesReportStatus, QuantitiesReportSummary, QuantitiesReportView } from '../../core/services/production-quantities-report-api.service';

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

export type ReportsWorkspaceSummary = QuantitiesReportSummary | FinancialReportSummary;
export type ReportsWorkspaceRow = QuantitiesReportRow | FinancialReportRow;
export type ReportsWorkspaceResult = QuantitiesReportResult | FinancialReportResult;
