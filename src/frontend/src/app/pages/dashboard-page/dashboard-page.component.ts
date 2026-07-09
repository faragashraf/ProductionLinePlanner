import { Component } from '@angular/core';
import {
  DashboardCard,
  FactoryMapLine,
  FactoryReadinessSummary,
  AttendanceIndicator,
  MockDataService
} from '../../core/services/mock-data.service';
import { KpiTrend } from '../../core/services/mock-data.service';
import { FactoryStatus } from '../../shared/models/factory-status.model';

interface StageReadinessAlert {
  lineName: string;
  stageName: string;
  workersCurrent: number;
  workersRequired: number;
  shortageWorkers: number;
}

@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-page.component.html',
  styleUrls: ['./dashboard-page.component.scss']
})
export class DashboardPageComponent {
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

  constructor(private readonly dataService: MockDataService) {}

  ngOnInit(): void {
    this.cards = this.dataService.getDashboardCards();
    this.previewLines = this.dataService.getFactoryMapData();
    this.lineReadinessSummary = this.dataService.getFactoryReadinessSummary(this.previewLines);
    this.attendanceIndicators = this.dataService.getAttendanceIndicators();
    this.criticalReadinessAlerts = this.extractCriticalStageAlerts(this.previewLines);
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

  private extractCriticalStageAlerts(lines: FactoryMapLine[]): StageReadinessAlert[] {
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

  getIndicatorClass(tone: AttendanceIndicator['tone']): string {
    return `attendance-pill ${tone}`;
  }
}
