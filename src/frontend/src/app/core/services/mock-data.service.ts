import { Injectable } from '@angular/core';
import { FactoryStatus, deriveStatusFromReadiness } from '../../shared/models/factory-status.model';
import {
  AttendanceIndicator,
  DashboardCard,
  FactoryMapLine,
  FactoryReadinessSummary,
  FactorySubStage,
  KpiTrend,
  StatusTone
} from '../../shared/models/dashboard.model';
import { WorkerPageItem } from '../../shared/models/worker.model';
import { FactoryLayout, MainStageLayout, ProductionLineLayout, SubStageLayout } from '../../shared/models/factory-visualization.model';

export type {
  AttendanceIndicator,
  DashboardCard,
  FactoryMapLine,
  FactoryReadinessSummary,
  FactorySubStage,
  KpiTrend,
  StatusTone
};

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

  getWorkersMock(): WorkerPageItem[] {
    return [
      { code: 'W-101', fullName: 'أحمد سعيد', state: 'على رأس العمل', employmentStatus: 'Active', isActive: true },
      { code: 'W-102', fullName: 'سارة علي', state: 'على رأس العمل', employmentStatus: 'Active', isActive: true },
      { code: 'W-109', fullName: 'محمود يونس', state: 'خارج الخدمة', employmentStatus: 'LeftEmployment', isActive: false }
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

  getFactoryLayout(): FactoryLayout {
    return {
      id: 'factory-01',
      type: 'factory',
      name: 'مصنع الطموح',
      status: this.buildNodeStatus(79, 100),
      readinessPercent: 79,
      workersCurrent: 33,
      workersRequired: 45,
      description: 'خريطة مرئية تعتمد على الميتاداتا للمستويات الأربعة.',
      lines: [
        this.createProductionLine({
          id: 'line-red',
          name: 'الخط الأحمر',
          statusPercent: 84,
          statusText: 'جاهز',
          activeStageId: 'line-red-pack',
          activeStageName: 'مرحلة التغليف',
          workersCurrent: 17,
          workersRequired: 21,
          position: { row: 1, column: 1 },
          stages: [
            this.createMainStage({
              id: 'line-red-mix',
              name: 'مرحلة الخلط',
              workersCurrent: 8,
              workersRequired: 9,
              note: 'تعديلات تغذية مستمرة خلال الوردية الصباحية.',
              position: { row: 1, column: 1 },
              subStages: [
                this.createSubStage({
                  id: 'line-red-mix-input',
                  name: 'إدخال الخامات',
                  workersCurrent: 5,
                  workersRequired: 6,
                  workers: [
                    {
                      id: 'worker-r-mix-01',
                      fullName: 'أحمد كامل',
                      code: 'W-101',
                      status: 'ready',
                      assignmentType: 'ثابت',
                      lastActivity: 'متاح منذ 2:15'
                    },
                    {
                      id: 'worker-r-mix-02',
                      fullName: 'محمد سليم',
                      code: 'W-102',
                      status: 'late',
                      assignmentType: 'مؤقت',
                      lastActivity: 'دخل في استجابة إعادة تخصيص'
                    }
                  ],
                  position: { row: 1, column: 1 }
                }),
                this.createSubStage({
                  id: 'line-red-mix-control',
                  name: 'مراقبة الخلط',
                  workersCurrent: 3,
                  workersRequired: 3,
                  workers: [
                    {
                      id: 'worker-r-mix-03',
                      fullName: 'سارة محمود',
                      code: 'W-103',
                      status: 'present',
                      assignmentType: 'ثابت',
                      lastActivity: 'منسق على الخط منذ 48 دقيقة'
                    }
                  ],
                  position: { row: 1, column: 2 }
                })
              ]
            }),
            this.createMainStage({
              id: 'line-red-pack',
              name: 'مرحلة التغليف',
              workersCurrent: 9,
              workersRequired: 12,
              note: 'تشابك زمني أعلى في نهاية الوردية الأولى.',
              position: { row: 2, column: 1 },
              subStages: [
                this.createSubStage({
                  id: 'line-red-pack-internal',
                  name: 'تغليف داخلي',
                  workersCurrent: 4,
                  workersRequired: 5,
                  workers: [
                    {
                      id: 'worker-r-pack-01',
                      fullName: 'نادية حاتم',
                      code: 'W-104',
                      status: 'present',
                      assignmentType: 'ثابت',
                      lastActivity: 'مستوعبة مرحلة التغليف'
                    },
                    {
                      id: 'worker-r-pack-02',
                      fullName: 'عبدالله يونس',
                      code: 'W-105',
                      status: 'warning',
                      assignmentType: 'مؤقت',
                      lastActivity: 'متابعة تغطية خلال 11 دقيقة'
                    }
                  ],
                  position: { row: 2, column: 1 }
                }),
                this.createSubStage({
                  id: 'line-red-pack-seal',
                  name: 'إغلاق العبوات',
                  workersCurrent: 5,
                  workersRequired: 7,
                  workers: [
                    {
                      id: 'worker-r-pack-03',
                      fullName: 'رنا عبده',
                      code: 'W-106',
                      status: 'ready',
                      assignmentType: 'ثابت',
                      lastActivity: 'تم التحديث قبل 9 دقائق'
                    }
                  ],
                  position: { row: 2, column: 2 }
                })
              ]
            })
          ]
        }),
        this.createProductionLine({
          id: 'line-blue',
          name: 'الخط الأزرق',
          statusPercent: 74,
          statusText: 'يتطلب دعم',
          activeStageId: 'line-blue-feed',
          activeStageName: 'مرحلة التغذية',
          workersCurrent: 16,
          workersRequired: 24,
          position: { row: 2, column: 1 },
          stages: [
            this.createMainStage({
              id: 'line-blue-feed',
              name: 'مرحلة التغذية',
              workersCurrent: 7,
              workersRequired: 11,
              note: 'تشبع العمالة منخفض في بداية التزود.',
              position: { row: 1, column: 1 },
              subStages: [
                this.createSubStage({
                  id: 'line-blue-feed-main',
                  name: 'تغذية الخط الأساسي',
                  workersCurrent: 4,
                  workersRequired: 6,
                  workers: [
                    {
                      id: 'worker-b-feed-01',
                      fullName: 'عمر نجات',
                      code: 'W-201',
                      status: 'ready',
                      assignmentType: 'ثابت',
                      lastActivity: 'استقرار جيد خلال الوردية'
                    },
                    {
                      id: 'worker-b-feed-02',
                      fullName: 'هند فارس',
                      code: 'W-202',
                      status: 'present',
                      assignmentType: 'ثابت',
                      lastActivity: 'تبديل دوري خلال الساعة الماضية'
                    }
                  ],
                  position: { row: 1, column: 1 }
                }),
                this.createSubStage({
                  id: 'line-blue-feed-aux',
                  name: 'تحويلات الفرز',
                  workersCurrent: 3,
                  workersRequired: 5,
                  workers: [],
                  position: { row: 1, column: 2 }
                })
              ]
            }),
            this.createMainStage({
              id: 'line-blue-final',
              name: 'مرحلة الفحص النهائي',
              workersCurrent: 9,
              workersRequired: 13,
              note: 'نقص بنية تدقيق الجودة أمام نهاية الوردية.',
              position: { row: 2, column: 1 },
              subStages: [
                this.createSubStage({
                  id: 'line-blue-final-qa',
                  name: 'مركز الفحص',
                  workersCurrent: 5,
                  workersRequired: 7,
                  workers: [
                    {
                      id: 'worker-b-final-01',
                      fullName: 'فاطمة ناصر',
                      code: 'W-203',
                      status: 'warning',
                      assignmentType: 'مؤقت',
                      lastActivity: 'إعادة توظيف من نهاية الوردية'
                    },
                    {
                      id: 'worker-b-final-02',
                      fullName: 'طارق سالم',
                      code: 'W-204',
                      status: 'present',
                      assignmentType: 'ثابت',
                      lastActivity: 'مراجعة عينات من 10 دقائق'
                    }
                  ],
                  position: { row: 2, column: 1 }
                }),
                this.createSubStage({
                  id: 'line-blue-final-scan',
                  name: 'مسح وتوثيق',
                  workersCurrent: 4,
                  workersRequired: 6,
                  workers: [
                    {
                      id: 'worker-b-final-03',
                      fullName: 'مروان جابر',
                      code: 'W-205',
                      status: 'absent',
                      assignmentType: 'غير محدد',
                      lastActivity: 'غياب متقطع 2x'
                    }
                  ],
                  position: { row: 2, column: 2 }
                })
              ]
            })
          ]
        })
      ]
    };
  }

  private buildReadinessPercent(current: number, required: number): number {
    if (required <= 0) {
      return 0;
    }

    return Math.min(100, Math.max(0, Math.round((current / required) * 100)));
  }

  private buildNodeStatus(current: number, required: number): FactoryStatus {
    const readiness = this.buildReadinessPercent(current, required);
    return deriveStatusFromReadiness(readiness);
  }

  private createProductionLine(config: {
    id: string;
    name: string;
    statusPercent: number;
    statusText: string;
    activeStageId: string;
    activeStageName: string;
    workersCurrent: number;
    workersRequired: number;
    position?: { row?: number; column?: number; x?: number; y?: number; width?: number; height?: number };
    stages: MainStageLayout[];
  }): ProductionLineLayout {
    return {
      id: config.id,
      type: 'line',
      name: config.name,
      status: this.buildNodeStatus(config.workersCurrent, config.workersRequired),
      readinessPercent: config.statusPercent,
      statusText: config.statusText,
      activeStageId: config.activeStageId,
      activeStageName: config.activeStageName,
      workersCurrent: config.workersCurrent,
      workersRequired: config.workersRequired,
      position: config.position,
      stages: config.stages,
      description: `خط ${config.name} ضمن الميتاداتا المؤسسة`
    };
  }

  private createMainStage(config: {
    id: string;
    name: string;
    workersCurrent: number;
    workersRequired: number;
    note?: string;
    position?: { row?: number; column?: number; x?: number; y?: number; width?: number; height?: number };
    subStages: SubStageLayout[];
  }): MainStageLayout {
    return {
      id: config.id,
      type: 'main-stage',
      name: config.name,
      status: this.buildNodeStatus(config.workersCurrent, config.workersRequired),
      readinessPercent: this.buildReadinessPercent(config.workersCurrent, config.workersRequired),
      workersCurrent: config.workersCurrent,
      workersRequired: config.workersRequired,
      note: config.note,
      position: config.position,
      subStages: config.subStages
    };
  }

  private createSubStage(config: {
    id: string;
    name: string;
    workersCurrent: number;
    workersRequired: number;
    workers: Array<{
      id: string;
      fullName: string;
      code: string;
      status: FactoryStatus | string;
      assignmentType: string;
      lastActivity: string;
    }>;
    position?: { row?: number; column?: number; x?: number; y?: number; width?: number; height?: number };
  }): SubStageLayout {
    return {
      id: config.id,
      type: 'sub-stage',
      name: config.name,
      status: this.buildNodeStatus(config.workersCurrent, config.workersRequired),
      readinessPercent: this.buildReadinessPercent(config.workersCurrent, config.workersRequired),
      workersCurrent: config.workersCurrent,
      workersRequired: config.workersRequired,
      workers: config.workers,
      position: config.position
    };
  }
}
