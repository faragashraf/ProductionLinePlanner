import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { forkJoin, map, Observable } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { resolveFactoryStatus, FactoryStatus } from '../../shared/models/factory-status.model';
import { WorkerPageItem } from '../../shared/models/worker.model';

type RawRecord = Record<string, unknown>;

export interface WorkersApiData {
  workers: WorkerPageItem[];
  hasBackendData: boolean;
  hasUsableBackendData: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class WorkersApiService {
  constructor(private readonly http: HttpClient) {}

  loadWorkers(): Observable<WorkersApiData> {
    return forkJoin({
      workers: this.getWorkers()
    }).pipe(
      map(({ workers }) => {
        const hasBackendData = workers.length > 0;
        const mappedWithCompleteness = workers.map((worker, index) => ({
          worker: this.mapWorker(worker, index),
          hasIdentity: this.hasWorkerIdentity(worker)
        }));
        const mapped = mappedWithCompleteness.filter((entry) => entry.hasIdentity).map((entry) => entry.worker);

        const hasUsableBackendData = this.hasUsableWorkerData(mapped, hasBackendData);

        return {
          workers: hasUsableBackendData ? mapped : [],
          hasBackendData,
          hasUsableBackendData
        };
      })
    );
  }

  private getWorkers(): Observable<RawRecord[]> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/workers'))
      .pipe(map((response) => this.parseEntityList(this.extractPayload(response))));
  }

  private mapWorker(worker: RawRecord, index: number): WorkerPageItem {
    const safeRecord = this.normalizeObject(worker);
    const code = this.pickString(safeRecord, ['code', 'workerCode', 'empCode']);
    const fullName = this.pickString(safeRecord, ['fullName', 'name', 'workerName', 'displayName', 'employeeName']);
    const status = this.resolveWorkerState(
      this.pickFirst(safeRecord, ['status', 'state', 'availability', 'attendanceStatus', 'workerStatus'])
    );

    return {
      code: code || `W-${index + 1}`,
      fullName: fullName || 'عامل غير محدد',
      state: status
    };
  }

  private hasWorkerIdentity(worker: RawRecord): boolean {
    const code = this.pickString(worker, ['code', 'workerCode', 'empCode']);
    const fullName = this.pickString(worker, ['fullName', 'name', 'workerName', 'displayName', 'employeeName']);
    return this.hasText(code) && this.hasText(fullName);
  }

  private hasUsableWorkerData(mappedWorkers: WorkerPageItem[], hasBackendData: boolean): boolean {
    return hasBackendData && mappedWorkers.length > 0 && mappedWorkers.every((worker) => this.hasText(worker.code) && this.hasText(worker.fullName));
  }

  private resolveWorkerState(rawStatus: unknown): WorkerPageItem['state'] {
    const statusMeta = resolveFactoryStatus(rawStatus as string | FactoryStatus | null);
    if (statusMeta.status === 'present') {
      return 'جاهز';
    }
    if (statusMeta.status === 'late') {
      return 'متأخر';
    }
    if (statusMeta.status === 'absent') {
      return 'غائب';
    }

    const fallbackLabel = this.toString(rawStatus).trim();
    if (fallbackLabel === 'جاهز' || fallbackLabel === 'متأخر' || fallbackLabel === 'غائب') {
      return fallbackLabel;
    }

    if (fallbackLabel === 'حاضر') {
      return 'جاهز';
    }
    if (fallbackLabel === 'موجود') {
      return 'جاهز';
    }

    return 'غائب';
  }

  private extractPayload<T>(response: ApiResponse<T>): T {
    if (response && typeof response === 'object' && 'success' in response) {
      if (response.success === false) {
        throw new Error(response.error?.message || 'API returned an unsuccessful response.');
      }
      if (!response.data) {
        throw new Error('API response data is missing.');
      }
      return response.data;
    }
    return response as T;
  }

  private parseEntityList(payload: unknown): RawRecord[] {
    const source = this.normalizeObject(payload);
    const nestedList = this.toArray(
      this.pickFirst(source, ['items', 'data', 'workers', 'results', 'resultsList'])
    );
    if (nestedList.length > 0) {
      return nestedList;
    }
    return this.toArray(payload);
  }

  private toArray(value: unknown): RawRecord[] {
    return Array.isArray(value) ? (value as RawRecord[]) : [];
  }

  private normalizeObject(value: unknown): RawRecord {
    return value && typeof value === 'object' && !Array.isArray(value)
      ? (value as RawRecord)
      : {};
  }

  private pickFirst(record: RawRecord, keys: string[]): unknown {
    for (const key of keys) {
      if (Object.prototype.hasOwnProperty.call(record, key) && record[key] !== undefined && record[key] !== null) {
        return record[key] as unknown;
      }
    }
    return undefined;
  }

  private pickString(record: RawRecord, keys: string[]): string {
    const value = this.pickFirst(record, keys);
    return typeof value === 'string' && value.trim().length > 0 ? value : '';
  }

  private toString(value: unknown): string {
    if (typeof value === 'string') {
      return value;
    }
    if (typeof value === 'number') {
      return String(value);
    }
    return '';
  }

  private hasText(value: string): boolean {
    return value.trim().length > 0;
  }
}
