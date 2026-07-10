import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { ApiResponse } from '../models/api-response.model';

type RawRecord = Record<string, unknown>;

const ASSIGNMENTS_READ_TIMEOUT_MS = 1500;
const ASSIGNMENTS_WRITE_TIMEOUT_MS = 8000;

export type ApiAssignmentType = 'Default' | 'Temporary' | 'Replacement';

export interface DefaultAssignmentRequest {
  workerId: string;
  subStageId: string;
  reason?: string;
}

export interface TemporaryAssignmentRequest {
  workerId: string;
  fromSubStageId: string;
  toSubStageId: string;
  startAtUtc: string;
  endAtUtc: string;
  reason: string;
  replacementForWorkerId?: string;
}

export interface ReplacementAssignmentRequest {
  replacementWorkerId: string;
  replacedWorkerId: string;
  subStageId: string;
  startAtUtc: string;
  endAtUtc: string;
  reason: string;
}

export interface AssignmentActionResult {
  assignmentId: string;
  workerId: string;
  assignmentType: ApiAssignmentType | '';
  subStageId: string | null;
  fromSubStageId: string | null;
  toSubStageId: string | null;
  startsAtUtc: string | null;
  endsAtUtc: string | null;
  status: string;
  replacementForWorkerId: string | null;
}

export interface AssignmentWorker {
  id: string;
  fullName: string;
  code: string;
  assignmentType: ApiAssignmentType;
  fromSubStageId: string | null;
  replacementForWorkerId: string | null;
}

export interface SubStageWorkersData {
  subStageId: string;
  workers: AssignmentWorker[];
  hasBackendData: boolean;
  hasUsableBackendData: boolean;
}

export interface CurrentWorkerAssignment {
  workerId: string;
  effectiveSubStageId: string | null;
  assignmentType: ApiAssignmentType | null;
  startedAtUtc: string | null;
  endsAtUtc: string | null;
  fromSubStageId: string | null;
  toSubStageId: string | null;
  replacementForWorkerId: string | null;
}

export interface AssignmentTimelineEntry {
  id: string;
  workerId: string;
  fromSubStageId: string | null;
  toSubStageId: string | null;
  assignmentType: string;
  actionType: string;
  reason: string;
  startAtUtc: string | null;
  endAtUtc: string | null;
  createdAtUtc: string | null;
}

export interface AssignmentRecommendation {
  workerId: string;
  workerName: string;
  score: number;
  reasons: string[];
  risks: string[];
}

@Injectable({
  providedIn: 'root'
})
export class AssignmentsApiService {
  constructor(private readonly http: HttpClient) {}

  getSubStageWorkers(subStageId: string): Observable<SubStageWorkersData> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/sub-stages/${encodeURIComponent(subStageId)}/workers`))
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.mapSubStageWorkers(this.extractPayload(response), subStageId))
      );
  }

  getCurrentWorkerAssignment(workerId: string): Observable<CurrentWorkerAssignment> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/current-assignment`))
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.mapCurrentAssignment(this.extractPayload(response), workerId))
      );
  }

  getWorkerTimeline(workerId: string): Observable<AssignmentTimelineEntry[]> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/${encodeURIComponent(workerId)}/timeline`))
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.parseEntityList(this.extractPayload(response)).map((entry) => this.mapTimelineEntry(entry)))
      );
  }

  getRecommendations(subStageId: string): Observable<AssignmentRecommendation[]> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/assignments/recommendations'), { params: { subStageId } })
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.parseRecommendations(this.extractPayload(response)))
      );
  }

  createDefaultAssignment(request: DefaultAssignmentRequest): Observable<AssignmentActionResult> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl('/api/assignments/default'), request)
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  createTemporaryAssignment(request: TemporaryAssignmentRequest): Observable<AssignmentActionResult> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl('/api/assignments/temporary'), request)
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  createReplacementAssignment(request: ReplacementAssignmentRequest): Observable<AssignmentActionResult> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl('/api/assignments/replacement'), request)
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  cancelTemporaryAssignment(assignmentId: string): Observable<AssignmentActionResult> {
    return this.http
      .delete<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/temporary/${encodeURIComponent(assignmentId)}`))
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  private mapSubStageWorkers(payload: unknown, requestedSubStageId: string): SubStageWorkersData {
    const source = this.normalizeObject(payload);
    const workers = this.parseEntityList(this.pickFirst(source, ['items', 'workers', 'assignedWorkers'])).map((worker) =>
      this.mapAssignmentWorker(worker)
    );
    const validWorkers = workers.filter((worker) => this.hasText(worker.id) && this.hasText(worker.fullName) && this.hasText(worker.code));
    const subStageId = this.pickString(source, ['subStageId', 'id']) || requestedSubStageId;
    const hasBackendData = workers.length > 0;

    return {
      subStageId,
      workers: validWorkers,
      hasBackendData,
      hasUsableBackendData: hasBackendData && validWorkers.length === workers.length && this.hasText(subStageId)
    };
  }

  private mapAssignmentWorker(record: RawRecord): AssignmentWorker {
    return {
      id: this.pickString(record, ['workerId', 'id', '_id']),
      fullName: this.pickString(record, ['fullName', 'workerName', 'name', 'employeeName']),
      code: this.pickString(record, ['employeeCode', 'code', 'workerCode', 'empCode']),
      assignmentType: this.toAssignmentType(this.pickFirst(record, ['assignmentType', 'type'])),
      fromSubStageId: this.pickNullableString(record, ['fromSubStageId', 'fromStageId']),
      replacementForWorkerId: this.pickNullableString(record, ['replacementForWorkerId', 'replacedWorkerId'])
    };
  }

  private mapCurrentAssignment(payload: unknown, requestedWorkerId: string): CurrentWorkerAssignment {
    const record = this.normalizeObject(payload);
    return {
      workerId: this.pickString(record, ['workerId', 'id']) || requestedWorkerId,
      effectiveSubStageId: this.pickNullableString(record, ['effectiveSubStageId', 'subStageId']),
      assignmentType: this.toNullableAssignmentType(this.pickFirst(record, ['assignmentType', 'type'])),
      startedAtUtc: this.pickNullableString(record, ['startedAtUtc', 'startsAtUtc', 'startAtUtc']),
      endsAtUtc: this.pickNullableString(record, ['endsAtUtc', 'endAtUtc']),
      fromSubStageId: this.pickNullableString(record, ['fromSubStageId', 'fromStageId']),
      toSubStageId: this.pickNullableString(record, ['toSubStageId', 'toStageId']),
      replacementForWorkerId: this.pickNullableString(record, ['replacementForWorkerId', 'replacedWorkerId'])
    };
  }

  private mapTimelineEntry(record: RawRecord): AssignmentTimelineEntry {
    return {
      id: this.pickString(record, ['id', 'timelineId', '_id']),
      workerId: this.pickString(record, ['workerId']),
      fromSubStageId: this.pickNullableString(record, ['fromSubStageId']),
      toSubStageId: this.pickNullableString(record, ['toSubStageId']),
      assignmentType: this.pickString(record, ['assignmentType', 'type']),
      actionType: this.pickString(record, ['actionType', 'action']),
      reason: this.pickString(record, ['reason', 'details']),
      startAtUtc: this.pickNullableString(record, ['startAtUtc', 'startsAtUtc']),
      endAtUtc: this.pickNullableString(record, ['endAtUtc', 'endsAtUtc']),
      createdAtUtc: this.pickNullableString(record, ['createdAtUtc', 'createdAt'])
    };
  }

  private parseRecommendations(payload: unknown): AssignmentRecommendation[] {
    const source = this.normalizeObject(payload);
    const candidates = this.parseEntityList(this.pickFirst(source, ['candidates', 'items', 'recommendations']));

    return candidates
      .map((candidate) => ({
        workerId: this.pickString(candidate, ['workerId', 'id']),
        workerName: this.pickString(candidate, ['workerName', 'fullName', 'name']),
        score: this.toNumber(this.pickFirst(candidate, ['score', 'rank', 'points'])),
        reasons: this.toStringList(this.pickFirst(candidate, ['reasons', 'reasonList'])),
        risks: this.toStringList(this.pickFirst(candidate, ['riskWarnings', 'risks', 'warnings']))
      }))
      .filter((candidate) => this.hasText(candidate.workerId) && this.hasText(candidate.workerName));
  }

  private mapActionResult(payload: unknown): AssignmentActionResult {
    const record = this.normalizeObject(payload);
    return {
      assignmentId: this.pickString(record, ['assignmentId', 'id']),
      workerId: this.pickString(record, ['workerId', 'replacementWorkerId']),
      assignmentType: this.toNullableAssignmentType(this.pickFirst(record, ['assignmentType', 'type'])) ?? '',
      subStageId: this.pickNullableString(record, ['subStageId']),
      fromSubStageId: this.pickNullableString(record, ['fromSubStageId']),
      toSubStageId: this.pickNullableString(record, ['toSubStageId']),
      startsAtUtc: this.pickNullableString(record, ['startsAtUtc', 'startAtUtc', 'startsAt']),
      endsAtUtc: this.pickNullableString(record, ['endsAtUtc', 'endAtUtc', 'endsAt']),
      status: this.pickString(record, ['status']),
      replacementForWorkerId: this.pickNullableString(record, ['replacementForWorkerId', 'replacedWorkerId'])
    };
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

  private parseEntityList(value: unknown): RawRecord[] {
    if (Array.isArray(value)) {
      return value.map((item) => this.normalizeObject(item));
    }

    const source = this.normalizeObject(value);
    const nested = this.pickFirst(source, ['items', 'data', 'results', 'resultsList']);
    return Array.isArray(nested) ? nested.map((item) => this.normalizeObject(item)) : [];
  }

  private normalizeObject(value: unknown): RawRecord {
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as RawRecord) : {};
  }

  private pickFirst(record: RawRecord, keys: string[]): unknown {
    for (const key of keys) {
      if (Object.prototype.hasOwnProperty.call(record, key) && record[key] !== undefined && record[key] !== null) {
        return record[key];
      }
    }
    return undefined;
  }

  private pickString(record: RawRecord, keys: string[]): string {
    const value = this.pickFirst(record, keys);
    return typeof value === 'string' && value.trim().length > 0 ? value.trim() : '';
  }

  private pickNullableString(record: RawRecord, keys: string[]): string | null {
    const value = this.pickString(record, keys);
    return value || null;
  }

  private toAssignmentType(value: unknown): ApiAssignmentType {
    return this.toNullableAssignmentType(value) ?? 'Default';
  }

  private toNullableAssignmentType(value: unknown): ApiAssignmentType | null {
    const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
    if (normalized === 'default' || normalized === 'ثابت') {
      return 'Default';
    }
    if (normalized === 'temporary' || normalized === 'مؤقت') {
      return 'Temporary';
    }
    if (normalized === 'replacement' || normalized === 'بديل') {
      return 'Replacement';
    }
    return null;
  }

  private toStringList(value: unknown): string[] {
    return Array.isArray(value)
      ? value.filter((item): item is string => typeof item === 'string' && item.trim().length > 0).map((item) => item.trim())
      : [];
  }

  private toNumber(value: unknown): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : 0;
    }
    return 0;
  }

  private hasText(value: string): boolean {
    return value.trim().length > 0;
  }
}
