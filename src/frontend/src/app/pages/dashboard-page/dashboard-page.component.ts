import { Component, OnInit } from '@angular/core';
import { catchError, finalize, of } from 'rxjs';
import {
  AttendanceIndicator,
  MockDataService
} from '../../core/services/mock-data.service';
import { DashboardApiData, DashboardApiService, StageReadinessAlert } from '../../core/services/dashboard-api.service';
import { DashboardCard, FactoryMapLine, FactoryReadinessSummary, KpiTrend } from '../../core/services/mock-data.service';
import { FactoryStatus } from '../../shared/models/factory-status.model';

@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-page.component.html',
  styleUrls: ['./dashboard-page.component.scss']
})
export class DashboardPageComponent implements OnInit {
  isLoading = true;
  showFallbackWarning = false;
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

  constructor(
    private readonly dataService: MockDataService,
    private readonly dashboardApiService: DashboardApiService
  ) {}

  ngOnInit(): void {
    this.loadDashboardData();
  }

  private loadDashboardData(): void {
    this.dashboardApiService
      .loadDashboardData()
      .pipe(
        catchError(() => {
          this.showFallbackWarning = true;
          return of(this.getMockDashboardData());
        }),
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
  }

  private getMockDashboardData(): DashboardApiData {
    const previewLines = this.dataService.getFactoryMapData();
    return {
      cards: this.dataService.getDashboardCards(),
      lineReadinessSummary: this.dataService.getFactoryReadinessSummary(previewLines),
      attendanceIndicators: this.dataService.getAttendanceIndicators(),
      previewLines,
      criticalReadinessAlerts: this.dashboardApiService.extractCriticalReadinessAlerts(previewLines)
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

  get totalAttendanceDeficit(): number {
    return this.lineReadinessSummary.totalWorkers - this.lineReadinessSummary.activeWorkers;
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
    if (this.totalAttendanceDeficit <= 2) {
      return 'ready';
    }
    if (this.totalAttendanceDeficit <= 6) {
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
}
