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

export type CommandCenterStatusTone = 'success' | 'warning' | 'danger' | 'neutral';
export type CommandCenterLineStatusKey = 'execution' | 'route' | 'staffing' | 'data';

export interface CommandCenterLineStatusDimension {
  key: CommandCenterLineStatusKey;
  label: string;
  value: string;
  tone: CommandCenterStatusTone;
}

export interface CommandCenterProblemLine {
  factoryName: string;
  departmentName: string;
  line: CommandCenterLine;
  reasons: string[];
  severity: number;
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

export function commandCenterLineStatusDimensions(line: CommandCenterLine): CommandCenterLineStatusDimension[] {
  const operationStatuses = [...new Set(line.operations.map(operation => operation.status))]
    .sort((first, second) => operationStatusSeverity(second) - operationStatusSeverity(first));
  const executionTone = operationStatuses.reduce<CommandCenterStatusTone>(
    (tone, status) => strongerTone(tone, operationStatusTone(status)),
    'neutral'
  );
  const hasDataGap = line.readinessStatus === 'DataIncomplete'
    || line.operations.some(operation => operation.stages.some(stage => !stage.hasPrice || !stage.hasStandardTime));
  const staffingGap = Math.max(0, line.requiredWorkers - line.presentPermanentlyAssignedWorkers);

  return [
    {
      key: 'execution',
      label: 'التنفيذ',
      value: operationStatuses.length
        ? operationStatuses.map(commandCenterOperationLabel).join('، ')
        : commandCenterOperationLabel('None'),
      tone: operationStatuses.length ? executionTone : 'warning'
    },
    {
      key: 'route',
      label: 'المسار',
      value: line.journeyStages > 0 ? 'مهيأ' : 'غير مهيأ',
      tone: line.journeyStages > 0 ? 'success' : 'danger'
    },
    {
      key: 'staffing',
      label: 'تغطية العمالة',
      value: line.requiredWorkers === 0
        ? (line.journeyStages > 0 ? 'لا يوجد احتياج مسجل' : 'غير قابلة للقياس')
        : staffingGap > 0 ? `نقص ${staffingGap}` : 'مكتملة',
      tone: line.requiredWorkers === 0
        ? (line.journeyStages > 0 ? 'success' : 'neutral')
        : staffingGap > 0 ? 'warning' : 'success'
    },
    {
      key: 'data',
      label: 'جودة البيانات',
      value: hasDataGap ? 'فجوة بيانات' : 'مكتملة',
      tone: hasDataGap ? 'danger' : 'success'
    }
  ];
}

export function commandCenterLineProblemReasons(line: CommandCenterLine): string[] {
  const reasons: string[] = [];
  const operationStatuses = new Set(line.operations.map(operation => operation.status));

  if (line.operations.length === 0) reasons.push('لا يوجد تشغيل مسجل لليوم');
  if (operationStatuses.has('Cancelled')) reasons.push('يوجد تشغيل ملغى');
  if (operationStatuses.has('ApprovalCancelled')) reasons.push('يوجد تشغيل ملغي الاعتماد');
  if (operationStatuses.has('Draft')) reasons.push('توجد مسودة تحتاج إجراء');
  if (line.journeyStages === 0 || line.readinessStatus === 'JourneyNotConfigured') reasons.push('مسار الموديل غير مهيأ');
  if (line.requiredWorkers > line.presentPermanentlyAssignedWorkers) {
    reasons.push(`نقص ${line.requiredWorkers - line.presentPermanentlyAssignedWorkers} من العمال الحاضرين المسكنين`);
  }
  if (line.stagesWithoutPresentWorker > 0) reasons.push(`${line.stagesWithoutPresentWorker} مرحلة بلا عامل حاضر`);
  if (line.readinessStatus === 'DataIncomplete'
    || line.operations.some(operation => operation.stages.some(stage => !stage.hasPrice || !stage.hasStandardTime))) {
    reasons.push('بيانات المراحل غير مكتملة');
  }

  return [...new Set([...reasons, ...line.alerts])];
}

export function commandCenterLineProblemSeverity(line: CommandCenterLine): number {
  const operationStatuses = new Set(line.operations.map(operation => operation.status));
  let severity = 0;
  if (operationStatuses.has('Cancelled')) severity = Math.max(severity, 700);
  if (operationStatuses.has('ApprovalCancelled')) severity = Math.max(severity, 650);
  if (line.operations.length === 0) severity = Math.max(severity, 600);
  if (line.journeyStages === 0 || line.readinessStatus === 'JourneyNotConfigured') severity = Math.max(severity, 550);
  if (line.readinessStatus === 'DataIncomplete') severity = Math.max(severity, 500);
  if (line.requiredWorkers > line.presentPermanentlyAssignedWorkers || line.stagesWithoutPresentWorker > 0) {
    severity = Math.max(severity, 400 + Math.min(99, line.stagesWithoutPresentWorker));
  }
  if (operationStatuses.has('Draft')) severity = Math.max(severity, 300);
  if (line.alerts.length) severity = Math.max(severity, 200);
  return severity;
}

export function commandCenterProblemLines(data: ManufacturingCommandCenter): CommandCenterProblemLine[] {
  const problems: CommandCenterProblemLine[] = [];
  for (const factory of data.factories) {
    for (const department of factory.departments) {
      for (const line of department.lines) {
        const reasons = commandCenterLineProblemReasons(line);
        if (!reasons.length) continue;
        problems.push({ factoryName: factory.name, departmentName: department.name, line, reasons, severity: commandCenterLineProblemSeverity(line) });
      }
    }
  }
  return problems.sort((first, second) => second.severity - first.severity || first.line.name.localeCompare(second.line.name, 'ar'));
}

function operationStatusTone(status: Exclude<CommandCenterOperationStatus, 'All' | 'None'>): CommandCenterStatusTone {
  if (status === 'Approved') return 'success';
  if (status === 'Cancelled') return 'danger';
  return 'warning';
}

function operationStatusSeverity(status: Exclude<CommandCenterOperationStatus, 'All' | 'None'>): number {
  return ({ Cancelled: 4, ApprovalCancelled: 3, Draft: 2, Approved: 1 } as const)[status];
}

function strongerTone(first: CommandCenterStatusTone, second: CommandCenterStatusTone): CommandCenterStatusTone {
  const weight: Record<CommandCenterStatusTone, number> = { neutral: 0, success: 1, warning: 2, danger: 3 };
  return weight[second] > weight[first] ? second : first;
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
