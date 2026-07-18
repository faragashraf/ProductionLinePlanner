import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';

export type QuantitiesReportView = 'Details' | 'ByStage' | 'ByWorker' | 'WorkerStages' | 'StageWorkers';
export type QuantitiesReportSortBy = 'ProductionDate' | 'StageCode' | 'WorkerCode' | 'ProducedQuantity' | 'AcceptedQuantity' | 'RejectedQuantity' | 'WorkerAllocatedQuantity' | 'RecordCount' | 'WorkerCount' | 'StageCount';
export type QuantitiesReportSortDirection = 'Ascending' | 'Descending';
export type QuantitiesReportStatus = 'Draft' | 'Approved' | 'Cancelled';

export const QUANTITIES_REPORT_VIEWS: readonly QuantitiesReportView[] = ['Details', 'ByStage', 'ByWorker', 'WorkerStages', 'StageWorkers'];

export interface QuantitiesReportFilter {
  from: string;
  to: string;
  factoryId?: string;
  productionLineId?: string;
  productModelId?: string;
  productionOrderId?: string;
  productModelStageId?: string;
  workerId?: string;
  status?: QuantitiesReportStatus;
  view: QuantitiesReportView;
  page: number;
  pageSize: number;
  sortBy?: QuantitiesReportSortBy;
  sortDirection: QuantitiesReportSortDirection;
}

export interface ReportSourceReference {
  sourceType: string;
  stageProductionRecordId?: string | null;
  stageProductionWorkerAllocationId?: string | null;
  productionOrderId?: string | null;
  productModelStageId?: string | null;
  workerId?: string | null;
}

export interface QuantitiesReportSummary {
  totalPhysicalProducedQuantity: number;
  totalPhysicalAcceptedQuantity: number;
  totalPhysicalRejectedQuantity: number;
  totalStageProducedQuantity: number;
  totalAcceptedQuantity: number;
  totalRejectedQuantity: number;
  recordCount: number;
  stageCount: number;
  workerCount: number;
}

export interface QuantitiesReportRow {
  source: ReportSourceReference;
  productionDate?: string | null;
  status: string;
  productionOrderNumber?: string | null;
  factoryCode?: string | null;
  factoryName?: string | null;
  productionLineCode?: string | null;
  productionLineName?: string | null;
  productModelCode?: string | null;
  productModelName?: string | null;
  mainStageName?: string | null;
  stageCode?: string | null;
  stageName?: string | null;
  workerCode?: string | null;
  workerName?: string | null;
  producedQuantity?: number | null;
  acceptedQuantity?: number | null;
  rejectedQuantity?: number | null;
  workerAllocatedQuantity?: number | null;
  recordCount: number;
  stageCount: number;
  workerCount: number;
}

export interface QuantitiesReportResult {
  summary: QuantitiesReportSummary;
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

export function buildProductionReportQuery(filter: QuantitiesReportFilter): URLSearchParams {
  const query = new URLSearchParams({
    from: filter.from,
    to: filter.to,
    view: filter.view,
    page: String(filter.page),
    pageSize: String(filter.pageSize),
    sortDirection: filter.sortDirection
  });

  optionalReportFilter(query, 'factoryId', filter.factoryId);
  optionalReportFilter(query, 'productionLineId', filter.productionLineId);
  optionalReportFilter(query, 'productModelId', filter.productModelId);
  optionalReportFilter(query, 'productionOrderId', filter.productionOrderId);
  optionalReportFilter(query, 'productModelStageId', filter.productModelStageId);
  optionalReportFilter(query, 'workerId', filter.workerId);
  optionalReportFilter(query, 'status', filter.status);
  optionalReportFilter(query, 'sortBy', filter.sortBy);

  return query;
}

export function unwrapProductionReportResponse<T>(response: ApiResponse<T>, fallbackMessage: string): T {
  if (!response.success || response.data === undefined || response.data === null) {
    throw new Error(response.error?.message || fallbackMessage);
  }
  return response.data;
}

@Injectable({ providedIn: 'root' })
export class ProductionQuantitiesReportApiService {
  constructor(private readonly http: HttpClient) {}

  query(filter: QuantitiesReportFilter): Observable<QuantitiesReportResult> {
    const query = buildProductionReportQuery(filter);

    return this.http
      .get<ApiResponse<QuantitiesReportResult>>(buildApiUrl(`/api/reports/production/quantities?${query.toString()}`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => unwrapProductionReportResponse(response, 'تعذر تحميل تقرير الكميات.')));
  }
}

function optionalReportFilter(query: URLSearchParams, key: string, value: string | undefined): void {
  if (value) query.set(key, value);
}
