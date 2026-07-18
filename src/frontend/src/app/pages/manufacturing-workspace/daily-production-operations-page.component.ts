import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject, TimeoutError, finalize, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { AttendanceApiService, AttendanceSyncResult } from '../../core/services/attendance-api.service';
import {
  DailyProductionDraft,
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
import { FactoryItem, ManufacturingMasterDataApiService, ProductModelItem, ProductionLineOption } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { createClientRequestId } from '../../core/utils/client-request-id';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';
import { productionDisplayLabel } from '../../shared/product/production-display-labels';

type StageFilter = 'all' | 'ready' | 'absent' | 'no-check-in' | 'no-staffing' | 'cost-review';
type EditableDailyStage = Omit<DailyProductionStage, 'workers'> & {
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
  exclusionReason: string | null;
  isCalculated: boolean;
}

interface StageAllocationProjection {
  stageId: string;
  stageCode: string;
  stageName: string;
  stageQuantity: number;
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
  stageQuantity: number;
  allocatedQuantity: number;
  percentage: number | null;
  workerMinutes: number;
  calculatedEarning: number;
  distribution: string;
  participationType: string;
  readiness: string;
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
  productionLines: ProductionLineOption[] = [];
  productModels: ProductModelItem[] = [];
  operations: DailyProductionOperations | null = null;
  stages: EditableDailyStage[] = [];
  savedDraft: DailyProductionDraft | null = null;
  approving = false;
  stageAllocationRows: StageAllocationProjection[] = [];
  workerAllocationRows: WorkerAllocationProjection[] = [];
  expandedStageRows: Record<string, boolean> = {};
  expandedWorkerRows: Record<string, boolean> = {};

  private previewValue: DailyProductionPreview | null = null;

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
  lineQuantity: number | null = null;
  notes = '';
  stageFilter: StageFilter = 'all';
  stageSearch = '';
  replacementWorkerId = '';

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

  private revision = 0;
  private previewRevision = -1;
  private clientRequestId = createClientRequestId();
  private operationsRequestVersion = 0;
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly masterData: ManufacturingMasterDataApiService,
    private readonly attendance: AttendanceApiService,
    private readonly production: ProductionCostRecordingApiService,
    private readonly permissionsService: PermissionService,
    private readonly formValidation: FormSubmissionValidationService
  ) {}

  ngOnInit(): void {
    this.loadFactories();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get canOverrideParticipants(): boolean {
    return this.permissionsService.hasPermission(this.permissions.assignments.manage);
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

  get selectedStage(): EditableDailyStage | null {
    return this.stages.find(stage => stage.productModelStageId === this.selectedStageId) ?? null;
  }

  get filteredStages(): EditableDailyStage[] {
    const search = this.stageSearch.trim().toLocaleLowerCase('ar');
    return this.stages.filter(stage => {
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
    return !!this.preview && this.previewRevision === this.revision;
  }

  get hasExistingDraft(): boolean {
    return !!this.currentDailyDraft;
  }

  get isDailyOperationApproved(): boolean {
    const draft = this.currentDailyDraft;
    return !!draft && draft.stages.length > 0 && draft.stages.every(stage => stage.status === 'Approved');
  }

  get isDailyOperationReadOnly(): boolean {
    const draft = this.currentDailyDraft;
    return !!draft && draft.stages.some(stage => stage.status !== 'Draft');
  }

  get canApproveDailyOperation(): boolean {
    const draft = this.currentDailyDraft;
    return this.permissionsService.hasPermission(this.permissions.production.approve) &&
      !!draft &&
      draft.stages.length > 0 &&
      draft.stages.every(stage => stage.status === 'Draft') &&
      !this.operationsLoading &&
      !this.saving &&
      !this.approving;
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
    this.productionLines = [];
    this.productModels = [];
    this.resetOperations();
    if (!factoryId) return;

    this.linesLoading = true;
    this.masterData.allProductionLines()
      .pipe(finalize(() => this.linesLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: lines => this.productionLines = lines,
        error: error => this.error = this.formValidation.serverMessage(error, 'تعذر تحميل خطوط الإنتاج.')
      });
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
    this.operationsLoading = true;
    this.error = '';
    this.successMessage = '';
    this.production.loadDailyOperations(
      this.selectedFactoryId,
      this.selectedProductionLineId,
      this.selectedProductModelId,
      this.productionDate
    )
      .pipe(finalize(() => {
        if (version === this.operationsRequestVersion) this.operationsLoading = false;
      }), takeUntil(this.destroy$))
      .subscribe({
        next: operations => {
          if (version !== this.operationsRequestVersion) return;
          this.operations = operations;
          this.stages = operations.stages.map(stage => this.toEditableStage(stage));
          this.selectedStageId = this.stages.some(stage => stage.productModelStageId === selectedStageId)
            ? selectedStageId
            : this.stages[0]?.productModelStageId ?? '';
          this.replacementWorkerId = '';
          this.invalidatePreview(false);
          if (operations.existingDraft) {
            this.applyExistingDraft(operations.existingDraft);
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
    if (stage.compensationMode !== 'SharedPercentage') return;
    const participants = stage.workers
      .filter(worker => worker.includedInProduction !== false && worker.isProductionReady && worker.workerMinutes > 0)
      .sort((left, right) => left.workerId.localeCompare(right.workerId));
    const totalMinutes = participants.reduce((total, worker) => total + worker.workerMinutes, 0);
    const percentageUnits = 1_000_000;
    const shares = participants.map(worker => {
      const exactUnits = totalMinutes > 0 ? worker.workerMinutes * percentageUnits / totalMinutes : 0;
      const units = Math.floor(exactUnits);
      return { worker, units, remainder: exactUnits - units };
    });
    let remainingUnits = percentageUnits - shares.reduce((total, share) => total + share.units, 0);
    shares
      .sort((left, right) => right.remainder - left.remainder || left.worker.workerId.localeCompare(right.worker.workerId))
      .forEach(share => {
        if (remainingUnits <= 0) return;
        share.units++;
        remainingUnits--;
      });

    stage.workers.forEach(worker => worker.percentage = null);
    shares.forEach(share => share.worker.percentage = share.units / 10_000);
    this.synchronizeStageQuantities(stage);
    if (markChanged) this.stageChanged();
  }

  updateWorkerPercentage(stage: EditableDailyStage, worker: EditableDailyWorker, value: number | null): void {
    worker.percentage = this.numericValue(value);
    worker.quantity = this.isValidLineQuantity() && worker.percentage !== null
      ? this.roundQuantity(this.lineQuantity! * worker.percentage / 100)
      : null;
    this.reconcileQuantityRounding(stage, worker);
    this.stageChanged();
  }

  updateWorkerQuantity(stage: EditableDailyStage, worker: EditableDailyWorker, value: number | null): void {
    worker.quantity = this.numericValue(value);
    worker.percentage = this.isValidLineQuantity() && worker.quantity !== null
      ? this.roundPercentage(worker.quantity / this.lineQuantity! * 100)
      : null;
    this.reconcilePercentageRounding(stage, worker);
    this.stageChanged();
  }

  stageChanged(): void {
    this.error = '';
    this.validationMessages = [];
    this.invalidatePreview();
  }

  lineQuantityChanged(): void {
    this.stages.forEach(stage => this.synchronizeStageQuantities(stage));
    this.stageChanged();
  }

  calculatePreview(): void {
    const validation = this.validateOperation();
    this.validationMessages = validation;
    if (validation.length || this.previewing) return;

    const revision = this.revision;
    this.error = '';
    this.previewing = true;
    this.production.previewDailyOperations(this.operationRequest(null))
      .pipe(finalize(() => this.previewing = false), takeUntil(this.destroy$))
      .subscribe({
        next: preview => {
          if (revision !== this.revision) return;
          this.preview = preview;
          this.previewRevision = revision;
          this.successMessage = 'تم احتساب معاينة موحّدة لكل المراحل والعمال. يمكنك الآن مراجعة المستحقات قبل حفظ المسودة.';
        },
        error: error => this.error = error instanceof TimeoutError
          ? 'استغرق احتساب معاينة تشغيل اليوم وقتًا أطول من المسموح. بقيت بياناتك كما هي؛ أعد المحاولة.'
          : this.formValidation.serverMessage(error, 'تعذر احتساب معاينة تشغيل اليوم.')
      });
  }

  saveDailyDraft(): void {
    if (this.hasExistingDraft) {
      this.error = 'توجد مسودة محفوظة بالفعل لهذا اليوم. تم تحميلها كما هي ولن تُكتب فوقها بصمت.';
      return;
    }
    if (!this.isPreviewCurrent || !this.preview || this.saving) {
      this.error = 'احسب معاينة حديثة أولًا؛ أي تغيير في المرحلة أو الكمية يجعل الحفظ غير صالح.';
      return;
    }
    const validation = this.validateOperation();
    this.validationMessages = validation;
    if (validation.length) return;

    this.saving = true;
    this.error = '';
    this.production.saveDailyDraft(this.operationRequest(this.preview.previewToken))
      .pipe(finalize(() => this.saving = false), takeUntil(this.destroy$))
      .subscribe({
        next: draft => {
          this.savedDraft = draft;
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
    this.production.approveDailyOperation(draft.productionOrderId, stageApprovals)
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
    this.masterData.factories()
      .pipe(finalize(() => this.factoriesLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: factories => this.factories = factories.filter(factory => factory.isActive),
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

  private toEditableStage(stage: DailyProductionStage): EditableDailyStage {
    const assignedWorkers = this.uniqueWorkers(stage.workers);
    return {
      ...stage,
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

  private stageInput(stage: EditableDailyStage): DailyProductionStageInput {
    const workers: DailyProductionWorkerInput[] = stage.workers
      .filter(worker => worker.includedInProduction !== false && worker.isProductionReady)
      .map(worker => ({
      workerId: worker.workerId,
      percentage: stage.compensationMode === 'SharedPercentage' ? worker.percentage : null,
      fixedAmount: stage.compensationMode === 'FixedAmount' ? worker.fixedAmount : null,
      notes: worker.notes.trim() || null,
      manualOverrideReason: this.workerNeedsOverride(stage, worker) ? worker.manualOverrideReason.trim() || null : null,
      inputQuantity: null
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

  private get currentDailyDraft(): DailyProductionDraft | null {
    return this.savedDraft ?? this.operations?.existingDraft ?? null;
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
    const activeWorkers = new Map((this.operations?.activeWorkers ?? []).map(worker => [worker.workerId, worker]));
    this.stageAllocationRows = this.previewValue.stages.map(previewStage => {
      const currentStage = currentStages.get(previewStage.productModelStageId);
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
        stageQuantity: previewStage.stageQuantity,
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
        stages: []
      };
      row.stages.push({
        stageId: stage.stageId,
        stageCode: stage.stageCode,
        stageName: stage.stageName,
        stageQuantity: stage.stageQuantity,
        allocatedQuantity: worker.allocatedQuantity,
        percentage: worker.percentage,
        workerMinutes: worker.workerMinutes,
        calculatedEarning: worker.calculatedEarning,
        distribution: stage.distribution,
        participationType: worker.participationType,
        readiness: worker.readiness
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
        totalEntitlement: workerTotals.get(worker.workerId) ?? this.roundMoney(worker.stages.reduce((total, stage) => total + stage.calculatedEarning, 0))
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
      exclusionReason: worker?.exclusionReason ?? null,
      isCalculated: !!allocation
    };
  }

  private hasValidWorkerIdentity(worker: StageWorkerProjection): boolean {
    return !!worker.workerId && !!worker.workerCode && !!worker.workerName;
  }

  private participationTypeLabel(worker: DailyProductionWorker | undefined): string {
    if (worker?.isDailyOverride) return 'إضافة يومية';
    if ((worker?.effectiveAssignmentType ?? '').toLocaleLowerCase().includes('temporary')) return 'تعيين مؤقت';
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

  private roundMoney(value: number): number {
    return Math.round((value + Number.EPSILON) * 10_000) / 10_000;
  }

  private invalidatePreview(incrementRevision = true): void {
    if (incrementRevision) this.revision++;
    this.preview = null;
    this.previewRevision = -1;
    this.savedDraft = this.operations?.existingDraft ?? null;
  }

  private resetOperations(): void {
    ++this.operationsRequestVersion;
    this.operations = null;
    this.stages = [];
    this.selectedStageId = '';
    this.lineQuantity = null;
    this.notes = '';
    this.replacementWorkerId = '';
    this.clientRequestId = createClientRequestId();
    this.invalidatePreview();
    this.error = '';
    this.successMessage = '';
    this.validationMessages = [];
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
