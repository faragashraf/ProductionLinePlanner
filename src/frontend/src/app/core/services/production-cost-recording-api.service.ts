import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';

export type ProductionOrderStatus = 'Draft' | 'Active' | 'Completed' | 'Cancelled';
export interface ProductionOrder { id: string; orderNumber: string; productModelId: string; productModelCode: string; productionLineId?: string; productionDate: string; plannedQuantity: number; status: ProductionOrderStatus; notes?: string; }
export interface ProductionWorkerAllocation { workerId: string; workerName: string; percentage?: number; fixedAmount?: number; equivalentQuantity: number; calculatedEarning: number; notes?: string; }
export interface StageProductionRecord { id: string; productionOrderId: string; productModelStageId: string; productionDate: string; producedQuantity: number; acceptedQuantity: number; rejectedQuantity: number; status: 'Draft' | 'Approved' | 'Cancelled'; stageCode: string; stageName: string; productModelCode: string; productModelName: string; piecePrice: number; standardSeconds?: number; compensationMode: string; totalWorkerEarnings: number; concurrencyToken: string; workers: ProductionWorkerAllocation[]; notes?: string; }
export interface DailyProductionCostReportRow extends StageProductionRecord { orderNumber: string; modelCode: string; stageCost: number; }
export interface ProductModelOption { id: string; code: string; name: string; isActive: boolean; description?: string; }
export interface ProductModelStageOption { id: string; subStageId: string; subStageCode?: string; subStageName?: string; stageOrder: number; piecePrice: number; standardSeconds?: number; compensationMode: string; isRequired: boolean; isActive: boolean; }
export interface WorkerOption { id: string; employeeCode: string; fullName: string; employmentStatus?: string; isActive?: boolean; }

@Injectable({ providedIn: 'root' })
export class ProductionCostRecordingApiService {
  constructor(private readonly http: HttpClient) {}
  listOrders(): Observable<ProductionOrder[]> { return this.get<ProductionOrder[]>('/api/production/orders'); }
  listModels(): Observable<ProductModelOption[]> { return this.getItems<ProductModelOption>('/api/production/lookups/models'); }
  listModelStages(modelId: string): Observable<ProductModelStageOption[]> { return this.get<ProductModelStageOption[]>(`/api/production/lookups/models/${modelId}/stages`); }
  listWorkers(): Observable<WorkerOption[]> { return this.getItems<WorkerOption>('/api/production/lookups/workers'); }
  createOrder(value: unknown): Observable<ProductionOrder> { return this.post('/api/production/orders', value); }
  updateOrder(id: string, value: unknown): Observable<ProductionOrder> { return this.http.put<ApiResponse<ProductionOrder>>(buildApiUrl(`/api/production/orders/${id}`), value).pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.unwrap(response))); }
  transitionOrder(id: string, action: 'activate' | 'complete' | 'cancel'): Observable<ProductionOrder> { return this.post(`/api/production/orders/${id}/${action}`, {}); }
  listRecords(from?: string, to?: string, status?: string): Observable<StageProductionRecord[]> { const query = new URLSearchParams(); if (from) query.set('from', from); if (to) query.set('to', to); if (status) query.set('status', status); const suffix = query.size ? `?${query.toString()}` : ''; return this.get<StageProductionRecord[]>(`/api/production/records${suffix}`); }
  getRecord(id: string): Observable<StageProductionRecord> { return this.get<StageProductionRecord>(`/api/production/records/${id}`); }
  createDraft(value: unknown): Observable<StageProductionRecord> { return this.post('/api/production/records', value); }
  updateDraft(id: string, value: unknown): Observable<StageProductionRecord> { return this.http.put<ApiResponse<StageProductionRecord>>(buildApiUrl(`/api/production/records/${id}`), value).pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.unwrap(response))); }
  calculatePreview(value: unknown): Observable<StageProductionRecord> { return this.post('/api/production/records/preview', value); }
  approve(id: string, concurrencyToken: string): Observable<StageProductionRecord> { return this.post(`/api/production/records/${id}/approve`, { concurrencyToken }); }
  cancel(id: string, concurrencyToken: string): Observable<StageProductionRecord> { return this.post(`/api/production/records/${id}/cancel`, { concurrencyToken }); }
  dailyReport(from: string, to: string): Observable<DailyProductionCostReportRow[]> { return this.get<DailyProductionCostReportRow[]>(`/api/production/reports/daily?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`); }
  private get<T>(path: string): Observable<T> { return this.http.get<ApiResponse<T>>(buildApiUrl(path)).pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.unwrap(response))); }
  private getItems<T>(path: string): Observable<T[]> { return this.get<{ items: T[] }>(path).pipe(map((page) => page.items ?? [])); }
  private post<T>(path: string, body: unknown): Observable<T> { return this.http.post<ApiResponse<T>>(buildApiUrl(path), body).pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.unwrap(response))); }
  private unwrap<T>(response: ApiResponse<T>): T { if (!response.success || response.data === undefined || response.data === null) { throw new Error(response.error?.message || 'تعذر إتمام العملية.'); } return response.data; }
}
