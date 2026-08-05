import { Component, isDevMode, OnDestroy, OnInit, Optional } from '@angular/core';
import { MessageService } from 'primeng/api';
import { Subject, TimeoutError, finalize, forkJoin, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { AttendanceApiService, AttendanceSyncResult } from '../../core/services/attendance-api.service';
import {
  DailyProductionDraft,
  DailyProductionDraftUpdateInput,
  DailyProductionOperations,
  DailyProductionPreview,
  DailyProductionStage,
  DailyProductionStagePreview,
  DailyProductionStageInput,
  DailyProductionWorker,
  DailyProductionWorkerInput,
  DailyStageApprovalInput,
  ProductionWorkerAllocation,
  ProductionCostRecordingApiService
} from '../../core/services/production-cost-recording-api.service';
import { DepartmentItem, FactoryItem, ManufacturingMasterDataApiService, ModelStageItem, ProductModelItem, ProductionLineOption } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingDataChanged } from '../../core/models/realtime-notification.models';
import { generateUuidV4 } from '../../core/utils/uuid-v4';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';
import { productionDisplayLabel } from '../../shared/product/production-display-labels';
import { buildFactoryStructureTree, FactoryStructureTreeNode } from './factory-structure-tree.adapter';
import { ManufacturingFilterOption } from './manufacturing-filter-card.component';
import { ExcelExportError, ExcelExportService, ExcelWorkbookDefinition } from '../../shared/utils/excel-export.service';

type StageFilter = 'all' | 'ready' | 'absent' | 'no-check-in' | 'no-staffing' | 'cost-review';
type UnifiedPreviewStatus = 'idle' | 'calculating' | 'success' | 'error' | 'stale';
type UnifiedPreviewSource = 'calculated' | 'persisted';
type EditableDailyStage = Omit<DailyProductionStage, 'workers'> & {
  standardSeconds: number | null;
  workers: EditableDailyWorker[];
};
type EditableDailyWorker = DailyProductionWorker & {
  includedInProduction: boolean;
  percentage: number | null;
  quantity: number | null;
  fixedAmount: number | null;
  notes: string;
  manualOverrideReason: string;
};

interface StageWorkerProjection {
  workerId: string;
  workerCode: string;
  workerName: string;
  participationType: string;
  attendance: string;
  readiness: string;
  contributionStartsAtUtc: string | null;
  contributionEndsAtUtc: string | null;
  workerMinutes: number;
  percentage: number | null;
  allocatedQuantity: number;
  calculatedEarning: number;
  notes: string;
  manualOverrideReason: string;
  exclusionReason: string | null;
  isCalculated: boolean;
}

interface StageAllocationProjection {
  stageId: string;
  stageCode: string;
  stageName: string;
  stageOrder: number;
  stageQuantity: number;
  piecePrice: number;
  standardSeconds: number | null;
  totalStandardSeconds: number | null;
  totalStandardMinutes: number | null;
  stageValue: number;
  participantCount: number;
  distribution: string;
  totalEntitlement: number;
  status: string;
  statusTone: string;
  warnings: readonly string[];
  workers: StageWorkerProjection[];
}

interface WorkerStageProjection {
  stageId: string;
  stageCode: string;
  stageName: string;
  stageOrder: number;
  stageQuantity: number;
  allocatedQuantity: number;
  percentage: number | null;
  piecePrice: number;
  standardSeconds: number | null;
  workerStandardSeconds: number | null;
  workerStandardMinutes: number | null;
  workerMinutes: number;
  calculatedEarning: number;
  notes: string;
  manualOverrideReason: string;
  distribution: string;
  participationType: string;
  readiness: string;
  status: string;
}

interface WorkerAllocationProjection {
  workerId: string;
  workerCode: string;
  workerName: string;
  contributionStartsAtUtc: string | null;
  contributionEndsAtUtc: string | null;
  workerMinutes: number;
  participationType: string;
  stageCount: number;
  visibleStageNames: string[];
  hiddenStageCount: number;
  totalAllocatedQuantity: number;
  totalEntitlement: number;
  totalStandardSeconds: number | null;
  stages: WorkerStageProjection[];
}

@Component({
  selector: 'app-daily-production-operations-page',
  templateUrl: './daily-production-operations-page.component.html',
  styleUrls: ['./daily-production-operations-page.component.scss']
})
export class DailyProductionOperationsPageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;

  factories: FactoryItem[] = [];
  departments: DepartmentItem[] = [];
  productionLines: ProductionLineOption[] = [];
  dailyStructureTreeNodes: FactoryStructureTreeNode[] = [];
  selectedDailyStructureNode: FactoryStructureTreeNode | null = null;
  productModels: ProductModelItem[] = [];
  modelStageMetadata: ModelStageItem[] = [];
  operations: DailyProductionOperations | null = null;
  stages: EditableDailyStage[] = [];
  savedDraft: DailyProductionDraft | null = null;
  approving = false;
  cancelling = false;
  dailyApprovalCancellationDialogVisible = false;
  dailyApprovalCancellationReason = '';
  stageAllocationRows: StageAllocationProjection[] = [];
  workerAllocationRows: WorkerAllocationProjection[] = [];
  expandedStageRows: Record<string, boolean> = {};
  expandedWorkerRows: Record<string, boolean> = {};
  renderLegacyDailyProductionTables = false;

  private previewValue: DailyProductionPreview | null = null;
  private previewSource: UnifiedPreviewSource | null = null;

  get preview(): DailyProductionPreview | null {
    return this.previewValue;
  }

  set preview(value: DailyProductionPreview | null) {
    this.previewValue = value;
    this.rebuildAllocationProjection();
  }

  productionDate = this.egyptToday();
  selectedFactoryId = '';
  selectedProductionLineId = '';
  selectedProductModelId = '';
  selectedStageId = '';
  selectedStageFilterId = '';
  lineQuantity: number | null = null;
  notes = '';
  stageFilter: StageFilter = 'all';
  stageSearch = '';
  replacementWorkerId = '';
  previewStatus: UnifiedPreviewStatus = 'idle';
  exportingExcel = false;

  factoriesLoading = false;
  linesLoading = false;
  modelsLoading = false;
  operationsLoading = false;
  attendanceSyncing = false;
  previewing = false;
  saving = false;
  attendanceSyncedForDate = '';
  error = '';
  successMessage = '';
  validationMessages: string[] = [];
  hasPendingRemoteUpdate = false;
  remoteUpdateMessage = '';

  private revision = 0;
  private previewRevision = -1;
  private clientRequestId = generateUuidV4();
  private operationsRequestVersion = 0;
  private stopRealtime?: () => void;
  private hasUnsavedChanges = false;
  private realtimeRefreshQueued = false;
  private readonly destroy$ = new Subject<void>();
  readonly dailyStageFilterOptions: readonly ManufacturingFilterOption[] = [
    { label: 'كل الحالات', value: 'all' },
    { label: 'جاهزة', value: 'ready' },
    { label: 'عامل غائب', value: 'absent' },
    { label: 'دون بصمة', value: 'no-check-in' },
    { label: 'دون تسكين', value: 'no-staffing' },
    { label: 'مراجعة تكلفة', value: 'cost-review' }
  ];

  constructor(
    private readonly masterData: ManufacturingMasterDataApiService,
    private readonly attendance: AttendanceApiService,
    private readonly production: ProductionCostRecordingApiService,
    private readonly permissionsService: PermissionService,
    private readonly formValidation: FormSubmissionValidationService,
    @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService,
    @Optional() private readonly excelExport?: ExcelExportService,
    @Optional() private readonly messageService?: MessageService
  ) {}

  ngOnInit(): void {
    this.subscribeToDailyOperationsRealtime();
    this.loadFactories();
  }

  ngOnDestroy(): void {
    this.stopRealtime?.();
    this.destroy$.next();
    this.destroy$.complete();
  }

  reloadFromRemoteUpdate(): void {
    this.hasUnsavedChanges = false;
    this.clearRemoteUpdateNotice();
    this.loadTodayOperations({
      kind: 'success',
      message: 'تمت إعادة تحميل تشغيل اليوم بأحدث تغييرات المستخدمين الآخرين.'
    });
  }

  get canView(): boolean {
    return this.permissionsService.hasPermission(this.permissions.production.view) ||
      this.permissionsService.hasPermission(this.permissions.production.record) ||
      this.permissionsService.hasPermission(this.permissions.production.approve);
  }

  get isApproved(): boolean {
    const draft = this.currentDailyDraft;
    return !!draft && draft.stages.length > 0 && draft.stages.every(stage => stage.status === 'Approved');
  }

  get canEditDraft(): boolean {
    return this.permissionsService.hasPermission(this.permissions.production.record) &&
      !this.isApproved &&
      !this.operationsLoading &&
      !this.saving &&
      !this.approving &&
      !this.cancelling;
  }

  get isReadOnly(): boolean { return !this.canEditDraft; }

  get canOverrideParticipants(): boolean {
    return this.canEditDraft && this.permissionsService.hasPermission(this.permissions.assignments.manage);
  }

  get visibleProductionLines(): ProductionLineOption[] {
    return this.productionLines.filter(line => line.isActive && line.factoryId === this.selectedFactoryId);
  }

  get activeProductModels(): ProductModelItem[] {
    return this.productModels.filter(model => model.isActive);
  }

  get canLoadOperations(): boolean {
    return Boolean(
      this.productionDate &&
      this.selectedFactoryId &&
      this.selectedProductionLineId &&
      this.selectedProductModelId &&
      this.attendanceSyncedForDate === this.productionDate &&
      !this.operationsLoading
    );
  }

  get dailyFiltersActive(): boolean { return !!this.selectedDailyStructureNode || !!this.selectedProductModelId || !!this.selectedStageFilterId || this.stageFilter !== 'all' || !!this.stageSearch.trim(); }

  get dailyStageOptions(): EditableDailyStage[] {
    return [...this.stages].sort((left, right) =>
      left.stageOrder - right.stageOrder ||
      left.stageName.localeCompare(right.stageName, 'ar') ||
      left.productModelStageId.localeCompare(right.productModelStageId)
    );
  }

  get selectedStage(): EditableDailyStage | null {
    return this.stages.find(stage => stage.productModelStageId === this.selectedStageId) ?? null;
  }

  get filteredStages(): EditableDailyStage[] {
    const search = this.stageSearch.trim().toLocaleLowerCase('ar');
    return this.stages.filter(stage => {
      if (this.selectedStageFilterId && stage.productModelStageId !== this.selectedStageFilterId) return false;
      const matchesSearch = !search || `${stage.stageCode} ${stage.stageName} ${stage.mainStageName}`.toLocaleLowerCase('ar').includes(search);
      if (!matchesSearch) return false;
      if (this.stageFilter === 'ready') return stage.isReady;
      if (this.stageFilter === 'absent') return stage.hasAbsentWorkers;
      if (this.stageFilter === 'no-check-in') return stage.hasNoSourceCheckInWorkers;
      if (this.stageFilter === 'no-staffing') return stage.staffingStatus === 'NoStaffing';
      if (this.stageFilter === 'cost-review') return stage.isFinancialReviewPending;
      return true;
    });
  }

  get availableReplacementWorkers(): DailyProductionWorker[] {
    const stage = this.selectedStage;
    if (!stage || !this.operations) return [];
    const existing = new Set(stage.workers.map(worker => worker.workerId));
    return this.operations.activeWorkers
      .filter(worker => worker.isProductionReady && !existing.has(worker.workerId))
      .sort((left, right) => `${left.workerCode}|${left.workerId}`.localeCompare(`${right.workerCode}|${right.workerId}`));
  }

  get isPreviewCurrent(): boolean {
    return this.previewStatus === 'success' && this.previewSource === 'calculated' && !!this.preview && this.previewRevision === this.revision;
  }

  get canExportExcel(): boolean {
    return this.previewStatus === 'success' && !!this.preview && this.preview.stages.length > 0 && this.stageAllocationRows.length > 0 && !this.validationMessages.length;
  }

  get previewStatusLabel(): string {
    if (this.previewStatus === 'calculating') return 'جارٍ الحساب';
    if (this.previewStatus === 'success') return this.previewSource === 'persisted' ? 'نتيجة محفوظة' : 'صالحة للحفظ والتصدير';
    if (this.previewStatus === 'error') return 'تعذر الحساب';
    if (this.previewStatus === 'stale') return 'قديمة — أعد الحساب';
    return 'لم تُحسب بعد';
  }

  get hasExistingDraft(): boolean {
    return !!this.currentDailyDraft;
  }

  get isDailyOperationApproved(): boolean { return this.isApproved; }
  get isDailyOperationReadOnly(): boolean { return this.isReadOnly; }

  get canApproveDailyOperation(): boolean {
    const draft = this.currentDailyDraft;
    return this.permissionsService.hasPermission(this.permissions.production.approve) &&
      !!draft &&
      draft.stages.length > 0 &&
      draft.stages.every(stage => stage.status === 'Draft') &&
      !this.operationsLoading &&
      !this.saving &&
      !this.approving &&
      !this.cancelling;
  }

  get canCancelDailyOperationApproval(): boolean {
    return this.permissionsService.hasPermission(this.permissions.production.approve) &&
      this.isApproved &&
      !this.cancelling &&
      this.hasApprovalConcurrencyData;
  }

  get isDailyApprovalCancelled(): boolean {
    const draft = this.currentDailyDraft;
    return !!draft && draft.stages.some(stage => stage.status === 'Cancelled');
  }

  get totalEnteredWorkers(): number {
    return this.stages.reduce((total, stage) => total + stage.workers.filter(worker => worker.includedInProduction !== false).length, 0);
  }

  get firstLoadPending(): boolean {
    return this.operationsLoading && !this.operations;
  }

  selectFactory(factoryId: string): void {
    if (factoryId === this.selectedFactoryId) return;
    this.selectedFactoryId = factoryId;
    this.selectedProductionLineId = '';
    this.selectedProductModelId = '';
    this.productModels = [];
    this.resetOperations();
    if (!factoryId) this.selectedDailyStructureNode = null;
  }

  selectProductionLine(lineId: string): void {
    if (lineId === this.selectedProductionLineId) return;
    this.selectedProductionLineId = lineId;
    this.selectedProductModelId = '';
    this.productModels = [];
    this.resetOperations();
    if (!lineId) return;

    this.modelsLoading = true;
    this.masterData.models()
      .pipe(finalize(() => this.modelsLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: models => this.productModels = models,
        error: error => this.error = this.formValidation.serverMessage(error, 'تعذر تحميل الموديلات.')
      });
  }

  selectProductModel(modelId: string): void {
    if (modelId === this.selectedProductModelId) return;
    this.selectedProductModelId = modelId;
    this.resetOperations();
  }

  selectDailyStructure(node: FactoryStructureTreeNode): void {
    const data = node.data;
    if (!data) return;
    this.selectedDailyStructureNode = node;
    if (data.entityType === 'factory') { this.selectFactory(data.entityId); return; }
    if (data.entityType === 'department') {
      const factoryId = data.parentId ?? '';
      if (factoryId !== this.selectedFactoryId) this.selectFactory(factoryId);
      this.selectedProductionLineId = '';
      this.productModels = [];
      return;
    }
    const line = data.source as ProductionLineOption;
    if (line.factoryId !== this.selectedFactoryId) this.selectFactory(line.factoryId);
    this.selectProductionLine(line.id);
  }

  clearDailyFilters(): void {
    if (this.operations) {
      this.selectedStageFilterId = '';
      this.stageFilter = 'all';
      this.stageSearch = '';
      return;
    }
    this.selectedDailyStructureNode = null;
    this.selectedStageFilterId = '';
    this.stageFilter = 'all';
    this.stageSearch = '';
    this.selectFactory('');
  }

  changeProductionDate(date: string): void {
    if (date === this.productionDate) return;
    this.productionDate = date;
    this.attendanceSyncedForDate = '';
    this.resetOperations();
  }

  synchronizeAttendance(): void {
    if (!this.productionDate || this.attendanceSyncing) return;
    this.error = '';
    this.successMessage = '';
    this.attendanceSyncing = true;
    this.attendance.syncForProductionDate(this.productionDate)
      .pipe(finalize(() => this.attendanceSyncing = false), takeUntil(this.destroy$))
      .subscribe({
        next: result => this.onAttendanceSynchronized(result),
        error: error => this.error = this.attendanceSyncFailureMessage(error)
      });
  }

  loadTodayOperations(feedback?: { kind: 'success' | 'error'; message: string }): void {
    if (!this.canLoadOperations) {
      this.error = this.attendanceSyncedForDate !== this.productionDate
        ? 'نفّذ مزامنة الحضور يدويًا لتاريخ الإنتاج المحدد قبل تحميل تشغيل اليوم.'
        : 'اختر المصنع وخط الإنتاج والموديل أولًا.';
      return;
    }

    const version = ++this.operationsRequestVersion;
    const selectedStageId = this.selectedStageId;
    const selectedStageFilterId = this.selectedStageFilterId;
    this.operationsLoading = true;
    this.error = '';
    this.successMessage = '';
    forkJoin({
      operations: this.production.loadDailyOperations(
        this.selectedFactoryId,
        this.selectedProductionLineId,
        this.selectedProductModelId,
        this.productionDate
      ),
      modelStages: this.masterData.modelStages(this.selectedProductModelId, this.selectedProductionLineId)
    })
      .pipe(finalize(() => {
        if (version === this.operationsRequestVersion) {
          this.operationsLoading = false;
          this.flushQueuedRealtimeRefresh();
        }
      }), takeUntil(this.destroy$))
      .subscribe({
        next: ({ operations, modelStages }) => {
          if (version !== this.operationsRequestVersion) return;
          this.operations = operations;
          this.modelStageMetadata = modelStages;
          const metadataById = new Map(modelStages.map(stage => [stage.id, stage]));
          this.stages = operations.stages.map(stage => this.toEditableStage(stage, metadataById.get(stage.productModelStageId)));
          this.selectedStageFilterId = this.stages.some(stage => stage.productModelStageId === selectedStageFilterId)
            ? selectedStageFilterId
            : '';
          this.selectedStageId = this.stages.some(stage => stage.productModelStageId === selectedStageId)
            ? selectedStageId
            : this.selectedStageFilterId || this.stages[0]?.productModelStageId || '';
          this.replacementWorkerId = '';
          this.invalidatePreview(false, 'idle');
          this.hasUnsavedChanges = false;
          this.clearRemoteUpdateNotice();
          if (operations.existingDraft) {
            this.applyPersistedDraftState(operations.existingDraft);
            this.successMessage = feedback?.kind === 'success'
              ? feedback.message
              : 'تم تحميل مسودة اليوم المحفوظة فوق لقطة التسكين الحالية دون إعادة بنائها أو الكتابة فوقها.';
          }
          if (feedback?.kind === 'error') {
            this.error = feedback.message;
          }
        },
        error: error => {
          if (version !== this.operationsRequestVersion) return;
          this.error = this.formValidation.serverMessage(error, 'تعذر تحميل مراحل تشغيل اليوم.');
        }
      });
  }

  selectStage(stageId: string): void {
    this.selectedStageId = stageId;
    this.replacementWorkerId = '';
  }

  selectDailyStageFilter(stageId: string | null | undefined): void {
    const normalizedStageId = stageId?.trim() ?? '';
    this.selectedStageFilterId = normalizedStageId;

    if (!normalizedStageId) {
      if (this.selectedStageId && !this.filteredStages.some(stage => stage.productModelStageId === this.selectedStageId)) {
        this.selectedStageId = '';
        this.expandedStageRows = {};
      }
      if (this.error === 'لا توجد مراحل مطابقة لفلتر المرحلة الحالي.') this.error = '';
      return;
    }

    const firstVisibleStage = this.filteredStages[0];
    if (!firstVisibleStage) {
      this.selectedStageId = '';
      this.expandedStageRows = {};
      this.error = 'لا توجد مراحل مطابقة لفلتر المرحلة الحالي.';
      return;
    }

    this.error = '';
    this.selectedStageId = firstVisibleStage.productModelStageId;
    const expandableRow = this.stageAllocationRows.find(row => row.stageId === firstVisibleStage.productModelStageId);
    this.expandedStageRows = expandableRow?.workers.length
      ? { [firstVisibleStage.productModelStageId]: true }
      : {};
    this.scrollToSelectedStage(firstVisibleStage.productModelStageId);
  }

  addReplacementWorker(): void {
    const stage = this.selectedStage;
    const worker = this.availableReplacementWorkers.find(candidate => candidate.workerId === this.replacementWorkerId);
    if (!stage || !worker || !this.canOverrideParticipants) return;
    stage.workers = [...stage.workers, this.toEditableWorker({ ...worker, isAssignedWorker: false, isDailyOverride: true }, true)];
    if (stage.compensationMode === 'SharedPercentage') this.applyEqualDistribution(stage, false);
    this.replacementWorkerId = '';
    this.stageChanged();
  }

  removeWorker(stage: EditableDailyStage, workerId: string): void {
    if (!this.canOverrideParticipants) return;
    const worker = stage.workers.find(candidate => candidate.workerId === workerId);
    if (!worker) return;
    if (worker.isAssignedWorker !== false && !worker.isDailyOverride) worker.includedInProduction = false;
    else stage.workers = stage.workers.filter(candidate => candidate.workerId !== workerId);
    if (stage.compensationMode === 'SharedPercentage') this.applyEqualDistribution(stage, false);
    this.stageChanged();
  }

  restoreWorker(stage: EditableDailyStage, worker: EditableDailyWorker): void {
    if (!this.canOverrideParticipants || !worker.isProductionReady) return;
    worker.includedInProduction = true;
    if (stage.compensationMode === 'SharedPercentage') this.applyEqualDistribution(stage, false);
    this.stageChanged();
  }

  applyEqualDistribution(stage: EditableDailyStage, markChanged = true): void {
    if (!this.canEditDraft || stage.compensationMode !== 'SharedPercentage') return;
    const participants = stage.workers
      .filter(worker => worker.includedInProduction !== false && worker.isProductionReady && worker.workerMinutes > 0)
      .sort((left, right) => left.workerId.localeCompare(right.workerId));
    const percentageUnits = 1_000_000;
    const equalUnits = participants.length ? Math.floor(percentageUnits / participants.length) : 0;
    const remainingUnits = percentageUnits - equalUnits * participants.length;
    const shares = participants.map((worker, index) => ({ worker, units: equalUnits + (index < remainingUnits ? 1 : 0) }));

    stage.workers.forEach(worker => worker.percentage = null);
    shares.forEach(share => share.worker.percentage = share.units / 10_000);
    this.synchronizeStageQuantities(stage);
    if (markChanged) this.stageChanged();
  }

  updateWorkerPercentage(stage: EditableDailyStage, worker: EditableDailyWorker, value: number | null): void {
    if (!this.canEditDraft) return;
    worker.percentage = this.numericValue(value);
    worker.quantity = this.isValidLineQuantity() && worker.percentage !== null
      ? this.roundQuantity(this.lineQuantity! * worker.percentage / 100)
      : null;
    this.reconcileQuantityRounding(stage, worker);
    this.stageChanged();
  }

  updateWorkerQuantity(stage: EditableDailyStage, worker: EditableDailyWorker, value: number | null): void {
    if (!this.canEditDraft) return;
    worker.quantity = this.numericValue(value);
    worker.percentage = this.isValidLineQuantity() && worker.quantity !== null
      ? this.roundPercentage(worker.quantity / this.lineQuantity! * 100)
      : null;
    this.reconcilePercentageRounding(stage, worker);
    this.stageChanged();
  }

  stageChanged(): void {
    if (!this.canEditDraft) return;
    this.hasUnsavedChanges = true;
    this.error = '';
    this.validationMessages = [];
    this.invalidatePreview();
  }

  lineQuantityChanged(): void {
    if (!this.canEditDraft) return;
    this.stages.forEach(stage => this.synchronizeStageQuantities(stage));
    this.stageChanged();
  }

  calculatePreview(): void {
    if (!this.canEditDraft) return;
    const validation = this.validateOperation();
    this.validationMessages = validation;
    if (validation.length || this.previewing) {
      if (validation.length) this.previewStatus = 'error';
      return;
    }

    const revision = this.revision;
    this.error = '';
    this.previewing = true;
    this.previewStatus = 'calculating';
    this.production.previewDailyOperations(this.operationRequest(null))
      .pipe(finalize(() => this.previewing = false), takeUntil(this.destroy$))
      .subscribe({
        next: preview => {
          if (revision !== this.revision) return;
          this.previewSource = 'calculated';
          this.preview = preview;
          this.previewRevision = revision;
          this.previewStatus = 'success';
          this.successMessage = 'تم احتساب معاينة موحّدة لكل المراحل والعمال. يمكنك الآن مراجعة المستحقات قبل حفظ المسودة.';
        },
        error: error => {
          this.previewStatus = 'error';
          this.error = error instanceof TimeoutError
            ? 'استغرق احتساب معاينة تشغيل اليوم وقتًا أطول من المسموح. بقيت بياناتك كما هي؛ أعد المحاولة.'
            : this.formValidation.serverMessage(error, 'تعذر احتساب معاينة تشغيل اليوم.');
        }
      });
  }

  dailyProductionExcelWorkbook(): ExcelWorkbookDefinition | null {
    const preview = this.preview;
    const operations = this.operations;
    if (!preview || this.previewStatus !== 'success' || !this.stageAllocationRows.length || this.validationMessages.length || !operations) return null;

    const line = this.productionLines.find(item => item.id === operations.productionLineId);
    const department = this.departments.find(item => item.id === line?.departmentId);
    const factory = this.factories.find(item => item.id === operations.factoryId);
    const draft = this.currentDailyDraft;
    const recordsByStage = new Map((draft?.stages ?? []).map(record => [record.productModelStageId, record]));
    const status = this.dailyOperationExportStatus;
    const productionDate = this.toExcelDate(preview.productionDate);
    const exportedAt = new Date();
    const draftCreatedAt = this.toExcelDate(draft?.recordedAtUtc);
    const approvedRecord = draft?.stages.find(stage => !!stage.approvedAtUtc);
    const approvedAt = this.toExcelDate(approvedRecord?.approvedAtUtc);
    const uniqueWorkerCount = new Set(this.workerAllocationRows.map(worker => worker.workerId)).size;
    const stageRows = [...this.stageAllocationRows].sort((left, right) => left.stageOrder - right.stageOrder || left.stageCode.localeCompare(right.stageCode));
    const detailRows = stageRows.flatMap(stage => stage.workers
      .filter(worker => worker.isCalculated)
      .map(worker => {
        const record = recordsByStage.get(stage.stageId);
        const workerStandardSeconds = stage.standardSeconds === null
          ? null
          : this.roundQuantity(worker.allocatedQuantity * stage.standardSeconds);
        return {
          'تاريخ الإنتاج': productionDate,
          'حالة التشغيل': status,
          'المصنع': factory?.name ?? operations.factoryName,
          'القسم': department?.nameAr ?? department?.name ?? line?.departmentNameAr ?? '',
          'كود خط الإنتاج': line?.lineCode ?? '',
          'اسم خط الإنتاج': line?.name ?? operations.productionLineName,
          'كود الموديل': operations.productModelCode,
          'اسم الموديل': operations.productModelName,
          'ترتيب المرحلة': stage.stageOrder,
          'كود المرحلة': stage.stageCode,
          'اسم المرحلة': stage.stageName,
          'رقم العامل / Badge Number': worker.workerCode,
          'اسم العامل': worker.workerName,
          'نسبة العامل في المرحلة': worker.percentage,
          'كمية المرحلة الإجمالية': stage.stageQuantity,
          'كمية العامل في المرحلة': worker.allocatedQuantity,
          'سعر القطعة': stage.piecePrice,
          'قيمة إنتاج العامل': worker.calculatedEarning,
          'الزمن القياسي للقطعة بالثواني': stage.standardSeconds,
          'إجمالي الزمن القياسي للعامل بالثواني': workerStandardSeconds,
          'إجمالي الزمن بالدقائق': workerStandardSeconds === null ? null : this.roundQuantity(workerStandardSeconds / 60),
          'نوع التوزيع': stage.distribution,
          'حالة السجل': this.exportRecordStatus(record?.status),
          'الملاحظات أو سبب Override': [worker.notes, worker.manualOverrideReason, record?.notes ?? ''].filter(Boolean).join(' — '),
          'وقت إنشاء المسودة': draftCreatedAt,
          'وقت الاعتماد': approvedAt,
          'منشئ المسودة': '',
          'معتمد التشغيل': ''
        };
      }));
    const totalStageQuantity = this.roundQuantity(stageRows.reduce((total, stage) => total + stage.stageQuantity, 0));
    const totalWorkerQuantity = this.roundQuantity(detailRows.reduce((total, row) => total + Number(row['كمية العامل في المرحلة'] ?? 0), 0));
    const totalValue = this.roundMoney(detailRows.reduce((total, row) => total + Number(row['قيمة إنتاج العامل'] ?? 0), 0));
    const hasCompleteStandardTime = detailRows.every(row => row['إجمالي الزمن القياسي للعامل بالثواني'] !== null);
    const totalStandardSeconds = hasCompleteStandardTime
      ? this.roundQuantity(detailRows.reduce((total, row) => total + Number(row['إجمالي الزمن القياسي للعامل بالثواني'] ?? 0), 0))
      : null;
    const detailTotalRow = {
      'اسم المرحلة': 'الإجمالي',
      'كمية المرحلة الإجمالية': totalStageQuantity,
      'كمية العامل في المرحلة': totalWorkerQuantity,
      'قيمة إنتاج العامل': totalValue,
      'إجمالي الزمن القياسي للعامل بالثواني': totalStandardSeconds,
      'إجمالي الزمن بالدقائق': totalStandardSeconds === null ? null : this.roundQuantity(totalStandardSeconds / 60)
    };
    const stageSummaryRows = stageRows.map(stage => ({
      'تاريخ الإنتاج': productionDate,
      'القسم': department?.nameAr ?? department?.name ?? line?.departmentNameAr ?? '',
      'الخط': line ? `${line.lineCode ?? ''} — ${line.name}`.replace(/^\s*—\s*|\s*—\s*$/g, '') : operations.productionLineName,
      'الموديل': `${operations.productModelCode} — ${operations.productModelName}`,
      'ترتيب المرحلة': stage.stageOrder,
      'كود المرحلة': stage.stageCode,
      'اسم المرحلة': stage.stageName,
      'كمية المرحلة': stage.stageQuantity,
      'سعر القطعة': stage.piecePrice,
      'إجمالي قيمة المرحلة': stage.stageValue,
      'الزمن القياسي للقطعة': stage.standardSeconds,
      'إجمالي الزمن': stage.totalStandardSeconds,
      'عدد العمال': stage.participantCount
    }));
    const stageSummaryTotalRow = {
      'اسم المرحلة': 'الإجمالي',
      'كمية المرحلة': totalStageQuantity,
      'إجمالي قيمة المرحلة': this.roundMoney(stageRows.reduce((total, stage) => total + stage.stageValue, 0)),
      'إجمالي الزمن': stageRows.every(stage => stage.totalStandardSeconds !== null)
        ? this.roundQuantity(stageRows.reduce((total, stage) => total + (stage.totalStandardSeconds ?? 0), 0))
        : null,
      'عدد العمال': stageRows.reduce((total, stage) => total + stage.participantCount, 0)
    };
    const workerSummaryRows = [...this.workerAllocationRows]
      .sort((left, right) => left.workerCode.localeCompare(right.workerCode))
      .map(worker => ({
        'رقم العامل': worker.workerCode,
        'اسم العامل': worker.workerName,
        'عدد المراحل': worker.stageCount,
        'إجمالي كمية العامل': worker.totalAllocatedQuantity,
        'إجمالي قيمة إنتاج العامل': worker.totalEntitlement,
        'إجمالي الزمن القياسي': worker.totalStandardSeconds,
        'أسماء المراحل': worker.stages.sort((left, right) => left.stageOrder - right.stageOrder).map(stage => stage.stageName).join('، ')
      }));
    const workerSummaryTotalRow = {
      'اسم العامل': 'الإجمالي',
      'عدد المراحل': workerSummaryRows.reduce((total, row) => total + Number(row['عدد المراحل']), 0),
      'إجمالي كمية العامل': this.roundQuantity(workerSummaryRows.reduce((total, row) => total + Number(row['إجمالي كمية العامل']), 0)),
      'إجمالي قيمة إنتاج العامل': this.roundMoney(workerSummaryRows.reduce((total, row) => total + Number(row['إجمالي قيمة إنتاج العامل']), 0)),
      'إجمالي الزمن القياسي': workerSummaryRows.every(row => row['إجمالي الزمن القياسي'] !== null)
        ? this.roundQuantity(workerSummaryRows.reduce((total, row) => total + Number(row['إجمالي الزمن القياسي'] ?? 0), 0))
        : null
    };

    return {
      fileName: `Production-Daily_${preview.productionDate}_${line?.lineCode || operations.productionLineName}_${operations.productModelCode}_${status}`,
      worksheets: [
        {
          name: 'تفاصيل الإنتاج',
          rows: [...detailRows, detailTotalRow],
          columnWidths: [15, 16, 24, 24, 18, 26, 16, 26, 14, 18, 30, 22, 28, 18, 20, 20, 16, 20, 24, 28, 20, 20, 18, 38, 22, 22, 22, 22],
          columnFormats: {
            'تاريخ الإنتاج': 'yyyy-mm-dd',
            'نسبة العامل في المرحلة': '0.00"%"',
            'كمية المرحلة الإجمالية': '#,##0.####',
            'كمية العامل في المرحلة': '#,##0.####',
            'سعر القطعة': '#,##0.0000',
            'قيمة إنتاج العامل': '#,##0.0000',
            'الزمن القياسي للقطعة بالثواني': '#,##0.####',
            'إجمالي الزمن القياسي للعامل بالثواني': '#,##0.####',
            'إجمالي الزمن بالدقائق': '#,##0.####',
            'وقت إنشاء المسودة': 'yyyy-mm-dd hh:mm',
            'وقت الاعتماد': 'yyyy-mm-dd hh:mm'
          },
          footerRowCount: 1
        },
        {
          name: 'ملخص المراحل',
          rows: [...stageSummaryRows, stageSummaryTotalRow],
          columnWidths: [15, 24, 28, 28, 14, 18, 30, 18, 16, 22, 22, 20, 14],
          columnFormats: {
            'تاريخ الإنتاج': 'yyyy-mm-dd',
            'كمية المرحلة': '#,##0.####',
            'سعر القطعة': '#,##0.0000',
            'إجمالي قيمة المرحلة': '#,##0.0000',
            'الزمن القياسي للقطعة': '#,##0.####',
            'إجمالي الزمن': '#,##0.####'
          },
          footerRowCount: 1
        },
        {
          name: 'ملخص العمال',
          rows: [...workerSummaryRows, workerSummaryTotalRow],
          columnWidths: [18, 28, 14, 22, 24, 24, 42],
          columnFormats: {
            'إجمالي كمية العامل': '#,##0.####',
            'إجمالي قيمة إنتاج العامل': '#,##0.0000',
            'إجمالي الزمن القياسي': '#,##0.####'
          },
          footerRowCount: 1
        },
        {
          name: 'بيانات التشغيل',
          rows: [{
            'تاريخ الإنتاج': productionDate,
            'المصنع': factory ? `${factory.code} — ${factory.name}` : operations.factoryName,
            'القسم': department ? `${department.code ?? ''} — ${department.nameAr ?? department.name ?? ''}`.replace(/^\s*—\s*|\s*—\s*$/g, '') : line?.departmentNameAr ?? '',
            'الخط': line ? `${line.lineCode ?? ''} — ${line.name}`.replace(/^\s*—\s*|\s*—\s*$/g, '') : operations.productionLineName,
            'الموديل': `${operations.productModelCode} — ${operations.productModelName}`,
            'حالة التشغيل': status,
            'عدد المراحل': stageRows.length,
            'عدد العمال الفريدين': uniqueWorkerCount,
            'إجمالي كمية التشغيل': preview.lineQuantity,
            'إجمالي القيمة': totalValue,
            'وقت إنشاء الملف': exportedAt,
            'وقت إنشاء المسودة': draftCreatedAt,
            'وقت الاعتماد': approvedAt,
            'المستخدم المنشئ': '',
            'معتمد التشغيل': ''
          }],
          columnWidths: [15, 28, 28, 30, 30, 18, 14, 18, 22, 20, 22, 22, 22, 22, 22],
          columnFormats: {
            'تاريخ الإنتاج': 'yyyy-mm-dd',
            'إجمالي كمية التشغيل': '#,##0.####',
            'إجمالي القيمة': '#,##0.0000',
            'وقت إنشاء الملف': 'yyyy-mm-dd hh:mm',
            'وقت إنشاء المسودة': 'yyyy-mm-dd hh:mm',
            'وقت الاعتماد': 'yyyy-mm-dd hh:mm'
          }
        }
      ]
    };
  }

  async exportDailyProductionExcel(): Promise<void> {
    if (this.exportingExcel || !this.canExportExcel || !this.excelExport) return;
    const workbook = this.dailyProductionExcelWorkbook();
    if (!workbook) return;

    this.exportingExcel = true;
    this.error = '';
    try {
      await this.excelExport.exportWorkbook(workbook);
      this.successMessage = 'تم إنشاء ملف Excel بنجاح.';
      this.messageService?.add({ severity: 'success', summary: 'تم التصدير', detail: this.successMessage });
    } catch (error) {
      if (isDevMode()) {
        const step = error instanceof ExcelExportError ? error.step : 'unknown';
        console.error(`Excel export failed at ${step}.`, error);
      }
      this.error = 'تعذر إنشاء ملف Excel. لم تتغير بيانات تشغيل اليوم.';
      this.messageService?.add({ severity: 'error', summary: 'تعذر التصدير', detail: this.error });
    } finally {
      this.exportingExcel = false;
    }
  }

  saveDailyDraft(): void {
    if (!this.canEditDraft || !this.isPreviewCurrent || !this.preview || this.saving) {
      this.error = 'احسب معاينة حديثة أولًا؛ أي تغيير في المرحلة أو الكمية يجعل الحفظ غير صالح.';
      return;
    }
    const validation = this.validateOperation();
    this.validationMessages = validation;
    if (validation.length) return;

    this.saving = true;
    this.error = '';
    const correlationId = this.manufacturingRealtime?.registerLocalOperation('daily-production-operations');
    const existingDraft = this.currentDailyDraft;
    if (existingDraft?.productionOrderId && !this.hasDraftUpdateConcurrencyData(existingDraft)) {
      this.saving = false;
      this.error = 'بيانات تزامن المسودة غير مكتملة. أعد تحميل تشغيل اليوم قبل الحفظ.';
      return;
    }

    const saveRequest = existingDraft?.productionOrderId
      ? this.production.updateDailyDraft(
          existingDraft.productionOrderId,
          this.dailyDraftUpdateRequest(existingDraft, this.preview.previewToken),
          correlationId
        )
      : this.production.createDailyDraft(this.operationRequest(this.preview.previewToken), correlationId);
    saveRequest
      .pipe(finalize(() => this.saving = false), takeUntil(this.destroy$))
      .subscribe({
        next: draft => {
          this.applyPersistedDraftState(draft);
          this.hasUnsavedChanges = false;
          this.successMessage = draft.wasAlreadySaved
            ? 'هذه المسودة حُفظت مسبقًا بنفس طلب الحفظ؛ تم عرض النتيجة المحفوظة دون تكرار المراحل.'
            : 'تم حفظ مسودة تشغيل اليوم كاملة في معاملة واحدة؛ بقي تاريخ الإنتاج منفصلًا عن وقت التسجيل.';
        },
        error: error => this.error = this.formValidation.serverMessage(error, 'تعذر حفظ مسودة تشغيل اليوم.')
      });
  }

  approveDailyOperation(): void {
    const draft = this.currentDailyDraft;
    if (!draft || !this.canApproveDailyOperation) return;

    const context = this.operations;
    const message = [
      `سيتم اعتماد تشغيل يوم ${this.productionDateLabel(draft.productionDate)}.`,
      `المصنع والخط: ${context?.factoryName ?? '—'} / ${context?.productionLineName ?? '—'}.`,
      `عدد المراحل: ${draft.stages.length}.`,
      'سيتم تثبيت الكميات والتوزيعات، ولن يعود التشغيل قابلاً للتعديل.'
    ].join('\n');
    if (!window.confirm(message)) return;

    const stageApprovals: DailyStageApprovalInput[] = draft.stages.map(stage => ({
      stageProductionRecordId: stage.id,
      concurrencyToken: stage.concurrencyToken
    }));
    this.approving = true;
    this.error = '';
    this.successMessage = '';
    const correlationId = this.manufacturingRealtime?.registerLocalOperation('daily-production-operations');
    this.production.approveDailyOperation(draft.productionOrderId, stageApprovals, correlationId)
      .pipe(finalize(() => this.approving = false), takeUntil(this.destroy$))
      .subscribe({
        next: () => this.loadTodayOperations({
          kind: 'success',
          message: 'تم اعتماد تشغيل اليوم بنجاح. أصبحت بيانات التشغيل للقراءة فقط.'
        }),
        error: error => {
          if (error?.status === 409) {
            this.loadTodayOperations({
              kind: 'error',
              message: 'تغيرت حالة المسودة أو لم تعد صالحة للاعتماد. تم تحديث بيانات تشغيل اليوم.'
            });
            return;
          }
          this.error = error?.status === 403
            ? 'لا تملك صلاحية اعتماد تشغيل اليوم.'
            : this.formValidation.serverMessage(error, 'تعذر اعتماد تشغيل اليوم.');
        }
      });
  }

  openDailyApprovalCancellationDialog(): void {
    if (!this.canCancelDailyOperationApproval) return;
    this.dailyApprovalCancellationReason = '';
    this.dailyApprovalCancellationDialogVisible = true;
  }

  closeDailyApprovalCancellationDialog(): void {
    if (this.cancelling) return;
    this.dailyApprovalCancellationDialogVisible = false;
    this.dailyApprovalCancellationReason = '';
  }

  confirmDailyApprovalCancellation(): void {
    const draft = this.currentDailyDraft;
    const reason = this.dailyApprovalCancellationReason.trim();
    if (!draft || !this.canCancelDailyOperationApproval || !reason) {
      this.error = !reason
        ? 'سبب إلغاء اعتماد تشغيل اليوم مطلوب.'
        : 'لا يمكن إلغاء اعتماد تشغيل اليوم في حالته الحالية.';
      return;
    }

    const stageApprovals: DailyStageApprovalInput[] = draft.stages.map(stage => ({
      stageProductionRecordId: stage.id,
      concurrencyToken: stage.concurrencyToken
    }));
    this.cancelling = true;
    this.error = '';
    const correlationId = this.manufacturingRealtime?.registerLocalOperation('daily-production-operations');
    this.production.cancelDailyOperationApproval(draft.productionOrderId, stageApprovals, reason, correlationId)
      .pipe(finalize(() => this.cancelling = false), takeUntil(this.destroy$))
      .subscribe({
        next: draft => {
          this.dailyApprovalCancellationDialogVisible = false;
          this.dailyApprovalCancellationReason = '';
          this.applyPersistedDraftState(draft);
          this.hasUnsavedChanges = false;
          this.successMessage = 'تم إلغاء اعتماد تشغيل اليوم. يمكنك الآن تصحيح المسودة ثم اعتمادها من جديد.';
        },
        error: error => {
          if (error?.status === 409) {
            this.loadTodayOperations({
              kind: 'error',
              message: 'تغيرت حالة تشغيل اليوم أثناء إلغاء الاعتماد. تم تحديث بيانات التشغيل.'
            });
            return;
          }
          this.error = error?.status === 403
            ? 'لا تملك صلاحية إلغاء اعتماد تشغيل اليوم.'
            : this.formValidation.serverMessage(error, 'تعذر إلغاء اعتماد تشغيل اليوم.');
        }
      });
  }

  workerNeedsOverride(stage: EditableDailyStage, worker: EditableDailyWorker): boolean {
    return worker.includedInProduction && (worker.isDailyOverride === true || worker.isAssignedWorker === false || !worker.isProductionReady);
  }

  stagePreview(stageId: string) {
    return this.preview?.stages.find(stage => stage.productModelStageId === stageId) ?? null;
  }

  stageStatusLabel(stage: EditableDailyStage): string {
    if (stage.staffingStatus === 'NoStaffing') return 'دون تسكين';
    if (stage.hasAbsentWorkers) return 'عامل غائب';
    if (stage.hasNoSourceCheckInWorkers) return 'دون بصمة';
    return stage.isReady ? 'جاهزة' : 'تحتاج مراجعة';
  }

  stageStatusTone(stage: EditableDailyStage): string {
    if (stage.staffingStatus === 'NoStaffing' || stage.hasAbsentWorkers) return 'critical';
    if (stage.hasNoSourceCheckInWorkers || !stage.isReady) return 'warning';
    return 'ready';
  }

  stageCostStatusLabel(stage: EditableDailyStage): string {
    return stage.isFinancialReviewPending ? 'تحتاج مراجعة تكلفة' : 'تكلفة جاهزة';
  }

  stageCostStatusTone(stage: EditableDailyStage): string {
    return stage.isFinancialReviewPending ? 'warning' : 'ready';
  }

  compensationModeLabel(value: string | null | undefined): string {
    return productionDisplayLabel(value, 'طريقة الاحتساب غير محدة');
  }

  assignmentTypeLabel(value: string | null | undefined): string {
    return productionDisplayLabel(value, 'بديل / تجاوز تشغيلي');
  }

  staffingStatusLabel(value: string | null | undefined): string {
    return productionDisplayLabel(value, 'حالة التسكين غير محدة');
  }

  workerServiceLabel(worker: DailyProductionWorker): string {
    return worker.isOnActiveService ? 'على رأس العمل' : 'خارج الخدمة';
  }

  stageEntitlement(stage: DailyProductionStagePreview): number {
    return stage.workers.reduce((total, worker) => total + worker.calculatedEarning, 0);
  }

  get savedDraftTitle(): string {
    if (!this.savedDraft) return '';
    return `تم حفظ مسودة تشغيل يوم ${this.productionDateLabel(this.savedDraft.productionDate)} بنجاح.`;
  }

  get savedDraftDetail(): string {
    return this.savedDraft ? `تم حفظ ${this.savedDraft.stages.length} مرحلة مع بيانات العمال والتكلفة.` : '';
  }

  attendanceLabel(worker: DailyProductionWorker): string {
    return productionDisplayLabel(worker.attendanceStatus, 'لا توجد بصمة مصدر');
  }

  contributionTime(value: string | null | undefined): string {
    if (!value) return '—';
    return new Intl.DateTimeFormat('en-GB', { timeZone: 'Africa/Cairo', hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(value));
  }

  contributionDuration(value: number | null | undefined): string {
    const minutes = Math.max(0, Math.round(value ?? 0));
    const hours = Math.floor(minutes / 60);
    const remainingMinutes = minutes % 60;
    if (!hours) return `${remainingMinutes} دقيقة`;
    if (!remainingMinutes) return `${hours} ساعة`;
    return `${hours} ساعة ${remainingMinutes} دقيقة`;
  }

  exclusionReasonLabel(reason: string | null | undefined): string {
    return ({
      Absent: 'غياب',
      OutsideAssignmentWindow: 'خارج فترة التسكين',
      NoTemporalIntersection: 'لا يوجد تقاطع زمني',
      IncompleteAttendance: 'حضور غير مكتمل',
      NotProductionReady: 'غير جاهز للإنتاج'
    } as Record<string, string>)[reason ?? ''] ?? 'غير جاهز للإنتاج';
  }

  calculatedWorkerQuantity(stage: EditableDailyStage, worker: EditableDailyWorker): number {
    if (worker.includedInProduction === false || !worker.isProductionReady || !this.lineQuantity || stage.compensationMode !== 'SharedPercentage' || worker.percentage === null) return 0;
    return worker.quantity ?? this.roundQuantity(this.lineQuantity * worker.percentage / 100);
  }

  stageAllocationPercentage(stage: EditableDailyStage): number {
    return this.roundPercentage(this.allocationParticipants(stage).reduce((total, worker) => total + (worker.percentage ?? 0), 0));
  }

  stageAllocationQuantity(stage: EditableDailyStage): number {
    return this.roundQuantity(this.allocationParticipants(stage).reduce((total, worker) => total + (worker.quantity ?? 0), 0));
  }

  stageIncludedWorkerCount(stage: EditableDailyStage): number {
    return this.allocationParticipants(stage).length;
  }

  stageSummaryCost(stage: EditableDailyStage): number | null {
    return this.stagePreview(stage.productModelStageId)?.stageCost ?? null;
  }

  stageAllocationStatusLabel(stage: EditableDailyStage): string {
    if (stage.compensationMode !== 'SharedPercentage') return this.stageStatusLabel(stage);
    if (!this.isValidLineQuantity()) return 'أدخل كمية الخط';
    return this.stageAllocationError(stage) || 'التوزيع متوازن';
  }

  stageAllocationStatusTone(stage: EditableDailyStage): string {
    return this.stageAllocationStatusLabel(stage) === 'التوزيع متوازن' ? 'ready' : 'warning';
  }

  workerAllocationError(stage: EditableDailyStage, worker: EditableDailyWorker): string {
    if (stage.compensationMode !== 'SharedPercentage' || worker.includedInProduction === false || !worker.isProductionReady) return '';
    if (worker.percentage === null || worker.percentage <= 0 || worker.percentage > 100) return 'النسبة يجب أن تكون أكبر من 0 ولا تتجاوز 100٪.';
    if (worker.quantity === null || worker.quantity <= 0) return 'أدخل كمية صحيحة للعامل.';
    if (this.isValidLineQuantity() && worker.quantity > this.lineQuantity! + .001) return 'كمية العامل لا يمكن أن تتجاوز كمية المرحلة.';
    return '';
  }

  stageAllocationError(stage: EditableDailyStage): string {
    if (stage.compensationMode !== 'SharedPercentage' || !this.isValidLineQuantity()) return '';
    const participants = this.allocationParticipants(stage);
    if (participants.some(worker => this.workerAllocationError(stage, worker))) return 'راجع قيم العمال';
    if (Math.abs(this.stageAllocationPercentage(stage) - 100) > .0001) return 'مجموع النسب يجب أن يساوي 100٪';
    if (Math.abs(this.stageAllocationQuantity(stage) - this.lineQuantity!) > .001) return 'مجموع الكميات يجب أن يساوي كمية المرحلة';
    return '';
  }

  dailyStaffingLabel(worker: EditableDailyWorker): string {
    if (worker.isDailyOverride) return 'إضافة يومية';
    if (!worker.includedInProduction && worker.isProductionReady) return 'مسكن — مستبعد من تشغيل اليوم';
    if (worker.isProductionReady) return 'مسكن وجاهز';
    if (worker.attendanceStatus === 'Absent' || worker.exclusionReason === 'Absent') return 'مسكن — غائب';
    if (worker.exclusionReason === 'IncompleteAttendance' || worker.attendanceStatus === 'NoSourceCheckIn') return 'مسكن — حضور غير مكتمل';
    if (worker.exclusionReason === 'NoTemporalIntersection' || worker.exclusionReason === 'OutsideAssignmentWindow') return 'مسكن — خارج الفترة';
    return 'مسكن — غير جاهز للإنتاج';
  }

  dailyStaffingTone(worker: EditableDailyWorker): string {
    if (worker.isDailyOverride) return 'info';
    if (worker.isProductionReady && worker.includedInProduction) return 'ready';
    return worker.attendanceStatus === 'Absent' || worker.exclusionReason === 'Absent' ? 'critical' : 'warning';
  }

  trackById(_: number, item: { productModelStageId?: string; stageId?: string; workerId?: string; id?: string }): string {
    return item.productModelStageId ?? item.stageId ?? item.workerId ?? item.id ?? String(_);
  }

  private loadFactories(): void {
    this.factoriesLoading = true;
    this.linesLoading = true;
    forkJoin({ factories: this.masterData.factories(), departments: this.masterData.departments(undefined, false), lines: this.masterData.allProductionLines() })
      .pipe(finalize(() => { this.factoriesLoading = false; this.linesLoading = false; }), takeUntil(this.destroy$))
      .subscribe({
        next: data => {
          this.factories = data.factories.filter(factory => factory.isActive);
          this.departments = data.departments.filter(department => department.isActive !== false);
          this.productionLines = data.lines.filter(line => line.isActive);
          this.dailyStructureTreeNodes = buildFactoryStructureTree({ factories: this.factories, departments: this.departments, lines: this.productionLines, eligibility: new Map() });
        },
        error: error => this.error = this.formValidation.serverMessage(error, 'تعذر تحميل المصانع.')
      });
  }

  private onAttendanceSynchronized(result: AttendanceSyncResult): void {
    this.attendanceSyncedForDate = this.productionDate;
    this.successMessage = `تمت مزامنة حضور ${this.productionDate} يدويًا: ${result.matchedWorkersCount} عاملًا مطابقًا و${result.sourceCheckInsCount} تسجيل مصدر.`;
    if (this.operations && this.selectedFactoryId && this.selectedProductionLineId && this.selectedProductModelId) {
      this.loadTodayOperations();
    }
  }

  private attendanceSyncFailureMessage(error: unknown): string {
    const response = error as {
      status?: number;
      name?: string;
      error?: { error?: { code?: string }; code?: string };
    };
    const payload = response.error?.error ?? response.error;
    const code = payload?.code ?? '';

    if (code === 'AttendanceSyncInProgress') return 'المزامنة جارية بالفعل.';
    if (code === 'AttendanceSyncTimeout' || code === 'AttendanceSourceTimeout' || response.status === 504 || response.name === 'TimeoutError') {
      return 'انتهت مهلة مزامنة الحضور.';
    }
    if (code === 'AttendanceSourceError' || code === 'AttendanceSyncCancelled' || response.status === 0 || response.status === 503) {
      return 'تعذر الاتصال بمصدر الحضور.';
    }

    return this.formValidation.serverMessage(error, 'تعذر مزامنة حضور تاريخ الإنتاج المحدد.');
  }

  private toEditableStage(stage: DailyProductionStage, metadata?: ModelStageItem): EditableDailyStage {
    const assignedWorkers = this.uniqueWorkers(stage.workers);
    return {
      ...stage,
      standardSeconds: metadata?.standardSeconds ?? null,
      workers: assignedWorkers.map(worker => this.toEditableWorker(worker, worker.isProductionReady))
    };
  }

  private toEditableWorker(worker: DailyProductionWorker, includedInProduction = worker.isProductionReady): EditableDailyWorker {
    return {
      ...worker,
      includedInProduction,
      percentage: worker.suggestedPercentage ?? null,
      quantity: null,
      fixedAmount: null,
      notes: '',
      manualOverrideReason: ''
    };
  }

  private validateOperation(): string[] {
    const messages: string[] = [];
    if (!this.operations) messages.push('حمّل تشغيل اليوم بعد مزامنة الحضور أولًا.');
    if (!this.lineQuantity || this.lineQuantity <= 0) messages.push('أدخل كمية تشغيل الخط مرة واحدة بقيمة أكبر من صفر.');

    this.stages.forEach(stage => {
      const participants = stage.workers.filter(worker => worker.includedInProduction !== false && worker.isProductionReady);
      if (!participants.length) messages.push(`${stage.stageCode}: لا يوجد عامل جاهز محتسب في تشغيل هذه المرحلة.`);
      if (stage.compensationMode === 'SharedPercentage' && participants.length) {
        const allocationError = this.stageAllocationError(stage);
        if (allocationError) messages.push(`${stage.stageCode}: ${allocationError}.`);
      }
      if (stage.compensationMode === 'FixedAmount' && participants.some(worker => worker.fixedAmount === null || worker.fixedAmount < 0)) {
        messages.push(`${stage.stageCode}: أدخل قيمة ثابتة صالحة لكل عامل.`);
      }
      participants.filter(worker => this.workerNeedsOverride(stage, worker)).forEach(worker => {
        if (!this.canOverrideParticipants) messages.push(`${stage.stageCode}: العامل ${worker.workerCode} يحتاج تجاوزًا معتمدًا، ولا تملك صلاحية إدارته.`);
        else if (!worker.manualOverrideReason.trim()) messages.push(`${stage.stageCode}: سبب التجاوز مطلوب للعامل ${worker.workerCode}.`);
      });
    });
    return [...new Set(messages)];
  }

  private operationRequest(previewToken: string | null) {
    return {
      factoryId: this.selectedFactoryId,
      productionLineId: this.selectedProductionLineId,
      productModelId: this.selectedProductModelId,
      productionDate: this.productionDate,
      lineQuantity: this.lineQuantity ?? 0,
      clientRequestId: this.clientRequestId,
      notes: this.notes.trim() || null,
      previewToken,
      stages: this.stages.map(stage => this.stageInput(stage))
    };
  }

  private dailyDraftUpdateRequest(draft: DailyProductionDraft, previewToken: string): DailyProductionDraftUpdateInput {
    const request = this.operationRequest(previewToken);
    const recordsByStage = new Map(draft.stages.map(record => [record.productModelStageId, record]));
    return {
      ...request,
      concurrencyToken: draft.concurrencyToken,
      stages: request.stages.map(stage => {
        const record = recordsByStage.get(stage.productModelStageId)!;
        return {
          ...stage,
          stageProductionRecordId: record.id,
          concurrencyToken: record.concurrencyToken
        };
      })
    };
  }

  private stageInput(stage: EditableDailyStage): DailyProductionStageInput {
    const workers: DailyProductionWorkerInput[] = stage.workers
      .filter(worker => worker.includedInProduction !== false && worker.isProductionReady)
      .map(worker => ({
      workerId: worker.workerId,
      percentage: stage.compensationMode === 'SharedPercentage' ? worker.percentage : null,
      fixedAmount: stage.compensationMode === 'FixedAmount' ? worker.fixedAmount : null,
      notes: worker.notes.trim() || null,
      manualOverrideReason: this.workerNeedsOverride(stage, worker) ? worker.manualOverrideReason.trim() || null : null,
      inputQuantity: stage.compensationMode === 'SharedPercentage' ? worker.quantity : null
    }));
    return { productModelStageId: stage.productModelStageId, workers };
  }

  private applyExistingDraft(draft: DailyProductionDraft): void {
    const activeWorkers = new Map((this.operations?.activeWorkers ?? []).map(worker => [worker.workerId, worker]));
    const recordsByStage = new Map(draft.stages.map(record => [record.productModelStageId, record]));
    this.stages.forEach(stage => {
      const saved = recordsByStage.get(stage.productModelStageId);
      const workersById = new Map(stage.workers.map(worker => [worker.workerId, worker]));
      stage.workers.forEach(worker => worker.includedInProduction = false);
      for (const allocation of saved?.workers ?? []) {
        let worker = workersById.get(allocation.workerId);
        if (!worker) {
          const active = activeWorkers.get(allocation.workerId);
          if (!active) continue;
          worker = this.toEditableWorker({ ...active, isAssignedWorker: false, isDailyOverride: true }, true);
          stage.workers.push(worker);
          workersById.set(worker.workerId, worker);
        }
        worker.includedInProduction = true;
        worker.isDailyOverride = worker.isAssignedWorker === false || worker.isDailyOverride === true;
        worker.percentage = allocation.percentage ?? null;
        worker.quantity = allocation.inputQuantity ?? null;
        worker.fixedAmount = allocation.fixedAmount ?? null;
        worker.notes = allocation.notes ?? '';
        worker.manualOverrideReason = allocation.manualOverrideReason ?? '';
      }
      stage.workers = this.uniqueWorkers(stage.workers);
    });
    this.lineQuantity = draft.lineQuantity;
    this.stages.forEach(stage => this.synchronizeStageQuantities(stage));
    this.savedDraft = draft;
  }

  private applyPersistedDraftState(draft: DailyProductionDraft): void {
    if (this.operations) this.operations = { ...this.operations, existingDraft: draft };
    this.applyExistingDraft(draft);
    this.previewSource = 'persisted';
    this.preview = this.previewFromDraft(draft);
    this.previewRevision = this.revision;
    this.previewStatus = 'success';
  }

  private previewFromDraft(draft: DailyProductionDraft): DailyProductionPreview {
    const workerTotals = new Map<string, { workerId: string; workerCode: string; workerName: string; totalEntitlement: number }>();
    const stages = draft.stages.map(stage => {
      stage.workers.forEach(worker => {
        const current = workerTotals.get(worker.workerId) ?? {
          workerId: worker.workerId,
          workerCode: worker.workerCode,
          workerName: worker.workerName,
          totalEntitlement: 0
        };
        current.totalEntitlement = this.roundMoney(current.totalEntitlement + worker.calculatedEarning);
        workerTotals.set(worker.workerId, current);
      });
      return {
        productModelStageId: stage.productModelStageId,
        stageCode: stage.stageCode,
        stageName: stage.stageName,
        stageQuantity: stage.producedQuantity,
        stageCost: stage.totalWorkerEarnings,
        compensationMode: stage.compensationMode,
        workers: stage.workers,
        warnings: []
      };
    });
    return {
      productionDate: draft.productionDate,
      lineQuantity: draft.lineQuantity,
      previewToken: '',
      totalWorkerEntitlements: this.roundMoney([...workerTotals.values()].reduce((total, worker) => total + worker.totalEntitlement, 0)),
      stages,
      workerTotals: [...workerTotals.values()],
      warnings: []
    };
  }

  private get currentDailyDraft(): DailyProductionDraft | null {
    return this.savedDraft ?? this.operations?.existingDraft ?? null;
  }

  private get hasApprovalConcurrencyData(): boolean {
    const draft = this.currentDailyDraft;
    return !!draft && draft.stages.length > 0 && draft.stages.every(stage => !!stage.id && !!stage.concurrencyToken);
  }

  private hasDraftUpdateConcurrencyData(draft: DailyProductionDraft): boolean {
    if (!draft.concurrencyToken || draft.stages.length !== this.stages.length) return false;
    const recordsByStage = new Map(draft.stages.map(record => [record.productModelStageId, record]));
    return this.stages.every(stage => {
      const record = recordsByStage.get(stage.productModelStageId);
      return !!record?.id && !!record.concurrencyToken;
    });
  }

  private allocationParticipants(stage: EditableDailyStage): EditableDailyWorker[] {
    return stage.workers.filter(worker => worker.includedInProduction !== false && worker.isProductionReady);
  }

  private synchronizeStageQuantities(stage: EditableDailyStage): void {
    if (stage.compensationMode !== 'SharedPercentage') return;
    const participants = this.allocationParticipants(stage);
    participants.forEach(worker => {
      worker.quantity = this.isValidLineQuantity() && worker.percentage !== null
        ? this.roundQuantity(this.lineQuantity! * worker.percentage / 100)
        : null;
    });
    const lastParticipant = participants.at(-1);
    if (lastParticipant) this.reconcileQuantityRounding(stage, lastParticipant);
  }

  private reconcileQuantityRounding(stage: EditableDailyStage, editedWorker: EditableDailyWorker): void {
    if (!this.isValidLineQuantity() || editedWorker.percentage === null) return;
    const participants = this.allocationParticipants(stage);
    const totalPercentage = participants.reduce((total, worker) => total + (worker.percentage ?? 0), 0);
    if (Math.abs(totalPercentage - 100) > .0001) return;
    const otherQuantity = participants
      .filter(worker => worker.workerId !== editedWorker.workerId)
      .reduce((total, worker) => total + (worker.quantity ?? 0), 0);
    editedWorker.quantity = this.roundQuantity(this.lineQuantity! - otherQuantity);
  }

  private reconcilePercentageRounding(stage: EditableDailyStage, editedWorker: EditableDailyWorker): void {
    if (!this.isValidLineQuantity() || editedWorker.quantity === null) return;
    const participants = this.allocationParticipants(stage);
    const totalQuantity = participants.reduce((total, worker) => total + (worker.quantity ?? 0), 0);
    if (Math.abs(totalQuantity - this.lineQuantity!) > .001) return;
    const otherPercentage = participants
      .filter(worker => worker.workerId !== editedWorker.workerId)
      .reduce((total, worker) => total + (worker.percentage ?? 0), 0);
    editedWorker.percentage = this.roundPercentage(100 - otherPercentage);
  }

  private numericValue(value: number | null): number | null {
    if (value === null || value === undefined || value === ('' as unknown as number)) return null;
    const numeric = Number(value);
    return Number.isFinite(numeric) ? numeric : null;
  }

  private isValidLineQuantity(): boolean {
    return this.lineQuantity !== null && Number.isFinite(this.lineQuantity) && this.lineQuantity > 0;
  }

  private roundPercentage(value: number): number {
    return Math.round((value + Number.EPSILON) * 10_000) / 10_000;
  }

  private roundQuantity(value: number): number {
    return Math.round((value + Number.EPSILON) * 1_000) / 1_000;
  }

  private uniqueWorkers<T extends DailyProductionWorker>(workers: readonly T[]): T[] {
    return [...new Map(workers.map(worker => [worker.workerId, worker])).values()];
  }

  workerOverviewStatus(worker: StageWorkerProjection): string {
    if (worker.isCalculated) return 'جاهز';
    const attendance = worker.attendance.trim();
    return attendance.includes('غائب') || attendance.includes('غياب') ? 'غائب' : 'غير جاهز';
  }

  private rebuildAllocationProjection(): void {
    if (!this.previewValue) {
      this.stageAllocationRows = [];
      this.workerAllocationRows = [];
      this.expandedStageRows = {};
      this.expandedWorkerRows = {};
      return;
    }

    const currentStages = new Map(this.stages.map(stage => [stage.productModelStageId, stage]));
    const persistedStages = new Map((this.currentDailyDraft?.stages ?? []).map(stage => [stage.productModelStageId, stage]));
    const activeWorkers = new Map((this.operations?.activeWorkers ?? []).map(worker => [worker.workerId, worker]));
    this.stageAllocationRows = this.previewValue.stages.map(previewStage => {
      const currentStage = currentStages.get(previewStage.productModelStageId);
      const persistedStage = this.previewSource === 'persisted' ? persistedStages.get(previewStage.productModelStageId) : undefined;
      const piecePrice = persistedStage?.piecePrice ?? currentStage?.piecePrice ?? 0;
      const standardSeconds = persistedStage?.standardSeconds ?? currentStage?.standardSeconds ?? null;
      const currentWorkers = new Map((currentStage?.workers ?? []).map(worker => [worker.workerId, worker]));
      const calculatedWorkers = new Map(previewStage.workers.map(worker => [worker.workerId, worker]));
      const workerIds = [...new Set([
        ...(currentStage?.workers ?? []).map(worker => worker.workerId),
        ...previewStage.workers.map(worker => worker.workerId)
      ])];
      const workers = workerIds
        .map(workerId => this.projectStageWorker(
          workerId,
          currentWorkers.get(workerId) ?? activeWorkers.get(workerId),
          calculatedWorkers.get(workerId)
        ))
        .filter(worker => this.hasValidWorkerIdentity(worker));

      return {
        stageId: previewStage.productModelStageId,
        stageCode: previewStage.stageCode,
        stageName: previewStage.stageName,
        stageOrder: currentStage?.stageOrder ?? Number.MAX_SAFE_INTEGER,
        stageQuantity: previewStage.stageQuantity,
        piecePrice,
        standardSeconds,
        totalStandardSeconds: standardSeconds === null ? null : this.roundQuantity(previewStage.stageQuantity * standardSeconds),
        totalStandardMinutes: standardSeconds === null ? null : this.roundQuantity(previewStage.stageQuantity * standardSeconds / 60),
        stageValue: this.roundMoney(workers.filter(worker => worker.isCalculated).reduce((total, worker) => total + worker.calculatedEarning, 0)),
        participantCount: workers.filter(worker => worker.isCalculated).length,
        distribution: this.compensationModeLabel(previewStage.compensationMode),
        totalEntitlement: this.roundMoney(workers.reduce((total, worker) => total + worker.calculatedEarning, 0)),
        status: currentStage ? this.stageStatusLabel(currentStage) : (previewStage.warnings.length ? 'تحتاج مراجعة' : 'جاهزة'),
        statusTone: currentStage ? this.stageStatusTone(currentStage) : (previewStage.warnings.length ? 'warning' : 'ready'),
        warnings: previewStage.warnings,
        workers
      };
    });

    const workerTotals = new Map(this.previewValue.workerTotals.map(worker => [worker.workerId, worker.totalEntitlement]));
    const workerRows = new Map<string, WorkerAllocationProjection>();
    this.stageAllocationRows.forEach(stage => stage.workers.filter(worker => worker.isCalculated).forEach(worker => {
      const row = workerRows.get(worker.workerId) ?? {
        workerId: worker.workerId,
        workerCode: worker.workerCode,
        workerName: worker.workerName,
        contributionStartsAtUtc: worker.contributionStartsAtUtc,
        contributionEndsAtUtc: worker.contributionEndsAtUtc,
        workerMinutes: worker.workerMinutes,
        participationType: worker.participationType,
        stageCount: 0,
        visibleStageNames: [],
        hiddenStageCount: 0,
        totalAllocatedQuantity: 0,
        totalEntitlement: workerTotals.get(worker.workerId) ?? 0,
        totalStandardSeconds: null,
        stages: []
      };
      const workerStandardSeconds = stage.standardSeconds === null
        ? null
        : this.roundQuantity(worker.allocatedQuantity * stage.standardSeconds);
      row.stages.push({
        stageId: stage.stageId,
        stageCode: stage.stageCode,
        stageName: stage.stageName,
        stageOrder: stage.stageOrder,
        stageQuantity: stage.stageQuantity,
        allocatedQuantity: worker.allocatedQuantity,
        percentage: worker.percentage,
        piecePrice: stage.piecePrice,
        standardSeconds: stage.standardSeconds,
        workerStandardSeconds,
        workerStandardMinutes: workerStandardSeconds === null ? null : this.roundQuantity(workerStandardSeconds / 60),
        workerMinutes: worker.workerMinutes,
        calculatedEarning: worker.calculatedEarning,
        notes: worker.notes,
        manualOverrideReason: worker.manualOverrideReason,
        distribution: stage.distribution,
        participationType: worker.participationType,
        readiness: worker.readiness,
        status: stage.status
      });
      workerRows.set(worker.workerId, row);
    }));

    this.workerAllocationRows = [...workerRows.values()].map(worker => {
      const stageNames = worker.stages.map(stage => stage.stageName);
      return {
        ...worker,
        stageCount: worker.stages.length,
        visibleStageNames: stageNames.slice(0, 2),
        hiddenStageCount: Math.max(0, stageNames.length - 2),
        totalAllocatedQuantity: this.roundQuantity(worker.stages.reduce((total, stage) => total + stage.allocatedQuantity, 0)),
        totalEntitlement: workerTotals.get(worker.workerId) ?? this.roundMoney(worker.stages.reduce((total, stage) => total + stage.calculatedEarning, 0)),
        totalStandardSeconds: worker.stages.some(stage => stage.workerStandardSeconds === null)
          ? null
          : this.roundQuantity(worker.stages.reduce((total, stage) => total + (stage.workerStandardSeconds ?? 0), 0))
      };
    });
  }

  private projectStageWorker(
    workerId: string,
    worker: DailyProductionWorker | undefined,
    allocation: ProductionWorkerAllocation | undefined
  ): StageWorkerProjection {
    return {
      workerId: workerId.trim(),
      workerCode: (worker?.workerCode ?? allocation?.workerCode ?? '').trim(),
      workerName: (worker?.workerName ?? allocation?.workerName ?? '').trim(),
      participationType: this.participationTypeLabel(worker),
      attendance: worker ? this.attendanceLabel(worker) : '—',
      readiness: allocation ? 'جاهز ومحتسب' : 'غير جاهز وغير محتسب',
      contributionStartsAtUtc: worker?.contributionStartsAtUtc ?? null,
      contributionEndsAtUtc: worker?.contributionEndsAtUtc ?? null,
      workerMinutes: worker?.workerMinutes ?? 0,
      percentage: allocation?.percentage ?? null,
      allocatedQuantity: allocation?.equivalentQuantity ?? 0,
      calculatedEarning: allocation?.calculatedEarning ?? 0,
      notes: allocation?.notes ?? '',
      manualOverrideReason: allocation?.manualOverrideReason ?? '',
      exclusionReason: worker?.exclusionReason ?? null,
      isCalculated: !!allocation
    };
  }

  private hasValidWorkerIdentity(worker: StageWorkerProjection): boolean {
    return !!worker.workerId && !!worker.workerCode && !!worker.workerName;
  }

  private participationTypeLabel(worker: DailyProductionWorker | undefined): string {
    if (worker?.isDailyOverride) return 'إضافة يومية';
    if ((worker?.effectiveAssignmentType ?? '').toLocaleLowerCase().includes('temporary')) return 'تسكين مؤقت';
    return 'تسكين أساسي';
  }

  private productionDateLabel(value: string): string {
    return new Intl.DateTimeFormat('ar-EG', {
      timeZone: 'Africa/Cairo',
      day: 'numeric',
      month: 'long',
      year: 'numeric'
    }).format(new Date(`${value}T12:00:00+03:00`));
  }

  private scrollToSelectedStage(stageId: string): void {
    setTimeout(() => {
      document.getElementById(`daily-stage-row-${stageId}`)?.scrollIntoView({
        behavior: 'auto',
        block: 'nearest',
        inline: 'nearest'
      });
    }, 200);
  }

  private toExcelDate(value: string | null | undefined): Date | null {
    if (!value) return null;
    const date = /^\d{4}-\d{2}-\d{2}$/.test(value)
      ? new Date(`${value}T12:00:00`)
      : new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
  }

  private exportRecordStatus(status: 'Draft' | 'Approved' | 'Cancelled' | undefined): string {
    if (status === 'Approved') return 'معتمد';
    if (status === 'Cancelled') return 'ملغي اعتماده';
    if (status === 'Draft') return 'مسودة';
    return 'غير محفوظ';
  }

  private roundMoney(value: number): number {
    return Math.round((value + Number.EPSILON) * 10_000) / 10_000;
  }

  private get dailyOperationExportStatus(): string {
    if (this.isApproved) return 'معتمدة';
    if (this.isDailyApprovalCancelled) return 'أُلغي اعتمادها';
    if (this.hasExistingDraft) return 'مسودة';
    return 'غير محفوظة';
  }

  private invalidatePreview(incrementRevision = true, status: Extract<UnifiedPreviewStatus, 'idle' | 'stale'> = 'stale'): void {
    if (incrementRevision) this.revision++;
    this.preview = null;
    this.previewSource = null;
    this.previewRevision = -1;
    this.previewStatus = status;
    this.savedDraft = this.operations?.existingDraft ?? null;
  }

  private subscribeToDailyOperationsRealtime(): void {
    this.stopRealtime = this.manufacturingRealtime?.watchScreen({
      screen: 'daily-production-operations',
      matches: change => this.matchesCurrentDailyOperationsContext(change),
      refresh: () => this.handleDailyOperationsRealtimeChange()
    });
  }

  private matchesCurrentDailyOperationsContext(change: ManufacturingDataChanged): boolean {
    if (!this.selectedFactoryId || !this.selectedProductionLineId || !this.selectedProductModelId)
      return false;

    if (change.entityType === 'ProductionOrder') {
      return change.productionDate === this.productionDate &&
        change.factoryId === this.selectedFactoryId &&
        change.productionLineId === this.selectedProductionLineId &&
        change.productModelId === this.selectedProductModelId;
    }

    // Worker invalidations affect the active worker selector. Attendance is
    // additionally constrained to the open production day when the API sends
    // its additive date scope; missing dates remain compatible with older API
    // instances during a rolling deployment.
    if (change.entityType === 'Worker')
      return true;
    if (change.entityType === 'AttendanceRecord') {
      const dates = change.affectedAttendanceDates?.length
        ? change.affectedAttendanceDates
        : change.productionDate ? [change.productionDate] : [];
      return dates.length === 0 || dates.includes(this.productionDate);
    }

    if (change.entityType !== 'WorkerDefaultAssignment' || !change.subStageId)
      return false;

    return change.factoryId === this.selectedFactoryId &&
      change.productionLineId === this.selectedProductionLineId &&
      this.stages.some(stage => stage.subStageId === change.subStageId);
  }

  private handleDailyOperationsRealtimeChange(): void {
    if (this.hasUnsavedChanges) {
      this.hasPendingRemoteUpdate = true;
      this.remoteUpdateMessage = 'تم تعديل تشغيل اليوم بواسطة مستخدم آخر. احتفظنا بتعديلاتك غير المحفوظة؛ اضغط تحديث الآن لإعادة التحميل.';
      return;
    }

    if (this.operationsLoading) {
      this.realtimeRefreshQueued = true;
      return;
    }

    if (!this.canLoadOperations) return;
    this.loadTodayOperations({
      kind: 'success',
      message: 'تم تحديث تشغيل اليوم تلقائيًا بعد تغير من مستخدم آخر.'
    });
  }

  private flushQueuedRealtimeRefresh(): void {
    if (!this.realtimeRefreshQueued) return;
    this.realtimeRefreshQueued = false;
    this.handleDailyOperationsRealtimeChange();
  }

  private clearRemoteUpdateNotice(): void {
    this.hasPendingRemoteUpdate = false;
    this.remoteUpdateMessage = '';
  }

  private resetOperations(): void {
    ++this.operationsRequestVersion;
    this.operations = null;
    this.stages = [];
    this.modelStageMetadata = [];
    this.selectedStageId = '';
    this.selectedStageFilterId = '';
    this.lineQuantity = null;
    this.notes = '';
    this.replacementWorkerId = '';
    this.clientRequestId = generateUuidV4();
    this.invalidatePreview(true, 'idle');
    this.error = '';
    this.successMessage = '';
    this.validationMessages = [];
    this.realtimeRefreshQueued = false;
    this.hasUnsavedChanges = false;
    this.clearRemoteUpdateNotice();
  }

  private egyptToday(): string {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Africa/Cairo',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit'
    }).formatToParts(new Date());
    const value = (type: Intl.DateTimeFormatPartTypes) => parts.find(part => part.type === type)?.value ?? '';
    return `${value('year')}-${value('month')}-${value('day')}`;
  }
}
