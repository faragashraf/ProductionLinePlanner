import { Injectable } from '@angular/core';

export type KpiTrend = 'up' | 'down' | 'stable';

export interface DashboardCard {
  title: string;
  value: string;
  trend: KpiTrend;
  trendLabel: string;
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
}
