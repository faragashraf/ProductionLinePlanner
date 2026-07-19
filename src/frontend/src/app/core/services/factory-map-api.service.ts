import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, forkJoin, map, Observable, of } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { PERMISSIONS } from '../config/permission-identifiers';
import { PermissionService } from './permission.service';
import { FactoryStatus } from '../../shared/models/factory-status.model';
import {
  FactoryLayout,
  LayoutNode,
  LayoutPosition,
  MainStageLayout,
  ProductionLineLayout,
  SubStageLayout
} from '../../shared/models/factory-visualization.model';

type RawRecord = Record<string, unknown>;
type FactoryMapFallbackReason = 'incomplete' | 'connection';
type StaffingStatus = 'RequirementNotDefined' | 'Unstaffed' | 'Understaffed' | 'Staffed';
type SubStageAttendanceStatus = 'FullyPresent' | 'PartiallyPresent' | 'AllAbsent' | 'NeedsSync' | 'NoAssignments' | 'NotAuthorized' | 'Unavailable';
type AttendanceSummaryAvailability = 'available' | 'not-authorized' | 'unavailable';
interface HierarchySummaryOverride {
  workersCurrent?: number;
  presentAssignedWorkers?: number;
  absentAssignedWorkers?: number;
}

export function createEmptyFactoryLayout(): FactoryLayout {
  return {
    id: 'factory-empty',
    type: 'factory',
    name: 'خريطة المصنع',
    status: 'info',
    readinessPercent: 0,
    workersCurrent: 0,
    workersRequired: 0,
    workerRequirementDefined: false,
    description: 'لا توجد بيانات تسكين متاحة من الخادم.',
    lines: []
  };
}

export interface FactoryMapApiData {
  layout: FactoryLayout;
  hasBackendData: boolean;
  hasUsableBackendData: boolean;
  fallbackReason?: FactoryMapFallbackReason;
}

@Injectable({ providedIn: 'root' })
export class FactoryMapApiService {
  constructor(
    private readonly http: HttpClient,
    private readonly permissionService: PermissionService
  ) {}

  loadFactoryMapData(): Observable<FactoryMapApiData> {
    return forkJoin({
      factories: this.getEntityList('/api/factories?pageSize=200'),
      productionLines: this.getEntityList('/api/production-lines?pageSize=200'),
      mainStages: this.getEntityList('/api/main-stages?isActive=true&pageSize=200'),
      subStages: this.getEntityList('/api/sub-stages?isActive=true&pageSize=200'),
      staffingCoverage: this.getEntityList('/api/factory-structure/sub-stages/staffing-coverage'),
      attendanceSummary: this.loadAttendanceSummary()
    }).pipe(
      map(({ factories, productionLines, mainStages, subStages, staffingCoverage, attendanceSummary }) => {
        const selectedFactory = factories[0] ?? {};
        const selectedFactoryId = this.resolveString(selectedFactory, ['id', 'factoryId', '_id']);
        const mapped = this.mapFactoryLayout(
          selectedFactory,
          productionLines.filter((line) => this.resolveString(line, ['factoryId', 'parentFactoryId']) === selectedFactoryId),
          mainStages,
          subStages,
          this.indexByKey(staffingCoverage, ['subStageId', 'id']),
          this.indexByKey(attendanceSummary.items, ['subStageId', 'id']),
          attendanceSummary.availability
        );
        const hasBackendData = factories.length > 0;

        if (!hasBackendData || !this.hasUsableBackendData(mapped, hasBackendData)) {
          return this.createFallbackData('incomplete', hasBackendData);
        }

        return { layout: mapped, hasBackendData: true, hasUsableBackendData: true };
      }),
      catchError(() => of(this.createFallbackData('connection')))
    );
  }

  private loadAttendanceSummary(): Observable<{ items: RawRecord[]; availability: AttendanceSummaryAvailability }> {
    if (!this.permissionService.hasPermission(PERMISSIONS.attendance.view)) {
      return of({ items: [], availability: 'not-authorized' as const });
    }

    return this.getEntityList('/api/factory-structure/sub-stages/attendance-summary').pipe(
      map((items) => ({ items, availability: 'available' as const })),
      catchError(() => of({ items: [], availability: 'unavailable' as const }))
    );
  }

  private mapFactoryLayout(
    selectedFactory: RawRecord,
    productionLines: RawRecord[],
    mainStages: RawRecord[],
    subStages: RawRecord[],
    staffingCoverageBySubStageId: Map<string, RawRecord>,
    attendanceSummaryBySubStageId: Map<string, RawRecord>,
    attendanceSummaryAvailability: AttendanceSummaryAvailability
  ): FactoryLayout {
    const mainStageMap = this.groupByParentId(mainStages, ['lineId', 'productionLineId', 'parentLineId']);
    const subStageByMainMap = this.groupByParentId(subStages, ['mainStageId', 'parentMainStageId', 'parentId']);
    const lines = productionLines.map((line, index) => this.mapLine(
      line,
      index,
      mainStageMap,
      subStageByMainMap,
      staffingCoverageBySubStageId,
      attendanceSummaryBySubStageId,
      attendanceSummaryAvailability
    ));
    const subStageIds = lines.flatMap(line => line.stages.flatMap(stage => stage.subStages.map(subStage => subStage.id)));
    const summary = this.summarizeNodes(lines, this.hierarchySummaryOverride(
      subStageIds,
      staffingCoverageBySubStageId,
      attendanceSummaryBySubStageId,
      'factory'
    ));

    return {
      id: this.resolveString(selectedFactory, ['id', 'factoryId', '_id']) || 'factory-01',
      type: 'factory',
      name: this.resolveString(selectedFactory, ['name', 'factoryName', 'title']) || 'مصنع غير محدد',
      status: this.statusFor(summary.staffingStatus, summary.readinessPercent),
      readinessPercent: summary.readinessPercent,
      workersCurrent: summary.workersCurrent,
      workersRequired: summary.workersRequired,
      workerRequirementDefined: summary.workerRequirementDefined,
      staffingSummaryAvailable: summary.staffingSummaryAvailable,
      attendanceSummaryAvailable: summary.attendanceSummaryAvailable,
      presentAssignedWorkers: summary.presentAssignedWorkers,
      absentAssignedWorkers: summary.absentAssignedWorkers,
      attendanceStatus: summary.attendanceStatus,
      attendanceSummaryText: this.attendanceSummaryText(summary),
      assignmentParticipationsCount: summary.assignmentParticipationsCount,
      description: 'خريطة مرئية لتغطية التسكين الفعّال.',
      lines
    };
  }

  private mapLine(
    lineRecord: RawRecord,
    index: number,
    mainStageMap: Map<string, RawRecord[]>,
    subStageByMainMap: Map<string, RawRecord[]>,
    staffingCoverageBySubStageId: Map<string, RawRecord>,
    attendanceSummaryBySubStageId: Map<string, RawRecord>,
    attendanceSummaryAvailability: AttendanceSummaryAvailability
  ): ProductionLineLayout {
    const id = this.resolveString(lineRecord, ['id', 'lineId', 'productionLineId', '_id'], `line-${index + 1}`);
    const name = this.resolveString(lineRecord, ['name', 'lineName', 'title'], `الخط ${index + 1}`);
    const stages = (mainStageMap.get(id) ?? []).map((stage, stageIndex) => this.mapMainStage(
      stage,
      stageIndex,
      id,
      name,
      subStageByMainMap,
      staffingCoverageBySubStageId,
      attendanceSummaryBySubStageId,
      attendanceSummaryAvailability
    ));
    const subStageIds = stages.flatMap(stage => stage.subStages.map(subStage => subStage.id));
    const summary = this.summarizeNodes(stages, this.hierarchySummaryOverride(
      subStageIds,
      staffingCoverageBySubStageId,
      attendanceSummaryBySubStageId,
      'productionLine'
    ));
    const activeStage = stages[0];

    return {
      id,
      type: 'line',
      name,
      status: this.statusFor(summary.staffingStatus, summary.readinessPercent),
      readinessPercent: summary.readinessPercent,
      statusText: this.statusText(summary.staffingStatus),
      activeStageId: activeStage?.id ?? '',
      activeStageName: activeStage?.name ?? 'بدون مرحلة نشطة',
      workersCurrent: summary.workersCurrent,
      workersRequired: summary.workersRequired,
      workerRequirementDefined: summary.workerRequirementDefined,
      staffingSummaryAvailable: summary.staffingSummaryAvailable,
      attendanceSummaryAvailable: summary.attendanceSummaryAvailable,
      presentAssignedWorkers: summary.presentAssignedWorkers,
      absentAssignedWorkers: summary.absentAssignedWorkers,
      attendanceStatus: summary.attendanceStatus,
      attendanceSummaryText: this.attendanceSummaryText(summary),
      assignmentParticipationsCount: summary.assignmentParticipationsCount,
      position: this.parsePosition(lineRecord),
      stages,
      description: this.resolveString(lineRecord, ['description', 'summary'])
    };
  }

  private mapMainStage(
    stageRecord: RawRecord,
    index: number,
    lineId: string,
    lineName: string,
    subStageByMainMap: Map<string, RawRecord[]>,
    staffingCoverageBySubStageId: Map<string, RawRecord>,
    attendanceSummaryBySubStageId: Map<string, RawRecord>,
    attendanceSummaryAvailability: AttendanceSummaryAvailability
  ): MainStageLayout {
    const id = this.resolveString(stageRecord, ['id', 'mainStageId', 'stageId', '_id'], `${lineId}-stage-${index + 1}`);
    const subStages = (subStageByMainMap.get(id) ?? []).map((subStage, subStageIndex) =>
      this.mapSubStage(
        subStage,
        `${id}-sub-${subStageIndex + 1}`,
        staffingCoverageBySubStageId,
        attendanceSummaryBySubStageId,
        attendanceSummaryAvailability
      )
    );
    const summary = this.summarizeNodes(subStages, this.hierarchySummaryOverride(
      subStages.map(subStage => subStage.id),
      staffingCoverageBySubStageId,
      attendanceSummaryBySubStageId,
      'mainStage'
    ));

    return {
      id,
      type: 'main-stage',
      name: this.resolveString(stageRecord, ['name', 'stageName', 'title'], `${lineName} - مرحلة`),
      status: this.statusFor(summary.staffingStatus, summary.readinessPercent),
      readinessPercent: summary.readinessPercent,
      workersCurrent: summary.workersCurrent,
      workersRequired: summary.workersRequired,
      workerRequirementDefined: summary.workerRequirementDefined,
      staffingSummaryAvailable: summary.staffingSummaryAvailable,
      attendanceSummaryAvailable: summary.attendanceSummaryAvailable,
      presentAssignedWorkers: summary.presentAssignedWorkers,
      absentAssignedWorkers: summary.absentAssignedWorkers,
      attendanceStatus: summary.attendanceStatus,
      attendanceSummaryText: this.attendanceSummaryText(summary),
      assignmentParticipationsCount: summary.assignmentParticipationsCount,
      note: this.resolveString(stageRecord, ['note', 'description']),
      position: this.parsePosition(stageRecord),
      subStages
    };
  }

  private mapSubStage(
    subStageRecord: RawRecord,
    fallbackId: string,
    staffingCoverageBySubStageId: Map<string, RawRecord>,
    attendanceSummaryBySubStageId: Map<string, RawRecord>,
    attendanceSummaryAvailability: AttendanceSummaryAvailability
  ): SubStageLayout {
    const id = this.resolveString(subStageRecord, ['id', 'subStageId', '_id'], fallbackId);
    const coverage = staffingCoverageBySubStageId.get(id);
    const staffingSummaryAvailable = !!coverage;
    const workerRequirementDefined = staffingSummaryAvailable && this.toBoolean(
      this.pickFirst(coverage!, ['hasAuthoritativeRequiredWorkerCount']),
      false
    );
    const workersCurrent = staffingSummaryAvailable
      ? this.toNumber(this.pickFirst(coverage!, ['assignedWorkersCount']), 0)
      : 0;
    const workersRequired = workerRequirementDefined
      ? this.toNumber(this.pickFirst(coverage!, ['requiredWorkersCount']), 0)
      : 0;
    const staffingStatus = staffingSummaryAvailable
      ? this.toStaffingStatus(this.pickFirst(coverage!, ['staffingStatus']), workersCurrent, workersRequired, workerRequirementDefined)
      : 'RequirementNotDefined';
    const readinessPercent = workerRequirementDefined
      ? this.toPercent(this.pickFirst(coverage!, ['assignmentCoveragePercent']), workersCurrent, workersRequired)
      : 0;
    const attendance = attendanceSummaryBySubStageId.get(id);
    const attendanceSummaryAvailable = attendanceSummaryAvailability === 'available' && !!attendance;
    const attendanceStatus = attendanceSummaryAvailability === 'not-authorized'
      ? 'NotAuthorized'
      : attendanceSummaryAvailability === 'unavailable' || !attendance
        ? 'Unavailable'
        : this.toAttendanceStatus(this.pickFirst(attendance, ['attendanceStatus']));

    return {
      id,
      type: 'sub-stage',
      name: this.resolveString(subStageRecord, ['name', 'stageName', 'title'], 'مرحلة فرعية'),
      status: this.statusFor(staffingStatus, readinessPercent),
      readinessPercent,
      workersCurrent,
      workersRequired,
      workerRequirementDefined,
      staffingSummaryAvailable,
      attendanceSummaryAvailable,
      presentAssignedWorkers: attendanceSummaryAvailable
        ? this.toNumber(this.pickFirst(attendance!, ['presentAssignedWorkersCount']), 0)
        : 0,
      absentAssignedWorkers: attendanceSummaryAvailable
        ? this.toNumber(this.pickFirst(attendance!, ['absentAssignedWorkersCount']), 0)
        : 0,
      attendanceStatus,
      assignmentParticipationsCount: workersCurrent,
      workers: [],
      position: this.parsePosition(subStageRecord)
    };
  }

  private summarizeNodes(nodes: LayoutNode[], authoritative: HierarchySummaryOverride = {}): {
    workersCurrent: number;
    assignmentParticipationsCount: number;
    workersRequired: number;
    workerRequirementDefined: boolean;
    staffingSummaryAvailable: boolean;
    readinessPercent: number;
    staffingStatus: StaffingStatus;
    attendanceSummaryAvailable: boolean;
    presentAssignedWorkers: number;
    absentAssignedWorkers: number;
    attendanceStatus: SubStageAttendanceStatus;
  } {
    const assignmentParticipationsCount = nodes.reduce(
      (sum, node) => sum + (node.assignmentParticipationsCount ?? node.workersCurrent ?? 0),
      0
    );
    const workersCurrent = authoritative.workersCurrent ?? assignmentParticipationsCount;
    const workersRequired = nodes.reduce((sum, node) => sum + (node.workersRequired ?? 0), 0);
    const staffingSummaryAvailable = nodes.length > 0 && nodes.every((node) => node.staffingSummaryAvailable === true);
    const workerRequirementDefined = staffingSummaryAvailable && nodes.every((node) => node.workerRequirementDefined === true);
    const readinessPercent = workerRequirementDefined ? this.toPercent(undefined, assignmentParticipationsCount, workersRequired) : 0;
    const staffingStatus: StaffingStatus = !workerRequirementDefined
      ? 'RequirementNotDefined'
      : workersCurrent === 0
        ? 'Unstaffed'
        : assignmentParticipationsCount < workersRequired
          ? 'Understaffed'
          : 'Staffed';

    const presentAssignedWorkers = authoritative.presentAssignedWorkers
      ?? nodes.reduce((sum, node) => sum + (node.presentAssignedWorkers ?? 0), 0);
    const absentAssignedWorkers = authoritative.absentAssignedWorkers
      ?? nodes.reduce((sum, node) => sum + (node.absentAssignedWorkers ?? 0), 0);
    const attendanceSummaryAvailable = nodes.length > 0 && nodes.every(node => node.attendanceSummaryAvailable === true);
    const statuses = nodes.map(node => node.attendanceStatus);
    const attendanceStatus: SubStageAttendanceStatus = workersCurrent === 0
      ? 'NoAssignments'
      : statuses.some(status => status === 'NotAuthorized')
        ? 'NotAuthorized'
        : !attendanceSummaryAvailable || statuses.some(status => status === 'NeedsSync' || status === 'Unavailable')
          ? 'NeedsSync'
          : presentAssignedWorkers === workersCurrent
            ? 'FullyPresent'
            : presentAssignedWorkers === 0
              ? 'AllAbsent'
              : 'PartiallyPresent';
    return { workersCurrent, assignmentParticipationsCount, workersRequired, workerRequirementDefined, staffingSummaryAvailable, readinessPercent, staffingStatus, attendanceSummaryAvailable, presentAssignedWorkers, absentAssignedWorkers, attendanceStatus };
  }

  private hierarchySummaryOverride(
    subStageIds: string[],
    staffingCoverageBySubStageId: Map<string, RawRecord>,
    attendanceSummaryBySubStageId: Map<string, RawRecord>,
    scope: 'mainStage' | 'productionLine' | 'factory'
  ): HierarchySummaryOverride {
    const staffingKey = `${scope}DistinctWorkersCount`;
    const attendancePrefix = `${scope}Distinct`;
    return {
      workersCurrent: this.firstAggregateNumber(subStageIds, staffingCoverageBySubStageId, staffingKey),
      presentAssignedWorkers: this.firstAggregateNumber(subStageIds, attendanceSummaryBySubStageId, `${attendancePrefix}PresentWorkersCount`),
      absentAssignedWorkers: this.firstAggregateNumber(subStageIds, attendanceSummaryBySubStageId, `${attendancePrefix}AbsentWorkersCount`)
    };
  }

  private firstAggregateNumber(ids: string[], records: Map<string, RawRecord>, key: string): number | undefined {
    for (const id of ids) {
      const record = records.get(id);
      if (!record) continue;
      const value = this.pickFirst(record, [key]);
      if (typeof value === 'number' && Number.isFinite(value)) return value;
    }
    return undefined;
  }

  private attendanceSummaryText(summary: { workersCurrent: number; presentAssignedWorkers: number; attendanceStatus: SubStageAttendanceStatus }): string {
    if (summary.attendanceStatus === 'NotAuthorized') return 'غير متاح بالصلاحية';
    if (summary.attendanceStatus === 'NeedsSync' || summary.attendanceStatus === 'Unavailable') return 'تحتاج مزامنة حضور اليوم';
    if (summary.attendanceStatus === 'NoAssignments') return 'لا يوجد عمال مسكنون';
    return `${summary.presentAssignedWorkers} من ${summary.workersCurrent}`;
  }

  private hasUsableBackendData(layout: FactoryLayout, hasBackendData: boolean): boolean {
    if (!hasBackendData || layout.lines.length === 0) {
      return false;
    }

    const subStages = layout.lines.flatMap((line) => line.stages.flatMap((stage) => stage.subStages));
    return subStages.length > 0 && subStages.every((stage) => stage.staffingSummaryAvailable === true);
  }

  private createFallbackData(fallbackReason: FactoryMapFallbackReason, hasBackendData = false): FactoryMapApiData {
    return { layout: createEmptyFactoryLayout(), hasBackendData, hasUsableBackendData: false, fallbackReason };
  }

  private getEntityList(path: string): Observable<RawRecord[]> {
    return this.http.get<ApiResponse<unknown>>(buildApiUrl(path)).pipe(
      map((response) => this.parseEntityList(this.extractPayload(response))),
      map((items) => items.map((item) => this.normalizeObject(item)))
    );
  }

  private parseEntityList(raw: unknown): unknown[] {
    if (Array.isArray(raw)) {
      return raw;
    }

    const source = this.normalizeObject(raw);
    const candidate = this.pickFirst(source, ['items', 'data', 'results']);
    return Array.isArray(candidate) ? candidate : [];
  }

  private groupByParentId(records: RawRecord[], parentKeys: string[]): Map<string, RawRecord[]> {
    const grouped = new Map<string, RawRecord[]>();
    records.forEach((record) => {
      const parentId = this.resolveString(record, parentKeys);
      const collection = grouped.get(parentId) ?? [];
      collection.push(record);
      grouped.set(parentId, collection);
    });
    return grouped;
  }

  private indexByKey(records: RawRecord[], keys: string[]): Map<string, RawRecord> {
    const index = new Map<string, RawRecord>();
    records.forEach((record) => {
      const key = this.resolveString(record, keys);
      if (key) {
        index.set(key, record);
      }
    });
    return index;
  }

  private parsePosition(record: RawRecord): LayoutPosition {
    return {
      row: this.toNumber(this.pickFirst(record, ['row', 'y'])),
      column: this.toNumber(this.pickFirst(record, ['column', 'x'])),
      x: this.toNumber(this.pickFirst(record, ['positionX', 'layoutX'])),
      y: this.toNumber(this.pickFirst(record, ['positionY', 'layoutY'])),
      width: this.toNumber(this.pickFirst(record, ['width', 'layoutWidth'])),
      height: this.toNumber(this.pickFirst(record, ['height', 'layoutHeight']))
    };
  }

  private statusFor(status: StaffingStatus, percent: number): FactoryStatus {
    if (status === 'RequirementNotDefined') return 'info';
    if (status === 'Unstaffed') return 'critical';
    if (status === 'Understaffed') return 'warning';
    return percent >= 100 ? 'ready' : 'warning';
  }

  private statusText(status: StaffingStatus): string {
    if (status === 'RequirementNotDefined') return 'الاحتياج غير محدد';
    if (status === 'Unstaffed') return 'غير مسكن';
    if (status === 'Understaffed') return 'تغطية ناقصة';
    return 'مغطى';
  }

  private toStaffingStatus(value: unknown, workersCurrent: number, workersRequired: number, requirementDefined: boolean): StaffingStatus {
    if (value === 'RequirementNotDefined' || value === 'Unstaffed' || value === 'Understaffed' || value === 'Staffed') {
      return value;
    }
    if (!requirementDefined) return 'RequirementNotDefined';
    if (workersCurrent === 0) return 'Unstaffed';
    return workersCurrent < workersRequired ? 'Understaffed' : 'Staffed';
  }

  private toAttendanceStatus(value: unknown): SubStageAttendanceStatus {
    if (value === 'FullyPresent' || value === 'PartiallyPresent' || value === 'AllAbsent' || value === 'NeedsSync' || value === 'NoAssignments') {
      return value;
    }

    return 'Unavailable';
  }

  private toPercent(rawPercent: unknown, workersCurrent: number, workersRequired: number): number {
    const percent = this.toNumber(rawPercent, -1);
    if (percent >= 0) return this.clampPercent(percent);
    return workersRequired > 0 ? this.clampPercent(Math.round((workersCurrent / workersRequired) * 100)) : 0;
  }

  private resolveString(record: RawRecord, keys: string[], fallback = ''): string {
    const value = this.pickFirst(record, keys);
    return typeof value === 'string' && value.trim().length > 0 ? value.trim() : fallback;
  }

  private toNumber(value: unknown, fallback = 0): number {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) return parsed;
    }
    return fallback;
  }

  private toBoolean(value: unknown, fallback: boolean): boolean {
    return typeof value === 'boolean' ? value : fallback;
  }

  private pickFirst(record: RawRecord, keys: string[]): unknown {
    for (const key of keys) {
      if (Object.prototype.hasOwnProperty.call(record, key) && record[key] !== undefined && record[key] !== null) {
        return record[key];
      }
    }
    return undefined;
  }

  private extractPayload<T>(response: ApiResponse<T>): T {
    if (response && typeof response === 'object' && 'success' in response) {
      if (response.success === false || !response.data) {
        throw new Error(response.error?.message || 'API response data is missing.');
      }
      return response.data;
    }
    return response as T;
  }

  private normalizeObject(value: unknown): RawRecord {
    return value && typeof value === 'object' && !Array.isArray(value) ? value as RawRecord : {};
  }

  private clampPercent(value: number): number {
    return Math.max(0, Math.min(100, value));
  }
}
