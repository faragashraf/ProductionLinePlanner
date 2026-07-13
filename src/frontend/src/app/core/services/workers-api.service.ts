import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { resolveFactoryStatus, FactoryStatus } from '../../shared/models/factory-status.model';
import { WorkerPageItem } from '../../shared/models/worker.model';

type RawRecord = Record<string, unknown>;

export interface WorkersApiData {
  workers: WorkerPageItem[];
  hasBackendData: boolean;
  hasUsableBackendData: boolean;
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  supportsServerPagination: boolean;
}

export interface WorkersApiQuery {
  page?: number;
  pageSize?: number;
  search?: string;
}

@Injectable({
  providedIn: 'root'
})
export class WorkersApiService {
  constructor(private readonly http: HttpClient) {}

  loadWorkers(query: WorkersApiQuery = {}): Observable<WorkersApiData> {
    const page = Math.max(Math.trunc(query.page ?? 1), 1);
    const pageSize = Math.max(Math.trunc(query.pageSize ?? 20), 1);
    const search = (query.search ?? '').trim();

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/workers'), { params: this.buildWorkersParams(page, pageSize, search) })
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map((response) => {
          const payload = this.extractPayload(response);
          const workersPayload = this.parseEntityList(payload);
          const mappedWithCompleteness = workersPayload.map((worker, index) => ({
            worker: this.mapWorker(worker, index),
            hasIdentity: this.hasWorkerIdentity(worker)
          }));
          const mappedWorkers = mappedWithCompleteness.filter((entry) => entry.hasIdentity).map((entry) => entry.worker);
          const hasBackendData = workersPayload.length > 0;
          const hasUsableBackendData = this.hasUsableWorkerData(mappedWorkers, hasBackendData);
          const pagination = this.parsePaginationMetadata(payload, page, pageSize, mappedWorkers.length);

          return {
            workers: hasUsableBackendData ? mappedWorkers : [],
            hasBackendData,
            hasUsableBackendData,
            totalCount: hasUsableBackendData ? pagination.totalCount : 0,
            page: hasUsableBackendData ? pagination.page : 1,
            pageSize: hasUsableBackendData ? pagination.pageSize : pageSize,
            totalPages: hasUsableBackendData ? pagination.totalPages : 1,
            supportsServerPagination: hasUsableBackendData ? pagination.supportsServerPagination : false
          };
        })
      );
  }

  private buildWorkersParams(page: number, pageSize: number, search: string): HttpParams {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    if (search.length > 0) {
      params = params.set('search', search);
    }

    return params;
  }

  private mapWorker(worker: RawRecord, index: number): WorkerPageItem {
    const safeRecord = this.normalizeObject(worker);
    const code = this.pickString(safeRecord, ['code', 'workerCode', 'empCode', 'employeeCode', 'badge']);
    const fullName = this.pickString(safeRecord, ['fullName', 'name', 'workerName', 'displayName', 'employeeName']);
    const status = this.resolveWorkerState(
      this.pickFirst(safeRecord, ['status', 'state', 'availability', 'attendanceStatus', 'workerStatus'])
    );

    const department = this.pickString(safeRecord, ['department', 'departmentName', 'groupName']);
    const email = this.pickString(safeRecord, ['email', 'emailAddress', 'mail']);
    const phone = this.pickString(safeRecord, ['phone', 'phoneNumber', 'mobile']);

    return {
      id: this.pickString(safeRecord, ['id', 'workerId', '_id']),
      code: code || `W-${index + 1}`,
      fullName: fullName || 'عامل غير محدد',
      state: status,
      ...(department ? { department } : {}),
      ...(email ? { email } : {}),
      ...(phone ? { phone } : {})
    };
  }

  private hasWorkerIdentity(worker: RawRecord): boolean {
    const code = this.pickString(worker, ['code', 'workerCode', 'empCode', 'employeeCode', 'badge']);
    const fullName = this.pickString(worker, ['fullName', 'name', 'workerName', 'displayName', 'employeeName']);
    return this.hasText(code) && this.hasText(fullName);
  }

  private hasUsableWorkerData(mappedWorkers: WorkerPageItem[], hasBackendData: boolean): boolean {
    return hasBackendData && mappedWorkers.length > 0 && mappedWorkers.every((worker) => this.hasText(worker.code) && this.hasText(worker.fullName));
  }

  private parsePaginationMetadata(payload: unknown, requestedPage: number, requestedPageSize: number, fallbackTotalCount: number): {
    totalCount: number;
    totalPages: number;
    page: number;
    pageSize: number;
    supportsServerPagination: boolean;
  } {
    const source = this.normalizeObject(payload);
    const sourceWithPagination = this.normalizeObject(this.pickFirst(source, ['pagination', 'meta', 'paging', 'pageMeta', 'pageInfo']));
    const explicitTotal = this.toNumber(this.pickFirst(source, ['totalCount', 'total', 'count', 'records', 'totalItems', 'totalRecords']));
    const explicitTotalFromPagination = this.toNumber(this.pickFirst(sourceWithPagination, ['totalCount', 'total', 'count', 'records', 'totalItems', 'totalRecords']));
    const totalFromPayload = explicitTotal > 0 ? explicitTotal : explicitTotalFromPagination;
    const pageSizeFromPayload = this.toNumber(this.pickFirst(sourceWithPagination, ['pageSize', 'size', 'limit', 'perPage']));
    const requestedPageFromPayload = this.toNumber(this.pickFirst(sourceWithPagination, ['page', 'pageIndex', 'pageNumber', 'currentPage']));
    const pageSize = pageSizeFromPayload > 0 ? pageSizeFromPayload : requestedPageSize;

    const totalItems = totalFromPayload > 0 ? totalFromPayload : fallbackTotalCount;
    const page = requestedPageFromPayload > 0 ? requestedPageFromPayload : requestedPage;
    const totalPagesCandidate = this.toNumber(this.pickFirst(sourceWithPagination, ['totalPages', 'pages', 'pageCount', 'totalPageCount']));
    const totalPages = totalPagesCandidate > 0 ? totalPagesCandidate : Math.max(1, Math.ceil(totalItems / Math.max(pageSize, 1)));
    const explicitPage = this.toNumber(this.pickFirst(sourceWithPagination, ['page', 'pageIndex', 'pageNumber', 'currentPage']));
    const explicitFirst = this.toNumber(this.pickFirst(sourceWithPagination, ['startIndex', 'offset', 'skip']));
    const hasExplicitHint =
      explicitTotal > 0 ||
      explicitTotalFromPagination > 0 ||
      explicitPage > 0 ||
      explicitFirst > 0 ||
      totalPagesCandidate > 0 ||
      pageSizeFromPayload > 0 ||
      this.toNumber(this.pickFirst(sourceWithPagination, ['start', 'end'])) > 0;
    const supportsServerPagination =
      hasExplicitHint && (totalItems > pageSize || explicitPage > 0 || explicitFirst > 0 || totalPagesCandidate > 1);

    return {
      totalCount: totalItems,
      totalPages,
      page,
      pageSize,
      supportsServerPagination
    };
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
      if (response.data === undefined || response.data === null) {
        throw new Error('API response data is missing.');
      }
      return response.data;
    }
    return response as T;
  }

  private parseEntityList(payload: unknown): RawRecord[] {
    const source = this.normalizeObject(payload);
    const nestedList = this.toArray(
      this.pickFirst(source, ['items', 'data', 'workers', 'results', 'resultsList', 'rows'])
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

  private toNumber(value: unknown): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return Math.trunc(value);
    }
    if (typeof value === 'string') {
      const parsed = Number(value.trim());
      return Number.isFinite(parsed) ? Math.trunc(parsed) : 0;
    }
    return 0;
  }
}
