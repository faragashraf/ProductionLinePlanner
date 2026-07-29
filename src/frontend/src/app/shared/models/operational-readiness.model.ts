export type ReadinessStatus = 'Ready' | 'Warning' | 'Critical' | 'Unknown' | 'NoAssignments';
export type AttendanceSyncStatus = 'Fresh' | 'Stale' | 'Failed' | 'NeverSynced' | 'RecordsAvailable';
export type OperationalAttendanceState = 'Present' | 'Late' | 'Absent' | 'NotCheckedIn' | 'CheckedOut' | 'Unknown';
export type ReadinessNodeType = 'Factory' | 'Department' | 'ProductionLine' | 'Stage';

export interface OperationalReadinessMetrics {
  assignedWorkerCount: number;
  currentlyPresentCount: number;
  lateCount: number;
  absentCount: number;
  checkedOutCount: number;
  unknownCount: number;
  operationalReadinessPercentage: number | null;
  contributionToParentShortage: number | null;
  childCount: number;
  status: ReadinessStatus;
}

export interface AttendanceSyncFreshness {
  status: AttendanceSyncStatus;
  isTrusted: boolean;
  lastAttemptAtUtc: string | null;
  lastSuccessfulAtUtc: string | null;
  lastErrorCode: string | null;
  ageMinutes: number | null;
}

export interface OperationalReadinessWorkdayPolicy {
  workdayBoundaryTime: string;
  dayStartTime: string;
  gracePeriodMinutes: number;
  freshnessThresholdMinutes: number;
}

export interface OperationalReadinessModelOption {
  id: string;
  name: string;
  code: string;
  stageCount: number;
}

export interface OperationalReadinessLine {
  id: string;
  factoryId: string;
  departmentId: string;
  name: string;
  code: string | null;
  metrics: OperationalReadinessMetrics;
  modelNames: string[];
  models: OperationalReadinessModelOption[];
}

export interface OperationalReadinessDepartment {
  id: string;
  factoryId: string;
  name: string;
  code: string;
  metrics: OperationalReadinessMetrics;
  productionLines: OperationalReadinessLine[];
}

export interface OperationalReadinessFactory {
  id: string;
  name: string;
  code: string;
  metrics: OperationalReadinessMetrics;
  departments: OperationalReadinessDepartment[];
}

export interface OperationalReadinessStage {
  id: string;
  factoryId: string;
  departmentId: string;
  productionLineId: string;
  mainStageId: string;
  name: string;
  code: string;
  mainStageName: string;
  metrics: OperationalReadinessMetrics;
  modelNames: string[];
}

export interface OperationalReadinessWorker {
  workerId: string;
  productionLineId: string;
  stageId: string;
  employeeCode: string;
  fullName: string;
  attendanceState: OperationalAttendanceState;
  attendanceLabel: string;
  isOperationallyPresent: boolean;
  checkInAtUtc: string | null;
  checkOutAtUtc: string | null;
  lateByMinutes: number | null;
}

export interface OperationalReadinessSnapshot {
  operationalDate: string;
  calculatedAtUtc: string;
  workdayPolicy: OperationalReadinessWorkdayPolicy;
  attendanceSync: AttendanceSyncFreshness;
  factories: OperationalReadinessFactory[];
}

export interface OperationalReadinessStages {
  operationalDate: string;
  calculatedAtUtc: string;
  attendanceSync: AttendanceSyncFreshness;
  factoryId: string;
  factoryName: string;
  departmentId: string;
  departmentName: string;
  productionLineId: string;
  productionLineName: string;
  selectedProductModelId: string | null;
  selectedProductModelName: string | null;
  requiresModelSelection: boolean;
  availableModels: OperationalReadinessModelOption[];
  stages: OperationalReadinessStage[];
}

export interface OperationalReadinessWorkers extends Omit<OperationalReadinessStages,
  'stages' | 'selectedProductModelId' | 'selectedProductModelName' | 'requiresModelSelection' | 'availableModels'> {
  stageId: string;
  stageName: string;
  workers: OperationalReadinessWorker[];
}

export interface OperationalReadinessNodePatch {
  id: string;
  parentId: string | null;
  nodeType: ReadinessNodeType;
  name: string;
  code: string | null;
  metrics: OperationalReadinessMetrics;
  modelNames: string[];
}

export interface OperationalReadinessWorkerPatch {
  productionLineId: string;
  stageId: string;
  workerId: string;
  isRemoved: boolean;
  worker: OperationalReadinessWorker | null;
}

export interface OperationalReadinessDelta {
  eventId: string;
  operationalDate: string;
  calculatedAtUtc: string;
  attendanceSync: AttendanceSyncFreshness;
  requiresSnapshotReload: boolean;
  nodes: OperationalReadinessNodePatch[];
  workers: OperationalReadinessWorkerPatch[];
}

export type ReadinessWorkerFilter = 'all' | 'present' | 'late' | 'absent' | 'checkedOut';
export type ReadinessLevel = 'factory' | 'department' | 'line' | 'stage';
