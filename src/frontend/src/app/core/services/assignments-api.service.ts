import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { map, Observable, of, timeout } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { ApiResponse } from '../models/api-response.model';
import { isBackendGuid } from '../../shared/models/assignment-context.model';

type RawRecord = Record<string, unknown>;

const ASSIGNMENTS_READ_TIMEOUT_MS = 1500;
const ASSIGNMENTS_WRITE_TIMEOUT_MS = 8000;

export type ApiAssignmentType = 'Default' | 'Temporary' | 'Replacement';

export interface DefaultAssignmentRequest {
  workerId: string;
  productionLineId: string;
  subStageId: string;
  reason?: string;
}

export interface StageDefaultAssignmentsUpdateResult {
  subStageId: string;
  addedWorkersCount: number;
  removedWorkersCount: number;
  activeWorkerIds: string[];
}

export interface TemporaryAssignmentRequest {
  workerId: string;
  fromSubStageId?: string | null;
  toSubStageId: string;
  startAtUtc: string;
  endAtUtc: string;
  reason: string;
  replacementForWorkerId?: string;
  participationMode?: 'TemporaryMove' | 'AdditionalParticipation';
}

export interface MoveCurrentAssignmentRequest {
  workerId: string;
  sourceAssignmentId: string;
  fromSubStageId: string;
  toSubStageId: string;
  effectiveAtUtc: string;
  temporaryEndAtUtc?: string;
  reason: string;
}

export interface ReplacementAssignmentRequest {
  replacementWorkerId: string;
  replacedWorkerId: string;
  subStageId: string;
  fromSubStageId?: string | null;
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

export interface AssignmentWorkflowWorker {
  workerId: string;
  employeeCode: string;
  fullName: string;
  photoReference?: string | null;
  departmentName?: string | null;
  attendanceStatus: 'Present' | 'Late' | 'Absent' | 'Unassigned' | '';
  attendanceTimeUtc: string | null;
  attendanceSource?: string | null;
  attendanceEvidence?: 'ActualCheckInFound' | 'ConfirmedAbsent' | 'NoSourceCheckIn' | 'NoAttendanceData' | '';
  hasAttendanceData?: boolean;
  actualCheckInFound?: boolean;
  assignmentId?: string | null;
  assignmentType: ApiAssignmentType | null;
  assignmentStartsAtUtc?: string | null;
  assignmentEndsAtUtc?: string | null;
  effectiveSubStageId: string | null;
  isAvailable: boolean;
}

export interface SubStageWorkerContext {
  subStageId: string;
  productionDate?: string;
  activeServiceWorkersCount?: number;
  workersWithAttendanceDataCount?: number;
  actualCheckInWorkersCount?: number;
  noSourceCheckInWorkersCount?: number;
  currentWorkers: AssignmentWorkflowWorker[];
  presentWorkers: AssignmentWorkflowWorker[];
  availableWorkers: AssignmentWorkflowWorker[];
  unavailableWorkersCount: number;
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
  workerCode?: string;
  score: number | null;
  reasons: string[];
  risks: string[];
}

/**
 * Organizational staffing only.  This deliberately contains no attendance
 * status: daily production operations apply attendance to this plan later.
 */
export interface LineStaffingWorker {
  workerId: string;
  employeeCode: string;
  fullName: string;
  departmentName: string | null;
  isOnActiveService: boolean;
  hasPhoto: boolean;
  photoReference: string | null;
  photoVersion: string | null;
  defaultSubStageId: string | null;
  defaultSubStageName: string | null;
  effectiveAssignmentId: string | null;
  effectiveAssignmentType: ApiAssignmentType | null;
  effectiveSubStageId: string | null;
  effectiveSubStageName: string | null;
  fromSubStageId: string | null;
  fromSubStageName: string | null;
  temporaryStartsAtUtc: string | null;
  temporaryEndsAtUtc: string | null;
  replacementForWorkerId: string | null;
  participations: LineStaffingParticipation[];
}

export interface LineStaffingParticipation {
  assignmentId: string;
  assignmentType: ApiAssignmentType;
  subStageId: string;
  subStageName: string | null;
  fromSubStageId: string | null;
  fromSubStageName: string | null;
  startsAtUtc: string | null;
  endsAtUtc: string | null;
  replacementForWorkerId: string | null;
  temporaryParticipationMode: 'TemporaryMove' | 'AdditionalParticipation' | null;
}

export interface LineStaffingStage {
  productModelStageId: string;
  subStageId: string;
  mainStageName: string;
  stageCode: string;
  stageName: string;
  stageOrder: number;
  piecePrice: number;
  compensationMode: string;
  compensationConfigurationStatus: string;
  isFinancialReviewPending: boolean;
  defaultAssignedWorkersCount: number;
  effectiveAssignedWorkersCount: number;
  temporaryAssignedWorkersCount: number;
  requiredWorkers: number | null;
  hasAuthoritativeRequiredWorkerCount: boolean;
  staffingStatus: string;
  workerStatusText: string;
  effectiveWorkerIds: string[];
}

export interface LineStaffingPlan {
  factoryId: string;
  factoryName: string;
  productionLineId: string;
  productionLineName: string;
  productModelId: string;
  productModelCode: string;
  productModelName: string;
  staffingReferenceDate: string;
  totalStages: number;
  stagesWithWorkers: number;
  stagesWithoutWorkers: number;
  stagesWithTemporaryAssignments: number;
  stagesNeedingCompensationReview: number;
  stagesNeedingStaffingReview: number;
  overallStaffingStatus: string;
  staffingPlanComplete: boolean;
  operationalAttendanceChecked: boolean;
  financialConfigurationPending: boolean;
  stages: LineStaffingStage[];
  workers: LineStaffingWorker[];
}

/** Narrow authoritative payload used after one stage's staffing changes. */
export interface LineStaffingStageRefresh {
  stage: LineStaffingStage;
  stages: LineStaffingStage[];
  workers: LineStaffingWorker[];
  stagesWithWorkers: number;
  stagesWithoutWorkers: number;
  stagesWithTemporaryAssignments: number;
  stagesNeedingCompensationReview: number;
  stagesNeedingStaffingReview: number;
  overallStaffingStatus: string;
  staffingPlanComplete: boolean;
  operationalAttendanceChecked: boolean;
  financialConfigurationPending: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class AssignmentsApiService {
  constructor(private readonly http: HttpClient) {}

  getLineStaffingPlan(factoryId: string, productionLineId: string, productModelId: string, staffingReferenceDate: string): Observable<LineStaffingPlan> {
    const query = new URLSearchParams({ factoryId, productionLineId, productModelId, staffingReferenceDate });
    return this.http
      .get<ApiResponse<LineStaffingPlan>>(buildApiUrl(`/api/line-staffing?${query.toString()}`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.extractPayload(response)));
  }

  getLineStaffingStageRefresh(factoryId: string, productionLineId: string, productModelId: string, subStageId: string, staffingReferenceDate: string): Observable<LineStaffingStageRefresh> {
    const query = new URLSearchParams({ factoryId, productionLineId, productModelId, staffingReferenceDate });
    return this.http
      .get<ApiResponse<LineStaffingStageRefresh>>(buildApiUrl(`/api/line-staffing/stages/${encodeURIComponent(subStageId)}?${query.toString()}`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.extractPayload(response)));
  }

  /**
   * Shared organizational worker source for staffing dialogs. It filters only
   * active employment and never applies daily attendance eligibility.
   */
  getActiveLineStaffingWorkers(staffingReferenceDate: string): Observable<LineStaffingWorker[]> {
    const query = new URLSearchParams({ staffingReferenceDate });
    return this.http
      .get<ApiResponse<LineStaffingWorker[]>>(buildApiUrl(`/api/line-staffing/workers?${query.toString()}`))
      .pipe(timeout(STANDARD_API_TIMEOUT_MS), map((response) => this.extractPayload(response)));
  }

  getSubStageWorkers(subStageId: string): Observable<SubStageWorkersData> {
    if (!isBackendGuid(subStageId)) {
      return of<SubStageWorkersData>({
        subStageId,
        workers: [],
        hasBackendData: false,
        hasUsableBackendData: false
      });
    }

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/sub-stages/${encodeURIComponent(subStageId)}/workers`))
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.mapSubStageWorkers(this.extractPayload(response), subStageId))
      );
  }

  getSubStageWorkerContext(subStageId: string, productionDate: string): Observable<SubStageWorkerContext> {
    if (!isBackendGuid(subStageId)) {
      return of({ subStageId, productionDate, activeServiceWorkersCount: 0, workersWithAttendanceDataCount: 0, actualCheckInWorkersCount: 0, noSourceCheckInWorkersCount: 0, currentWorkers: [], presentWorkers: [], availableWorkers: [], unavailableWorkersCount: 0 });
    }

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/sub-stages/${encodeURIComponent(subStageId)}/worker-context?productionDate=${encodeURIComponent(productionDate)}`))
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map((response) => this.mapSubStageWorkerContext(this.extractPayload(response), subStageId))
      );
  }

  getFactoryStructureSubStageWorkers(subStageId: string): Observable<SubStageWorkersData> {
    if (!isBackendGuid(subStageId)) {
      return of<SubStageWorkersData>({
        subStageId,
        workers: [],
        hasBackendData: false,
        hasUsableBackendData: false
      });
    }

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/factory-structure/sub-stages/${encodeURIComponent(subStageId)}/workers`))
      .pipe(
        timeout(STANDARD_API_TIMEOUT_MS),
        map((response) => this.mapSubStageWorkers(this.extractPayload(response), subStageId))
      );
  }

  getCurrentWorkerAssignment(workerId: string): Observable<CurrentWorkerAssignment> {
    if (!this.isBackendWorkerId(workerId)) {
      return of<CurrentWorkerAssignment>({
        workerId,
        effectiveSubStageId: null,
        assignmentType: null,
        startedAtUtc: null,
        endsAtUtc: null,
        fromSubStageId: null,
        toSubStageId: null,
        replacementForWorkerId: null
      });
    }

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/workers/${encodeURIComponent(workerId)}/current-assignment`))
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.mapCurrentAssignment(this.extractPayload(response), workerId))
      );
  }

  getWorkerTimeline(workerId: string): Observable<AssignmentTimelineEntry[]> {
    if (!this.isBackendWorkerId(workerId)) {
      return of<AssignmentTimelineEntry[]>([]);
    }

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/${encodeURIComponent(workerId)}/timeline`))
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.parseEntityList(this.extractPayload(response)).map((entry) => this.mapTimelineEntry(entry)))
      );
  }

  getRecommendations(productionLineId: string, subStageId: string): Observable<AssignmentRecommendation[]> {
    if (!isBackendGuid(productionLineId) || !isBackendGuid(subStageId)) {
      return of([]);
    }

    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/assignments/recommendations'), { params: { productionLineId, subStageId } })
      .pipe(
        timeout(ASSIGNMENTS_READ_TIMEOUT_MS),
        map((response) => this.parseRecommendations(this.extractPayload(response)))
      );
  }

  createDefaultAssignment(request: DefaultAssignmentRequest, correlationId?: string): Observable<AssignmentActionResult> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl('/api/assignments/default'), request, { headers: this.correlationHeaders(correlationId) })
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  updateStageDefaultAssignments(productionLineId: string, subStageId: string, workerIds: string[], correlationId?: string): Observable<StageDefaultAssignmentsUpdateResult> {
    return this.http
      .put<ApiResponse<StageDefaultAssignmentsUpdateResult>>(
        buildApiUrl(`/api/assignments/default/stages/${encodeURIComponent(subStageId)}`),
        { productionLineId, workerIds },
        { headers: this.correlationHeaders(correlationId) }
      )
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.extractPayload(response)));
  }

  removeDefaultAssignment(workerId: string, productionLineId: string, subStageId: string, reason: string, correlationId?: string): Observable<AssignmentActionResult> {
    return this.http
      .delete<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/default/${encodeURIComponent(workerId)}`), { params: { productionLineId, subStageId, reason }, headers: this.correlationHeaders(correlationId) })
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  createFactoryStructureDefaultAssignment(request: DefaultAssignmentRequest): Observable<AssignmentActionResult> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl('/api/factory-structure/assignments/default'), request)
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

  moveCurrentAssignment(request: MoveCurrentAssignmentRequest): Observable<AssignmentActionResult> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl('/api/assignments/move'), request)
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  cancelTemporaryAssignment(assignmentId: string, reason: string): Observable<AssignmentActionResult> {
    return this.http
      .delete<ApiResponse<unknown>>(buildApiUrl(`/api/assignments/temporary/${encodeURIComponent(assignmentId)}`), { params: { reason } })
      .pipe(timeout(ASSIGNMENTS_WRITE_TIMEOUT_MS), map((response) => this.mapActionResult(this.extractPayload(response))));
  }

  private correlationHeaders(correlationId?: string): HttpHeaders | undefined {
    return correlationId
      ? new HttpHeaders({ 'X-Manufacturing-Realtime-Correlation-Id': correlationId })
      : undefined;
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

  private mapSubStageWorkerContext(payload: unknown, requestedSubStageId: string): SubStageWorkerContext {
    const source = this.normalizeObject(payload);
    return {
      subStageId: this.pickString(source, ['subStageId']) || requestedSubStageId,
      productionDate: this.pickString(source, ['productionDate']),
      activeServiceWorkersCount: Math.max(0, this.toNumber(this.pickFirst(source, ['activeServiceWorkersCount']))),
      workersWithAttendanceDataCount: Math.max(0, this.toNumber(this.pickFirst(source, ['workersWithAttendanceDataCount']))),
      actualCheckInWorkersCount: Math.max(0, this.toNumber(this.pickFirst(source, ['actualCheckInWorkersCount']))),
      noSourceCheckInWorkersCount: Math.max(0, this.toNumber(this.pickFirst(source, ['noSourceCheckInWorkersCount']))),
      currentWorkers: this.parseEntityList(this.pickFirst(source, ['currentWorkers'])).map((worker) => this.mapWorkflowWorker(worker)),
      presentWorkers: this.parseEntityList(this.pickFirst(source, ['presentWorkers'])).map((worker) => this.mapWorkflowWorker(worker)),
      availableWorkers: this.parseEntityList(this.pickFirst(source, ['availableWorkers'])).map((worker) => this.mapWorkflowWorker(worker)),
      unavailableWorkersCount: Math.max(0, this.toNumber(this.pickFirst(source, ['unavailableWorkersCount'])))
    };
  }

  private mapWorkflowWorker(record: RawRecord): AssignmentWorkflowWorker {
    return {
      workerId: this.pickString(record, ['workerId', 'id']),
      employeeCode: this.pickString(record, ['employeeCode', 'code', 'workerCode']),
      fullName: this.pickString(record, ['fullName', 'workerName', 'name']),
      photoReference: this.pickNullableString(record, ['photoReference', 'photoUrl']),
      departmentName: this.pickNullableString(record, ['departmentName', 'localDepartmentName', 'department']),
      attendanceStatus: this.toAttendanceStatus(this.pickFirst(record, ['attendanceStatus', 'status'])),
      attendanceTimeUtc: this.pickNullableString(record, ['attendanceTimeUtc', 'attendanceTime']),
      attendanceSource: this.pickNullableString(record, ['attendanceSource', 'source']),
      attendanceEvidence: this.toAttendanceEvidence(this.pickFirst(record, ['attendanceEvidence'])),
      hasAttendanceData: this.toBoolean(this.pickFirst(record, ['hasAttendanceData'])),
      actualCheckInFound: this.toBoolean(this.pickFirst(record, ['actualCheckInFound'])),
      assignmentId: this.pickNullableString(record, ['assignmentId']),
      assignmentType: this.toNullableAssignmentType(this.pickFirst(record, ['assignmentType', 'type'])),
      assignmentStartsAtUtc: this.pickNullableString(record, ['assignmentStartsAtUtc', 'startsAtUtc', 'startAtUtc']),
      assignmentEndsAtUtc: this.pickNullableString(record, ['assignmentEndsAtUtc', 'endsAtUtc', 'endAtUtc']),
      effectiveSubStageId: this.pickNullableString(record, ['effectiveSubStageId', 'subStageId']),
      isAvailable: this.toBoolean(this.pickFirst(record, ['isAvailable', 'available']))
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
    const candidates = this.parseEntityList(
      Array.isArray(payload) ? payload : this.pickFirst(source, ['candidates', 'items', 'recommendations', 'results', 'data'])
    );

    return candidates
      .map((candidate) => {
        const workerCode = this.pickNullableString(candidate, ['workerCode']);
        return {
          workerId: this.pickString(candidate, ['workerId', 'id']),
          workerName: this.pickString(candidate, ['workerName', 'fullName', 'name']),
          ...(workerCode ? { workerCode } : {}),
          score: this.toNullableNumber(this.pickFirst(candidate, ['score', 'rank', 'points'])),
          reasons: this.toStringList(this.pickFirst(candidate, ['reasons', 'reasonList'])),
          risks: this.toStringList(this.pickFirst(candidate, ['riskWarnings', 'risks', 'warnings']))
        };
      })
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

  private toAttendanceStatus(value: unknown): AssignmentWorkflowWorker['attendanceStatus'] {
    const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
    if (normalized === 'present') return 'Present';
    if (normalized === 'late') return 'Late';
    if (normalized === 'absent') return 'Absent';
    if (normalized === 'unassigned') return 'Unassigned';
    return '';
  }

  private toAttendanceEvidence(value: unknown): AssignmentWorkflowWorker['attendanceEvidence'] {
    const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
    if (normalized === 'actualcheckinfound') return 'ActualCheckInFound';
    if (normalized === 'confirmedabsent') return 'ConfirmedAbsent';
    if (normalized === 'nosourcecheckin') return 'NoSourceCheckIn';
    if (normalized === 'noattendancedata') return 'NoAttendanceData';
    return '';
  }

  private toBoolean(value: unknown): boolean {
    return value === true || value === 'true' || value === 1 || value === '1';
  }

  private toNumber(value: unknown): number {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : 0;
    }
    return 0;
  }

  private toStringList(value: unknown): string[] {
    return Array.isArray(value)
      ? value.filter((item): item is string => typeof item === 'string' && item.trim().length > 0).map((item) => item.trim())
      : [];
  }

  private toNullableNumber(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : null;
    }
    return null;
  }

  private hasText(value: string): boolean {
    return value.trim().length > 0;
  }

  private isBackendWorkerId(workerId: string): boolean {
    return isBackendGuid(workerId);
  }
}
