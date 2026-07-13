import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, forkJoin, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';

export interface MainStageOption { id: string; productionLineId: string; name: string; sequenceOrder: number; isCritical: boolean; isActive: boolean; }
export interface SubStageOption { id: string; mainStageId: string; name: string; code: string; capacity: number; sequenceOrder: number; isActive: boolean; }
export interface FactoryItem { id: string; name: string; code: string; location?: string; isActive: boolean; }
export interface ProductionLineOption { id: string; factoryId: string; name: string; lineCode?: string; sequenceOrder: number; isActive: boolean; }
export interface ProductModelItem { id: string; code: string; name: string; description?: string; isActive: boolean; }
export interface ModelStageItem { id: string; subStageId: string; stageOrder: number; piecePrice: number; standardSeconds?: number; compensationMode: string; isRequired: boolean; isActive: boolean; }
export interface DepartmentItem { departmentId: number; name: string; isActive?: boolean; status?: string; }

@Injectable({ providedIn: 'root' })
export class ManufacturingMasterDataApiService {
  constructor(private readonly http: HttpClient) {}
  mainStages(): Observable<MainStageOption[]> { return this.getItems('/api/main-stages'); } subStages(): Observable<SubStageOption[]> { return this.getItems('/api/sub-stages'); } productionLines(): Observable<ProductionLineOption[]> { return this.getItems('/api/production-lines'); }
  factories(): Observable<FactoryItem[]> { return this.getItems('/api/factories?pageSize=200'); }
  allProductionLines(): Observable<ProductionLineOption[]> { return this.getItems('/api/production-lines?includeInactive=true&pageSize=200'); }
  mainStagesForLine(productionLineId: string): Observable<MainStageOption[]> { return this.getItems(`/api/production-lines/${productionLineId}/main-stages?includeInactive=true&pageSize=200`); }
  subStagesForMainStage(mainStageId: string): Observable<SubStageOption[]> { return this.getItems(`/api/main-stages/${mainStageId}/sub-stages?includeInactive=true&pageSize=200`); }
  allMainStages(): Observable<MainStageOption[]> { return forkJoin([this.getItems<MainStageOption>('/api/main-stages?isActive=true&pageSize=200'), this.getItems<MainStageOption>('/api/main-stages?isActive=false&pageSize=200')]).pipe(map(([active, inactive]) => [...active, ...inactive])); }
  allSubStages(): Observable<SubStageOption[]> { return forkJoin([this.getItems<SubStageOption>('/api/sub-stages?isActive=true&pageSize=200'), this.getItems<SubStageOption>('/api/sub-stages?isActive=false&pageSize=200')]).pipe(map(([active, inactive]) => [...active, ...inactive])); }
  departments(): Observable<DepartmentItem[]> { return this.getItems('/api/departments'); }
  createFactory(value: unknown): Observable<FactoryItem> { return this.post('/api/factories', value); } updateFactory(id: string, value: unknown): Observable<FactoryItem> { return this.patch(`/api/factories/${id}`, value); }
  createProductionLine(value: unknown): Observable<ProductionLineOption> { return this.post('/api/production-lines', value); } updateProductionLine(id: string, value: unknown): Observable<ProductionLineOption> { return this.patch(`/api/production-lines/${id}`, value); }
  createMain(value: unknown): Observable<MainStageOption> { return this.post('/api/main-stages', value); } updateMain(id: string, value: unknown): Observable<MainStageOption> { return this.patch(`/api/main-stages/${id}`, value); } deactivateMain(id: string): Observable<unknown> { return this.delete(`/api/main-stages/${id}`); }
  createSub(value: unknown): Observable<SubStageOption> { return this.post('/api/sub-stages', value); } updateSub(id: string, value: unknown): Observable<SubStageOption> { return this.patch(`/api/sub-stages/${id}`, value); } deactivateSub(id: string): Observable<unknown> { return this.delete(`/api/sub-stages/${id}`); }
  models(): Observable<ProductModelItem[]> { return this.getItems('/api/product-models?includeInactive=true'); } modelStages(id: string): Observable<ModelStageItem[]> { return this.get(`/api/product-models/${id}/stages`); }
  createModel(value: unknown): Observable<ProductModelItem> { return this.post('/api/product-models', value); } updateModel(id: string, value: unknown): Observable<ProductModelItem> { return this.patch(`/api/product-models/${id}`, value); } setModelActivation(id: string, isActive: boolean): Observable<unknown> { return this.patch(`/api/product-models/${id}/activation?isActive=${isActive}`, {}); }
  addModelStage(modelId: string, value: unknown): Observable<ModelStageItem> { return this.post(`/api/product-models/${modelId}/stages`, value); } updateModelStage(modelId: string, stageId: string, value: unknown): Observable<ModelStageItem> { return this.patch(`/api/product-models/${modelId}/stages/${stageId}`, value); } deactivateModelStage(modelId: string, stageId: string): Observable<unknown> { return this.delete(`/api/product-models/${modelId}/stages/${stageId}`); }
  private get<T>(path: string): Observable<T> { return this.http.get<ApiResponse<T>>(buildApiUrl(path)).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); } private getItems<T>(path: string): Observable<T[]> { return this.get<{ items: T[] }>(path).pipe(map(page => page.items ?? [])); } private post<T>(path: string, value: unknown): Observable<T> { return this.http.post<ApiResponse<T>>(buildApiUrl(path), value).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); } private patch<T>(path: string, value: unknown): Observable<T> { return this.http.patch<ApiResponse<T>>(buildApiUrl(path), value).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); } private delete<T>(path: string): Observable<T> { return this.http.delete<ApiResponse<T>>(buildApiUrl(path)).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); }
  private unwrap<T>(response: ApiResponse<T>): T { if (!response.success || response.data === null || response.data === undefined) throw new Error(response.error?.message || 'تعذر إتمام العملية.'); return response.data; }
}
