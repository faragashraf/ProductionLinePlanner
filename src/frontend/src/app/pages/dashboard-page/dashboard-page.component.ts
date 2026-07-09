import { Component } from '@angular/core';
import {
  DashboardCard,
  FactoryMapLine,
  FactoryReadinessSummary,
  AttendanceIndicator,
  MockDataService
} from '../../core/services/mock-data.service';
import { KpiTrend } from '../../core/services/mock-data.service';

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

  constructor(private readonly dataService: MockDataService) {}

  ngOnInit(): void {
    this.cards = this.dataService.getDashboardCards();
    this.previewLines = this.dataService.getFactoryMapData();
    this.lineReadinessSummary = this.dataService.getFactoryReadinessSummary(this.previewLines);
    this.attendanceIndicators = this.dataService.getAttendanceIndicators();
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

  getIndicatorClass(tone: AttendanceIndicator['tone']): string {
    return `attendance-pill ${tone}`;
  }
}
