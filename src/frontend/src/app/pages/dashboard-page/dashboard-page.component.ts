import { Component, OnDestroy, OnInit } from '@angular/core';
import { catchError, finalize, map, of, Subject, switchMap, takeUntil } from 'rxjs';
import { DashboardApiData, DashboardApiService, StageReadinessAlert } from '../../core/services/dashboard-api.service';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { AttendanceApiService, AttendanceSyncResult } from '../../core/services/attendance-api.service';
import {
  AttendanceIndicator,
  DashboardCard,
  FactoryMapLine,
  FactoryReadinessSummary,
  KpiTrend
} from '../../shared/models/dashboard.model';
import { FactoryStatus } from '../../shared/models/factory-status.model';

@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-page.component.html',
  styleUrls: ['./dashboard-page.component.scss']
})
export class DashboardPageComponent implements OnInit, OnDestroy {
  isLoading = true;
  hasLoadError = false;
  cards: DashboardCard[] = [];
  lineReadinessSummary: FactoryReadinessSummary = {
    overallReadiness: 0,
    totalLines: 0,
    healthyLines: 0,
    warningLines: 0,
    criticalLines: 0,
    activeWorkers: 0,
    totalWorkers: 0,
    attendanceRate: 0
  };
  attendanceIndicators: AttendanceIndicator[] = [];
  previewLines: FactoryMapLine[] = [];
  criticalReadinessAlerts: StageReadinessAlert[] = [];
  readinessState: DashboardApiData['readinessState'] = 'error';
  attendanceState: DashboardApiData['attendanceState'] = 'not-authorized';
  assignmentCoveragePercent = 0;
  attendanceDataStatus = 'Unknown';
  attendanceSyncing = false;
  attendanceSyncMessage = '';
  attendanceSyncFailed = false;

  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly dashboardApiService: DashboardApiService,
    private readonly permissionService: PermissionService,
    private readonly attendanceApiService: AttendanceApiService
  ) {}

  ngOnInit(): void {
    this.isLoading = true;
    this.permissionService
      .ensureHydrated()
      .pipe(
        catchError(() => of([])),
        switchMap(() => this.dashboardApiService.loadDashboardData({
          includeAttendance: this.permissionService.hasPermission(PERMISSIONS.attendance.view)
        })),
        catchError(() => {
          this.hasLoadError = true;
          return of(this.getEmptyDashboardData());
        }),
        takeUntil(this.destroy$),
        finalize(() => {
          this.isLoading = false;
        })
      )
      .subscribe((data) => this.setDashboardData(data));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  retry(): void {
    this.loadDashboardData();
  }

  synchronizeAttendanceToday(): void {
    if (!this.canSynchronizeAttendance || this.attendanceSyncing) {
      return;
    }

    this.attendanceSyncing = true;
    this.attendanceSyncMessage = '';
    this.attendanceSyncFailed = false;

    this.attendanceApiService
      .syncToday()
      .pipe(
        switchMap((result) => this.dashboardApiService
          .loadDashboardData({ includeAttendance: this.canViewAttendance })
          .pipe(
            map((data) => ({ result, data })),
            catchError(() => of({ result, data: null }))
          )),
        takeUntil(this.destroy$),
        finalize(() => {
          this.attendanceSyncing = false;
        })
      )
      .subscribe({
        next: ({ result, data }) => {
          if (data) {
            this.setDashboardData(data);
            this.attendanceSyncMessage = this.formatSyncSuccessMessage(result);
            return;
          }

          this.attendanceSyncMessage = 'تمت مزامنة حضور اليوم، لكن تعذر تحديث مؤشرات لوحة التحكم. ستبقى البيانات السابقة معروضة.';
        },
        error: () => {
          this.attendanceSyncFailed = true;
          this.attendanceSyncMessage = 'تعذر مزامنة حضور اليوم. لم يتم تغيير المؤشرات المعروضة.';
        }
      });
  }

  private loadDashboardData(): void {
    this.isLoading = true;
    this.hasLoadError = false;
    this.dashboardApiService
      .loadDashboardData({
        includeAttendance: this.permissionService.hasPermission(PERMISSIONS.attendance.view)
      })
      .pipe(
        catchError(() => {
          this.hasLoadError = true;
          return of(this.getEmptyDashboardData());
        }),
        takeUntil(this.destroy$),
        finalize(() => {
          this.isLoading = false;
        })
      )
      .subscribe((data) => {
        this.setDashboardData(data);
      });
  }

  private setDashboardData(data: DashboardApiData): void {
    this.cards = data.cards;
    this.previewLines = data.previewLines;
    this.lineReadinessSummary = data.lineReadinessSummary;
    this.attendanceIndicators = data.attendanceIndicators;
    this.criticalReadinessAlerts = data.criticalReadinessAlerts;
    this.readinessState = data.readinessState;
    this.attendanceState = data.attendanceState;
    this.assignmentCoveragePercent = data.assignmentCoveragePercent;
    this.attendanceDataStatus = data.attendanceDataStatus;
    this.hasLoadError = data.hasLoadError;
  }

  private getEmptyDashboardData(): DashboardApiData {
    const previewLines: FactoryMapLine[] = [];
    return {
      cards: [],
      lineReadinessSummary: {
        overallReadiness: 0,
        totalLines: 0,
        healthyLines: 0,
        warningLines: 0,
        criticalLines: 0,
        activeWorkers: 0,
        totalWorkers: 0,
        attendanceRate: 0
      },
      attendanceIndicators: [],
      previewLines,
      criticalReadinessAlerts: [],
      assignmentCoveragePercent: 0,
      attendanceDataStatus: 'Unknown',
      readinessState: 'error',
      attendanceState: 'not-authorized',
      notificationsState: 'error',
      hasLoadError: true
    };
  }

  getTrendLabel(trend: KpiTrend): string {
    if (trend === 'up') {
      return 'ارتفع';
    }
    if (trend === 'down') {
      return 'انخفض';
    }
    return 'مستقر';
  }

  getTrendClass(trend: KpiTrend): string {
    if (trend === 'up') {
      return 'trend-up';
    }
    if (trend === 'down') {
      return 'trend-down';
    }
    return 'trend-stable';
  }

  getTrendIcon(trend: KpiTrend): string {
    if (trend === 'up') {
      return 'pi pi-arrow-up';
    }
    if (trend === 'down') {
      return 'pi pi-arrow-down';
    }
    return 'pi pi-minus';
  }

  getReadinessTone(): string {
    if (this.lineReadinessSummary.overallReadiness >= 85) {
      return 'green';
    }
    if (this.lineReadinessSummary.overallReadiness >= 60) {
      return 'yellow';
    }
    return 'red';
  }

  getAttendanceTone(): string {
    if (this.lineReadinessSummary.attendanceRate >= 90) {
      return 'green';
    }
    if (this.lineReadinessSummary.attendanceRate >= 75) {
      return 'yellow';
    }
    return 'red';
  }

  get totalOperationalDeficit(): number {
    return this.lineReadinessSummary.totalWorkers - this.lineReadinessSummary.activeWorkers;
  }

  get hasReadinessData(): boolean {
    return this.readinessState === 'available'
      && this.attendanceState === 'available'
      && this.attendanceDataStatus === 'Complete';
  }

  get hasReadinessEndpointData(): boolean {
    return this.readinessState === 'available';
  }

  get hasAttendanceData(): boolean {
    return this.attendanceState === 'available';
  }

  get attendanceUnavailableByPermission(): boolean {
    return this.attendanceState === 'not-authorized';
  }

  get canSynchronizeAttendance(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.attendance.sync);
  }

  private get canViewAttendance(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.attendance.view);
  }

  get readinessUnavailableMessage(): string {
    if (this.attendanceUnavailableByPermission) {
      return 'الجاهزية التشغيلية غير متاحة بصلاحياتك الحالية.';
    }

    if (this.attendanceDataStatus === 'Unavailable') {
      return 'تحتاج مزامنة حضور اليوم قبل تأكيد الجاهزية التشغيلية.';
    }

    if (this.attendanceDataStatus === 'Incomplete') {
      return 'بيانات الحضور لليوم غير مكتملة، لذلك لا يمكن تأكيد الجاهزية التشغيلية.';
    }

    if (this.attendanceDataStatus === 'NoAssignments') {
      return 'لا يوجد تسكين كافٍ لتأكيد الجاهزية التشغيلية.';
    }

    return 'الجاهزية التشغيلية غير متاحة حالياً.';
  }

  get readinessToneStatus(): FactoryStatus {
    if (this.lineReadinessSummary.overallReadiness >= 85) {
      return 'ready';
    }
    if (this.lineReadinessSummary.overallReadiness >= 60) {
      return 'warning';
    }
    return 'critical';
  }

  get attendanceToneStatus(): FactoryStatus {
    if (this.lineReadinessSummary.attendanceRate >= 90) {
      return 'present';
    }
    if (this.lineReadinessSummary.attendanceRate >= 70) {
      return 'warning';
    }
    return 'absent';
  }

  get shortageToneStatus(): FactoryStatus {
    if (this.totalOperationalDeficit <= 2) {
      return 'ready';
    }
    if (this.totalOperationalDeficit <= 6) {
      return 'warning';
    }
    return 'critical';
  }

  get recommendationAvailabilityTone(): FactoryStatus {
    if (this.criticalReadinessAlerts.length > 0) {
      return 'warning';
    }
    return 'ready';
  }

  get criticalAlertsText(): string {
    if (this.criticalReadinessAlerts.length === 0) {
      return 'لا توجد حالات عجز واضحة الآن.';
    }
    if (this.criticalReadinessAlerts.length === 1) {
      return 'يوجد مرحلة واحدة تحتاج تدخل سريع.';
    }
    return `يوجد ${this.criticalReadinessAlerts.length} مرحلة تحتاج تدخل سريع.`;
  }

  get readinessShortageToneStatus(): FactoryStatus {
    if (this.lineReadinessSummary.overallReadiness >= 80) {
      return 'ready';
    }
    if (this.lineReadinessSummary.overallReadiness >= 65) {
      return 'warning';
    }
    return 'critical';
  }

  getIndicatorClass(tone: AttendanceIndicator['tone']): string {
    return `attendance-pill ${tone}`;
  }

  private formatSyncSuccessMessage(result: AttendanceSyncResult): string {
    const changedRecords = result.insertedRecords + result.updatedRecords;
    return `تمت مزامنة حضور اليوم: ${changedRecords} سجل تم تحديثه، و${result.matchedWorkersCount} عامل تم ربطه.`;
  }
}
