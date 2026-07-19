import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';
import {
  buildProductionReportQuery,
  QuantitiesReportFilter,
  QuantitiesReportResult,
  QuantitiesReportRow,
  QuantitiesReportSummary,
  unwrapProductionReportResponse
} from './production-quantities-report-api.service';

export interface FinancialReportSummary extends Pick<QuantitiesReportSummary,
  'totalPhysicalProducedQuantity' | 'totalPhysicalAcceptedQuantity' | 'totalPhysicalRejectedQuantity' |
  'recordCount' | 'stageCount' | 'workerCount'> {
  totalProductionEarnings: number | null;
  totalStageProductionCost: number | null;
  averageProductionEarningPerWorker: number | null;
  averageCostPerPhysicalUnit: number | null;
  incompleteFinancialRecordCount: number;
  financialDataStatus: 'Complete' | 'Incomplete' | 'ReviewRequired';
  currencyCode: 'EGP';
}

export interface FinancialReportRow extends QuantitiesReportRow {
  stageProductionCost: number | null;
  productionEarning: number | null;
  stageUnitPrice: number | null;
  workerPercentage: number | null;
  compensationMode: string | null;
  financialDataStatus: 'Complete' | 'Incomplete' | 'ReviewRequired';
}

export interface FinancialReportResult extends Omit<QuantitiesReportResult, 'summary' | 'rows'> {
  summary: FinancialReportSummary;
  rows: FinancialReportRow[];
}

@Injectable({ providedIn: 'root' })
export class ProductionFinancialReportApiService {
  constructor(private readonly http: HttpClient) {}

  query(filter: QuantitiesReportFilter): Observable<FinancialReportResult> {
    const query = buildProductionReportQuery(filter);
    return this.http
      .get<ApiResponse<FinancialReportResult>>(buildApiUrl(`/api/reports/production/financials?${query.toString()}`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => unwrapProductionReportResponse(response, 'تعذر تحميل تقرير القيم المالية.')));
  }
}
