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
  fixedAmount: number | null;
  notes: string;
  manualOverrideReason: string;
};

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
  preview: DailyProductionPreview | null = null;
  savedDraft: DailyProductionDraft | null = null;

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
    return !!this.operations?.existingDraft;
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

  loadTodayOperations(): void {
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
            this.successMessage = 'تم تحميل مسودة اليوم المحفوظة فوق لقطة التسكين الحالية دون إعادة بنائها أو الكتابة فوقها.';
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
    if (markChanged) this.stageChanged();
  }

  stageChanged(): void {
    this.error = '';
    this.validationMessages = [];
    this.invalidatePreview();
  }

  lineQuantityChanged(): void {
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

  attendanceLabel(worker: DailyProductionWorker): string {
    return productionDisplayLabel(worker.attendanceStatus, 'لا توجد بصمة مصدر');
  }

  contributionTime(value: string | null | undefined): string {
    if (!value) return '—';
    return new Intl.DateTimeFormat('ar-EG', { timeZone: 'Africa/Cairo', hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(value));
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
    if (worker.includedInProduction === false || !worker.isProductionReady || !this.lineQuantity || stage.compensationMode !== 'SharedPercentage' || !worker.percentage) return 0;
    return Math.round(this.lineQuantity * worker.percentage / 100 * 1000) / 1000;
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

  trackById(_: number, item: { productModelStageId?: string; workerId?: string; id?: string }): string {
    return item.productModelStageId ?? item.workerId ?? item.id ?? String(_);
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
        const invalid = participants.some(worker => worker.percentage === null || worker.percentage <= 0);
        const total = participants.reduce((sum, worker) => sum + (worker.percentage ?? 0), 0);
        if (invalid || Math.abs(total - 100) > 0.000001) messages.push(`${stage.stageCode}: يجب أن يساوي مجموع نسب العمال 100٪ تمامًا.`);
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
        worker.fixedAmount = allocation.fixedAmount ?? null;
        worker.notes = allocation.notes ?? '';
        worker.manualOverrideReason = allocation.manualOverrideReason ?? '';
      }
      stage.workers = this.uniqueWorkers(stage.workers);
    });
    this.lineQuantity = draft.lineQuantity;
    this.savedDraft = draft;
  }

  private uniqueWorkers<T extends DailyProductionWorker>(workers: readonly T[]): T[] {
    return [...new Map(workers.map(worker => [worker.workerId, worker])).values()];
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
