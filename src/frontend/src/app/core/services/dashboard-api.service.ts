import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import {
  AttendanceIndicator,
  DashboardCard,
  FactoryMapLine,
  FactoryReadinessSummary
} from '../../shared/models/dashboard.model';
import {
  deriveStatusFromReadiness,
  FactoryStatus
} from '../../shared/models/factory-status.model';
import { catchError, forkJoin, map, Observable, of, timeout } from 'rxjs';

interface DashboardAttendanceSummary {
  presentWorkers: number;
  lateWorkers: number;
  absentWorkers: number;
  totalWorkers: number;
  attendanceRate: number;
}

interface DashboardAttendanceWorker {
  attendanceStatus?: string;
}

interface DashboardBackendFactoryReadiness {
  overallReadiness: number;
  assignmentCoveragePercent: number;
  attendanceDataStatus: string;
  totalLines: number;
  healthyLines: number;
  warningLines: number;
  criticalLines: number;
  activeWorkers: number;
  totalWorkers: number;
  attendanceRate: number;
}

export interface StageReadinessAlert {
  lineName: string;
  stageName: string;
  workersCurrent: number;
  workersRequired: number;
  shortageWorkers: number;
}

export interface DashboardApiData {
  cards: DashboardCard[];
  lineReadinessSummary: FactoryReadinessSummary;
  attendanceIndicators: AttendanceIndicator[];
  previewLines: FactoryMapLine[];
  criticalReadinessAlerts: StageReadinessAlert[];
  assignmentCoveragePercent: number;
  attendanceDataStatus: string;
  readinessState: DashboardDataSourceState;
  attendanceState: DashboardDataSourceState;
  notificationsState: DashboardDataSourceState;
  hasLoadError: boolean;
}

export type DashboardDataSourceState = 'available' | 'not-authorized' | 'error';

export interface DashboardLoadOptions {
  includeAttendance?: boolean;
}

interface DashboardDataSource<T> {
  value: T;
  state: DashboardDataSourceState;
}

const readinessStatusLabels: Record<FactoryStatus, string> = {
  ready: 'ممتاز',
  warning: 'متوسط',
  critical: 'ضعيف',
  present: 'متاح',
  late: 'متأخر',
  absent: 'مفقود',
  unassigned: 'غير معطل',
  info: 'غير متاح'
};

@Injectable({
  providedIn: 'root'
})
export class DashboardApiService {
  constructor(private readonly http: HttpClient) {}

  loadDashboardData(options: DashboardLoadOptions = {}): Observable<DashboardApiData> {
    const includeAttendance = options.includeAttendance ?? true;
    const emptyAttendance = this.getEmptyAttendanceSummary();

    return forkJoin({
      factoryReadiness: this.loadSource(this.getFactoryReadiness(), this.getEmptyFactoryReadiness()),
      productionLines: this.loadSource(this.getProductionLines(), []),
      attendanceSummary: includeAttendance
        ? this.loadSource(this.getAttendanceToday(), emptyAttendance)
        : of<DashboardDataSource<DashboardAttendanceSummary>>({ value: emptyAttendance, state: 'not-authorized' }),
      unreadNotifications: this.loadSource(this.getUnreadNotifications(), 0)
    }).pipe(
      map(({ factoryReadiness, productionLines, attendanceSummary, unreadNotifications }) => {
        const lineSummary = this.getFactoryReadinessFromLines(productionLines.value);
        const lineReadinessSummary = this.mergeFactoryReadiness(factoryReadiness.value, lineSummary, attendanceSummary.value);
        const criticalReadinessAlerts = this.extractCriticalReadinessAlerts(productionLines.value);
        const readinessState = this.resolveReadinessState(factoryReadiness.state, productionLines.state);
        const cards = this.buildDashboardCards(
          lineReadinessSummary,
          attendanceSummary.value,
          unreadNotifications.value,
          readinessState,
          attendanceSummary.state,
          unreadNotifications.state,
          factoryReadiness.value.attendanceDataStatus
        );
        const attendanceIndicators = attendanceSummary.state === 'available'
          ? this.buildAttendanceIndicators(attendanceSummary.value)
          : [];

        return {
          cards,
          lineReadinessSummary,
          attendanceIndicators,
          previewLines: productionLines.value,
          criticalReadinessAlerts,
          assignmentCoveragePercent: factoryReadiness.value.assignmentCoveragePercent,
          attendanceDataStatus: factoryReadiness.value.attendanceDataStatus,
          readinessState,
          attendanceState: attendanceSummary.state,
          notificationsState: unreadNotifications.state,
          hasLoadError: [factoryReadiness.state, productionLines.state, attendanceSummary.state, unreadNotifications.state]
            .some((state) => state === 'error')
        };
      })
    );
  }

  extractCriticalReadinessAlerts(lines: FactoryMapLine[]): StageReadinessAlert[] {
    const alerts = lines.flatMap((line) =>
      line.stages
        .map((stage) => {
          const shortageWorkers = Math.max(stage.workersRequired - stage.workersCurrent, 0);
          return {
            lineName: line.name,
            stageName: stage.name,
            workersCurrent: stage.workersCurrent,
            workersRequired: stage.workersRequired,
            shortageWorkers
          };
        })
        .filter((stage) => stage.shortageWorkers > 0)
    );

    alerts.sort((a, b) => b.shortageWorkers - a.shortageWorkers);
    return alerts.slice(0, 3);
  }

  private getFactoryReadiness(): Observable<DashboardBackendFactoryReadiness> {
    return this.http
      .get<ApiResponse<Record<string, unknown>>>(buildApiUrl('/api/readiness/factory'))
      .pipe(map(response => this.parseFactoryReadiness(this.extractPayload(response))));
  }

  private getProductionLines(): Observable<FactoryMapLine[]> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/readiness/production-lines'))
      .pipe(map(response => this.parseProductionLines(this.extractPayload(response))));
  }

  private getAttendanceToday(): Observable<DashboardAttendanceSummary> {
    return this.http
      .get<ApiResponse<Record<string, unknown>>>(buildApiUrl('/api/attendance/today'))
      .pipe(map(response => this.parseAttendanceSummary(this.extractPayload(response))));
  }

  private getUnreadNotifications(): Observable<number> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/notifications/unread-count'))
      .pipe(map(response => this.parseUnreadCount(this.extractPayload(response))));
  }

  private mergeFactoryReadiness(
    factoryReadiness: DashboardBackendFactoryReadiness,
    lineSummary: FactoryReadinessSummary,
    attendance: DashboardAttendanceSummary
  ): FactoryReadinessSummary {
    const attendanceRate = attendance.attendanceRate > 0 ? attendance.attendanceRate : lineSummary.attendanceRate;
    const hasFactoryReadiness = factoryReadiness.totalLines > 0
      || factoryReadiness.totalWorkers > 0
      || factoryReadiness.overallReadiness > 0
      || factoryReadiness.attendanceDataStatus !== 'Unknown';

    if (hasFactoryReadiness) {
      return {
        overallReadiness: factoryReadiness.overallReadiness,
        totalLines: factoryReadiness.totalLines || lineSummary.totalLines,
        healthyLines: factoryReadiness.healthyLines || lineSummary.healthyLines,
        warningLines: factoryReadiness.warningLines || lineSummary.warningLines,
        criticalLines: factoryReadiness.criticalLines || lineSummary.criticalLines,
        activeWorkers: factoryReadiness.activeWorkers || lineSummary.activeWorkers,
        totalWorkers: factoryReadiness.totalWorkers || lineSummary.totalWorkers,
        attendanceRate: attendanceRate || factoryReadiness.attendanceRate || 0
      };
    }

    return {
      overallReadiness: lineSummary.overallReadiness,
      totalLines: lineSummary.totalLines,
      healthyLines: lineSummary.healthyLines,
      warningLines: lineSummary.warningLines,
      criticalLines: lineSummary.criticalLines,
      activeWorkers: lineSummary.activeWorkers,
      totalWorkers: lineSummary.totalWorkers,
      attendanceRate: attendanceRate || 0
    };
  }

  private buildDashboardCards(
    summary: FactoryReadinessSummary,
    attendance: DashboardAttendanceSummary,
    unreadNotifications: number,
    readinessState: DashboardDataSourceState,
    attendanceState: DashboardDataSourceState,
    notificationsState: DashboardDataSourceState,
    attendanceDataStatus: string
  ): DashboardCard[] {
    const presentWorkers = attendance.presentWorkers + attendance.lateWorkers;
    const attendanceTrend = summary.attendanceRate >= 80 ? 'up' : summary.attendanceRate >= 70 ? 'stable' : 'down';
    const readinessTrend = summary.overallReadiness >= 80 ? 'up' : summary.overallReadiness >= 65 ? 'stable' : 'down';

    const cards: DashboardCard[] = [];

    if (readinessState === 'available' && attendanceState === 'available' && attendanceDataStatus === 'Complete') {
      cards.push({
        title: 'جاهزية المصنع',
        value: `${summary.overallReadiness}%`,
        trend: readinessTrend,
        trendLabel: 'مؤشر جاهزية عام الآن'
      });
    }

    if (attendanceState === 'available') {
      cards.push(
        {
          title: 'العاملون الحاضرون',
          value: String(presentWorkers),
          trend: presentWorkers > 0 ? 'up' : 'down',
          trendLabel: `إجمالي ${summary.totalWorkers} عامل`
        },
        {
          title: 'العاملون المتأخرون',
          value: String(attendance.lateWorkers),
          trend: attendance.lateWorkers > 0 ? 'down' : 'stable',
          trendLabel: attendance.lateWorkers > 0 ? 'تحديث مطلوب' : 'ضمن النطاق'
        }
      );
    }

    if (notificationsState === 'available') {
      cards.push({
        title: 'الإشعارات غير المقروءة',
        value: String(unreadNotifications),
        trend: unreadNotifications > 0 ? 'up' : 'stable',
        trendLabel: unreadNotifications > 0 ? 'تتطلب متابعة' : 'لا يوجد تنبيهات جديدة'
      });
    }

    return cards;
  }

  private loadSource<T>(source: Observable<T>, fallback: T): Observable<DashboardDataSource<T>> {
    return source.pipe(
      timeout(STANDARD_API_TIMEOUT_MS),
      map((value) => ({ value, state: 'available' as const })),
      catchError((error: { status?: number }) => of({
        value: fallback,
        state: error?.status === 403 ? 'not-authorized' as const : 'error' as const
      }))
    );
  }

  private resolveReadinessState(
    factoryReadinessState: DashboardDataSourceState,
    productionLinesState: DashboardDataSourceState
  ): DashboardDataSourceState {
    if (factoryReadinessState === 'available' || productionLinesState === 'available') {
      return 'available';
    }

    if (factoryReadinessState === 'not-authorized' || productionLinesState === 'not-authorized') {
      return 'not-authorized';
    }

    return 'error';
  }

  private getEmptyFactoryReadiness(): DashboardBackendFactoryReadiness {
    return {
      overallReadiness: 0,
      assignmentCoveragePercent: 0,
      attendanceDataStatus: 'Unknown',
      totalLines: 0,
      healthyLines: 0,
      warningLines: 0,
      criticalLines: 0,
      activeWorkers: 0,
      totalWorkers: 0,
      attendanceRate: 0
    };
  }

  private getEmptyAttendanceSummary(): DashboardAttendanceSummary {
    return {
      presentWorkers: 0,
      lateWorkers: 0,
      absentWorkers: 0,
      totalWorkers: 0,
      attendanceRate: 0
    };
  }

  private buildAttendanceIndicators(summary: DashboardAttendanceSummary): AttendanceIndicator[] {
    return [
      { label: 'حاضر', value: summary.presentWorkers + summary.lateWorkers, icon: 'pi pi-check', tone: 'green' },
      { label: 'متأخر', value: summary.lateWorkers, icon: 'pi pi-clock', tone: 'yellow' },
      { label: 'غائب', value: summary.absentWorkers, icon: 'pi pi-times', tone: 'red' }
    ];
  }

  private getFactoryReadinessFromLines(lines: FactoryMapLine[]): FactoryReadinessSummary {
    if (lines.length === 0) {
      return {
        overallReadiness: 0,
        totalLines: 0,
        healthyLines: 0,
        warningLines: 0,
        criticalLines: 0,
        activeWorkers: 0,
        totalWorkers: 0,
        attendanceRate: 0
      };
    }

    const allStages = lines.flatMap((line) => line.stages);
    const activeWorkers = lines.reduce((sum, line) => sum + line.workersCurrent, 0);
    const totalWorkers = lines.reduce((sum, line) => sum + line.workersRequired, 0);
    const readinessPoints = lines.map((line) => line.statusPercent || 0);

    return {
      overallReadiness: Math.round(readinessPoints.reduce((sum, value) => sum + value, 0) / lines.length),
      totalLines: lines.length,
      healthyLines: lines.filter((line) => line.statusPercent >= 85).length,
      warningLines: lines.filter((line) => line.statusPercent >= 60 && line.statusPercent < 85).length,
      criticalLines: lines.filter((line) => line.statusPercent < 60).length,
      activeWorkers,
      totalWorkers,
      attendanceRate: totalWorkers > 0 ? Math.round((activeWorkers / totalWorkers) * 100) : 0
    };
  }

  private parseFactoryReadiness(raw: Record<string, unknown>): DashboardBackendFactoryReadiness {
    return {
      overallReadiness: this.toNumber(this.pickFirst(raw, ['readinessPercent', 'overallReadiness', 'readiness', 'overallReadyPercent'])),
      assignmentCoveragePercent: this.toNumber(this.pickFirst(raw, ['assignmentCoveragePercent'])),
      attendanceDataStatus: String(this.pickFirst(raw, ['attendanceDataStatus']) ?? 'Unknown'),
      totalLines: this.toNumber(this.pickFirst(raw, ['totalLines', 'linesCount'])),
      healthyLines: this.toNumber(this.pickFirst(raw, ['healthyLines', 'healthyLineCount'])),
      warningLines: this.toNumber(this.pickFirst(raw, ['warningLines', 'warningLineCount'])),
      criticalLines: this.toNumber(this.pickFirst(raw, ['criticalLines', 'criticalLineCount'])),
      activeWorkers: this.toNumber(this.pickFirst(raw, ['presentWorkers', 'activeWorkers', 'presentCount', 'attendedWorkers'])),
      totalWorkers: this.toNumber(this.pickFirst(raw, ['totalWorkers', 'requiredWorkers', 'expectedWorkers', 'allWorkers'])),
      attendanceRate: this.toNumber(this.pickFirst(raw, ['attendanceRate', 'attendancePercent', 'attendance_rate']))
    };
  }

  private parseProductionLines(raw: unknown): FactoryMapLine[] {
    const source = this.normalizeObject(raw);
    const nestedLines = this.toArray(this.pickFirst(source, ['lines', 'items', 'dataLines', 'productionLines', 'production_lines']));
    const rawLines = nestedLines.length > 0 ? nestedLines : this.toArray(raw);

    return rawLines.map((line, index) => {
      const record = this.normalizeObject(line);

      const stages = this.toArray(
        this.pickFirst(record, ['stages', 'subStages', 'nodes', 'items'])
      ).map((rawStage, stageIndex) => {
        const stage = this.normalizeObject(rawStage);
        const workersCurrent = this.toNumber(this.pickFirst(stage, ['workersCurrent', 'currentWorkers', 'workersPresent']));
        const workersRequired = this.toNumber(this.pickFirst(stage, ['workersRequired', 'requiredWorkers', 'workersNeeded']));

        return {
          name: this.pickString(stage, ['name', 'stageName', 'title']) || `مرحلة ${stageIndex + 1}`,
          workersCurrent,
          workersRequired
        };
      });

      const stageWorkersCurrent = stages.reduce((sum, stage) => sum + stage.workersCurrent, 0);
      const stageWorkersRequired = stages.reduce((sum, stage) => sum + stage.workersRequired, 0);
      const workersCurrentFromLine = this.toNumber(
        this.pickFirst(record, ['workersCurrent', 'currentWorkers', 'activeWorkers', 'presentWorkers'])
      );
      const workersRequiredFromLine = this.toNumber(
        this.pickFirst(record, ['workersRequired', 'requiredWorkers', 'expectedWorkers'])
      );

      const workersCurrent = workersCurrentFromLine || stageWorkersCurrent;
      const workersRequired = workersRequiredFromLine || stageWorkersRequired;
      const statusPercent = this.toPercent(
        this.pickFirst(record, ['statusPercent', 'readiness', 'readinessPercent']),
        workersCurrent,
        workersRequired
      );
      const name = this.pickString(record, ['name', 'lineName', 'title']) || `الخط ${index + 1}`;

      return {
        name,
        statusPercent,
        readinessLabel: this.toReadinessLabel(statusPercent),
        workersCurrent,
        workersRequired,
        stages
      };
    });
  }

  private parseAttendanceSummary(raw: Record<string, unknown>): DashboardAttendanceSummary {
    const items = this.toArray(this.pickFirst(raw, ['items']))
      .map((item) => this.normalizeObject(item) as DashboardAttendanceWorker);

    if (items.length > 0) {
      const count = (status: string) => items.filter((item) => item.attendanceStatus === status).length;
      const presentWorkers = count('Present');
      const lateWorkers = count('Late');
      const absentWorkers = count('Absent');
      const totalWorkers = items.length;

      return {
        presentWorkers,
        lateWorkers,
        absentWorkers,
        totalWorkers,
        attendanceRate: Math.round(((presentWorkers + lateWorkers) / totalWorkers) * 100)
      };
    }

    const presentWorkers = this.toNumber(this.pickFirst(raw, ['presentWorkers', 'present', 'onDuty']));
    const lateWorkers = this.toNumber(this.pickFirst(raw, ['lateWorkers', 'late', 'lateCount']));
    const absentWorkers = this.toNumber(this.pickFirst(raw, ['absentWorkers', 'absent', 'absentCount']));
    const totalWorkers = this.toNumber(this.pickFirst(raw, ['totalWorkers', 'expectedWorkers', 'allWorkers']));
    const attendanceRate = this.toNumber(this.pickFirst(raw, ['attendanceRate', 'attendancePercent']));

    const safeTotalWorkers = totalWorkers || presentWorkers + lateWorkers + absentWorkers;
    const safeAttendanceRate = attendanceRate || (safeTotalWorkers > 0 ? Math.round(((presentWorkers + lateWorkers) / safeTotalWorkers) * 100) : 0);

    return {
      presentWorkers,
      lateWorkers,
      absentWorkers,
      totalWorkers: safeTotalWorkers,
      attendanceRate: safeAttendanceRate
    };
  }

  private parseUnreadCount(raw: unknown): number {
    const source = this.normalizeObject(raw);
    return this.toNumber(this.pickFirst(source, ['unreadCount', 'count', 'total', 'notificationsUnread']));
  }

  private toReadinessLabel(value: number): string {
    const status = deriveStatusFromReadiness(value);
    return readinessStatusLabels[status] || 'غير متاح';
  }

  private toPercent(value: unknown, currentWorkers: number, requiredWorkers: number): number {
    const resolved = this.toNumber(value);
    if (resolved > 0) {
      return this.clampPercent(Math.round(resolved));
    }
    return this.toPercentFromWorkers(currentWorkers, requiredWorkers);
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

  private clampPercent(value: number): number {
    if (value < 0) {
      return 0;
    }
    if (value > 100) {
      return 100;
    }
    return value;
  }

  private toPercentFromWorkers(currentWorkers: number, requiredWorkers: number): number {
    return requiredWorkers > 0 ? this.clampPercent(Math.round((currentWorkers / requiredWorkers) * 100)) : 0;
  }

  private toNumber(value: unknown): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }
    return 0;
  }

  private pickFirst(record: Record<string, unknown>, keys: string[]): unknown {
    for (const key of keys) {
      if (Object.prototype.hasOwnProperty.call(record, key) && record[key] !== undefined && record[key] !== null) {
        return record[key] as unknown;
      }
    }
    return undefined;
  }

  private pickString(record: Record<string, unknown>, keys: string[]): string {
    const value = this.pickFirst(record, keys);
    return typeof value === 'string' && value.trim().length > 0 ? value : '';
  }

  private toArray(value: unknown): unknown[] {
    return Array.isArray(value) ? value : [];
  }

  private normalizeObject(value: unknown): Record<string, unknown> {
    return value && typeof value === 'object' && !Array.isArray(value)
      ? value as Record<string, unknown>
      : {};
  }
}
