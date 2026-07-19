import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, forkJoin, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';

export interface MainStageOption { id: string; productionLineId: string; name: string; sequenceOrder: number; isCritical: boolean; isActive: boolean; }
export interface SubStageOption { id: string; mainStageId: string; name: string; code: string; capacity: number; sequenceOrder: number; isActive: boolean; }
export interface SubStagePage { items: SubStageOption[]; totalCount: number; pageNumber: number; pageSize: number; }
interface SubStageApiDto extends Omit<SubStageOption, 'sequenceOrder'> { defaultOrder?: number | null; }
export interface FactoryItem { id: string; name: string; code: string; location?: string; isActive: boolean; }
export interface ProductionLineOption { id: string; factoryId: string; name: string; lineCode?: string; sequenceOrder: number; isActive: boolean; }
export interface ProductModelItem { id: string; code: string; name: string; description?: string; isActive: boolean; }
export type CompensationMode = 'SharedPercentage' | 'FullRatePerWorker' | 'FixedAmount';
export interface ModelStageItem { id: string; productModelId?: string; subStageId: string; subStageCode?: string; subStageName?: string; stageOrder: number; piecePrice: number; standardSeconds?: number | null; compensationMode: CompensationMode; isRequired: boolean; isActive: boolean; }
export interface CompensationModelStageUpdate { compensationMode: CompensationMode; piecePrice: number; standardSeconds: number | null; }
export interface DepartmentItem { departmentId: number; name: string; isActive?: boolean; status?: string; }

@Injectable({ providedIn: 'root' })
export class ManufacturingMasterDataApiService {
  constructor(private readonly http: HttpClient) {}
  mainStages(): Observable<MainStageOption[]> { return this.getItems('/api/main-stages'); } subStages(): Observable<SubStageOption[]> { return this.getSubStageItems('/api/sub-stages'); } productionLines(): Observable<ProductionLineOption[]> { return this.getItems('/api/production-lines'); }
  searchSubStages(search = '', page = 1, pageSize = 50, isActive = true): Observable<SubStagePage> { return this.get<{ items: SubStageApiDto[]; totalCount: number; pageNumber: number; pageSize: number }>(`/api/sub-stages?isActive=${isActive}&page=${page}&pageSize=${pageSize}&search=${encodeURIComponent(search)}`).pipe(map(result => ({ ...result, items: (result.items ?? []).map(item => this.toSubStageOption(item)) }))); }
  factories(): Observable<FactoryItem[]> { return this.getItems('/api/factories?pageSize=200'); }
  allProductionLines(): Observable<ProductionLineOption[]> { return this.getItems('/api/production-lines?includeInactive=true&pageSize=200'); }
  mainStagesForLine(productionLineId: string): Observable<MainStageOption[]> { return this.getItems(`/api/production-lines/${productionLineId}/main-stages?includeInactive=true&pageSize=200`); }
  subStagesForMainStage(mainStageId: string): Observable<SubStageOption[]> { return this.getSubStageItems(`/api/main-stages/${mainStageId}/sub-stages?includeInactive=true&pageSize=200`); }
  allMainStages(): Observable<MainStageOption[]> { return forkJoin([this.getItems<MainStageOption>('/api/main-stages?isActive=true&pageSize=200'), this.getItems<MainStageOption>('/api/main-stages?isActive=false&pageSize=200')]).pipe(map(([active, inactive]) => [...active, ...inactive])); }
  allSubStages(): Observable<SubStageOption[]> { return forkJoin([this.getSubStageItems('/api/sub-stages?isActive=true&pageSize=200'), this.getSubStageItems('/api/sub-stages?isActive=false&pageSize=200')]).pipe(map(([active, inactive]) => [...active, ...inactive])); }
  departments(): Observable<DepartmentItem[]> { return this.getItems('/api/departments'); }
  createFactory(value: unknown): Observable<FactoryItem> { return this.post('/api/factories', value); } updateFactory(id: string, value: unknown): Observable<FactoryItem> { return this.patch(`/api/factories/${id}`, value); }
  createProductionLine(value: unknown): Observable<ProductionLineOption> { return this.post('/api/production-lines', value); } updateProductionLine(id: string, value: unknown): Observable<ProductionLineOption> { return this.patch(`/api/production-lines/${id}`, value); }
  createMain(value: unknown): Observable<MainStageOption> { return this.post('/api/main-stages', value); } updateMain(id: string, value: unknown): Observable<MainStageOption> { return this.patch(`/api/main-stages/${id}`, value); } deactivateMain(id: string): Observable<unknown> { return this.delete(`/api/main-stages/${id}`); } setMainActivation(id: string, isActive: boolean): Observable<MainStageOption> { return this.patch(`/api/main-stages/${id}`, { isActive }); }
  createSub(value: unknown): Observable<SubStageOption> { return this.postSub('/api/sub-stages', value); } updateSub(id: string, value: unknown): Observable<SubStageOption> { return this.patchSub(`/api/sub-stages/${id}`, value); } deactivateSub(id: string): Observable<unknown> { return this.delete(`/api/sub-stages/${id}`); } setSubActivation(id: string, isActive: boolean): Observable<SubStageOption> { return this.patchSub(`/api/sub-stages/${id}`, { isActive }); }
  models(): Observable<ProductModelItem[]> { return this.getItems('/api/product-models?includeInactive=true'); } modelStages(id: string): Observable<ModelStageItem[]> { return this.get(`/api/product-models/${id}/stages`); }
  createModel(value: unknown): Observable<ProductModelItem> { return this.post('/api/product-models', value); } updateModel(id: string, value: unknown): Observable<ProductModelItem> { return this.patch(`/api/product-models/${id}`, value); } setModelActivation(id: string, isActive: boolean): Observable<unknown> { return this.patch(`/api/product-models/${id}/activation?isActive=${isActive}`, {}); }
  addModelStage(modelId: string, value: unknown): Observable<ModelStageItem> { return this.post(`/api/product-models/${modelId}/stages`, value); } updateModelStage(modelId: string, stageId: string, value: unknown): Observable<ModelStageItem> { return this.patch(`/api/product-models/${modelId}/stages/${stageId}`, value); } deactivateModelStage(modelId: string, stageId: string): Observable<unknown> { return this.delete(`/api/product-models/${modelId}/stages/${stageId}`); }
  compensationModels(includeInactive = false): Observable<ProductModelItem[]> { return this.getItems(`/api/compensation/models?includeInactive=${includeInactive}`); }
  compensationModelStages(modelId: string): Observable<ModelStageItem[]> { return this.get(`/api/compensation/models/${modelId}/stages`); }
  updateCompensationModelStage(modelId: string, stageId: string, value: CompensationModelStageUpdate): Observable<ModelStageItem> { return this.patch(`/api/compensation/models/${modelId}/stages/${stageId}`, value); }
  private get<T>(path: string): Observable<T> { return this.http.get<ApiResponse<T>>(buildApiUrl(path)).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); } private getItems<T>(path: string): Observable<T[]> { return this.get<{ items: T[] }>(path).pipe(map(page => page.items ?? [])); } private post<T>(path: string, value: unknown): Observable<T> { return this.http.post<ApiResponse<T>>(buildApiUrl(path), value).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); } private patch<T>(path: string, value: unknown): Observable<T> { return this.http.patch<ApiResponse<T>>(buildApiUrl(path), value).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); } private delete<T>(path: string): Observable<T> { return this.http.delete<ApiResponse<T>>(buildApiUrl(path)).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(x => this.unwrap(x))); }
  private getSubStageItems(path: string): Observable<SubStageOption[]> { return this.getItems<SubStageApiDto>(path).pipe(map(items => items.map(item => this.toSubStageOption(item)))); }
  private postSub(path: string, value: unknown): Observable<SubStageOption> { return this.post<SubStageApiDto>(path, value).pipe(map(item => this.toSubStageOption(item))); }
  private patchSub(path: string, value: unknown): Observable<SubStageOption> { return this.patch<SubStageApiDto>(path, value).pipe(map(item => this.toSubStageOption(item))); }
  private toSubStageOption(item: SubStageApiDto): SubStageOption { return { ...item, sequenceOrder: item.defaultOrder ?? 0 }; }
  private unwrap<T>(response: ApiResponse<T>): T { if (!response.success || response.data === null || response.data === undefined) throw new Error(response.error?.message || 'تعذر إتمام العملية.'); return response.data; }
}
