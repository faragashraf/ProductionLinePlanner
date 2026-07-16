import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { Observable, Subject, finalize, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { STAGE_COST_TERMINOLOGY } from '../../core/config/stage-cost-terminology';
import {
  AssignmentsApiService,
  LineStaffingPlan,
  LineStaffingStage,
  LineStaffingWorker
} from '../../core/services/assignments-api.service';
import {
  FactoryItem,
  ManufacturingMasterDataApiService,
  ProductModelItem,
  ProductionLineOption
} from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { FormSubmissionValidationService, RequiredFieldRule } from '../../shared/forms/form-submission-validation.service';

type StageFilter = 'all' | 'without-workers' | 'default' | 'temporary' | 'review';
type AssignmentDialogMode = 'default' | 'temporary' | 'replacement' | 'move' | 'remove-default' | 'cancel-temporary';

@Component({
  selector: 'app-line-staffing-workspace-page',
  templateUrl: './line-staffing-workspace-page.component.html',
  styleUrls: ['./line-staffing-workspace-page.component.scss']
})
export class LineStaffingWorkspacePageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;
  readonly stageCostTerminology = STAGE_COST_TERMINOLOGY;
  readonly staffingReferenceDate = this.egyptToday();
  readonly assignmentForm = this.fb.group({
    workerId: ['', Validators.required],
    targetSubStageId: [''],
    startAtLocal: [''],
    endAtLocal: [''],
    reason: ['', [Validators.maxLength(500)]]
  });

  factories: FactoryItem[] = [];
  productionLines: ProductionLineOption[] = [];
  productModels: ProductModelItem[] = [];
  plan: LineStaffingPlan | null = null;

  selectedFactoryId = '';
  selectedProductionLineId = '';
  selectedProductModelId = '';
  selectedSubStageId = '';
  referenceDate = this.staffingReferenceDate;
  stageFilter: StageFilter = 'all';
  stageSearch = '';
  workerSearch = '';
  departmentFilter = '';
  dialogWorkers: LineStaffingWorker[] = [];

  factoriesLoading = false;
  linesLoading = false;
  modelsLoading = false;
  planLoading = false;
  planError = '';
  successMessage = '';

  assignmentDialogVisible = false;
  assignmentDialogMode: AssignmentDialogMode = 'default';
  assignmentDialogError = '';
  assignmentValidationSummary = '';
  assignmentSaving = false;
  workerDirectoryLoading = false;
  workerDirectoryError = '';
  pendingWorker: LineStaffingWorker | null = null;
  private planRequestVersion = 0;
  private workerDirectoryRequestVersion = 0;
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly masterData: ManufacturingMasterDataApiService,
    private readonly assignments: AssignmentsApiService,
    private readonly permissionService: PermissionService,
    private readonly fb: FormBuilder,
    private readonly formValidation: FormSubmissionValidationService
  ) {}

  ngOnInit(): void {
    this.loadFactories();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get canManageAssignments(): boolean {
    return this.permissionService.hasPermission(this.permissions.assignments.manage);
  }

  get visibleProductionLines(): ProductionLineOption[] {
    return this.productionLines.filter(line => line.factoryId === this.selectedFactoryId && line.isActive);
  }

  get activeProductModels(): ProductModelItem[] {
    return this.productModels.filter(model => model.isActive);
  }

  get hasCompleteContext(): boolean {
    return Boolean(this.selectedFactoryId && this.selectedProductionLineId && this.selectedProductModelId && this.referenceDate);
  }

  get selectedStage(): LineStaffingStage | null {
    return this.plan?.stages.find(stage => stage.subStageId === this.selectedSubStageId) ?? null;
  }

  get filteredStages(): LineStaffingStage[] {
    const search = this.stageSearch.trim().toLocaleLowerCase('ar');
    return (this.plan?.stages ?? []).filter(stage => {
      const matchesSearch = !search || `${stage.stageCode} ${stage.stageName} ${stage.mainStageName}`.toLocaleLowerCase('ar').includes(search);
      if (!matchesSearch) return false;
      if (this.stageFilter === 'without-workers') return stage.effectiveAssignedWorkersCount === 0;
      if (this.stageFilter === 'default') return stage.defaultAssignedWorkersCount > 0;
      if (this.stageFilter === 'temporary') return stage.temporaryAssignedWorkersCount > 0;
      if (this.stageFilter === 'review') return stage.staffingStatus === 'NeedsStaffingReview' || stage.compensationConfigurationStatus === 'NeedsReview' || stage.isFinancialReviewPending;
      return true;
    });
  }

  get selectedStageWorkers(): LineStaffingWorker[] {
    const stageId = this.selectedSubStageId;
    if (!stageId) return [];
    return (this.plan?.workers ?? [])
      .filter(worker => worker.effectiveSubStageId === stageId || worker.defaultSubStageId === stageId)
      .sort((left, right) => left.employeeCode.localeCompare(right.employeeCode));
  }

  get availableWorkers(): LineStaffingWorker[] {
    const search = this.workerSearch.trim().toLocaleLowerCase('ar');
    return this.dialogWorkers
      .filter(worker => !this.departmentFilter || worker.departmentName === this.departmentFilter)
      .filter(worker => !search || `${worker.employeeCode} ${worker.fullName}`.toLocaleLowerCase('ar').includes(search))
      .filter(worker => this.assignmentDialogMode !== 'replacement' || worker.workerId !== this.pendingWorker?.workerId)
      .sort((left, right) => left.employeeCode.localeCompare(right.employeeCode));
  }

  get departments(): string[] {
    return [...new Set(this.dialogWorkers.map(worker => worker.departmentName).filter((name): name is string => Boolean(name)))].sort();
  }

  get selectedDialogWorker(): LineStaffingWorker | null {
    const workerId = this.assignmentForm.controls.workerId.value;
    return this.dialogWorkers.find(worker => worker.workerId === workerId)
      ?? (this.plan?.workers ?? []).find(worker => worker.workerId === workerId)
      ?? null;
  }

  get assignmentMissingRequirements(): string[] {
    return this.formValidation.missingMessages(this.assignmentForm, this.assignmentRequiredRules(), this.assignmentExtraRequirements());
  }

  get assignmentDialogTitle(): string {
    return {
      default: 'تعيين دائم',
      temporary: 'تعيين مؤقت',
      replacement: 'استبدال عامل مؤقتًا',
      move: 'نقل العامل',
      'remove-default': 'إلغاء التعيين الدائم',
      'cancel-temporary': 'إلغاء التعيين المؤقت'
    }[this.assignmentDialogMode];
  }

  get assignmentDialogSaveLabel(): string {
    return this.assignmentDialogMode.startsWith('remove') || this.assignmentDialogMode === 'cancel-temporary' ? 'تأكيد الإلغاء' : 'حفظ التعيين';
  }

  get dialogNeedsWorkerPicker(): boolean {
    return this.assignmentDialogMode === 'default' || this.assignmentDialogMode === 'temporary' || this.assignmentDialogMode === 'replacement';
  }

  get dialogNeedsTemporaryPeriod(): boolean {
    return this.assignmentDialogMode === 'temporary' || this.assignmentDialogMode === 'replacement';
  }

  get dialogNeedsTargetStage(): boolean {
    return this.assignmentDialogMode === 'move';
  }

  get selectedFactoryName(): string {
    return this.factories.find(factory => factory.id === this.selectedFactoryId)?.name ?? 'غير محدد';
  }

  get selectedLineName(): string {
    return this.visibleProductionLines.find(line => line.id === this.selectedProductionLineId)?.name ?? 'غير محدد';
  }

  get selectedProductName(): string {
    const model = this.activeProductModels.find(candidate => candidate.id === this.selectedProductModelId);
    return model ? `${model.code} — ${model.name}` : 'غير محدد';
  }

  selectFactory(factoryId: string): void {
    if (factoryId === this.selectedFactoryId) return;
    this.selectedFactoryId = factoryId;
    this.selectedProductionLineId = '';
    this.selectedProductModelId = '';
    this.productionLines = [];
    this.productModels = [];
    this.clearPlan();
    if (!factoryId) return;

    this.linesLoading = true;
    this.masterData.allProductionLines()
      .pipe(finalize(() => this.linesLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: lines => this.productionLines = lines,
        error: error => this.planError = this.formValidation.serverMessage(error, 'تعذر تحميل خطوط الإنتاج.')
      });
  }

  selectProductionLine(lineId: string): void {
    if (lineId === this.selectedProductionLineId) return;
    this.selectedProductionLineId = lineId;
    this.selectedProductModelId = '';
    this.productModels = [];
    this.clearPlan();
    if (!lineId) return;

    this.modelsLoading = true;
    this.masterData.models()
      .pipe(finalize(() => this.modelsLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: models => this.productModels = models,
        error: error => this.planError = this.formValidation.serverMessage(error, 'تعذر تحميل الموديلات.')
      });
  }

  selectProductModel(modelId: string): void {
    this.selectedProductModelId = modelId;
    this.clearPlan();
  }

  changeReferenceDate(referenceDate: string): void {
    this.referenceDate = referenceDate;
    this.clearPlan();
  }

  loadProductStages(preserveSelectedStage = false, preserveFeedback = false): void {
    if (!this.hasCompleteContext || this.planLoading) return;
    const requestVersion = ++this.planRequestVersion;
    const previouslySelectedStageId = preserveSelectedStage ? this.selectedSubStageId : '';
    this.planLoading = true;
    this.planError = '';
    if (!preserveFeedback) this.successMessage = '';
    this.assignments.getLineStaffingPlan(
      this.selectedFactoryId,
      this.selectedProductionLineId,
      this.selectedProductModelId,
      this.referenceDate
    )
      .pipe(finalize(() => {
        if (requestVersion === this.planRequestVersion) this.planLoading = false;
      }), takeUntil(this.destroy$))
      .subscribe({
        next: plan => {
          if (requestVersion !== this.planRequestVersion) return;
          this.plan = plan;
          this.selectedSubStageId = plan.stages.some(stage => stage.subStageId === previouslySelectedStageId)
            ? previouslySelectedStageId
            : plan.stages[0]?.subStageId ?? '';
        },
        error: error => {
          if (requestVersion !== this.planRequestVersion) return;
          this.planError = this.formValidation.serverMessage(error, 'تعذر تحميل مراحل الموديل وخطة التسكين.');
        }
      });
  }

  selectStage(subStageId: string): void {
    this.selectedSubStageId = subStageId;
    this.successMessage = '';
    this.focusSelectedStage();
  }

  previousStage(): void {
    this.navigateStages(-1);
  }

  nextStage(): void {
    this.navigateStages(1);
  }

  previousProblemStage(): void {
    this.navigateStages(-1, true);
  }

  nextProblemStage(): void {
    this.navigateStages(1, true);
  }

  retryActiveStaffingWorkers(): void {
    this.loadActiveStaffingWorkers();
  }

  canNavigateStages(direction: -1 | 1, problemsOnly = false): boolean {
    const stages = this.navigationStages(problemsOnly);
    const index = stages.findIndex(stage => stage.subStageId === this.selectedSubStageId);
    if (index < 0) return stages.length > 0;
    return direction < 0 ? index > 0 : index >= 0 && index < stages.length - 1;
  }

  openDefaultAssignment(): void {
    this.openAssignmentDialog('default');
  }

  openTemporaryAssignment(): void {
    this.openAssignmentDialog('temporary');
  }

  openReplacement(worker: LineStaffingWorker): void {
    if (worker.defaultSubStageId !== this.selectedSubStageId) {
      this.successMessage = 'الاستبدال المؤقت يتطلب أن يكون للعامل المستبدَل تعيين دائم في المرحلة المحددة.';
      return;
    }
    this.openAssignmentDialog('replacement', worker);
  }

  openMove(worker: LineStaffingWorker): void {
    if (!worker.effectiveAssignmentId || !worker.effectiveSubStageId) return;
    this.openAssignmentDialog('move', worker);
  }

  openCancellation(worker: LineStaffingWorker): void {
    const mode: AssignmentDialogMode = worker.effectiveAssignmentType === 'Temporary' || worker.effectiveAssignmentType === 'Replacement'
      ? 'cancel-temporary'
      : 'remove-default';
    this.openAssignmentDialog(mode, worker);
  }

  closeAssignmentDialog(): void {
    if (this.assignmentSaving) return;
    this.workerDirectoryRequestVersion++;
    this.assignmentDialogVisible = false;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    this.pendingWorker = null;
  }

  selectDialogWorker(worker: LineStaffingWorker): void {
    if (this.workerSelectionUnavailableMessage(worker)) return;
    this.assignmentForm.controls.workerId.setValue(worker.workerId);
    this.assignmentValidationSummary = '';
  }

  saveAssignment(): void {
    if (this.assignmentSaving || !this.selectedStage) return;
    const validation = this.formValidation.validate(this.assignmentForm, this.assignmentRequiredRules(), this.assignmentExtraRequirements());
    if (!validation.valid) {
      this.assignmentValidationSummary = validation.summary;
      return;
    }

    const worker = this.pendingWorker ?? this.selectedDialogWorker;
    if (!worker) {
      this.assignmentValidationSummary = 'العامل مطلوب';
      return;
    }

    this.assignmentSaving = true;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    const reason = (this.assignmentForm.controls.reason.value ?? '').trim();
    const targetSubStageId = this.assignmentForm.controls.targetSubStageId.value || this.selectedStage.subStageId;
    const startAtUtc = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const endAtUtc = this.toUtc(this.assignmentForm.controls.endAtLocal.value);

    let request$: Observable<unknown>;
    switch (this.assignmentDialogMode) {
      case 'default':
        request$ = this.assignments.createDefaultAssignment({ workerId: worker.workerId, subStageId: this.selectedStage.subStageId, reason: reason || undefined });
        break;
      case 'temporary':
        request$ = this.assignments.createTemporaryAssignment({
          workerId: worker.workerId,
          fromSubStageId: worker.effectiveSubStageId!,
          toSubStageId: this.selectedStage.subStageId,
          startAtUtc: startAtUtc!,
          endAtUtc: endAtUtc!,
          reason
        });
        break;
      case 'replacement':
        request$ = this.assignments.createReplacementAssignment({
          replacementWorkerId: worker.workerId,
          replacedWorkerId: this.pendingWorker!.workerId,
          subStageId: this.selectedStage.subStageId,
          startAtUtc: startAtUtc!,
          endAtUtc: endAtUtc!,
          reason
        });
        break;
      case 'move':
        request$ = this.assignments.moveCurrentAssignment({
          workerId: worker.workerId,
          sourceAssignmentId: worker.effectiveAssignmentId!,
          fromSubStageId: worker.effectiveSubStageId!,
          toSubStageId: targetSubStageId,
          effectiveAtUtc: new Date().toISOString(),
          temporaryEndAtUtc: worker.effectiveAssignmentType === 'Default' ? undefined : worker.temporaryEndsAtUtc ?? undefined,
          reason
        });
        break;
      case 'remove-default':
        request$ = this.assignments.removeDefaultAssignment(worker.workerId, worker.defaultSubStageId!, reason);
        break;
      case 'cancel-temporary':
        request$ = this.assignments.cancelTemporaryAssignment(worker.effectiveAssignmentId!, reason);
        break;
    }

    request$
      .pipe(finalize(() => this.assignmentSaving = false), takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.assignmentDialogVisible = false;
          this.successMessage = 'تم حفظ تغيير التسكين مع الاحتفاظ بسجل التعيينات.';
          this.pendingWorker = null;
          this.loadProductStages(true, true);
        },
        error: error => this.assignmentDialogError = this.formValidation.serverMessage(error, 'تعذر حفظ تغيير التسكين. راجع البيانات وحاول مرة أخرى.')
      });
  }

  workerAssignmentLabel(worker: LineStaffingWorker): string {
    if (worker.effectiveSubStageId === this.selectedSubStageId) {
      if (worker.effectiveAssignmentType === 'Temporary') return `تعيين مؤقت ${this.temporaryPeriod(worker)}`;
      if (worker.effectiveAssignmentType === 'Replacement') return `بديل مؤقت ${this.temporaryPeriod(worker)}`;
      return 'تعيين دائم فعّال';
    }
    if (worker.defaultSubStageId === this.selectedSubStageId) return `تعيين دائم؛ فعّال مؤقتًا في ${worker.effectiveSubStageName ?? 'مرحلة أخرى'}`;
    return worker.effectiveSubStageName ? `فعّال في ${worker.effectiveSubStageName}` : 'دون تعيين فعّال';
  }

  workerElsewhereWarning(worker: LineStaffingWorker): string | null {
    return worker.effectiveSubStageId && worker.effectiveSubStageId !== this.selectedSubStageId
      ? `العامل مكلّف حاليًا في ${worker.effectiveSubStageName ?? 'مرحلة أخرى'}؛ سيظهر التغيير في سجل التسكين.`
      : null;
  }

  workerSelectionUnavailableMessage(worker: LineStaffingWorker): string | null {
    if (!worker.isOnActiveService) return 'العامل خارج الخدمة ولا يمكن إضافته إلى خطة التسكين.';
    if (this.assignmentDialogMode === 'temporary') {
      if (!worker.defaultSubStageId) return 'لا يوجد للعامل تعيين دائم يعود إليه بعد انتهاء الفترة المؤقتة.';
      if (worker.effectiveSubStageId === this.selectedSubStageId) return 'العامل مكلّف فعليًا بهذه المرحلة بالفعل.';
      if (this.hasTemporaryPeriodOverlap(worker)) return this.temporaryConflictMessage(worker);
    }
    return null;
  }

  workerTemporaryAssignmentMessage(worker: LineStaffingWorker): string | null {
    if (this.assignmentDialogMode !== 'temporary' || (worker.effectiveAssignmentType !== 'Temporary' && worker.effectiveAssignmentType !== 'Replacement')) return null;
    return this.hasTemporaryPeriodOverlap(worker)
      ? this.temporaryConflictMessage(worker)
      : `لدى العامل تعيين مؤقت فعّال في ${worker.effectiveSubStageName ?? 'مرحلة أخرى'} ${this.temporaryPeriod(worker)}. أدخل الفترة للتحقق من عدم التداخل.`;
  }

  stageStatusLabel(stage: LineStaffingStage): string {
    if (stage.staffingStatus === 'NeedsStaffing') return 'يحتاج تسكين';
    if (stage.staffingStatus === 'NeedsStaffingReview') return 'يحتاج مراجعة التسكين';
    return 'مُسكّن';
  }

  stageStatusTone(stage: LineStaffingStage): 'ready' | 'warning' | 'critical' {
    return stage.staffingStatus === 'Staffed' ? 'ready' : stage.staffingStatus === 'NeedsStaffingReview' ? 'warning' : 'critical';
  }

  compensationStatusLabel(stage: LineStaffingStage): string {
    if (stage.compensationConfigurationStatus === 'NeedsReview') return 'إعداد تكلفة المرحلة يحتاج مراجعة';
    if (stage.isFinancialReviewPending) return 'إعداد تكلفة المرحلة مؤقت';
    return `${stage.compensationMode} — مُهيأ`;
  }

  trackById(_: number, item: { id?: string; subStageId?: string; workerId?: string }): string {
    return item.id ?? item.subStageId ?? item.workerId ?? '';
  }

  private loadFactories(): void {
    this.factoriesLoading = true;
    this.masterData.factories()
      .pipe(finalize(() => this.factoriesLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: factories => this.factories = factories.filter(factory => factory.isActive),
        error: error => this.planError = this.formValidation.serverMessage(error, 'تعذر تحميل المصانع.')
      });
  }

  private clearPlan(): void {
    this.planRequestVersion++;
    this.workerDirectoryRequestVersion++;
    this.plan = null;
    this.selectedSubStageId = '';
    this.planError = '';
    this.successMessage = '';
    this.planLoading = false;
    this.dialogWorkers = [];
    this.workerDirectoryError = '';
    this.workerDirectoryLoading = false;
  }

  private openAssignmentDialog(mode: AssignmentDialogMode, worker: LineStaffingWorker | null = null): void {
    if (!this.selectedStage || !this.canManageAssignments) return;
    this.assignmentDialogMode = mode;
    this.pendingWorker = worker;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    this.workerSearch = '';
    this.departmentFilter = '';
    this.dialogWorkers = [];
    this.workerDirectoryError = '';
    this.assignmentForm.reset({
      workerId: mode === 'move' || mode === 'remove-default' || mode === 'cancel-temporary' ? worker?.workerId ?? '' : '',
      targetSubStageId: '',
      startAtLocal: mode === 'temporary' || mode === 'replacement' ? this.nowLocalInput() : '',
      endAtLocal: '',
      reason: ''
    });
    this.assignmentDialogVisible = true;
    this.loadActiveStaffingWorkers();
  }

  private assignmentRequiredRules(): RequiredFieldRule[] {
    const rules: RequiredFieldRule[] = [];
    if (this.dialogNeedsWorkerPicker || this.assignmentDialogMode === 'move' || this.assignmentDialogMode === 'remove-default' || this.assignmentDialogMode === 'cancel-temporary') {
      rules.push({ control: 'workerId', message: 'العامل مطلوب' });
    }
    if (this.dialogNeedsTargetStage) rules.push({ control: 'targetSubStageId', message: 'مرحلة النقل مطلوبة' });
    if (this.dialogNeedsTemporaryPeriod) {
      rules.push({ control: 'startAtLocal', message: 'تاريخ ووقت البداية مطلوب' });
      rules.push({ control: 'endAtLocal', message: 'تاريخ ووقت النهاية مطلوب' });
    }
    if (this.assignmentDialogMode === 'temporary') rules.push({ control: 'reason', message: 'سبب التعيين المؤقت مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    if (this.assignmentDialogMode === 'replacement') rules.push({ control: 'reason', message: 'سبب الاستبدال مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    if (this.assignmentDialogMode === 'move') rules.push({ control: 'reason', message: 'سبب النقل مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    if (this.assignmentDialogMode === 'remove-default' || this.assignmentDialogMode === 'cancel-temporary') rules.push({ control: 'reason', message: 'سبب الإلغاء مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    if (this.assignmentDialogMode === 'default' && this.selectedDialogWorker?.defaultSubStageId && this.selectedDialogWorker.defaultSubStageId !== this.selectedSubStageId) {
      rules.push({ control: 'reason', message: 'سبب تغيير التعيين الدائم مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    }
    return rules;
  }

  private assignmentExtraRequirements(): string[] {
    if (!this.dialogNeedsTemporaryPeriod) return [];
    const start = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const end = this.toUtc(this.assignmentForm.controls.endAtLocal.value);
    const requirements = start && end && new Date(end).getTime() < new Date(start).getTime()
      ? ['تاريخ النهاية يجب ألا يسبق تاريخ البداية']
      : [];
    const worker = this.selectedDialogWorker;
    const unavailable = worker ? this.workerSelectionUnavailableMessage(worker) : null;
    return unavailable ? [...requirements, unavailable] : requirements;
  }

  private loadActiveStaffingWorkers(): void {
    const requestVersion = ++this.workerDirectoryRequestVersion;
    this.workerDirectoryLoading = true;
    this.workerDirectoryError = '';
    this.assignments.getActiveLineStaffingWorkers(this.referenceDate)
      .pipe(finalize(() => {
        if (requestVersion === this.workerDirectoryRequestVersion) this.workerDirectoryLoading = false;
      }), takeUntil(this.destroy$))
      .subscribe({
        next: workers => {
          if (requestVersion !== this.workerDirectoryRequestVersion) return;
          this.dialogWorkers = workers;
        },
        error: error => {
          if (requestVersion !== this.workerDirectoryRequestVersion) return;
          this.workerDirectoryError = this.formValidation.serverMessage(error, 'تعذر تحميل العمال على رأس العمل. أعد المحاولة.');
        }
      });
  }

  private navigateStages(direction: -1 | 1, problemsOnly = false): void {
    const stages = this.navigationStages(problemsOnly);
    const index = stages.findIndex(stage => stage.subStageId === this.selectedSubStageId);
    const targetIndex = index < 0 ? (direction > 0 ? 0 : stages.length - 1) : index + direction;
    if (targetIndex < 0 || targetIndex >= stages.length) return;
    this.selectStage(stages[targetIndex].subStageId);
  }

  private navigationStages(problemsOnly: boolean): LineStaffingStage[] {
    const stages = this.filteredStages;
    return problemsOnly ? stages.filter(stage => stage.staffingStatus !== 'Staffed' || stage.isFinancialReviewPending) : stages;
  }

  private focusSelectedStage(): void {
    if (typeof document === 'undefined' || !this.selectedSubStageId) return;
    queueMicrotask(() => {
      const stage = document.getElementById(`staffing-stage-${this.selectedSubStageId}`) as HTMLButtonElement | null;
      stage?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      stage?.focus({ preventScroll: true });
    });
  }

  private hasTemporaryPeriodOverlap(worker: LineStaffingWorker): boolean {
    const start = this.toUtc(this.assignmentForm.controls.startAtLocal.value);
    const end = this.toUtc(this.assignmentForm.controls.endAtLocal.value);
    if (!start || !end || !worker.temporaryStartsAtUtc || !worker.temporaryEndsAtUtc) return false;
    return new Date(worker.temporaryStartsAtUtc).getTime() < new Date(end).getTime()
      && new Date(worker.temporaryEndsAtUtc).getTime() > new Date(start).getTime();
  }

  private temporaryConflictMessage(worker: LineStaffingWorker): string {
    return `الفترة تتداخل مع تعيين مؤقت في ${worker.effectiveSubStageName ?? 'مرحلة أخرى'} ${this.temporaryPeriod(worker)}.`;
  }

  private toUtc(value: string | null): string | null {
    if (!value) return null;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  private nowLocalInput(): string {
    const date = new Date();
    date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
    return date.toISOString().slice(0, 16);
  }

  private egyptToday(): string {
    return new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  }

  private displayDate(value: string | null): string {
    return value ? value.slice(0, 10) : 'غير محدد';
  }

  private temporaryPeriod(worker: LineStaffingWorker): string {
    return `من ${this.displayDate(worker.temporaryStartsAtUtc)} إلى ${this.displayDate(worker.temporaryEndsAtUtc)}`;
  }
}
