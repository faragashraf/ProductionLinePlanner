export type CommandCenterOperationStatus = 'All' | 'None' | 'Draft' | 'Approved' | 'ApprovalCancelled' | 'Cancelled';
export type CommandCenterReadinessStatus = 'Ready' | 'NoOperation' | 'StaffingShortage' | 'JourneyNotConfigured' | 'DataIncomplete';

export interface CommandCenterFilters {
  operationDate: string;
  factoryId: string | null;
  departmentId: string | null;
  productionLineId: string | null;
  operationStatus: CommandCenterOperationStatus;
}

export interface CommandCenterScope {
  productionDate: string;
  factoryId: string | null;
  departmentId: string | null;
  productionLineId: string | null;
  operationStatus: CommandCenterOperationStatus;
  description: string;
}

export interface CommandCenterFactoryOption { id: string; name: string; code: string; }
export interface CommandCenterDepartmentOption { id: string; factoryId: string; name: string; code: string; }
export interface CommandCenterLineOption { id: string; factoryId: string; departmentId: string | null; name: string; code: string | null; }
export interface CommandCenterStructureCatalog {
  factories: CommandCenterFactoryOption[];
  departments: CommandCenterDepartmentOption[];
  lines: CommandCenterLineOption[];
}

export interface CommandCenterRatio {
  numerator: number;
  denominator: number;
  percentage: number | null;
  scope: string;
  date: string;
  zeroBehavior: 'NoData' | 'Calculated' | 'NotAttributable';
}

export interface CommandCenterWorkerDetail {
  workerId: string;
  workerCode: string;
  workerName: string;
  attendanceStatus: string;
  permanentAssignments: string[];
}

export interface CommandCenterWorkforce {
  activeWorkers: number | null;
  presentWorkers: number;
  presentPermanentlyAssignedWorkers: number;
  presentUnassignedWorkers: number | null;
  permanentlyAssignedNotPresentWorkers: number;
  assignmentCoverage: CommandCenterRatio;
  attendanceEvidenceComplete: boolean;
  attributionNote: string;
  presentAssignedDetails: CommandCenterWorkerDetail[];
  presentUnassignedDetails: CommandCenterWorkerDetail[];
  assignedNotPresentDetails: CommandCenterWorkerDetail[];
}

export interface CommandCenterLineSummary {
  activeLines: number;
  readyLines: number;
  staffingShortageLines: number;
  journeyNotConfiguredLines: number;
  dataIncompleteLines: number;
  problemLines: number;
  stagesWithoutPresentWorker: number;
}

export interface CommandCenterOperationsSummary {
  linesWithOperation: number;
  linesWithoutOperation: number;
  draftOperations: number;
  approvedOperations: number;
  approvalCancelledOperations: number;
  cancelledOperations: number;
  approvedRecordedValue: number;
  items: CommandCenterOperation[];
}

export interface CommandCenterDataQuality {
  modelStagesWithoutPrice: number;
  modelStagesWithoutStandardTime: number;
  activeJourneyStagesWithoutPresentWorker: number;
  activeModelsWithoutJourney: number | null;
  issues: CommandCenterQualityIssue[];
  modelsWithoutJourneyScopeNote: string;
}

export interface CommandCenterQualityIssue {
  type: 'MissingPrice' | 'MissingStandardTime' | 'StageWithoutPresentWorker' | 'ModelWithoutJourney' | 'LineWithoutDepartment' | string;
  title: string;
  detail: string;
  factoryId: string | null;
  departmentId: string | null;
  productionLineId: string | null;
  productModelId: string | null;
  productModelStageId: string | null;
}

export interface CommandCenterFactory {
  id: string;
  name: string;
  code: string;
  activeDepartments: number;
  activeLines: number;
  presentPermanentlyAssignedWorkers: number;
  problemLines: number;
  draftOperations: number;
  approvedOperations: number;
  departments: CommandCenterDepartment[];
}

export interface CommandCenterDepartment {
  id: string | null;
  name: string;
  code: string | null;
  activeLines: number;
  presentPermanentlyAssignedWorkers: number;
  permanentlyAssignedWorkers: number;
  presentUnassignedWorkers: number | null;
  readyLines: number;
  notReadyLines: number;
  draftOperations: number;
  approvedOperations: number;
  workforceAttributionNote: string;
  lines: CommandCenterLine[];
}

export interface CommandCenterLine {
  id: string;
  factoryId: string;
  departmentId: string | null;
  name: string;
  code: string | null;
  readinessStatus: CommandCenterReadinessStatus;
  permanentlyAssignedWorkers: number;
  presentPermanentlyAssignedWorkers: number;
  requiredWorkers: number;
  journeyStages: number;
  stagesCoveredByPresentWorker: number;
  stagesWithoutPresentWorker: number;
  lastReliableUpdateUtc: string;
  alerts: string[];
  operations: CommandCenterOperation[];
}

export interface CommandCenterOperation {
  productionOrderId: string;
  productionLineId: string;
  productModelId: string;
  productModelCode: string;
  productModelName: string;
  status: Exclude<CommandCenterOperationStatus, 'All' | 'None'>;
  finalLineQuantity: number;
  recordedStageValue: number;
  registeredStages: number;
  journeyStages: number;
  stageRegistrationCoverage: CommandCenterRatio;
  lastReliableUpdateUtc: string;
  stages: CommandCenterStage[];
}

export interface CommandCenterStage {
  productModelStageId: string;
  subStageId: string;
  mainStageName: string;
  stageCode: string;
  stageName: string;
  stageOrder: number;
  requiredWorkers: number;
  permanentlyAssignedWorkers: number;
  presentPermanentlyAssignedWorkers: number;
  hasPrice: boolean;
  hasStandardTime: boolean;
  isRegistered: boolean;
  alerts: string[];
}

export interface ManufacturingCommandCenter {
  scope: CommandCenterScope;
  filterCatalog: CommandCenterStructureCatalog;
  workforce: CommandCenterWorkforce;
  lineSummary: CommandCenterLineSummary;
  operations: CommandCenterOperationsSummary;
  dataQuality: CommandCenterDataQuality;
  factories: CommandCenterFactory[];
  calculatedAtUtc: string;
}

export function cairoToday(): string {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Africa/Cairo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit'
  }).format(new Date());
}

export function defaultCommandCenterFilters(): CommandCenterFilters {
  return {
    operationDate: cairoToday(),
    factoryId: null,
    departmentId: null,
    productionLineId: null,
    operationStatus: 'All'
  };
}

export function commandCenterOperationLabel(status: string): string {
  return ({
    None: 'لا يوجد تشغيل',
    Draft: 'مسودة تحتاج استكمالًا',
    Approved: 'معتمد',
    ApprovalCancelled: 'ملغي الاعتماد',
    Cancelled: 'ملغى'
  } as Record<string, string>)[status] ?? 'غير معروف';
}

export function commandCenterReadinessLabel(status: string): string {
  return ({
    Ready: 'جاهز',
    NoOperation: 'لا يوجد تشغيل اليوم',
    StaffingShortage: 'نقص عمالة',
    JourneyNotConfigured: 'رحلة موديل غير مهيأة',
    DataIncomplete: 'بيانات غير مكتملة'
  } as Record<string, string>)[status] ?? 'غير معروف';
}

export function commandCenterScopeMatches(filters: CommandCenterFilters, change: {
  productionDate?: string | null;
  factoryId?: string | null;
  departmentId?: string | null;
  productionLineId?: string | null;
}): boolean {
  if (change.productionDate && change.productionDate !== filters.operationDate) return false;
  if (filters.factoryId && change.factoryId && change.factoryId !== filters.factoryId) return false;
  if (filters.departmentId && change.departmentId && change.departmentId !== filters.departmentId) return false;
  if (filters.productionLineId && change.productionLineId && change.productionLineId !== filters.productionLineId) return false;
  return true;
}
