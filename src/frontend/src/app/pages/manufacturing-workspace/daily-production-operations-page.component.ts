import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject, finalize, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { AttendanceApiService, AttendanceSyncResult } from '../../core/services/attendance-api.service';
import {
  DailyProductionDraft,
  DailyProductionOperations,
  DailyProductionPreview,
  DailyProductionStage,
  DailyProductionStageInput,
  DailyProductionWorker,
  DailyProductionWorkerInput,
  ProductionCostRecordingApiService
} from '../../core/services/production-cost-recording-api.service';
import { FactoryItem, ManufacturingMasterDataApiService, ProductModelItem, ProductionLineOption } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { createClientRequestId } from '../../core/utils/client-request-id';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';

type StageFilter = 'all' | 'ready' | 'absent' | 'no-check-in' | 'no-staffing' | 'cost-review';
type EditableDailyStage = Omit<DailyProductionStage, 'workers'> & {
  workers: EditableDailyWorker[];
  originalWorkerIds: string[];
};
type EditableDailyWorker = DailyProductionWorker & {
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
      .filter(worker => !existing.has(worker.workerId))
      .sort((left, right) => `${left.workerCode}|${left.workerId}`.localeCompare(`${right.workerCode}|${right.workerId}`));
  }

  get isPreviewCurrent(): boolean {
    return !!this.preview && this.previewRevision === this.revision;
  }

  get totalEnteredWorkers(): number {
    return this.stages.reduce((total, stage) => total + stage.workers.length, 0);
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
        error: error => this.error = this.formValidation.serverMessage(error, 'تعذر مزامنة حضور تاريخ الإنتاج المحدد.')
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
    stage.workers = [...stage.workers, this.toEditableWorker(worker)];
    if (stage.compensationMode === 'SharedPercentage') this.applyEqualDistribution(stage, false);
    this.replacementWorkerId = '';
    this.stageChanged();
  }

  removeWorker(stage: EditableDailyStage, workerId: string): void {
    if (!this.canOverrideParticipants) return;
    stage.workers = stage.workers.filter(worker => worker.workerId !== workerId);
    if (stage.compensationMode === 'SharedPercentage') this.applyEqualDistribution(stage, false);
    this.stageChanged();
  }

  applyEqualDistribution(stage: EditableDailyStage, markChanged = true): void {
    if (stage.compensationMode !== 'SharedPercentage' || !stage.workers.length) return;
    const ordered = [...stage.workers].sort((left, right) => `${left.workerCode}|${left.workerId}`.localeCompare(`${right.workerCode}|${right.workerId}`));
    const base = Math.floor((100 / ordered.length) * 10_000) / 10_000;
    const remainingUnits = Math.round((100 - base * ordered.length) * 10_000);
    ordered.forEach((worker, index) => worker.percentage = Number((base + (index < remainingUnits ? 0.0001 : 0)).toFixed(4)));
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
        error: error => this.error = this.formValidation.serverMessage(error, 'تعذر احتساب معاينة تشغيل اليوم.')
      });
  }

  saveDailyDraft(): void {
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
    return !stage.originalWorkerIds.includes(worker.workerId) || !worker.isPresent;
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

  attendanceLabel(worker: DailyProductionWorker): string {
    if (worker.attendanceStatus === 'Present') return 'حاضر';
    if (worker.attendanceStatus === 'Absent') return 'غائب';
    return 'لا توجد بصمة مصدر';
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

  private toEditableStage(stage: DailyProductionStage): EditableDailyStage {
    return {
      ...stage,
      originalWorkerIds: stage.workers.map(worker => worker.workerId),
      workers: stage.workers.map(worker => this.toEditableWorker(worker))
    };
  }

  private toEditableWorker(worker: DailyProductionWorker): EditableDailyWorker {
    return {
      ...worker,
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
      if (!stage.workers.length) messages.push(`${stage.stageCode}: أضف عاملًا واحدًا على الأقل أو عالج نقص التسكين.`);
      if (stage.compensationMode === 'SharedPercentage' && stage.workers.length) {
        const invalid = stage.workers.some(worker => worker.percentage === null || worker.percentage <= 0);
        const total = stage.workers.reduce((sum, worker) => sum + (worker.percentage ?? 0), 0);
        if (invalid || Math.abs(total - 100) > 0.000001) messages.push(`${stage.stageCode}: يجب أن يساوي مجموع نسب العمال 100٪ تمامًا.`);
      }
      if (stage.compensationMode === 'FixedAmount' && stage.workers.some(worker => worker.fixedAmount === null || worker.fixedAmount < 0)) {
        messages.push(`${stage.stageCode}: أدخل قيمة ثابتة صالحة لكل عامل.`);
      }
      stage.workers.filter(worker => this.workerNeedsOverride(stage, worker)).forEach(worker => {
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
    const workers: DailyProductionWorkerInput[] = stage.workers.map(worker => ({
      workerId: worker.workerId,
      percentage: stage.compensationMode === 'SharedPercentage' ? worker.percentage : null,
      fixedAmount: stage.compensationMode === 'FixedAmount' ? worker.fixedAmount : null,
      notes: worker.notes.trim() || null,
      manualOverrideReason: this.workerNeedsOverride(stage, worker) ? worker.manualOverrideReason.trim() || null : null,
      inputQuantity: null
    }));
    return { productModelStageId: stage.productModelStageId, workers };
  }

  private invalidatePreview(incrementRevision = true): void {
    if (incrementRevision) this.revision++;
    this.preview = null;
    this.previewRevision = -1;
    this.savedDraft = null;
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
