import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';

export type WorkforceAttendanceStatus = 'Present' | 'Late' | 'Absent' | 'Incomplete' | 'Unassigned' | 'NeedsSync';
export interface WorkforceAssignment { assignmentId: string; assignmentType: 'Default' | 'Temporary' | 'Replacement'; subStageId: string; mainStageId: string; productionLineId: string; factoryId: string; factoryName: string; productionLineName: string; mainStageName: string; subStageName: string; startsAtUtc: string | null; endsAtUtc: string | null; reason: string | null; }
export interface WorkforceRow { workerId: string; employeeCode: string; fullName: string; departmentName: string | null; photoReference: string | null; hasPhoto: boolean; attendanceStatus: WorkforceAttendanceStatus; firstCheckInUtc: string | null; lastCheckOutUtc: string | null; hasAttendanceData: boolean; hasSinglePunch: boolean; assignments: WorkforceAssignment[]; isAssigned: boolean; hasTemporaryAssignment: boolean; needsReview: boolean; }
export interface WorkforceSummary { totalWorkers: number; presentWorkers: number; absentWorkers: number; lateWorkers: number; incompleteWorkers: number; unassignedPresentWorkers: number; assignedAbsentWorkers: number; reviewRequiredWorkers: number; attendanceDataAvailable: boolean; scope: string; }
export interface WorkforcePage { productionDate: string; items: WorkforceRow[]; summary: WorkforceSummary; page: number; pageSize: number; totalCount: number; totalPages: number; }
export interface WorkforceDetail { workerId: string; productionDate: string; attendanceRecords: { occurredAtUtc: string; label: 'Punch' }[]; assignments: WorkforceAssignment[]; }
export interface WorkforceQuery { productionDate: string; page?: number; pageSize?: number; search?: string; factoryId?: string; productionLineId?: string; mainStageId?: string; subStageId?: string; department?: string; attendanceFilter?: string; assignmentFilter?: string; operationalFilter?: string; sortBy?: string; sortDirection?: string; }

@Injectable({ providedIn: 'root' })
export class AttendanceWorkforceApiService {
  constructor(private readonly http: HttpClient) {}
  getPage(query: WorkforceQuery): Observable<WorkforcePage> { return this.http.get<ApiResponse<WorkforcePage>>(buildApiUrl('/api/attendance/workforce'), { params: this.params(query) }).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => this.unwrap(response))); }
  getDetail(workerId: string, productionDate: string): Observable<WorkforceDetail> { return this.http.get<ApiResponse<WorkforceDetail>>(buildApiUrl(`/api/attendance/workforce/workers/${encodeURIComponent(workerId)}/details`), { params: new HttpParams().set('productionDate', productionDate) }).pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => this.unwrap(response))); }
  private params(query: WorkforceQuery): HttpParams { let params = new HttpParams().set('productionDate', query.productionDate).set('page', String(query.page ?? 1)).set('pageSize', String(query.pageSize ?? 25)); for (const [key, value] of Object.entries(query)) if (key !== 'productionDate' && value !== undefined && value !== null && value !== '' && key !== 'page' && key !== 'pageSize') params = params.set(key, String(value)); return params; }
  private unwrap<T>(response: ApiResponse<T>): T { if (!response.success || response.data === undefined || response.data === null) throw new Error(response.error?.message || 'تعذر تحميل الحضور والتسكين.'); return response.data; }
}
