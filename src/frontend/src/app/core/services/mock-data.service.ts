import { Injectable } from '@angular/core';

export type KpiTrend = 'up' | 'down' | 'stable';
export type StatusTone = 'green' | 'yellow' | 'red';

export interface DashboardCard {
  title: string;
  value: string;
  trend: KpiTrend;
  trendLabel: string;
}

export interface AttendanceIndicator {
  label: string;
  value: number;
  icon: string;
  tone: StatusTone;
}

export interface FactorySubStage {
  name: string;
  workersCurrent: number;
  workersRequired: number;
}

export interface FactoryMapLine {
  name: string;
  statusPercent: number;
  readinessLabel: string;
  stages: FactorySubStage[];
}

export interface FactoryReadinessSummary {
  overallReadiness: number;
  totalLines: number;
  healthyLines: number;
  warningLines: number;
  criticalLines: number;
  activeWorkers: number;
  totalWorkers: number;
  attendanceRate: number;
}

@Injectable({
  providedIn: 'root'
})
export class MockDataService {
  getDashboardCards(): DashboardCard[] {
    return [
      { title: 'جاهزية المصنع', value: '82%', trend: 'up', trendLabel: 'ارتفع 3% خلال 24 ساعة' },
      { title: 'العاملون الحاضرون', value: '74', trend: 'up', trendLabel: '+5 عن الوردية السابقة' },
      { title: 'العاملون المتأخرون', value: '12', trend: 'down', trendLabel: '-1 بعد التحديث الأخير' },
      { title: 'الإشعارات غير المقروءة', value: '8', trend: 'stable', trendLabel: 'ينتظر المعالجة' }
    ];
  }

  getAttendanceIndicators(): AttendanceIndicator[] {
    return [
      { label: 'حاضر', value: 74, icon: 'pi pi-check', tone: 'green' },
      { label: 'متأخر', value: 5, icon: 'pi pi-clock', tone: 'yellow' },
      { label: 'غائب', value: 12, icon: 'pi pi-times', tone: 'red' }
    ];
  }

  getFactoryMapData(): FactoryMapLine[] {
    return [
      {
        name: 'الخط الأحمر',
        statusPercent: 88,
        readinessLabel: 'ممتاز',
        stages: [
          { name: 'مرحلة خلط', workersCurrent: 5, workersRequired: 6 },
          { name: 'مرحلة تغليف', workersCurrent: 7, workersRequired: 7 },
          { name: 'مرحلة فحص', workersCurrent: 4, workersRequired: 5 }
        ]
      },
      {
        name: 'الخط الأزرق',
        statusPercent: 64,
        readinessLabel: 'متوسط',
        stages: [
          { name: 'مرحلة تغذية', workersCurrent: 3, workersRequired: 5 },
          { name: 'مرحلة تعبئة', workersCurrent: 2, workersRequired: 4 },
          { name: 'مرحلة تجهيز نهائي', workersCurrent: 5, workersRequired: 6 }
        ]
      }
    ];
  }

  getFactoryReadinessSummary(lines: FactoryMapLine[]): FactoryReadinessSummary {
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
    const activeWorkers = allStages.reduce((sum, stage) => sum + stage.workersCurrent, 0);
    const totalWorkers = allStages.reduce((sum, stage) => sum + stage.workersRequired, 0);
    const readinessPoints = lines.map((line) => line.statusPercent);

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
}
