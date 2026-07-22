import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import {
  CommandCenterFilters,
  ManufacturingCommandCenter
} from '../../shared/models/manufacturing-command-center.model';

@Injectable({ providedIn: 'root' })
export class ManufacturingCommandCenterApiService {
  constructor(private readonly http: HttpClient) {}

  load(filters: CommandCenterFilters): Observable<ManufacturingCommandCenter> {
    let params = new HttpParams()
      .set('productionDate', filters.operationDate)
      .set('operationStatus', filters.operationStatus);
    if (filters.factoryId) params = params.set('factoryId', filters.factoryId);
    if (filters.departmentId) params = params.set('departmentId', filters.departmentId);
    if (filters.productionLineId) params = params.set('productionLineId', filters.productionLineId);

    return this.http
      .get<ApiResponse<ManufacturingCommandCenter>>(buildApiUrl('/api/manufacturing-command-center'), { params })
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map(response => {
          if (!response.success || !response.data) {
            throw new Error(response.error?.message || 'تعذر تحميل مركز قيادة التصنيع.');
          }
          return response.data;
        })
      );
  }
}
