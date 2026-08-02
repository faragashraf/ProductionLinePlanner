import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { WorkerPageItem, WorkerPermanentAssignment } from '../../shared/models/worker.model';

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
  serviceStatus?: 'all' | 'active' | 'inactive';
  hasPhoto?: boolean;
}

export interface WorkerIdentityUpdate {
  fullName?: string;
  phone?: string;
}

export interface WorkerEmploymentStatusUpdate {
  employmentStatus: 'Active' | 'Suspended' | 'LeftEmployment';
}

export interface WorkerDepartmentAssignmentResponse {
  workerId: string;
  departmentId: string;
  departmentName: string;
  factoryId: string;
  factoryName: string;
  concurrencyToken: string;
  updatedAtUtc: string;
}

export interface WorkerSalaryRecord {
  id: string;
  workerId: string;
  amount: number;
  currencyCode: string;
  effectiveFrom: string;
  effectiveTo: string | null;
}

export interface WorkerPhotoChangeResponse {
  photo: {
    workerId: string;
    photoReference: string;
    version: string;
    contentType: string;
    length: number;
  };
  created: boolean;
  replaced: boolean;
  unchanged: boolean;
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
      .get<ApiResponse<unknown>>(buildApiUrl('/api/workers'), {
        params: this.buildWorkersParams(page, pageSize, search, query.serviceStatus ?? 'all', query.hasPhoto)
      })
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

  loadFactoryStructureEligibleWorkers(subStageId: string): Observable<WorkerPageItem[]> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/factory-structure/sub-stages/${encodeURIComponent(subStageId)}/eligible-workers`))
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map((response) => {
          const payload = this.extractPayload(response);
          const workersPayload = this.parseEntityList(payload);
          return workersPayload
            .map((worker, index) => this.mapWorker(worker, index))
            .filter((worker) => this.hasText(worker.id ?? '') && this.hasText(worker.code) && this.hasText(worker.fullName));
        })
      );
  }

  /** Updates one worker and returns the authoritative row shape for local reconciliation. */
  updateWorker(workerId: string, update: WorkerIdentityUpdate, correlationId?: string): Observable<WorkerPageItem> {
    return this.http
      .patch<ApiResponse<unknown>>(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}`), update, { headers: this.correlationHeaders(correlationId) })
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map(response => this.mapWorker(this.normalizeObject(this.extractPayload(response)), 0))
      );
  }

  getWorker(workerId: string): Observable<WorkerPageItem> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}`))
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map(response => this.mapWorker(this.normalizeObject(this.extractPayload(response)), 0))
      );
  }

  setEmploymentStatus(workerId: string, update: WorkerEmploymentStatusUpdate, correlationId?: string): Observable<WorkerPageItem> {
    return this.http
      .patch<ApiResponse<unknown>>(
        buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/employment-status`),
        update,
        { headers: this.correlationHeaders(correlationId) }
      )
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map(response => this.mapWorker(this.normalizeObject(this.extractPayload(response)), 0))
      );
  }

  assignOrganizationalDepartment(workerId: string, departmentId: string, concurrencyToken: string, correlationId?: string): Observable<WorkerDepartmentAssignmentResponse> {
    return this.http
      .put<ApiResponse<WorkerDepartmentAssignmentResponse>>(
        buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/organizational-department`),
        { departmentId, concurrencyToken },
        { headers: this.correlationHeaders(correlationId) }
      )
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => this.extractPayload(response)));
  }

  getCurrentSalary(workerId: string): Observable<WorkerSalaryRecord> {
    return this.http
      .get<ApiResponse<WorkerSalaryRecord>>(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/compensation/current`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => this.extractPayload(response)));
  }

  uploadWorkerPhoto(workerId: string, photo: File, correlationId?: string): Observable<WorkerPhotoChangeResponse> {
    const form = new FormData();
    form.append('photo', photo, photo.name);
    return this.http
      .put<ApiResponse<WorkerPhotoChangeResponse>>(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/photo`), form, { headers: this.correlationHeaders(correlationId) })
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map(response => this.extractPayload(response))
      );
  }

  deleteWorkerPhoto(workerId: string, correlationId?: string): Observable<void> {
    return this.http
      .delete(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/photo`), { observe: 'response', headers: this.correlationHeaders(correlationId) })
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map(() => undefined)
      );
  }

  private buildWorkersParams(
    page: number,
    pageSize: number,
    search: string,
    serviceStatus: WorkersApiQuery['serviceStatus'],
    hasPhoto?: boolean
  ): HttpParams {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));

    if (search.length > 0) {
      params = params.set('search', search);
    }

    if (hasPhoto === true) {
      params = params.set('hasPhoto', 'true');
    } else if (hasPhoto === false) {
      params = params.set('hasPhoto', 'false');
    }

    if (serviceStatus === 'active') {
      params = params.set('isActive', 'true');
    } else if (serviceStatus === 'inactive') {
      params = params.set('isActive', 'false');
    }

    return params;
  }

  private correlationHeaders(correlationId?: string): HttpHeaders | undefined {
    return correlationId ? new HttpHeaders({ 'X-Manufacturing-Realtime-Correlation-Id': correlationId }) : undefined;
  }

  private mapWorker(worker: RawRecord, _index: number): WorkerPageItem {
    const safeRecord = this.normalizeObject(worker);
    const code = this.pickString(safeRecord, ['code', 'workerCode', 'empCode', 'employeeCode', 'badge']);
    const fullName = this.pickString(safeRecord, ['fullName', 'name', 'workerName', 'displayName', 'employeeName']);
    const employmentStatus = this.pickString(safeRecord, ['employmentStatus', 'employmentState', 'workerStatus']);
    const isActive = this.toBoolean(this.pickFirst(safeRecord, ['isActive', 'active', 'onService']));
    const status = this.resolveWorkerState(employmentStatus, isActive);

    const department = this.pickString(safeRecord, ['localDepartmentName', 'department', 'departmentName', 'groupName']);
    const email = this.pickString(safeRecord, ['email', 'emailAddress', 'mail']);
    const phone = this.pickString(safeRecord, ['phone', 'phoneNumber', 'mobile']);
    const photoReference = this.pickString(safeRecord, ['photoReference', 'photoUrl', 'imageUrl']);
    const hasPhotoValue = this.pickFirst(safeRecord, ['hasPhoto']);
    const hasPhoto = typeof hasPhotoValue === 'boolean' ? hasPhotoValue : Boolean(photoReference);
    const photoVersion = this.pickString(safeRecord, ['photoVersion']);
    const attendanceUserId = this.pickString(safeRecord, ['attendanceUserId']);
    const badgeNumber = this.pickString(safeRecord, ['badgeNumber']);
    const attendanceDepartmentId = this.toOptionalNumber(this.pickFirst(safeRecord, ['attendanceDepartmentId']));
    const defaultSubStageId = this.pickString(safeRecord, ['defaultSubStageId']);
    const employmentEndDate = this.pickString(safeRecord, ['employmentEndDate']);
    const lastExternalSyncAt = this.pickString(safeRecord, ['lastExternalSyncAt']);
    const createdAtUtc = this.pickString(safeRecord, ['createdAtUtc']);
    const updatedAtUtc = this.pickString(safeRecord, ['updatedAtUtc']);
    const permanentAssignments = this.toArray(this.pickFirst(safeRecord, ['permanentAssignments']))
      .map(assignment => this.mapPermanentAssignment(assignment))
      .filter((assignment): assignment is WorkerPermanentAssignment => assignment !== null);
    const organizationalDepartmentId = this.pickString(safeRecord, ['organizationalDepartmentId']);
    const organizationalDepartmentName = this.pickString(safeRecord, ['organizationalDepartmentName']);
    const organizationalFactoryId = this.pickString(safeRecord, ['organizationalFactoryId']);
    const organizationalFactoryName = this.pickString(safeRecord, ['organizationalFactoryName']);
    const organizationalDepartmentConcurrencyToken = this.pickString(safeRecord, ['organizationalDepartmentConcurrencyToken']);

    return {
      id: this.pickString(safeRecord, ['id', 'workerId', '_id']),
      code,
      fullName,
      state: status,
      ...(employmentStatus ? { employmentStatus } : {}),
      ...(isActive ? { isActive } : { isActive: false }),
      ...(department ? { department } : {}),
      ...(email ? { email } : {}),
      ...(phone ? { phone } : {}),
      ...(photoReference ? { photoReference } : {}),
      hasPhoto,
      ...(photoVersion ? { photoVersion } : {}),
      ...(attendanceUserId ? { attendanceUserId } : {}),
      ...(badgeNumber ? { badgeNumber } : {}),
      ...(attendanceDepartmentId !== null ? { attendanceDepartmentId } : {}),
      ...(defaultSubStageId ? { defaultSubStageId } : {}),
      permanentAssignments,
      ...(employmentEndDate ? { employmentEndDate } : {}),
      ...(lastExternalSyncAt ? { lastExternalSyncAt } : {}),
      ...(createdAtUtc ? { createdAtUtc } : {}),
      ...(updatedAtUtc ? { updatedAtUtc } : {}),
      ...(organizationalDepartmentId ? { organizationalDepartmentId } : {}),
      ...(organizationalDepartmentName ? { organizationalDepartmentName } : {}),
      ...(organizationalFactoryId ? { organizationalFactoryId } : {}),
      ...(organizationalFactoryName ? { organizationalFactoryName } : {}),
      ...(organizationalDepartmentConcurrencyToken ? { organizationalDepartmentConcurrencyToken } : {})
    };
  }

  private hasWorkerIdentity(worker: RawRecord): boolean {
    const code = this.pickString(worker, ['code', 'workerCode', 'empCode', 'employeeCode', 'badge']);
    const fullName = this.pickString(worker, ['fullName', 'name', 'workerName', 'displayName', 'employeeName']);
    return this.hasText(code) && this.hasText(fullName);
  }

  private mapPermanentAssignment(value: RawRecord): WorkerPermanentAssignment | null {
    const assignment = this.normalizeObject(value);
    const id = this.pickString(assignment, ['id']);
    const factoryId = this.pickString(assignment, ['factoryId']);
    const productionLineId = this.pickString(assignment, ['productionLineId']);
    const departmentId = this.pickString(assignment, ['departmentId']);
    const mainStageId = this.pickString(assignment, ['mainStageId']);
    const subStageId = this.pickString(assignment, ['subStageId']);
    if (![id, factoryId, productionLineId, departmentId, mainStageId, subStageId].every(item => this.hasText(item))) return null;
    return {
      id,
      factoryId,
      factoryName: this.pickString(assignment, ['factoryName']),
      productionLineId,
      productionLineName: this.pickString(assignment, ['productionLineName']),
      departmentId,
      departmentName: this.pickString(assignment, ['departmentName']),
      mainStageId,
      mainStageName: this.pickString(assignment, ['mainStageName']),
      subStageId,
      subStageName: this.pickString(assignment, ['subStageName']),
      assignedAtUtc: this.pickString(assignment, ['assignedAtUtc'])
    };
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

  private resolveWorkerState(employmentStatus: string, isActive: boolean): WorkerPageItem['state'] {
    const normalized = employmentStatus.trim().toLowerCase();
    return isActive && normalized !== 'suspended' && normalized !== 'leftemployment'
      ? 'على رأس العمل'
      : 'خارج الخدمة';
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

  private toOptionalNumber(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) return Math.trunc(value);
    if (typeof value === 'string' && value.trim()) {
      const parsed = Number(value.trim());
      return Number.isFinite(parsed) ? Math.trunc(parsed) : null;
    }
    return null;
  }

  private toBoolean(value: unknown): boolean {
    return value === true || value === 'true' || value === 1 || value === '1';
  }
}
