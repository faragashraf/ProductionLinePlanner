import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, ParamMap } from '@angular/router';
import { Subject, TimeoutError, distinctUntilChanged, finalize, forkJoin, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { FormSubmissionValidationService, RequiredFieldRule } from '../../shared/forms/form-submission-validation.service';
import { createClientRequestId } from '../../core/utils/client-request-id';
import { PermissionService } from '../../core/services/permission.service';
import { AttendanceApiService, AttendanceSyncResult, AttendanceTodaySnapshot } from '../../core/services/attendance-api.service';
import {
  AssignmentWorkflowWorker,
  AssignmentsApiService,
  SubStageWorkerContext
} from '../../core/services/assignments-api.service';
import {
  FactoryItem,
  MainStageOption,
  ManufacturingMasterDataApiService,
  ProductionLineOption,
  SubStageOption
} from '../../core/services/manufacturing-master-data-api.service';
import {
  DailyProductionCostReportRow,
  ProductModelOption,
  ProductModelStageOption,
  ProductionCostRecordingApiService,
  ProductionDayReview,
  ProductionOrder,
  ProductProductionReadiness,
  ProductStageReadiness,
  RealDataIntakeApplyResult,
  RealDataIntakePreview,
  StageProductionRecord
} from '../../core/services/production-cost-recording-api.service';

type AssignmentMode = 'default' | 'temporary';

interface ProductionRecordingRouteContext {
  factoryId: string;
  productionLineId: string;
  mainStageId: string;
  subStageId: string;
}

@Component({ selector: 'app-production-cost-recording-page', templateUrl: './production-cost-recording-page.component.html', styleUrls: ['./production-cost-recording-page.component.scss'] })
export class ProductionCostRecordingPageComponent implements OnInit, OnDestroy {
  orders: ProductionOrder[] = [];
  records: StageProductionRecord[] = [];
  report: DailyProductionCostReportRow[] = [];
  models: ProductModelOption[] = [];
  modelStages: ProductModelStageOption[] = [];
  factories: FactoryItem[] = [];
  productionLines: ProductionLineOption[] = [];
  mainStages: MainStageOption[] = [];
  subStages: SubStageOption[] = [];
  workerContext: SubStageWorkerContext | null = null;
  productReadiness: ProductProductionReadiness | null = null;

  selectedFactoryId = '';
  selectedProductionLineId = '';
  selectedMainStageId = '';
  selectedSubStageId = '';
  workerSearch = '';
  loading = true;
  loadingLines = false;
  loadingMainStages = false;
  loadingSubStages = false;
  loadingWorkers = false;
  workerContextError = '';
  productReadinessError = '';
  productReadinessLoading = false;
  showReadinessProblems = false;
  saving = false;
  assignmentSaving = false;
  error = '';
  assignmentSuccess = '';
  draftContextUnavailable = '';
  restoringDraftContext = false;
  unassignDialogVisible = false;
  moveDialogVisible = false;
  replaceDialogVisible = false;
  pendingUnassignWorker: AssignmentWorkflowWorker | null = null;
  movingWorker: AssignmentWorkflowWorker | null = null;
  replacingWorker: AssignmentWorkflowWorker | null = null;
  moveFactoryId = '';
  moveProductionLineId = '';
  moveMainStageId = '';
  moveSubStageId = '';
  moveLines: ProductionLineOption[] = [];
  moveMainStages: MainStageOption[] = [];
  moveSubStages: SubStageOption[] = [];
  loadingMoveLines = false;
  loadingMoveMainStages = false;
  loadingMoveSubStages = false;
  attendanceSnapshot: AttendanceTodaySnapshot | null = null;
  attendanceSyncResult: AttendanceSyncResult | null = null;
  attendanceLoading = false;
  attendanceSyncing = false;
  attendanceError = '';
  attendanceSuccess = '';
  lastSuccessfulAttendanceSyncAt: string | null = null;
  lastSuccessfulAttendanceSyncDate: string | null = null;
  intakeStagesFile: File | null = null;
  intakeSalaryFile: File | null = null;
  intakeProductionFile: File | null = null;
  intakePreview: RealDataIntakePreview | null = null;
  intakeResult: RealDataIntakeApplyResult | null = null;
  intakeLoading = false;
  intakeError = '';
  importedDayReview: ProductionDayReview | null = null;
  importedDayReviewLoading = false;
  importedDayResolutionReason = '';
  importedAllocationOverrideReasons: Record<string, string> = {};
  preview: StageProductionRecord | null = null;
  previewIsFresh = false;
  previewIsStale = false;
  previewStaleMessage = '';
  productionApprovalCancellationDialogVisible = false;
  pendingProductionApprovalCancellation: StageProductionRecord | null = null;
  recordActionSuccess = '';
  assignmentDraftWarning = '';
  assignmentDraftUpdateMode: 'assignment-only' | 'draft-too' = 'assignment-only';
  orderSearch = '';
  orderStatusFilter = '';
  editingOrderId = '';
  editingRecordId = '';
  recordOrderFilter = '';
  recordStatusFilter = '';
  recordDateFilter = '';
  private hierarchyRequestVersion = 0;
  private stageRequestVersion = 0;
  private workerContextRequestVersion = 0;
  private attendanceRequestVersion = 0;
  private draftRestoreRequestVersion = 0;
  private moveHierarchyRequestVersion = 0;
  private routeContextRestoreRequestVersion = 0;
  private productReadinessRequestVersion = 0;
  private readonly destroy$ = new Subject<void>();

  readonly today = this.egyptToday();
  readonly orderForm = this.fb.group({
    orderNumber: ['', Validators.required],
    productModelId: ['', Validators.required],
    productionLineId: ['', Validators.required],
    productionDate: [this.today, Validators.required],
    plannedQuantity: [null as number | null, [Validators.required, Validators.min(0.001)]],
    notes: ['']
  });
  readonly recordForm = this.fb.group({
    productionOrderId: ['', Validators.required],
    productModelStageId: ['', Validators.required],
    productionDate: [this.today, Validators.required],
    producedQuantity: [null as number | null, [Validators.required, Validators.min(0)]],
    acceptedQuantity: [null as number | null, [Validators.required, Validators.min(0)]],
    rejectedQuantity: [0, [Validators.required, Validators.min(0)]],
    clientRequestId: [''],
    concurrencyToken: [''],
    notes: [''],
    workers: this.fb.array([])
  });
  readonly assignmentForm = this.fb.group({
    workerId: ['', Validators.required],
    mode: ['default' as AssignmentMode, Validators.required],
    reason: [''],
    startAtLocal: [''],
    endAtLocal: ['']
  });
  readonly productionApprovalCancellationForm = this.fb.group({
    reason: ['', [Validators.required, Validators.maxLength(500)]]
  });
  readonly intakeForm = this.fb.group({
    factoryName: ['المصنع الرئيسي', Validators.required],
    productionLineName: ['خط الخياطه', Validators.required],
    productName: ['جرومان', Validators.required],
    quantityJuly11: [751, [Validators.required, Validators.min(0.001)]],
    quantityJuly12: [663, [Validators.required, Validators.min(0.001)]],
    quantityJuly13: [769, [Validators.required, Validators.min(0.001)]]
  });

  constructor(
    private readonly fb: FormBuilder,
    private readonly api: ProductionCostRecordingApiService,
    private readonly masterData: ManufacturingMasterDataApiService,
    private readonly assignments: AssignmentsApiService,
    private readonly attendance: AttendanceApiService,
    private readonly permissionService: PermissionService,
    private readonly route: ActivatedRoute,
    private readonly formSubmissionValidation: FormSubmissionValidationService
  ) {}

  ngOnInit(): void {
    this.reload();
    this.api.listModels().subscribe({ next: models => this.models = models.filter(model => model.isActive), error: error => this.handleError(error, 'تعذر تحميل موديلات الإنتاج.') });
    this.route.queryParamMap
      .pipe(takeUntil(this.destroy$))
      .subscribe(params => {
        const context = this.routeContextFrom(params);
        if (context) {
          this.restoreRouteContext(context);
          return;
        }

        this.loadFactories();
      });
    this.recordForm.controls.productionDate.valueChanges
      .pipe(distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => {
        if (!this.showWorkerPanel) return;
        this.loadAttendanceSummary();
        if (this.canViewAssignments && this.selectedSubStageId) this.loadWorkerContext(this.selectedSubStageId);
        this.refreshProductReadiness();
      });
    this.recordForm.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.invalidatePreviewForChangedDraft());
    this.orderForm.controls.productModelId.valueChanges
      .pipe(distinctUntilChanged(), takeUntil(this.destroy$))
      .subscribe(() => this.refreshProductReadiness());
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get workers(): FormArray { return this.recordForm.controls.workers; }
  get canRecord(): boolean { return this.permissionService.hasPermission(PERMISSIONS.production.record); }
  get canApprove(): boolean { return this.permissionService.hasPermission(PERMISSIONS.production.approve); }
  get canViewAssignments(): boolean { return this.permissionService.hasPermission(PERMISSIONS.assignments.view); }
  get canManageAssignments(): boolean { return this.permissionService.hasPermission(PERMISSIONS.assignments.manage); }
  get canViewAttendance(): boolean { return this.permissionService.hasPermission(PERMISSIONS.attendance.view); }
  get canSyncAttendance(): boolean { return this.permissionService.hasPermission(PERMISSIONS.attendance.sync); }
  get isRecordReadOnly(): boolean { return !!this.editingRecordId && this.recordForm.disabled; }
  get isDraftContextUnavailable(): boolean { return !!this.draftContextUnavailable; }
  get hierarchyComplete(): boolean { return !!this.selectedFactoryId && !!this.selectedProductionLineId && !!this.selectedMainStageId && !!this.selectedSubStageId; }
  get showWorkerPanel(): boolean { return !!this.selectedSubStageId; }
  get selectedProductionDate(): string { return this.recordForm.controls.productionDate.value || this.today; }
  get isSelectedProductionDateToday(): boolean { return this.selectedProductionDate === this.today; }
  get attendanceDate(): string { return this.selectedProductionDate; }
  get activeAttendanceWorkersCount(): number { return this.workerContext?.activeServiceWorkersCount ?? this.attendanceSnapshot?.items.length ?? 0; }
  get workersWithAttendanceCount(): number { return this.workerContext?.workersWithAttendanceDataCount ?? this.attendanceSnapshot?.items.filter(worker => worker.attendanceStatus !== 'Unassigned').length ?? 0; }
  get actualCheckInWorkersCount(): number { return this.workerContext?.actualCheckInWorkersCount ?? this.attendanceSnapshot?.items.filter(worker => worker.attendanceStatus === 'Present' || worker.attendanceStatus === 'Late').length ?? 0; }
  get noSourceCheckInWorkersCount(): number { return this.workerContext?.noSourceCheckInWorkersCount ?? 0; }
  get availableWorkersCount(): number { return this.workerContext?.availableWorkers.length ?? 0; }
  get orderMissingRequirements(): string[] {
    return this.formSubmissionValidation.missingMessages(this.orderForm, this.orderRequiredFields, this.selectedProductionLineId ? [] : ['خط الإنتاج مطلوب']);
  }
  get recordMissingRequirements(): string[] {
    return this.recordSubmissionMessages(true);
  }
  get assignmentMissingRequirements(): string[] {
    if (this.unassignDialogVisible) {
      return this.formSubmissionValidation.missingMessages(this.assignmentForm, this.assignmentRequiredFields(false, true));
    }
    if (this.moveDialogVisible) {
      const includeEnd = this.movingWorker?.assignmentType !== 'Default';
      return this.formSubmissionValidation.missingMessages(
        this.assignmentForm,
        this.assignmentRequiredFields(false, true, true, includeEnd),
        this.moveSubStageId ? [] : ['المرحلة الفرعية للوجهة مطلوبة']
      );
    }
    if (this.replaceDialogVisible) {
      const start = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
      const end = this.toUtc(this.assignmentForm.controls.endAtLocal.value);
      return this.formSubmissionValidation.missingMessages(
        this.assignmentForm,
        this.assignmentRequiredFields(true, true, true, true),
        end && start && end > start ? [] : ['أدخل فترة استبدال صالحة']
      );
    }

    const worker = this.selectedAssignmentWorker;
    const mode = this.assignmentForm.controls.mode.value as AssignmentMode;
    return this.formSubmissionValidation.missingMessages(
      this.assignmentForm,
      this.assignmentSubmissionRules(worker, mode),
      this.assignmentSubmissionMessages(worker, mode)
    );
  }
  get unavailableWorkersCount(): number { return this.workerContext?.unavailableWorkersCount ?? 0; }
  get hasAttendanceRecords(): boolean { return this.workersWithAttendanceCount > 0; }
  get hasActualCheckIns(): boolean { return this.actualCheckInWorkersCount > 0; }
  get showAttendanceEmptyState(): boolean { return this.canViewAttendance && !this.attendanceLoading && !this.attendanceError && !this.hasAttendanceRecords; }
  get attendanceStatus(): 'ready' | 'warning' | 'critical' | 'info' {
    if (this.attendanceSyncing || this.attendanceLoading) return 'info';
    if (this.attendanceError) return 'critical';
    if (this.hasActualCheckIns) return 'ready';
    return this.canViewAttendance ? 'warning' : 'info';
  }
  get attendanceStatusLabel(): string {
    if (this.attendanceSyncing) return 'جارٍ تحديث الحضور';
    if (this.attendanceLoading) return 'جارٍ تحميل الحضور';
    if (this.attendanceError) return 'تعذر تحديث حالة الحضور';
    if (this.hasActualCheckIns) return 'بصمات حضور مؤكدة';
    if (this.hasAttendanceRecords) return 'توجد سجلات محلية بلا بصمة مؤكدة';
    return this.canViewAttendance ? 'لا توجد بيانات حضور' : 'حالة الحضور حسب الصلاحيات';
  }
  get lastSuccessfulAttendanceSyncLabel(): string {
    return this.lastSuccessfulAttendanceSyncAt
      ? new Intl.DateTimeFormat('ar-EG', { timeZone: 'Africa/Cairo', hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date(this.lastSuccessfulAttendanceSyncAt))
      : '';
  }
  get assignmentCandidates(): AssignmentWorkflowWorker[] {
    const needle = this.workerSearch.trim().toLocaleLowerCase('ar');
    return (this.workerContext?.presentWorkers ?? [])
      .filter(worker => worker.effectiveSubStageId !== this.selectedSubStageId)
      .filter(worker => !needle || `${worker.employeeCode} ${worker.fullName}`.toLocaleLowerCase('ar').includes(needle));
  }
  get currentWorkers(): AssignmentWorkflowWorker[] { return this.workerContext?.currentWorkers ?? []; }
  get recordingWorkers(): AssignmentWorkflowWorker[] { return this.currentWorkers.filter(worker => this.isRecordableWorker(worker)); }
  get selectedOrder(): ProductionOrder | undefined { return this.orders.find(order => order.id === this.recordForm.controls.productionOrderId.value); }
  get selectedProductModelId(): string { return this.selectedOrder?.productModelId || this.orderForm.controls.productModelId.value || ''; }
  get hasProductReadinessContext(): boolean {
    return !!this.selectedFactoryId && !!this.selectedProductionLineId && !!this.selectedProductModelId && !!this.selectedProductionDate;
  }
  get selectedProductLabel(): string {
    const modelId = this.selectedProductModelId;
    const model = this.models.find(item => item.id === modelId);
    return model ? `${model.code} — ${model.name}` : this.selectedOrder?.productModelCode || 'غير محدد';
  }
  get selectedFactoryLabel(): string { const factory = this.factories.find(item => item.id === this.selectedFactoryId); return factory ? `${factory.code} — ${factory.name}` : 'غير محدد'; }
  get selectedProductionLineLabel(): string { const line = this.productionLines.find(item => item.id === this.selectedProductionLineId); return line ? `${line.lineCode || '-'} — ${line.name}` : 'غير محدد'; }
  get selectedMainStageLabel(): string { const stage = this.mainStages.find(item => item.id === this.selectedMainStageId); return stage ? stage.name : 'غير محدد'; }
  get selectedSubStageLabel(): string { const stage = this.subStages.find(item => item.id === this.selectedSubStageId); return stage ? `${stage.code} — ${stage.name}` : 'غير محدد'; }
  get selectedStageReadiness(): ProductStageReadiness | undefined {
    const stageId = this.recordForm.controls.productModelStageId.value;
    return this.productReadiness?.stages.find(stage => stage.productModelStageId === stageId || stage.subStageId === this.selectedSubStageId);
  }
  get selectedAssignmentWorker(): AssignmentWorkflowWorker | undefined { return (this.workerContext?.presentWorkers ?? []).find(worker => worker.workerId === this.assignmentForm.controls.workerId.value); }
  get canPreviewIntake(): boolean { return this.canRecord && !!this.intakeStagesFile && !!this.intakeSalaryFile && !!this.intakeProductionFile && this.intakeForm.valid && !this.intakeLoading; }
  get hasOpenImportedDayIssues(): boolean { return this.importedDayReview?.issues.some(issue => issue.status === 'Open') ?? false; }

  loadFactories(): void {
    this.masterData.factories().subscribe({
      next: factories => this.factories = factories.filter(factory => factory.isActive),
      error: error => this.handleError(error, 'تعذر تحميل المصانع المتاحة.')
    });
  }

  syncAttendanceToday(): void {
    if (this.attendanceSyncing || !this.canSyncAttendance) return;

    this.attendanceSyncing = true;
    this.attendanceError = '';
    this.attendanceSuccess = '';
    const productionDate = this.selectedProductionDate;
    const selectedDateSync = this.attendance.syncForProductionDate;
    (selectedDateSync ? selectedDateSync.call(this.attendance, productionDate) : this.attendance.syncToday()).pipe(finalize(() => this.attendanceSyncing = false)).subscribe({
      next: result => {
        this.attendanceSyncResult = result;
        this.lastSuccessfulAttendanceSyncAt = new Date().toISOString();
        this.lastSuccessfulAttendanceSyncDate = productionDate;
        this.attendanceSuccess = result.sourceCheckInsCount === 0
          ? productionDate === this.today
            ? 'اكتملت مزامنة حضور اليوم، لكن لم يتم العثور على سجلات بصمة لهذا اليوم.'
            : `اكتملت مزامنة حضور تاريخ الإنتاج ${productionDate}، لكن لم يتم العثور على سجلات بصمة.`
          : productionDate === this.today
            ? `تم تحديث حضور اليوم: تمت مطابقة ${result.matchedWorkersCount} عاملًا.`
            : `تم تحديث حضور تاريخ الإنتاج ${productionDate}: تمت مطابقة ${result.matchedWorkersCount} عاملًا.`;
        this.refreshAfterAttendanceSyncAttempt();
      },
      error: error => {
        this.attendanceError = this.attendanceSyncFailureMessage(error);
        // A browser timeout aborts only the client request. Perform one read-only
        // refresh in case the server completed after the browser disconnected.
        if (this.isAttendanceSyncTimeout(error)) this.refreshAfterAttendanceSyncAttempt(true);
      }
    });
  }

  selectIntakeFile(kind: 'stages' | 'salary' | 'production', event: Event): void {
    const file = (event.target as HTMLInputElement).files?.item(0) ?? null;
    if (kind === 'stages') this.intakeStagesFile = file;
    if (kind === 'salary') this.intakeSalaryFile = file;
    if (kind === 'production') this.intakeProductionFile = file;
    this.intakePreview = null;
    this.intakeResult = null;
    this.intakeError = '';
  }

  previewRealDataIntake(): void {
    if (!this.canPreviewIntake) return;
    this.intakeLoading = true;
    this.intakeError = '';
    this.intakeResult = null;
    this.api.previewRealDataIntake(this.realDataIntakeFormData()).pipe(finalize(() => this.intakeLoading = false)).subscribe({
      next: preview => this.intakePreview = preview,
      error: error => this.intakeError = error.message || 'تعذر إنشاء معاينة الاستيراد. لم يتم حفظ أي بيانات.'
    });
  }

  applyRealDataIntake(): void {
    if (!this.intakePreview?.canApply || this.intakeLoading) return;
    if (!window.confirm('سيتم تطبيق المعاينة الحالية كمسودات قابلة للمراجعة. هل تؤكد التطبيق؟')) return;
    this.intakeLoading = true;
    this.intakeError = '';
    this.api.applyRealDataIntake(this.realDataIntakeFormData()).pipe(finalize(() => this.intakeLoading = false)).subscribe({
      next: result => { this.intakeResult = result; this.reload(); },
      error: error => this.intakeError = error.message || 'تعذر تطبيق الاستيراد. تم التراجع عن أي تغيير غير مكتمل.'
    });
  }

  openImportedDayReview(order: ProductionOrder): void {
    if (!order.isImported || this.importedDayReviewLoading) return;
    this.importedDayReviewLoading = true;
    this.error = '';
    this.api.getProductionDayReview(order.id).pipe(finalize(() => this.importedDayReviewLoading = false)).subscribe({
      next: review => { this.importedDayReview = review; this.importedDayResolutionReason = ''; this.importedAllocationOverrideReasons = {}; },
      error: error => this.handleError(error, 'تعذر تحميل مراجعة يوم الإنتاج المستورد.')
    });
  }

  markImportedStageNotOperated(productModelStageId: string): void {
    if (!this.importedDayReview || !this.importedDayResolutionReason.trim() || this.importedDayReviewLoading) return;
    this.importedDayReviewLoading = true;
    this.api.markStageNotOperated(this.importedDayReview.productionOrderId, productModelStageId, this.importedDayResolutionReason.trim()).pipe(finalize(() => this.importedDayReviewLoading = false)).subscribe({
      next: review => { this.importedDayReview = review; this.importedDayResolutionReason = ''; },
      error: error => this.handleError(error, 'تعذر حفظ قرار المرحلة غير المشغلة.')
    });
  }

  participantOverrideKey(stageProductionRecordId: string, workerId: string): string { return `${stageProductionRecordId}:${workerId}`; }

  setImportedParticipantOverride(stageProductionRecordId: string, workerId: string): void {
    if (!this.importedDayReview || !this.canApprove || this.importedDayReviewLoading) return;
    const key = this.participantOverrideKey(stageProductionRecordId, workerId);
    const reason = this.importedAllocationOverrideReasons[key]?.trim();
    if (!reason) { this.error = 'سبب تفويض الحضور أو التعيين إلزامي.'; return; }
    if (!window.confirm('سيتم حفظ سبب التفويض في لقطة اليوم المستورد. متابعة؟')) return;
    this.importedDayReviewLoading = true;
    this.api.setParticipantOverride(this.importedDayReview.productionOrderId, stageProductionRecordId, workerId, reason).pipe(finalize(() => this.importedDayReviewLoading = false)).subscribe({
      next: review => { this.importedDayReview = review; this.importedAllocationOverrideReasons = {}; },
      error: error => this.handleError(error, 'تعذر حفظ تفويض المشارك.')
    });
  }

  approveImportedDay(): void {
    if (!this.importedDayReview || this.importedDayReviewLoading || !this.canApprove) return;
    this.importedDayReviewLoading = true;
    this.api.approveProductionDay(this.importedDayReview.productionOrderId).pipe(finalize(() => this.importedDayReviewLoading = false)).subscribe({
      next: review => { this.importedDayReview = review; this.reload(); },
      error: error => this.handleError(error, 'لا يمكن اعتماد اليوم قبل حل مشكلات المراجعة والحضور.')
    });
  }

  selectFactory(factoryId: string): void {
    if (factoryId === this.selectedFactoryId) return;
    ++this.routeContextRestoreRequestVersion;
    if (!this.confirmContextReset()) return;
    this.selectedFactoryId = factoryId;
    this.selectedProductionLineId = '';
    this.selectedMainStageId = '';
    this.selectedSubStageId = '';
    this.productionLines = [];
    this.mainStages = [];
    this.subStages = [];
    this.clearStageContext();
    this.clearProductReadiness();
    this.clearOrderDraft();
    if (!factoryId) return;

    const version = ++this.hierarchyRequestVersion;
    this.loadingLines = true;
    this.masterData.productionLines().pipe(finalize(() => this.loadingLines = false)).subscribe({
      next: lines => { if (version === this.hierarchyRequestVersion) this.productionLines = lines.filter(line => line.isActive && line.factoryId === factoryId); },
      error: error => this.handleError(error, 'تعذر تحميل خطوط الإنتاج للمصنع المحدد.')
    });
  }

  selectProductionLine(productionLineId: string): void {
    if (productionLineId === this.selectedProductionLineId) return;
    ++this.routeContextRestoreRequestVersion;
    if (!this.confirmContextReset()) return;
    this.selectedProductionLineId = productionLineId;
    this.selectedMainStageId = '';
    this.selectedSubStageId = '';
    this.mainStages = [];
    this.subStages = [];
    this.clearStageContext();
    this.clearProductReadiness();
    this.clearOrderDraft();
    if (!productionLineId) return;

    const version = ++this.hierarchyRequestVersion;
    this.loadingMainStages = true;
    this.masterData.mainStagesForLine(productionLineId).pipe(finalize(() => this.loadingMainStages = false)).subscribe({
      next: stages => { if (version === this.hierarchyRequestVersion) this.mainStages = stages.filter(stage => stage.isActive && stage.productionLineId === productionLineId); },
      error: error => this.handleError(error, 'تعذر تحميل المراحل الرئيسية للخط المحدد.')
    });
  }

  selectMainStage(mainStageId: string): void {
    if (mainStageId === this.selectedMainStageId) return;
    ++this.routeContextRestoreRequestVersion;
    if (!this.confirmContextReset()) return;
    this.selectedMainStageId = mainStageId;
    this.selectedSubStageId = '';
    this.subStages = [];
    this.clearStageContext();
    if (!mainStageId) return;

    const version = ++this.hierarchyRequestVersion;
    this.loadingSubStages = true;
    this.masterData.subStagesForMainStage(mainStageId).pipe(finalize(() => this.loadingSubStages = false)).subscribe({
      next: stages => { if (version === this.hierarchyRequestVersion) this.subStages = stages.filter(stage => stage.isActive && stage.mainStageId === mainStageId); },
      error: error => this.handleError(error, 'تعذر تحميل المراحل الفرعية للمرحلة المحددة.')
    });
  }

  selectSubStage(subStageId: string): void {
    if (subStageId === this.selectedSubStageId) return;
    ++this.routeContextRestoreRequestVersion;
    if (!this.confirmContextReset()) return;
    this.selectedSubStageId = subStageId;
    this.clearRecordContext();
    this.resetWorkerPanelState();
    this.assignmentSuccess = '';
    this.assignmentForm.reset({ workerId: '', mode: 'default', reason: '', startAtLocal: '', endAtLocal: '' });
    if (!subStageId) return;
    this.loadAttendanceSummary();
    if (this.canViewAssignments) this.loadWorkerContext(subStageId);
    this.refreshProductReadiness();
  }

  reload(): void {
    this.loading = true;
    this.error = '';
    this.api.listOrders().pipe(finalize(() => this.loading = false)).subscribe({
      next: orders => { this.orders = orders; this.loadRecords(); },
      error: error => this.handleError(error, 'تعذر تحميل أوامر الإنتاج.')
    });
  }

  loadRecords(): void {
    this.api.listRecords().subscribe({ next: records => this.records = records, error: error => this.handleError(error, 'تعذر تحميل سجلات الإنتاج.') });
    this.api.dailyReport(this.today, this.today).subscribe({ next: report => this.report = report, error: error => this.handleError(error, 'تعذر تحميل التقرير.') });
  }

  filteredOrders(): ProductionOrder[] {
    const needle = this.orderSearch.trim().toLowerCase();
    return this.orders.filter(order => (!this.orderStatusFilter || order.status === this.orderStatusFilter) && (!needle || order.orderNumber.toLowerCase().includes(needle) || order.productModelCode.toLowerCase().includes(needle)));
  }

  workflowOrders(): ProductionOrder[] {
    return this.orders.filter(order => order.productionLineId === this.selectedProductionLineId);
  }

  filteredRecords(): StageProductionRecord[] {
    return this.records.filter(record => (!this.recordOrderFilter || record.productionOrderId === this.recordOrderFilter) && (!this.recordStatusFilter || record.status === this.recordStatusFilter) && (!this.recordDateFilter || record.productionDate === this.recordDateFilter));
  }

  orderNumber(record: StageProductionRecord): string { return this.orders.find(order => order.id === record.productionOrderId)?.orderNumber ?? '-'; }

  private readonly orderRequiredFields: readonly RequiredFieldRule[] = [
    { control: 'orderNumber', message: 'أمر الإنتاج مطلوب' },
    { control: 'productModelId', message: 'الموديل مطلوب' },
    { control: 'productionDate', message: 'تاريخ الإنتاج مطلوب' },
    { control: 'plannedQuantity', message: 'الكمية المخططة مطلوبة' }
  ];

  private readonly recordRequiredFields: readonly RequiredFieldRule[] = [
    { control: 'productionOrderId', message: 'أمر الإنتاج مطلوب' },
    { control: 'productModelStageId', message: 'المرحلة مطلوبة' },
    { control: 'productionDate', message: 'تاريخ الإنتاج مطلوب' },
    { control: 'producedQuantity', message: 'الكمية المنتجة مطلوبة' },
    { control: 'acceptedQuantity', message: 'الكمية المقبولة مطلوبة' },
    { control: 'rejectedQuantity', message: 'الكمية المرفوضة مطلوبة' }
  ];

  loadOrderStages(keepStage = false): void {
    const order = this.selectedOrder;
    if (!keepStage) {
      this.recordForm.controls.productModelStageId.setValue('');
      this.workers.clear();
    }
    this.modelStages = [];
    if (!order || order.productionLineId !== this.selectedProductionLineId) return;
    this.refreshProductReadiness();
    if (!this.selectedSubStageId) return;

    const version = ++this.stageRequestVersion;
    this.api.listModelStages(order.productModelId).subscribe({
      next: stages => {
        if (version === this.stageRequestVersion) {
          this.modelStages = stages.filter(stage => stage.isActive && stage.subStageId === this.selectedSubStageId);
          this.invalidatePreviewForChangedDraft();
          if (keepStage && !this.modelStages.some(stage => stage.id === this.recordForm.controls.productModelStageId.value)) this.recordForm.controls.productModelStageId.setValue('');
        }
      },
      error: error => this.handleError(error, 'تعذر تحميل مرحلة الموديل للمسار المحدد.')
    });
  }

  selectedStage(): ProductModelStageOption | undefined { return this.modelStages.find(stage => stage.id === this.recordForm.controls.productModelStageId.value); }
  canSelectWorker(workerId: string, index: number): boolean { return !this.workers.controls.some((control, current) => current !== index && control.get('workerId')?.value === workerId); }

  createOrder(): void {
    this.orderForm.controls.productionLineId.setValue(this.selectedProductionLineId);
    if (this.saving || !this.canRecord) return;
    const validation = this.formSubmissionValidation.validate(
      this.orderForm,
      this.orderRequiredFields,
      this.selectedProductionLineId ? [] : ['خط الإنتاج مطلوب']
    );
    if (!validation.valid) {
      this.error = validation.summary;
      return;
    }
    this.saving = true;
    const value = this.orderForm.getRawValue();
    const request = this.editingOrderId
      ? this.api.updateOrder(this.editingOrderId, { productionDate: value.productionDate, plannedQuantity: value.plannedQuantity, notes: value.notes })
      : this.api.createOrder(value);
    request.pipe(finalize(() => this.saving = false)).subscribe({
      next: () => { this.editingOrderId = ''; this.orderForm.reset({ productionLineId: this.selectedProductionLineId, productionDate: this.today }); this.reload(); },
      error: error => this.handleError(error, 'تعذر حفظ أمر الإنتاج.')
    });
  }

  activate(order: ProductionOrder): void {
    if (!this.canRecord || order.productionLineId !== this.selectedProductionLineId) return;
    this.saving = true;
    this.api.transitionOrder(order.id, 'activate').pipe(finalize(() => this.saving = false)).subscribe({ next: () => this.reload(), error: error => this.handleError(error, 'تعذر تفعيل الأمر.') });
  }

  editOrder(order: ProductionOrder): void {
    if (order.status !== 'Draft' || !this.canRecord || order.productionLineId !== this.selectedProductionLineId) return;
    this.editingOrderId = order.id;
    this.orderForm.reset({ orderNumber: order.orderNumber, productModelId: order.productModelId, productionLineId: order.productionLineId, productionDate: order.productionDate, plannedQuantity: order.plannedQuantity, notes: order.notes ?? '' });
  }

  transitionOrder(order: ProductionOrder, action: 'complete' | 'cancel'): void {
    if (!confirm(action === 'complete' ? 'إكمال أمر الإنتاج؟' : 'إلغاء أمر الإنتاج؟')) return;
    this.saving = true;
    this.api.transitionOrder(order.id, action).pipe(finalize(() => this.saving = false)).subscribe({ next: () => this.reload(), error: error => this.handleError(error, 'تعذر تغيير حالة الأمر.') });
  }

  newRecord(): void {
    if (!this.hierarchyComplete) {
      this.error = 'أكمل اختيار المصنع والخط والمرحلتين قبل بدء مسودة الإنتاج.';
      return;
    }
    this.editingRecordId = '';
    this.clearPreviewState();
    this.draftContextUnavailable = '';
    this.recordForm.enable();
    this.clearRecordContext();
  }

  openRecord(record: StageProductionRecord): void {
    this.saving = true;
    this.api.getRecord(record.id).subscribe({
      next: loaded => this.restoreDraftContext(loaded),
      error: error => { this.saving = false; this.handleError(error, 'تعذر فتح سجل الإنتاج.'); }
    });
  }

  addWorker(worker?: AssignmentWorkflowWorker): void {
    if (this.isRecordReadOnly || !this.ensureProductionContext()) return;
    const workerId = worker?.workerId ?? '';
    if (workerId && !this.recordingWorkers.some(option => option.workerId === workerId)) {
      this.error = 'لا يمكن إرفاق عامل إلا بعد أن يصبح ضمن التعيين الحالي للمرحلة.';
      return;
    }
    if (workerId && !this.canSelectWorker(workerId, -1)) {
      this.error = 'لا يمكن إضافة العامل أكثر من مرة في سجل الإنتاج نفسه.';
      return;
    }
    this.workers.push(this.workerGroup(workerId));
    this.recordForm.markAsDirty();
    this.invalidatePreviewForChangedDraft();
  }

  removeWorker(index: number): void {
    if (!this.isRecordReadOnly) {
      this.workers.removeAt(index);
      this.recordForm.markAsDirty();
      this.invalidatePreviewForChangedDraft();
    }
  }

  applyAssignment(): void {
    const worker = this.selectedAssignmentWorker;
    const mode = this.assignmentForm.controls.mode.value as AssignmentMode;
    const reason = this.assignmentForm.controls.reason.value?.trim() ?? '';
    if (this.assignmentSaving) return;
    const validation = this.formSubmissionValidation.validate(
      this.assignmentForm,
      this.assignmentSubmissionRules(worker, mode),
      this.assignmentSubmissionMessages(worker, mode)
    );
    if (!this.canManageAssignments || !this.selectedSubStageId || !worker || !validation.valid) {
      this.error = validation.summary || (!this.canManageAssignments
        ? 'لا تملك صلاحية حفظ تعيين العامل.'
        : 'اختر عاملًا حاضرًا مؤهلًا قبل حفظ التعيين.');
      return;
    }
    const result = mode === 'temporary'
      ? this.createTemporaryMove(worker, reason)
      : this.createCurrentAssignment(worker, reason);
    this.assignmentSaving = true;
    this.assignmentSuccess = '';
    result.pipe(finalize(() => this.assignmentSaving = false)).subscribe({
      next: () => {
        this.assignmentSuccess = mode === 'temporary' ? 'تم حفظ النقل المؤقت للمرحلة المحددة.' : 'تم حفظ التعيين الحالي للمرحلة المحددة.';
        this.assignmentForm.reset({ workerId: '', mode: 'default', reason: '', startAtLocal: '', endAtLocal: '' });
        this.loadWorkerContext(this.selectedSubStageId);
        this.refreshProductReadiness();
      },
      error: error => this.handleError(error, 'تعذر حفظ تعيين العامل.')
    });
  }

  openUnassignDialog(worker: AssignmentWorkflowWorker): void {
    if (!this.canManageAssignments || !this.selectedSubStageId || !worker.assignmentId || this.assignmentSaving) return;
    if (!this.confirmAssignmentActionReset()) return;
    this.pendingUnassignWorker = worker;
    this.prepareDraftParticipantImpact(worker, 'remove');
    this.assignmentForm.reset({ workerId: worker.workerId, mode: worker.assignmentType === 'Default' ? 'default' : 'temporary', reason: '', startAtLocal: '', endAtLocal: '' });
    this.unassignDialogVisible = true;
  }

  openReplaceDialog(worker: AssignmentWorkflowWorker): void {
    if (!this.canManageAssignments || !worker.assignmentId || this.assignmentSaving) return;
    if (!this.confirmAssignmentActionReset()) return;
    this.replacingWorker = worker;
    this.prepareDraftParticipantImpact(worker, 'replace');
    const start = new Date();
    const end = new Date(start.getTime() + 8 * 60 * 60 * 1000);
    this.assignmentForm.reset({ workerId: '', mode: 'temporary', reason: '', startAtLocal: this.toLocalInput(start), endAtLocal: this.toLocalInput(end) });
    this.replaceDialogVisible = true;
  }

  closeReplaceDialog(): void {
    if (this.assignmentSaving) return;
    this.replaceDialogVisible = false;
    this.replacingWorker = null;
    this.clearDraftParticipantImpact();
  }

  selectReplacementWorker(worker: AssignmentWorkflowWorker): void {
    this.assignmentForm.controls.workerId.setValue(worker.workerId);
    this.assignmentForm.controls.workerId.markAsDirty();
  }

  confirmReplacement(): void {
    const replacedWorker = this.replacingWorker;
    const replacementWorker = this.selectedAssignmentWorker;
    const reason = this.assignmentForm.controls.reason.value?.trim() ?? '';
    const startAtUtc = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const endAtUtc = this.toUtc(this.assignmentForm.controls.endAtLocal.value);
    const validation = this.formSubmissionValidation.validate(
      this.assignmentForm,
      this.assignmentRequiredFields(true, true, true, true),
      endAtUtc && startAtUtc && endAtUtc > startAtUtc ? [] : ['أدخل فترة استبدال صالحة']
    );
    if (!replacedWorker || !replacementWorker || !this.selectedSubStageId || !validation.valid) {
      this.error = validation.summary || 'اختر عاملًا حاضرًا بديلًا وأدخل سببًا وفترة استبدال صالحة.';
      return;
    }

    this.assignmentSaving = true;
    this.assignmentSuccess = '';
    this.assignments.createReplacementAssignment({
      replacementWorkerId: replacementWorker.workerId,
      replacedWorkerId: replacedWorker.workerId,
      subStageId: this.selectedSubStageId,
      startAtUtc: startAtUtc!,
      endAtUtc: endAtUtc!,
      reason
    }).pipe(finalize(() => this.assignmentSaving = false)).subscribe({
      next: () => {
        this.applyDraftParticipantImpact(replacedWorker.workerId, replacementWorker.workerId);
        this.assignmentSuccess = 'تم حفظ الاستبدال المؤقت مع سببه وفترته. لم تتغير أي دفعة إنتاج سابقة.';
        this.replaceDialogVisible = false;
        this.replacingWorker = null;
        this.clearDraftParticipantImpact();
        this.assignmentForm.reset({ workerId: '', mode: 'default', reason: '', startAtLocal: '', endAtLocal: '' });
        this.loadWorkerContext(this.selectedSubStageId);
        this.refreshProductReadiness();
      },
      error: error => this.handleError(error, 'تعذر حفظ الاستبدال. حدّث حالة العمال ثم حاول مرة أخرى.')
    });
  }

  closeUnassignDialog(): void {
    if (this.assignmentSaving) return;
    this.unassignDialogVisible = false;
    this.pendingUnassignWorker = null;
    this.clearDraftParticipantImpact();
  }

  confirmUnassign(): void {
    const worker = this.pendingUnassignWorker;
    const reason = this.assignmentForm.controls.reason.value?.trim() ?? '';
    const validation = this.formSubmissionValidation.validate(this.assignmentForm, this.assignmentRequiredFields(false, true));
    if (!worker || !this.selectedSubStageId || !worker.assignmentId || !validation.valid) {
      this.error = validation.summary || 'سبب إلغاء التعيين مطلوب.';
      return;
    }

    const request = worker.assignmentType === 'Default'
      ? this.assignments.removeDefaultAssignment(worker.workerId, this.selectedSubStageId, reason)
      : this.assignments.cancelTemporaryAssignment(worker.assignmentId, reason);
    this.assignmentSaving = true;
    this.assignmentSuccess = '';
    request.pipe(finalize(() => this.assignmentSaving = false)).subscribe({
      next: () => {
        this.applyDraftParticipantImpact(worker.workerId);
        this.assignmentSuccess = 'تم إلغاء التعيين الحالي وحُفظ السبب. لم تتغير أي دفعة إنتاج سابقة.';
        this.unassignDialogVisible = false;
        this.pendingUnassignWorker = null;
        this.clearDraftParticipantImpact();
        this.assignmentForm.reset({ workerId: '', mode: 'default', reason: '', startAtLocal: '', endAtLocal: '' });
        this.loadWorkerContext(this.selectedSubStageId);
        this.refreshProductReadiness();
      },
      error: error => this.handleError(error, 'تعذر إلغاء تعيين العامل. حدّث البيانات وحاول مرة أخرى.')
    });
  }

  openMoveDialog(worker: AssignmentWorkflowWorker): void {
    if (!this.canManageAssignments || !worker.assignmentId || !worker.effectiveSubStageId || this.assignmentSaving) return;
    if (!this.confirmAssignmentActionReset()) return;
    this.movingWorker = worker;
    this.prepareDraftParticipantImpact(worker, 'remove');
    const now = new Date();
    this.assignmentForm.reset({
      workerId: worker.workerId,
      mode: worker.assignmentType === 'Default' ? 'default' : 'temporary',
      reason: '',
      startAtLocal: this.toLocalInput(now),
      endAtLocal: worker.assignmentEndsAtUtc ? this.toLocalInput(new Date(worker.assignmentEndsAtUtc)) : ''
    });
    this.moveFactoryId = this.selectedFactoryId;
    this.moveProductionLineId = '';
    this.moveMainStageId = '';
    this.moveSubStageId = '';
    this.moveLines = [];
    this.moveMainStages = [];
    this.moveSubStages = [];
    this.moveDialogVisible = true;
    this.loadMoveLines(this.moveFactoryId);
  }

  closeMoveDialog(): void {
    if (this.assignmentSaving) return;
    this.moveDialogVisible = false;
    this.movingWorker = null;
    this.clearDraftParticipantImpact();
    ++this.moveHierarchyRequestVersion;
  }

  selectMoveFactory(factoryId: string): void {
    this.moveFactoryId = factoryId;
    this.moveProductionLineId = '';
    this.moveMainStageId = '';
    this.moveSubStageId = '';
    this.moveLines = [];
    this.moveMainStages = [];
    this.moveSubStages = [];
    this.loadMoveLines(factoryId);
  }

  selectMoveProductionLine(lineId: string): void {
    this.moveProductionLineId = lineId;
    this.moveMainStageId = '';
    this.moveSubStageId = '';
    this.moveMainStages = [];
    this.moveSubStages = [];
    if (!lineId) return;
    const version = ++this.moveHierarchyRequestVersion;
    this.loadingMoveMainStages = true;
    this.masterData.mainStagesForLine(lineId).pipe(finalize(() => this.loadingMoveMainStages = false)).subscribe({
      next: stages => { if (version === this.moveHierarchyRequestVersion) this.moveMainStages = stages.filter(stage => stage.isActive && stage.productionLineId === lineId); },
      error: error => this.handleError(error, 'تعذر تحميل مراحل خط النقل.')
    });
  }

  selectMoveMainStage(mainStageId: string): void {
    this.moveMainStageId = mainStageId;
    this.moveSubStageId = '';
    this.moveSubStages = [];
    if (!mainStageId) return;
    const version = ++this.moveHierarchyRequestVersion;
    this.loadingMoveSubStages = true;
    this.masterData.subStagesForMainStage(mainStageId).pipe(finalize(() => this.loadingMoveSubStages = false)).subscribe({
      next: stages => { if (version === this.moveHierarchyRequestVersion) this.moveSubStages = stages.filter(stage => stage.isActive && stage.mainStageId === mainStageId); },
      error: error => this.handleError(error, 'تعذر تحميل المراحل الفرعية لوجهة النقل.')
    });
  }

  confirmMove(): void {
    const worker = this.movingWorker;
    const reason = this.assignmentForm.controls.reason.value?.trim() ?? '';
    const effectiveAtUtc = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const temporaryEndAtUtc = worker?.assignmentType === 'Default' ? undefined : this.toUtc(this.assignmentForm.controls.endAtLocal.value);
    const validation = this.formSubmissionValidation.validate(
      this.assignmentForm,
      this.assignmentRequiredFields(false, true, true, worker?.assignmentType !== 'Default'),
      this.moveSubStageId ? [] : ['المرحلة الفرعية للوجهة مطلوبة']
    );
    if (!worker || !worker.assignmentId || !worker.effectiveSubStageId || !this.moveSubStageId || !validation.valid) {
      this.error = validation.summary || 'اختر مسار الوجهة وأدخل سببًا ووقت سريان صالحين للنقل.';
      return;
    }
    if (worker.effectiveSubStageId === this.moveSubStageId) {
      this.error = 'اختر مرحلة وجهة مختلفة عن التعيين الحالي.';
      return;
    }

    this.assignmentSaving = true;
    this.assignmentSuccess = '';
    this.assignments.moveCurrentAssignment({
      workerId: worker.workerId,
      sourceAssignmentId: worker.assignmentId,
      fromSubStageId: worker.effectiveSubStageId,
      toSubStageId: this.moveSubStageId,
      effectiveAtUtc: effectiveAtUtc!,
      ...(temporaryEndAtUtc ? { temporaryEndAtUtc } : {}),
      reason
    }).pipe(finalize(() => this.assignmentSaving = false)).subscribe({
      next: () => {
        const sourceSubStageId = this.selectedSubStageId;
        const destinationSubStageId = this.moveSubStageId;
        this.assignmentSuccess = 'تم نقل العامل وحفظ سبب ووقت السريان. لم تتغير أي مشاركة إنتاج تاريخية.';
        this.applyDraftParticipantImpact(worker.workerId);
        this.moveDialogVisible = false;
        this.movingWorker = null;
        this.clearDraftParticipantImpact();
        ++this.moveHierarchyRequestVersion;
        this.assignmentForm.reset({ workerId: '', mode: 'default', reason: '', startAtLocal: '', endAtLocal: '' });
        this.loadWorkerContext(sourceSubStageId);
        this.refreshProductReadiness();
        if (destinationSubStageId !== sourceSubStageId) this.refreshWorkerContext(destinationSubStageId);
      },
      error: error => this.handleError(error, 'تعذر نقل العامل بسبب تعارض في التعيين. حدّث البيانات وحاول مرة أخرى.')
    });
  }

  saveDraft(): void {
    if (this.saving || this.restoringDraftContext || this.isDraftContextUnavailable || !this.canRecord || this.isRecordReadOnly) return;
    const validation = this.validateRecordSubmission(true);
    if (!validation.valid) {
      this.error = this.isDraftContextUnavailable ? this.draftContextUnavailable : validation.summary;
      return;
    }
    this.saving = true;
    if (!this.editingRecordId && !this.recordForm.controls.clientRequestId.value) this.recordForm.controls.clientRequestId.setValue(this.newClientRequestId());
    const value = this.recordForm.getRawValue();
    const request = this.editingRecordId ? this.api.updateDraft(this.editingRecordId, value) : this.api.createDraft(value);
    request.pipe(finalize(() => this.saving = false)).subscribe({
      next: record => {
        this.applyRecord(record);
        this.editingRecordId = record.id;
        this.recordForm.controls.concurrencyToken.setValue(record.concurrencyToken);
        this.recordForm.markAsPristine();
        this.setFreshPreview(record);
        this.loadRecords();
      },
      error: error => this.handleError(error, 'تعذر حفظ مسودة الإنتاج.')
    });
  }

  calculatePreview(): void {
    if (this.saving) return;
    const validation = this.validateRecordSubmission(false);
    if (!validation.valid) {
      this.error = validation.summary;
      return;
    }
    const fingerprint = this.currentDraftFingerprint();
    this.saving = true;
    this.api.calculatePreview(this.recordForm.getRawValue()).pipe(finalize(() => this.saving = false)).subscribe({
      next: preview => {
        if (fingerprint === this.currentDraftFingerprint()) this.setFreshPreview(preview);
        else this.invalidatePreviewForChangedDraft();
      },
      error: error => this.handleError(error, 'تعذر حساب المعاينة.')
    });
  }

  approve(record: StageProductionRecord): void {
    if (this.saving || !this.canApprove || record.status !== 'Draft') return;
    if (!this.hasConsistentEntitlementTotal(record)) {
      this.error = 'لا يمكن اعتماد السجل لأن إجمالي المستحقات لا يطابق مجموع مستحقات العمال. أعد حساب المعاينة واحفظ المسودة من جديد.';
      return;
    }
    if (!confirm('سيتم تثبيت Snapshot والحسابات. هل تريد الاعتماد؟')) return;
    this.saving = true;
    this.api.approve(record.id, record.concurrencyToken).pipe(finalize(() => this.saving = false)).subscribe({ next: updated => { this.applyRecord(updated); this.preview = updated; this.loadRecords(); if (this.editingRecordId === updated.id) this.openRecord(updated); }, error: error => this.handleError(error, 'تعذر اعتماد السجل.') });
  }

  canApproveProductionRecord(record: StageProductionRecord): boolean {
    return this.canApprove && record.status === 'Draft' && this.hasConsistentEntitlementTotal(record);
  }

  openProductionApprovalCancellationDialog(record: StageProductionRecord): void {
    if (this.saving || !this.canApprove || record.status !== 'Approved') return;
    this.pendingProductionApprovalCancellation = record;
    this.productionApprovalCancellationForm.reset({ reason: '' });
    this.productionApprovalCancellationDialogVisible = true;
  }

  closeProductionApprovalCancellationDialog(): void {
    if (this.saving) return;
    this.productionApprovalCancellationDialogVisible = false;
    this.pendingProductionApprovalCancellation = null;
    this.productionApprovalCancellationForm.reset({ reason: '' });
  }

  confirmProductionApprovalCancellation(): void {
    const record = this.pendingProductionApprovalCancellation;
    const reason = this.productionApprovalCancellationForm.controls.reason.value?.trim() ?? '';
    const validation = this.formSubmissionValidation.validate(
      this.productionApprovalCancellationForm,
      [{ control: 'reason', message: 'سبب إلغاء اعتماد الإنتاج مطلوب', isMissing: () => !reason }]
    );
    if (!record || this.saving || !this.canApprove || record.status !== 'Approved' || !validation.valid) {
      this.error = validation.summary || 'لا يمكن إلغاء اعتماد هذا السجل في حالته الحالية.';
      return;
    }
    this.saving = true;
    this.recordActionSuccess = '';
    this.api.cancelProductionApproval(record.id, record.concurrencyToken, reason).pipe(finalize(() => this.saving = false)).subscribe({
      next: updated => {
        this.applyRecord(updated);
        this.preview = updated;
        this.recordActionSuccess = 'تم إلغاء اعتماد الإنتاج. بقي السجل ولقطاته المالية محفوظة للمراجعة.';
        this.productionApprovalCancellationDialogVisible = false;
        this.pendingProductionApprovalCancellation = null;
        this.productionApprovalCancellationForm.reset({ reason: '' });
        this.loadRecords();
        if (this.editingRecordId === updated.id) this.openRecord(updated);
      },
      error: error => this.handleError(error, 'تعذر إلغاء اعتماد الإنتاج.')
    });
  }

  recordStatusLabel(status: StageProductionRecord['status']): string {
    if (status === 'Approved') return 'معتمد إنتاجيًا';
    if (status === 'Cancelled') return 'اعتماد الإنتاج ملغي';
    return 'مسودة';
  }

  hasConsistentEntitlementTotal(record: Pick<StageProductionRecord, 'totalWorkerEarnings' | 'workers'>): boolean {
    const allocationsTotal = record.workers.reduce((total, worker) => total + Number(worker.calculatedEarning || 0), 0);
    return Math.abs(Number(record.totalWorkerEarnings || 0) - allocationsTotal) < 0.00005;
  }

  hasValidQuantities(): boolean {
    const value = this.recordForm.getRawValue();
    return Number(value.acceptedQuantity) + Number(value.rejectedQuantity) <= Number(value.producedQuantity);
  }

  private assignmentRequiredFields(includeWorker = true, includeReason = false, includeStart = false, includeEnd = false): RequiredFieldRule[] {
    const fields: RequiredFieldRule[] = [];
    if (includeWorker) fields.push({ control: 'workerId', message: 'العامل مطلوب' });
    if (includeReason) fields.push({ control: 'reason', message: 'السبب مطلوب', isMissing: () => !this.assignmentForm.controls.reason.value?.trim() });
    if (includeStart) fields.push({ control: 'startAtLocal', message: 'وقت السريان مطلوب', isMissing: () => !this.toUtc(this.assignmentForm.controls.startAtLocal.value) });
    if (includeEnd) fields.push({ control: 'endAtLocal', message: 'وقت الانتهاء مطلوب', isMissing: () => !this.toUtc(this.assignmentForm.controls.endAtLocal.value) });
    return fields;
  }

  private assignmentSubmissionRules(worker: AssignmentWorkflowWorker | undefined, mode: AssignmentMode): RequiredFieldRule[] {
    const requiresReason = mode === 'temporary' || (!!worker?.effectiveSubStageId && worker.effectiveSubStageId !== this.selectedSubStageId);
    return this.assignmentRequiredFields(true, requiresReason, mode === 'temporary', mode === 'temporary');
  }

  private assignmentSubmissionMessages(worker: AssignmentWorkflowWorker | undefined, mode: AssignmentMode): string[] {
    if (mode !== 'temporary') return [];

    const start = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const end = this.toUtc(this.assignmentForm.controls.endAtLocal.value);
    const messages: string[] = [];
    if (!worker?.effectiveSubStageId) messages.push('العامل يجب أن يكون معينًا حاليًا قبل النقل المؤقت');
    if (start && end && end <= start) messages.push('أدخل فترة نقل مؤقت صالحة');
    return messages;
  }

  private recordSubmissionMessages(requireFreshPreview: boolean): string[] {
    const extra: string[] = [];
    if (!this.hasProductionContext()) extra.push('اختر مسار الإنتاج وأمرًا نشطًا ومرحلة مطابقة');
    if (!this.hasValidQuantities()) extra.push('يجب ألا تتجاوز الكمية المقبولة والمرفوضة الكمية المنتجة');
    if (this.workers.length === 0) extra.push('يجب إضافة عامل فعلي واحد على الأقل');
    if (!this.hasUniqueCurrentWorkersWithoutMessage()) extra.push('حدّث قائمة العمال من التعيين الحالي للمرحلة');
    if (requireFreshPreview && !this.previewIsFresh) extra.push(this.previewStaleMessage || (this.previewIsStale ? 'تم تغيير بيانات الدفعة. أعد حساب المعاينة.' : 'احسب المعاينة قبل حفظ المسودة'));
    return this.formSubmissionValidation.missingMessages(this.recordForm, this.recordRequiredFields, extra);
  }

  private validateRecordSubmission(requireFreshPreview: boolean) {
    return this.formSubmissionValidation.validate(
      this.recordForm,
      this.recordRequiredFields,
      this.recordSubmissionMessages(requireFreshPreview)
    );
  }

  private hasProductionContext(): boolean {
    const order = this.selectedOrder;
    const stage = this.selectedStage();
    return this.hierarchyComplete && !!order && order.status === 'Active' &&
      order.productionLineId === this.selectedProductionLineId && !!stage &&
      stage.subStageId === this.selectedSubStageId;
  }

  private hasUniqueCurrentWorkersWithoutMessage(): boolean {
    const selected = this.workers.controls.map(control => control.get('workerId')?.value).filter((workerId): workerId is string => !!workerId);
    return new Set(selected).size === selected.length &&
      selected.every(workerId => this.recordingWorkers.some(worker => worker.workerId === workerId));
  }

  private currentDraftFingerprint(): string {
    const value = this.recordForm.getRawValue();
    const stage = this.selectedStage();
    return JSON.stringify({
      orderId: value.productionOrderId,
      stageId: value.productModelStageId,
      productionDate: value.productionDate,
      producedQuantity: value.producedQuantity,
      acceptedQuantity: value.acceptedQuantity,
      rejectedQuantity: value.rejectedQuantity,
      workers: (value.workers as Array<{ workerId: string; percentage: number | null; fixedAmount: number | null; notes: string }>).map(worker => ({
        workerId: worker.workerId,
        percentage: worker.percentage,
        fixedAmount: worker.fixedAmount,
        notes: worker.notes
      })),
      compensation: stage ? [stage.id, stage.compensationMode, stage.piecePrice, stage.standardSeconds] : null
    });
  }

  private previewFingerprint = '';

  private setFreshPreview(preview: StageProductionRecord): void {
    this.preview = preview;
    this.previewFingerprint = this.currentDraftFingerprint();
    this.previewIsFresh = this.hasConsistentEntitlementTotal(preview);
    this.previewIsStale = !this.previewIsFresh;
    this.previewStaleMessage = this.previewIsFresh ? '' : 'نتيجة المعاينة غير متسقة: إجمالي المستحقات لا يطابق مجموع مستحقات العمال. أعد حساب المعاينة.';
  }

  private clearPreviewState(): void {
    this.preview = null;
    this.previewFingerprint = '';
    this.previewIsFresh = false;
    this.previewIsStale = false;
    this.previewStaleMessage = '';
  }

  private invalidatePreviewForChangedDraft(): void {
    if (!this.previewIsFresh || this.previewFingerprint === this.currentDraftFingerprint()) return;

    this.preview = null;
    this.previewIsFresh = false;
    this.previewIsStale = true;
    this.previewStaleMessage = 'تم تغيير بيانات الدفعة. أعد حساب المعاينة.';
  }

  private prepareDraftParticipantImpact(worker: AssignmentWorkflowWorker, action: 'remove' | 'replace'): void {
    const isCurrentDraftParticipant = !this.isRecordReadOnly &&
      this.workers.controls.some(control => control.get('workerId')?.value === worker.workerId);
    if (!isCurrentDraftParticipant) {
      this.clearDraftParticipantImpact();
      return;
    }

    this.assignmentDraftUpdateMode = 'draft-too';
    this.assignmentDraftWarning = action === 'replace'
      ? 'هذا العامل مضاف إلى الدفعة الحالية. اختر ما إذا كان الاستبدال سيحدّث الدفعة أيضًا.'
      : 'هذا العامل مضاف إلى الدفعة الحالية. اختر ما إذا كان تغيير التعيين سيزيله من الدفعة أيضًا.';
  }

  private clearDraftParticipantImpact(): void {
    this.assignmentDraftWarning = '';
    this.assignmentDraftUpdateMode = 'assignment-only';
  }

  private applyDraftParticipantImpact(previousWorkerId: string, replacementWorkerId?: string): void {
    const indexes = this.workers.controls
      .map((control, index) => control.get('workerId')?.value === previousWorkerId ? index : -1)
      .filter(index => index >= 0);

    if (indexes.length === 0) return;

    if (this.assignmentDraftUpdateMode === 'draft-too') {
      if (replacementWorkerId && !this.workers.controls.some(control => control.get('workerId')?.value === replacementWorkerId)) {
        this.workers.at(indexes[0]).get('workerId')?.setValue(replacementWorkerId);
        indexes.slice(1).reverse().forEach(index => this.workers.removeAt(index));
      } else {
        indexes.reverse().forEach(index => this.workers.removeAt(index));
      }
      this.recordForm.markAsDirty();
    }

    // Assignment-only is permitted, but never leaves a financial result presented
    // as current. The manager must recalculate or update the draft participants.
    this.preview = null;
    this.previewIsFresh = false;
    this.previewIsStale = true;
    this.previewStaleMessage = 'تم تغيير بيانات الدفعة. أعد حساب المعاينة.';
  }

  trackById(_: number, item: { id: string }): string { return item.id; }
  trackByWorkerId(_: number, item: AssignmentWorkflowWorker): string { return item.workerId; }
  isRecordableWorker(worker: AssignmentWorkflowWorker): boolean { return worker.attendanceStatus === 'Present' || worker.attendanceStatus === 'Late'; }
  workerAttendanceLabel(worker: AssignmentWorkflowWorker): string {
    if (worker.attendanceEvidence === 'ActualCheckInFound') return worker.attendanceStatus === 'Late' ? 'حاضر متأخر (بصمة مؤكدة)' : 'حاضر (بصمة مؤكدة)';
    if (worker.attendanceEvidence === 'ConfirmedAbsent') return 'غائب مؤكد';
    if (worker.attendanceEvidence === 'NoSourceCheckIn') return 'لا توجد بصمة مصدر / غير مؤكد';
    return 'بيانات الحضور غير متاحة';
  }
  workerAvailabilityLabel(worker: AssignmentWorkflowWorker): string {
    if (!this.isRecordableWorker(worker)) return 'غير مؤهل للإنتاج بسبب الحضور';
    return worker.isAvailable ? 'متاح لهذه المرحلة' : 'حاضر لكن معين بمرحلة أخرى';
  }

  private loadWorkerContext(subStageId: string): void {
    if (!subStageId || !this.canViewAssignments) return;
    const version = ++this.workerContextRequestVersion;
    this.loadingWorkers = true;
    this.workerContextError = '';
    this.assignments.getSubStageWorkerContext(subStageId, this.selectedProductionDate).pipe(finalize(() => {
      if (version === this.workerContextRequestVersion && this.selectedSubStageId === subStageId) {
        this.loadingWorkers = false;
      }
    })).subscribe({
      next: context => { if (version === this.workerContextRequestVersion && this.selectedSubStageId === subStageId) this.workerContext = context; },
      error: () => {
        if (version === this.workerContextRequestVersion && this.selectedSubStageId === subStageId) {
          this.workerContextError = 'تعذر تحميل العمال وحالة الحضور للمرحلة المحددة.';
        }
      }
    });
  }

  private loadAttendanceSummary(preserveExistingError = false): void {
    if (!this.showWorkerPanel || !this.canViewAttendance) return;

    const subStageId = this.selectedSubStageId;
    const version = ++this.attendanceRequestVersion;
    this.attendanceLoading = true;
    if (!preserveExistingError) this.attendanceError = '';
    const selectedDateRead = this.attendance.getForProductionDate;
    (selectedDateRead ? selectedDateRead.call(this.attendance, this.selectedProductionDate) : this.attendance.getToday()).pipe(finalize(() => {
      if (version === this.attendanceRequestVersion && this.selectedSubStageId === subStageId) {
        this.attendanceLoading = false;
      }
    })).subscribe({
      next: snapshot => {
        if (version === this.attendanceRequestVersion && this.selectedSubStageId === subStageId) {
          this.attendanceSnapshot = snapshot;
        }
      },
      error: () => {
        if (version === this.attendanceRequestVersion && this.selectedSubStageId === subStageId) {
          if (!preserveExistingError) {
            this.attendanceError = 'تعذر تحميل حالة حضور اليوم. تحقق من الصلاحية أو حاول مرة أخرى.';
          }
        }
      }
    });
  }

  retryWorkerContext(): void {
    if (this.selectedSubStageId) this.loadWorkerContext(this.selectedSubStageId);
  }

  private refreshAfterAttendanceSyncAttempt(preserveExistingError = false): void {
    this.loadAttendanceSummary(preserveExistingError);
    if (this.selectedSubStageId && this.canViewAssignments) {
      this.loadWorkerContext(this.selectedSubStageId);
    }
    this.refreshProductReadiness();
  }

  toggleReadinessProblems(): void {
    this.showReadinessProblems = !this.showReadinessProblems;
  }

  readinessStatusLabel(status: string): string {
    if (status === 'Ready') return 'جاهزة';
    if (status === 'NeedsAssignment') return 'تحتاج تسكين';
    if (status === 'AttendanceUnavailable') return 'بيانات حضور غير متاحة';
    if (status === 'CompensationNeedsReview') return 'إعداد تكلفة المرحلة يحتاج مراجعة';
    if (status === 'Incomplete') return 'غير مكتملة';
    return 'تحتاج مراجعة';
  }

  private refreshProductReadiness(): void {
    if (!this.hasProductReadinessContext) {
      this.clearProductReadiness();
      return;
    }

    this.loadProductLineReadiness();
  }

  retryProductReadiness(): void {
    this.refreshProductReadiness();
  }

  private loadProductLineReadiness(): void {
    if (!this.hasProductReadinessContext) return;

    const productModelId = this.selectedProductModelId;
    const version = ++this.productReadinessRequestVersion;
    this.productReadiness = null;
    this.productReadinessLoading = true;
    this.productReadinessError = '';
    this.api.getProductReadiness(productModelId, this.selectedProductionLineId, this.selectedProductionDate)
      .pipe(finalize(() => {
        if (version === this.productReadinessRequestVersion) this.productReadinessLoading = false;
      }))
      .subscribe({
        next: readiness => {
          if (version === this.productReadinessRequestVersion) this.productReadiness = readiness;
        },
        error: () => {
          if (version === this.productReadinessRequestVersion) this.productReadinessError = 'تعذر تحميل ملخص الجاهزية الآن. أعد المحاولة؛ تبقى المسودة مفتوحة ولا تتغير بياناتها.';
        }
      });
  }

  private clearProductReadiness(): void {
    ++this.productReadinessRequestVersion;
    this.productReadiness = null;
    this.productReadinessError = '';
    this.productReadinessLoading = false;
    this.showReadinessProblems = false;
  }

  private refreshWorkerContext(subStageId: string): void {
    if (!subStageId || !this.canViewAssignments) return;
    this.assignments.getSubStageWorkerContext(subStageId, this.selectedProductionDate).subscribe({
      next: context => {
        if (this.selectedSubStageId === subStageId) this.workerContext = context;
      },
      error: error => this.handleError(error, 'تعذر تحديث حالة عمال وجهة النقل.')
    });
  }

  private loadMoveLines(factoryId: string): void {
    if (!factoryId) return;
    const version = ++this.moveHierarchyRequestVersion;
    this.loadingMoveLines = true;
    this.masterData.allProductionLines().pipe(finalize(() => this.loadingMoveLines = false)).subscribe({
      next: lines => { if (version === this.moveHierarchyRequestVersion) this.moveLines = lines.filter(line => line.isActive && line.factoryId === factoryId); },
      error: error => this.handleError(error, 'تعذر تحميل خطوط مصنع وجهة النقل.')
    });
  }

  private createCurrentAssignment(worker: AssignmentWorkflowWorker, reason: string) {
    return this.assignments.createDefaultAssignment({ workerId: worker.workerId, subStageId: this.selectedSubStageId, ...(reason ? { reason } : {}) });
  }

  private createTemporaryMove(worker: AssignmentWorkflowWorker, reason: string) {
    const start = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const end = this.toUtc(this.assignmentForm.controls.endAtLocal.value);
    return this.assignments.createTemporaryAssignment({ workerId: worker.workerId, fromSubStageId: worker.effectiveSubStageId!, toSubStageId: this.selectedSubStageId, startAtUtc: start!, endAtUtc: end!, reason });
  }

  private routeContextFrom(params: ParamMap): ProductionRecordingRouteContext | null {
    const factoryId = params.get('factoryId')?.trim() ?? '';
    const productionLineId = params.get('productionLineId')?.trim() ?? '';
    const mainStageId = params.get('mainStageId')?.trim() ?? '';
    const subStageId = params.get('subStageId')?.trim() ?? '';

    return factoryId && productionLineId && mainStageId && subStageId
      ? { factoryId, productionLineId, mainStageId, subStageId }
      : null;
  }

  private restoreRouteContext(context: ProductionRecordingRouteContext): void {
    const version = ++this.routeContextRestoreRequestVersion;
    this.resetHierarchyForRouteContext();
    this.error = '';

    this.masterData.factories().subscribe({
      next: factories => {
        if (!this.isCurrentRouteContextRestore(version)) return;

        this.factories = factories.filter(factory => factory.isActive);
        const factory = this.factories.find(item => item.id === context.factoryId);
        if (!factory) {
          this.markRouteContextUnavailable('المصنع المحدد لم يعد متاحًا أو نشطًا. اختر مسار إنتاج جديدًا.');
          return;
        }

        this.selectedFactoryId = factory.id;
        this.loadingLines = true;
        this.masterData.productionLines().pipe(finalize(() => {
          if (this.isCurrentRouteContextRestore(version)) this.loadingLines = false;
        })).subscribe({
          next: lines => {
            if (!this.isCurrentRouteContextRestore(version)) return;

            this.productionLines = lines.filter(line => line.isActive && line.factoryId === factory.id);
            const line = this.productionLines.find(item => item.id === context.productionLineId);
            if (!line) {
              this.markRouteContextUnavailable('خط الإنتاج المحدد لا يتبع المصنع الحالي أو لم يعد نشطًا. اختر خطًا جديدًا.');
              return;
            }

            this.selectedProductionLineId = line.id;
            this.loadingMainStages = true;
            this.masterData.mainStagesForLine(line.id).pipe(finalize(() => {
              if (this.isCurrentRouteContextRestore(version)) this.loadingMainStages = false;
            })).subscribe({
              next: mainStages => {
                if (!this.isCurrentRouteContextRestore(version)) return;

                this.mainStages = mainStages.filter(stage => stage.isActive && stage.productionLineId === line.id);
                const mainStage = this.mainStages.find(item => item.id === context.mainStageId);
                if (!mainStage) {
                  this.markRouteContextUnavailable('المرحلة الرئيسية المحددة لا تتبع الخط الحالي أو لم تعد نشطة. اختر مرحلة جديدة.');
                  return;
                }

                this.selectedMainStageId = mainStage.id;
                this.loadingSubStages = true;
                this.masterData.subStagesForMainStage(mainStage.id).pipe(finalize(() => {
                  if (this.isCurrentRouteContextRestore(version)) this.loadingSubStages = false;
                })).subscribe({
                  next: subStages => {
                    if (!this.isCurrentRouteContextRestore(version)) return;

                    this.subStages = subStages.filter(stage => stage.isActive && stage.mainStageId === mainStage.id);
                    const subStage = this.subStages.find(item => item.id === context.subStageId);
                    if (!subStage) {
                      this.markRouteContextUnavailable('المرحلة الفرعية المحددة لا تتبع المرحلة الحالية أو لم تعد نشطة. اختر مرحلة جديدة.');
                      return;
                    }

                    this.selectedSubStageId = subStage.id;
                    this.loadAttendanceSummary();
                    if (this.canViewAssignments) this.loadWorkerContext(subStage.id);
                  },
                  error: () => this.handleRouteContextLoadError(version, 'تعذر تحميل المراحل الفرعية للمسار المرسل. اختر المرحلة من جديد.')
                });
              },
              error: () => this.handleRouteContextLoadError(version, 'تعذر تحميل المراحل الرئيسية للمسار المرسل. اختر المرحلة من جديد.')
            });
          },
          error: () => this.handleRouteContextLoadError(version, 'تعذر تحميل خطوط الإنتاج للمسار المرسل. اختر الخط من جديد.')
        });
      },
      error: () => this.handleRouteContextLoadError(version, 'تعذر تحميل المصانع للمسار المرسل. اختر المصنع من جديد.')
    });
  }

  private resetHierarchyForRouteContext(): void {
    ++this.hierarchyRequestVersion;
    this.selectedFactoryId = '';
    this.selectedProductionLineId = '';
    this.selectedMainStageId = '';
    this.selectedSubStageId = '';
    this.productionLines = [];
    this.mainStages = [];
    this.subStages = [];
    this.clearStageContext();
    this.clearOrderDraft();
  }

  private isCurrentRouteContextRestore(version: number): boolean {
    return version === this.routeContextRestoreRequestVersion;
  }

  private markRouteContextUnavailable(message: string): void {
    this.error = message;
  }

  private handleRouteContextLoadError(version: number, message: string): void {
    if (this.isCurrentRouteContextRestore(version)) this.markRouteContextUnavailable(message);
  }

  private restoreDraftContext(loaded: StageProductionRecord): void {
    const order = this.orders.find(candidate => candidate.id === loaded.productionOrderId);
    const version = ++this.draftRestoreRequestVersion;
    this.restoringDraftContext = true;
    this.draftContextUnavailable = '';
    this.recordForm.enable();
    // Do not leave an earlier hierarchy visible while a historical draft is being restored.
    this.selectedFactoryId = '';
    this.selectedProductionLineId = '';
    this.selectedMainStageId = '';
    this.selectedSubStageId = '';
    this.productionLines = [];
    this.mainStages = [];
    this.subStages = [];
    this.modelStages = [];
    this.resetWorkerPanelState();
    this.clearProductReadiness();

    if (!order) {
      this.applyLoadedRecord(loaded);
      this.draftContextUnavailable = 'تعذر استعادة مسار هذه المسودة لأن أمر الإنتاج لم يعد متاحًا. لا يمكن حفظها قبل معالجة بياناتها المرجعية.';
      this.restoringDraftContext = false;
      this.saving = false;
      return;
    }

    forkJoin({
      factories: this.masterData.factories(),
      lines: this.masterData.allProductionLines(),
      mainStages: this.masterData.allMainStages(),
      subStages: this.masterData.allSubStages(),
      modelStages: this.api.listModelStages(order.productModelId)
    }).subscribe({
      next: context => {
        if (version !== this.draftRestoreRequestVersion) return;

        const stage = context.modelStages.find(candidate => candidate.id === loaded.productModelStageId);
        const subStage = stage && context.subStages.find(candidate => candidate.id === stage.subStageId);
        const mainStage = subStage && context.mainStages.find(candidate => candidate.id === subStage.mainStageId);
        const line = mainStage && context.lines.find(candidate => candidate.id === mainStage.productionLineId);
        const factory = line && context.factories.find(candidate => candidate.id === line.factoryId);
        const validContext = !!stage && !!subStage && !!mainStage && !!line && !!factory &&
          stage.isActive && subStage.isActive && mainStage.isActive && line.isActive && factory.isActive &&
          order.productionLineId === line.id;

        if (!validContext || !stage || !subStage || !mainStage || !line || !factory) {
          this.applyLoadedRecord(loaded);
          this.draftContextUnavailable = 'تعذر استعادة مسار هذه المسودة لأن عنصرًا من المصنع أو الخط أو المراحل لم يعد نشطًا. تبقى بياناتها ظاهرة دون فقدان.';
          return;
        }

        this.selectedFactoryId = factory.id;
        this.productionLines = context.lines.filter(candidate => candidate.factoryId === factory.id && candidate.isActive);
        this.selectedProductionLineId = line.id;
        this.mainStages = context.mainStages.filter(candidate => candidate.productionLineId === line.id && candidate.isActive);
        this.selectedMainStageId = mainStage.id;
        this.subStages = context.subStages.filter(candidate => candidate.mainStageId === mainStage.id && candidate.isActive);
        this.selectedSubStageId = subStage.id;
        this.modelStages = context.modelStages.filter(candidate => candidate.subStageId === subStage.id && candidate.isActive);
        this.applyLoadedRecord(loaded);
        this.loadAttendanceSummary();
        this.loadWorkerContext(subStage.id);
        // Draft restoration assigns values without interactive selector events. Load once,
        // only after the complete Factory → Line → Model → Stage → Date context exists.
        this.refreshProductReadiness();
      },
      error: error => {
        if (version !== this.draftRestoreRequestVersion) return;
        this.applyLoadedRecord(loaded);
        this.draftContextUnavailable = 'تعذر تحميل المسار المرجعي للمسودة. تحقق من اتصال البيانات قبل الحفظ.';
        this.restoringDraftContext = false;
        this.saving = false;
        this.handleError(error, 'تعذر استعادة سياق مسودة الإنتاج.');
      },
      complete: () => {
        if (version === this.draftRestoreRequestVersion) {
          this.restoringDraftContext = false;
          this.saving = false;
        }
      }
    });
  }

  private applyLoadedRecord(loaded: StageProductionRecord): void {
    this.editingRecordId = loaded.id;
    this.workers.clear();
    this.recordForm.reset({
      productionOrderId: loaded.productionOrderId,
      productModelStageId: loaded.productModelStageId,
      productionDate: loaded.productionDate,
      producedQuantity: loaded.producedQuantity,
      acceptedQuantity: loaded.acceptedQuantity,
      rejectedQuantity: loaded.rejectedQuantity,
      // A reopened Draft does not expose its persisted idempotency key. Keep
      // one valid preview correlation id for this editing session so the
      // preview DTO is valid and a retry uses the same request identity.
      clientRequestId: this.newClientRequestId(),
      concurrencyToken: loaded.concurrencyToken,
      notes: loaded.notes ?? ''
    }, { emitEvent: false });
    loaded.workers.forEach(worker => this.workers.push(this.workerGroup(worker.workerId, worker.percentage ?? null, worker.fixedAmount ?? null, worker.notes ?? '')));
    if (loaded.status !== 'Draft') this.recordForm.disable();
    this.setFreshPreview(loaded);
  }

  private ensureProductionContext(): boolean {
    const order = this.selectedOrder;
    const stage = this.selectedStage();
    const valid = this.hierarchyComplete && !!order && order.status === 'Active' && order.productionLineId === this.selectedProductionLineId && !!stage && stage.subStageId === this.selectedSubStageId;
    if (!valid) this.error = 'اختر المصنع والخط والمراحل وأمرًا نشطًا ومرحلة موديل مطابقة قبل تسجيل الإنتاج.';
    return valid;
  }

  private hasUniqueCurrentWorkers(): boolean {
    const selected = this.workers.controls.map(control => control.get('workerId')?.value).filter((workerId): workerId is string => !!workerId);
    const allCurrent = selected.every(workerId => this.recordingWorkers.some(worker => worker.workerId === workerId));
    if (!allCurrent) this.error = 'تم تغيير التعيين الحالي. حدّث قائمة العمال قبل الحفظ.';
    return new Set(selected).size === selected.length && allCurrent;
  }

  private clearStageContext(): void {
    ++this.draftRestoreRequestVersion;
    ++this.stageRequestVersion;
    this.resetWorkerPanelState();
    this.assignmentSuccess = '';
    this.assignmentForm.reset({ workerId: '', mode: 'default', reason: '', startAtLocal: '', endAtLocal: '' });
    this.clearRecordContext();
  }

  private resetWorkerPanelState(): void {
    ++this.workerContextRequestVersion;
    ++this.attendanceRequestVersion;
    this.workerContext = null;
    this.workerContextError = '';
    this.loadingWorkers = false;
    this.attendanceSnapshot = null;
    this.attendanceError = '';
    this.attendanceLoading = false;
  }

  private clearRecordContext(): void {
    ++this.draftRestoreRequestVersion;
    this.restoringDraftContext = false;
    this.draftContextUnavailable = '';
    this.editingRecordId = '';
    this.clearPreviewState();
    this.recordForm.enable();
    this.workers.clear();
    this.recordForm.reset({ productionDate: this.today, rejectedQuantity: 0, clientRequestId: this.newClientRequestId(), concurrencyToken: '', productionOrderId: '', productModelStageId: '', notes: '' });
    this.recordForm.markAsPristine();
    this.modelStages = [];
  }

  private clearOrderDraft(): void {
    this.editingOrderId = '';
    this.orderForm.reset({ productionLineId: this.selectedProductionLineId, productionDate: this.today, notes: '' });
    this.orderForm.markAsPristine();
  }

  private confirmContextReset(): boolean {
    const hasUnsavedRecord = !this.isRecordReadOnly && (this.recordForm.dirty || this.workers.length > 0 || !!this.editingRecordId);
    const hasUnsavedAssignment = this.assignmentForm.dirty;
    const hasUnsavedOrder = this.orderForm.dirty;
    return !(hasUnsavedRecord || hasUnsavedAssignment || hasUnsavedOrder) || window.confirm('سيؤدي تغيير المسار إلى مسح مسودة التعيين أو الإنتاج أو الأمر غير المحفوظة. هل تريد المتابعة؟');
  }

  private confirmAssignmentActionReset(): boolean {
    return !this.assignmentForm.dirty || window.confirm('سيتم استبدال مسودة التعيين غير المحفوظة بإجراء العامل المحدد. هل تريد المتابعة؟');
  }

  private workerGroup(workerId = '', percentage: number | null = null, fixedAmount: number | null = null, notes = '') {
    return this.fb.group({ workerId: [workerId, Validators.required], percentage: [percentage], fixedAmount: [fixedAmount], notes: [notes] });
  }

  private toUtc(value: string | null): string | null {
    if (!value) return null;
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
  }

  private toLocalInput(value: Date): string {
    const offsetMinutes = value.getTimezoneOffset();
    return new Date(value.getTime() - offsetMinutes * 60_000).toISOString().slice(0, 16);
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

  private attendanceSyncFailureMessage(error: unknown): string {
    const response = error as {
      status?: number;
      message?: string;
      error?: { error?: { code?: string; message?: string }; code?: string; message?: string };
    };
    const payload = response.error?.error ?? response.error;
    const code = payload?.code ?? '';
    const message = payload?.message ?? response.message ?? '';
    const normalized = `${code} ${message}`.toLowerCase();
    const transportType = (response.error as { type?: string } | undefined)?.type?.toLowerCase();

    if (this.isAttendanceSyncTimeout(error)) return 'استغرقت مزامنة الحضور وقتًا أطول من المتوقع. تحقق من حالة المصدر ثم أعد المحاولة.';
    if (response.status === 403) return 'لا تملك صلاحية تحديث الحضور الآن.';
    if (response.status === 401) return 'انتهت جلسة المستخدم. سجّل الدخول ثم حاول مرة أخرى.';
    if (transportType === 'abort' || normalized.includes('abort')) return 'تم إلغاء طلب مزامنة الحضور من المتصفح. تحقق من الاتصال ثم أعد المحاولة.';
    if (code === 'AttendanceSourceError' || response.status === 0 || normalized.includes('attendance source')) {
      return 'تعذر الاتصال بمصدر البصمة. تحقق من اتصال مصدر الحضور ثم حاول مرة أخرى.';
    }
    if (normalized.includes('no attendance') || normalized.includes('no check-in')) {
      return 'لم يتم العثور على سجلات حضور لهذا اليوم في مصدر البصمة.';
    }

    if ((response.status ?? 0) >= 500) return 'حدث خطأ بالخادم أثناء مزامنة الحضور. حاول مرة أخرى.';
    return 'تعذر تحديث الحضور الآن. حاول مرة أخرى.';
  }

  private isAttendanceSyncTimeout(error: unknown): boolean {
    const candidate = error as { name?: string };
    return error instanceof TimeoutError || candidate?.name === 'TimeoutError';
  }

  private applyRecord(record: StageProductionRecord): void { this.records = [record, ...this.records.filter(item => item.id !== record.id)]; }
  private newClientRequestId(): string { return createClientRequestId(); }
  private realDataIntakeFormData(): FormData {
    const value = this.intakeForm.getRawValue();
    const form = new FormData();
    form.append('factoryName', value.factoryName || '');
    form.append('productionLineName', value.productionLineName || '');
    form.append('productName', value.productName || '');
    form.append('productionDayQuantities', JSON.stringify([
      { productionDate: '2026-07-11', lineQuantity: value.quantityJuly11 },
      { productionDate: '2026-07-12', lineQuantity: value.quantityJuly12 },
      { productionDate: '2026-07-13', lineQuantity: value.quantityJuly13 }
    ]));
    form.append('stagesWorkbook', this.intakeStagesFile!);
    form.append('salaryWorkbook', this.intakeSalaryFile!);
    form.append('productionWorkbook', this.intakeProductionFile!);
    return form;
  }
  private handleError(error: { status?: number; message?: string; error?: unknown }, fallback: string): void {
    this.error = error.status === 409
      ? 'تم تعديل السجل بواسطة مستخدم آخر. حدّث البيانات وحاول مرة أخرى.'
      : this.formSubmissionValidation.serverMessage(error, fallback);
  }
}
