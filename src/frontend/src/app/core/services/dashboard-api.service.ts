import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { DashboardCard, AttendanceIndicator, FactoryReadinessSummary, FactoryMapLine } from './mock-data.service';
import {
  deriveStatusFromReadiness,
  FactoryStatus
} from '../../shared/models/factory-status.model';
import { forkJoin, map, Observable, timeout } from 'rxjs';

interface DashboardAttendanceSummary {
  presentWorkers: number;
  lateWorkers: number;
  absentWorkers: number;
  totalWorkers: number;
  attendanceRate: number;
}

interface DashboardBackendFactoryReadiness {
  overallReadiness: number;
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

  loadDashboardData(): Observable<DashboardApiData> {
    return forkJoin({
      factoryReadiness: this.getFactoryReadiness(),
      productionLines: this.getProductionLines(),
      attendanceSummary: this.getAttendanceToday(),
      unreadNotifications: this.getUnreadNotifications()
    }).pipe(
      timeout(STANDARD_API_TIMEOUT_MS),
      map(({ factoryReadiness, productionLines, attendanceSummary, unreadNotifications }) => {
        const lineSummary = this.getFactoryReadinessFromLines(productionLines);
        const lineReadinessSummary = this.mergeFactoryReadiness(factoryReadiness, lineSummary, attendanceSummary);
        const criticalReadinessAlerts = this.extractCriticalReadinessAlerts(productionLines);
        const cards = this.buildDashboardCards(lineReadinessSummary, attendanceSummary, unreadNotifications);
        const attendanceIndicators = this.buildAttendanceIndicators(attendanceSummary);

        return {
          cards,
          lineReadinessSummary,
          attendanceIndicators,
          previewLines: productionLines,
          criticalReadinessAlerts
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
    const activeWorkersFromAttendance = attendance.presentWorkers + attendance.lateWorkers;
    const activeWorkers = attendance.totalWorkers > 0 ? activeWorkersFromAttendance : lineSummary.activeWorkers;
    const totalWorkers = attendance.totalWorkers > 0 ? attendance.totalWorkers : lineSummary.totalWorkers;
    const attendanceRate = attendance.attendanceRate > 0 ? attendance.attendanceRate : lineSummary.attendanceRate;

    if (Object.values(factoryReadiness).some((value) => value !== 0)) {
      return {
        overallReadiness: factoryReadiness.overallReadiness,
        totalLines: factoryReadiness.totalLines || lineSummary.totalLines,
        healthyLines: factoryReadiness.healthyLines || lineSummary.healthyLines,
        warningLines: factoryReadiness.warningLines || lineSummary.warningLines,
        criticalLines: factoryReadiness.criticalLines || lineSummary.criticalLines,
        activeWorkers,
        totalWorkers,
        attendanceRate: attendanceRate || factoryReadiness.attendanceRate || 0
      };
    }

    return {
      overallReadiness: lineSummary.overallReadiness,
      totalLines: lineSummary.totalLines,
      healthyLines: lineSummary.healthyLines,
      warningLines: lineSummary.warningLines,
      criticalLines: lineSummary.criticalLines,
      activeWorkers,
      totalWorkers,
      attendanceRate: attendanceRate || 0
    };
  }

  private buildDashboardCards(
    summary: FactoryReadinessSummary,
    attendance: DashboardAttendanceSummary,
    unreadNotifications: number
  ): DashboardCard[] {
    const presentWorkers = attendance.presentWorkers + attendance.lateWorkers;
    const attendanceTrend = summary.attendanceRate >= 80 ? 'up' : summary.attendanceRate >= 70 ? 'stable' : 'down';
    const readinessTrend = summary.overallReadiness >= 80 ? 'up' : summary.overallReadiness >= 65 ? 'stable' : 'down';

    return [
      {
        title: 'جاهزية المصنع',
        value: `${summary.overallReadiness}%`,
        trend: readinessTrend,
        trendLabel: 'مؤشر جاهزية عام الآن'
      },
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
      },
      {
        title: 'الإشعارات غير المقروءة',
        value: String(unreadNotifications),
        trend: unreadNotifications > 0 ? 'up' : 'stable',
        trendLabel: unreadNotifications > 0 ? 'تتطلب متابعة' : 'لا يوجد تنبيهات جديدة'
      }
    ];
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
    const activeWorkers = allStages.reduce((sum, stage) => sum + (stage.workersCurrent || 0), 0);
    const totalWorkers = allStages.reduce((sum, stage) => sum + (stage.workersRequired || 0), 0);
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
      overallReadiness: this.toNumber(this.pickFirst(raw, ['overallReadiness', 'readiness', 'overallReadyPercent'])),
      totalLines: this.toNumber(this.pickFirst(raw, ['totalLines', 'linesCount'])),
      healthyLines: this.toNumber(this.pickFirst(raw, ['healthyLines', 'healthyLineCount'])),
      warningLines: this.toNumber(this.pickFirst(raw, ['warningLines', 'warningLineCount'])),
      criticalLines: this.toNumber(this.pickFirst(raw, ['criticalLines', 'criticalLineCount'])),
      activeWorkers: this.toNumber(this.pickFirst(raw, ['activeWorkers', 'presentWorkers', 'presentCount', 'attendedWorkers'])),
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
        this.pickFirst(record, ['workersCurrent', 'currentWorkers', 'activeWorkers'])
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
        stages
      };
    });
  }

  private parseAttendanceSummary(raw: Record<string, unknown>): DashboardAttendanceSummary {
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
