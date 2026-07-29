import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import {
  OperationalReadinessSnapshot,
  OperationalReadinessStages,
  OperationalReadinessWorkers
} from '../../shared/models/operational-readiness.model';

interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error?: { message?: string } | null;
}
@Injectable({ providedIn: 'root' })
export class OperationalReadinessApiService {
  constructor(private readonly http: HttpClient) {}

  loadSnapshot(factoryId?: string | null): Observable<OperationalReadinessSnapshot> {
    const params = factoryId ? new HttpParams().set('factoryId', factoryId) : undefined;
    return this.http.get<ApiResponse<OperationalReadinessSnapshot>>(buildApiUrl('/operational-readiness'), { params })
      .pipe(map(response => this.unwrap(response)));
  }

  loadStages(productionLineId: string, productModelId?: string | null): Observable<OperationalReadinessStages> {
    const params = productModelId ? new HttpParams().set('productModelId', productModelId) : undefined;
    return this.http.get<ApiResponse<OperationalReadinessStages>>(
      buildApiUrl(`/operational-readiness/lines/${encodeURIComponent(productionLineId)}/stages`),
      { params }
    ).pipe(map(response => this.unwrap(response)));
  }

  loadWorkers(productionLineId: string, stageId: string): Observable<OperationalReadinessWorkers> {
    return this.http.get<ApiResponse<OperationalReadinessWorkers>>(
      buildApiUrl(`/operational-readiness/lines/${encodeURIComponent(productionLineId)}/stages/${encodeURIComponent(stageId)}/workers`)
    ).pipe(map(response => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response?.success || !response.data) throw new Error(response?.error?.message || 'Operational readiness data is unavailable.');
    return response.data;
  }
}
