import { OperationalReadinessStage } from '../../shared/models/operational-readiness.model';

export type ReadinessStageFilter =
  | 'HasAbsentWorkers'
  | 'HasLateWorkers'
  | 'HasUnknownAttendance'
  | 'NoAssignments'
  | 'NotFullyReady'
  | 'FullyReady'
  | 'HasCheckedOutWorkers';

export interface ReadinessStageFilterDefinition {
  value: ReadinessStageFilter;
  label: string;
  icon: string;
}

export interface ReadinessStageFilterOption extends ReadinessStageFilterDefinition {
  count: number;
}

export const READINESS_STAGE_FILTER_DEFINITIONS: readonly ReadinessStageFilterDefinition[] = [
  { value: 'HasAbsentWorkers', label: 'بها غائبون', icon: 'pi-times-circle' },
  { value: 'HasLateWorkers', label: 'بها متأخرون', icon: 'pi-clock' },
  { value: 'HasUnknownAttendance', label: 'حضور غير مؤكد', icon: 'pi-question-circle' },
  { value: 'NoAssignments', label: 'بدون تسكين', icon: 'pi-user-minus' },
  { value: 'NotFullyReady', label: 'جاهزية أقل من 100%', icon: 'pi-chart-line' },
  { value: 'FullyReady', label: 'جاهزية مكتملة', icon: 'pi-check-circle' },
  { value: 'HasCheckedOutWorkers', label: 'بها عمال منصرفون', icon: 'pi-sign-out' }
];

export function matchesReadinessStageFilter(
  stage: OperationalReadinessStage,
  filter: ReadinessStageFilter
): boolean {
  const metrics = stage.metrics;
  switch (filter) {
    case 'HasAbsentWorkers': return metrics.absentCount > 0;
    case 'HasLateWorkers': return metrics.lateCount > 0;
    case 'HasUnknownAttendance': return metrics.unknownCount > 0 || metrics.status === 'Unknown';
    case 'NoAssignments': return metrics.assignedWorkerCount === 0 || metrics.status === 'NoAssignments';
    case 'NotFullyReady':
      return metrics.assignedWorkerCount > 0
        && metrics.status !== 'Unknown'
        && metrics.operationalReadinessPercentage !== null
        && metrics.operationalReadinessPercentage < 100;
    case 'FullyReady':
      return metrics.assignedWorkerCount > 0
        && metrics.status !== 'Unknown'
        && metrics.operationalReadinessPercentage === 100;
    case 'HasCheckedOutWorkers': return metrics.checkedOutCount > 0;
  }
}

export function compareReadinessStagesByDomainOrder(
  left: OperationalReadinessStage,
  right: OperationalReadinessStage
): number {
  const leftOrder = validStageOrder(left.stageOrder);
  const rightOrder = validStageOrder(right.stageOrder);
  if (leftOrder !== null && rightOrder !== null && leftOrder !== rightOrder) return leftOrder - rightOrder;
  if (leftOrder !== null) return -1;
  if (rightOrder !== null) return 1;

  const nameComparison = left.name.localeCompare(right.name, 'ar', { sensitivity: 'base' });
  return nameComparison || left.id.localeCompare(right.id);
}

function validStageOrder(value: number | null | undefined): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : null;
}
