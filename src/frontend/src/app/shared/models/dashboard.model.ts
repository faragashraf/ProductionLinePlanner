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
  workersCurrent: number;
  workersRequired: number;
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
