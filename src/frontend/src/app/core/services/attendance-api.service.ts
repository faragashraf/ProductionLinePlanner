import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { ATTENDANCE_SYNC_TIMEOUT_MS, STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';

export type AttendanceStatus = 'Present' | 'Late' | 'Absent' | 'Unassigned';

export interface AttendanceWorkerState {
  workerId: string;
  employeeCode: string;
  fullName: string;
  attendanceStatus: AttendanceStatus;
  attendanceTimeUtc: string;
  source?: string | null;
}

export interface AttendanceTodaySnapshot {
  date: string;
  items: AttendanceWorkerState[];
}

export interface AttendanceSyncResult {
  syncDateUtc: string;
  sourceUsersCount: number;
  sourceCheckInsCount: number;
  matchedWorkersCount: number;
  unmatchedSourceUsersCount: number;
  workersWithoutAttendanceCount: number;
  insertedRecords: number;
  updatedRecords: number;
  skippedRecords: number;
}

@Injectable({ providedIn: 'root' })
export class AttendanceApiService {
  constructor(private readonly http: HttpClient) {}

  getToday(): Observable<AttendanceTodaySnapshot> {
    return this.http
      .get<ApiResponse<AttendanceTodaySnapshot>>(buildApiUrl('/api/attendance/today'))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => this.unwrap(response)));
  }

  syncToday(): Observable<AttendanceSyncResult> {
    return this.http
      .post<ApiResponse<AttendanceSyncResult>>(buildApiUrl('/api/attendance/sync/today'), {})
      .pipe(timeout(ATTENDANCE_SYNC_TIMEOUT_MS), map(response => this.unwrap(response)));
  }

  syncForProductionDate(productionDate: string): Observable<AttendanceSyncResult> {
    return this.http
      .post<ApiResponse<AttendanceSyncResult>>(buildApiUrl(`/api/attendance/sync/production-date/${encodeURIComponent(productionDate)}`), {})
      .pipe(timeout(ATTENDANCE_SYNC_TIMEOUT_MS), map(response => this.unwrap(response)));
  }

  getForProductionDate(productionDate: string): Observable<AttendanceTodaySnapshot> {
    return this.http
      .get<ApiResponse<AttendanceTodaySnapshot>>(buildApiUrl(`/api/attendance/today?dateUtc=${encodeURIComponent(`${productionDate}T12:00:00.000Z`)}`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map(response => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data === undefined || response.data === null) {
      throw new Error(response.error?.message || 'تعذر إتمام عملية الحضور.');
    }

    return response.data;
  }
}
