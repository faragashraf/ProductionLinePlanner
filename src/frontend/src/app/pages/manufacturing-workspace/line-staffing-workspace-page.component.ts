import { Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable, Subject, finalize, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { STAGE_COST_TERMINOLOGY } from '../../core/config/stage-cost-terminology';
import {
  AssignmentsApiService,
  LineStaffingPlan,
  LineStaffingParticipation,
  LineStaffingStage,
  LineStaffingWorker
} from '../../core/services/assignments-api.service';
import { PlpSectionNavigationItem } from '../../shared/product/plp-section-navigation.component';
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
type WorkspaceScrollPosition = { stageList: number; selectedPanel: number };
type TemporaryParticipationMode = 'TemporaryMove' | 'AdditionalParticipation';
type StaffingSection = 'choices' | 'summary' | 'stages' | 'workers';

@Component({
  selector: 'app-line-staffing-workspace-page',
  templateUrl: './line-staffing-workspace-page.component.html',
  styleUrls: ['./line-staffing-workspace-page.component.scss']
})
export class LineStaffingWorkspacePageComponent implements OnInit, OnDestroy {
  private static readonly TabletWorkspaceMediaQuery = '(min-width: 600px) and (max-width: 1023px)';
  private static readonly TabletScrollLockClass = 'plp-line-staffing-tablet-scroll-lock';
  @ViewChild('stageList') private stageList?: ElementRef<HTMLElement>;
  @ViewChild('selectedStagePanel') private selectedStagePanel?: ElementRef<HTMLElement>;
  @ViewChild('workspace') private workspace?: ElementRef<HTMLElement>;
  @ViewChild('tabletContent') private tabletContent?: ElementRef<HTMLElement>;
  @ViewChild('staffingChoices') private staffingChoices?: ElementRef<HTMLElement>;
  @ViewChild('staffingSummary') private staffingSummary?: ElementRef<HTMLElement>;
  readonly permissions = PERMISSIONS;
  readonly stageCostTerminology = STAGE_COST_TERMINOLOGY;
  readonly sectionNavigationItems: readonly PlpSectionNavigationItem[] = [
    { id: 'choices', label: 'اختيارات الخط' },
    { id: 'summary', label: 'ملخص التسكين' },
    { id: 'stages', label: 'قائمة المراحل' },
    { id: 'workers', label: 'عمال المرحلة المحددة' }
  ];
  readonly staffingReferenceDate = this.egyptToday();
  readonly assignmentForm = this.fb.group({
    workerId: ['', Validators.required],
    targetSubStageId: [''],
    startTime: [null as Date | null],
    endTime: [null as Date | null],
    temporaryParticipationMode: ['AdditionalParticipation' as TemporaryParticipationMode],
    temporarySourceSubStageId: [''],
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
  tabletWorkspaceHeightPx: number | null = null;
  pendingWorker: LineStaffingWorker | null = null;
  pendingParticipation: LineStaffingParticipation | null = null;
  private selectedDefaultWorkerIds = new Set<string>();
  private planRequestVersion = 0;
  private workerDirectoryRequestVersion = 0;
  private tabletWorkspaceMediaQuery: MediaQueryList | null = null;
  private tabletScrollLockApplied = false;
  private pendingFragmentSection: StaffingSection | null = null;
  private restoredFragmentSection: StaffingSection | null = null;
  private fragmentNavigationRequestVersion = 0;
  private currentRouteFragmentSection: StaffingSection | null = null;
  private pendingExplicitFragmentSection: StaffingSection | null = null;
  private pendingVisibilityFragmentSection: StaffingSection | null = null;
  private sectionVisibilityObserver: IntersectionObserver | null = null;
  private readonly observedSectionElements = new Map<HTMLElement, StaffingSection>();
  private readonly sectionIntersectionRatios = new Map<StaffingSection, number>();
  private observedScrollContainers: HTMLElement[] = [];
  private visibleSectionCandidate: StaffingSection | null = null;
  private sectionVisibilityFrame: number | null = null;
  private sectionVisibilityDebounceTimer: ReturnType<typeof setTimeout> | null = null;
  private fragmentScrollSuppressionTimer: ReturnType<typeof setTimeout> | null = null;
  private fragmentScrollSuppressed = false;
  private readonly destroy$ = new Subject<void>();
  private readonly onTabletWorkspaceBreakpointChange = (): void => {
    this.tabletWorkspaceHeightPx = null;
    this.scheduleTabletWorkspaceContainment();
  };
  private readonly onOrientationChange = (): void => {
    this.tabletWorkspaceHeightPx = null;
    this.scheduleTabletWorkspaceContainment();
  };
  private readonly onTabletContentScroll = (): void => this.handleInternalSectionScroll();
  private readonly onStageListScroll = (): void => this.handleInternalSectionScroll('stages');
  private readonly onSelectedStagePanelScroll = (): void => this.handleInternalSectionScroll('workers');

  constructor(
    private readonly masterData: ManufacturingMasterDataApiService,
    private readonly assignments: AssignmentsApiService,
    private readonly permissionService: PermissionService,
    private readonly fb: FormBuilder,
    private readonly formValidation: FormSubmissionValidationService,
    private readonly route: ActivatedRoute,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    this.bindTabletWorkspaceMediaQuery();
    this.observeWorkspaceSectionFragment();
    this.loadFactories();
  }

  ngOnDestroy(): void {
    this.unbindTabletWorkspaceMediaQuery();
    this.disconnectSectionVisibilityObserver();
    this.releaseTabletWorkspaceScrollLock();
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
    return Boolean(this.selectedFactoryId && this.selectedProductionLineId && this.selectedProductModelId);
  }

  get activeStaffingSection(): string {
    return this.currentRouteFragmentSection ?? 'choices';
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
      .filter(worker => this.participationForStage(worker, stageId) !== null)
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

  get isBulkDefaultAssignmentDialog(): boolean {
    return this.assignmentDialogMode === 'default';
  }

  get selectedDefaultWorkersCount(): number {
    return this.selectedDefaultWorkerIds.size;
  }

  get assignmentMissingRequirements(): string[] {
    return this.formValidation.missingMessages(this.assignmentForm, this.assignmentRequiredRules(), this.assignmentExtraRequirements());
  }

  get assignmentDialogTitle(): string {
    return {
      default: 'تسكين دائم للمرحلة',
      temporary: 'تعيين مؤقت',
      replacement: 'استبدال عامل مؤقتًا',
      move: 'نقل العامل',
      'remove-default': 'إلغاء التعيين الدائم',
      'cancel-temporary': 'إلغاء التعيين المؤقت'
    }[this.assignmentDialogMode];
  }

  get assignmentDialogSubtitle(): string {
    const stageContext = this.selectedStage ? `${this.selectedStage.stageCode} — ${this.selectedStage.stageName}` : '';
    const workerContext = this.pendingWorker ? `${this.pendingWorker.employeeCode} — ${this.pendingWorker.fullName}` : '';
    return [stageContext, workerContext].filter(Boolean).join(' · ');
  }

  get assignmentDialogSaveLabel(): string {
    if (this.assignmentDialogMode === 'default') return 'إضافة العمال المحددين';
    return this.assignmentDialogMode.startsWith('remove') || this.assignmentDialogMode === 'cancel-temporary' ? 'تأكيد الإلغاء' : 'حفظ التعيين';
  }

  get dialogNeedsWorkerPicker(): boolean {
    return this.assignmentDialogMode === 'temporary' || this.assignmentDialogMode === 'replacement';
  }

  get dialogNeedsTemporaryPeriod(): boolean {
    return this.assignmentDialogMode === 'temporary' || this.assignmentDialogMode === 'replacement';
  }

  get dialogRequiresReason(): boolean {
    return this.assignmentDialogMode !== 'default';
  }

  get temporaryParticipationMode(): TemporaryParticipationMode {
    return this.assignmentForm.controls.temporaryParticipationMode.value as TemporaryParticipationMode;
  }

  get temporarySourceParticipations(): LineStaffingParticipation[] {
    const worker = this.selectedDialogWorker;
    if (!worker) return [];
    return worker.participations.filter(participation =>
      participation.assignmentType === 'Default' &&
      participation.subStageId !== this.selectedStage?.subStageId);
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

  loadProductStages(preserveSelectedStage = false, preserveFeedback = false): void {
    if (!this.hasCompleteContext || this.planLoading) return;
    const requestVersion = ++this.planRequestVersion;
    const previouslySelectedStageId = preserveSelectedStage ? this.selectedSubStageId : '';
    const preservedScrollPosition = preserveSelectedStage ? this.workspaceScrollPosition() : null;
    this.planLoading = true;
    this.planError = '';
    if (!preserveFeedback) this.successMessage = '';
    this.assignments.getLineStaffingPlan(
      this.selectedFactoryId,
      this.selectedProductionLineId,
      this.selectedProductModelId,
      this.staffingReferenceDate
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
          if (preservedScrollPosition) this.restoreWorkspaceScrollPosition(preservedScrollPosition);
          this.scheduleTabletWorkspaceContainment();
          this.schedulePendingFragmentSectionNavigation();
        },
        error: error => {
          if (requestVersion !== this.planRequestVersion) return;
          this.planError = this.formValidation.serverMessage(error, 'تعذر تحميل مراحل الموديل وخطة التسكين.');
        }
      });
  }

  selectStage(subStageId: string): void {
    this.setSelectedStage(subStageId);
  }

  private setSelectedStage(subStageId: string, revealInList = false): void {
    this.selectedSubStageId = subStageId;
    this.successMessage = '';
    if (revealInList) this.revealSelectedStageInList();
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

  /**
   * This is the only user-driven section navigation entry point. It replaces
   * the route fragment and then moves only the bounded tablet regions.
   */
  requestStaffingSection(section: string): void {
    const target = this.workspaceSectionFromFragment(section);
    if (!target) return;
    this.currentRouteFragmentSection = target;
    this.pendingExplicitFragmentSection = target;
    void this.router.navigate([], {
      relativeTo: this.route,
      fragment: target,
      replaceUrl: true,
      queryParamsHandling: 'preserve'
    });
    this.requestFragmentSectionNavigation(target, true);
  }

  private scrollToStaffingSection(section: StaffingSection): boolean {
    if (!this.tabletScrollLockApplied) return false;

    const content = this.tabletContent?.nativeElement;
    switch (section) {
      case 'choices':
        if (!content || !this.staffingChoices?.nativeElement) return false;
        this.suppressVisibleSectionFragmentUpdates();
        this.scrollContainerToTarget(content, this.staffingChoices.nativeElement);
        return true;
      case 'summary':
        if (!content || !this.staffingSummary?.nativeElement) return false;
        this.suppressVisibleSectionFragmentUpdates();
        this.scrollContainerToTarget(content, this.staffingSummary.nativeElement);
        return true;
      case 'stages':
        if (!content || !this.workspace?.nativeElement || !this.stageList?.nativeElement) return false;
        this.suppressVisibleSectionFragmentUpdates();
        this.scrollContainerToTarget(content, this.workspace.nativeElement);
        this.scrollContainerToStart(this.stageList.nativeElement);
        return true;
      case 'workers':
        if (!content || !this.workspace?.nativeElement || !this.selectedStagePanel?.nativeElement) return false;
        this.suppressVisibleSectionFragmentUpdates();
        this.scrollContainerToTarget(content, this.workspace.nativeElement);
        this.scrollContainerToStart(this.selectedStagePanel.nativeElement);
        return true;
    }
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
    const participation = this.participationForStage(worker);
    if (!participation || participation.assignmentType !== 'Default') {
      this.successMessage = 'الاستبدال المؤقت يتطلب أن يكون للعامل المستبدَل تعيين دائم في المرحلة المحددة.';
      return;
    }
    this.openAssignmentDialog('replacement', worker, participation);
  }

  openMove(worker: LineStaffingWorker): void {
    const participation = this.participationForStage(worker);
    if (!participation) return;
    this.openAssignmentDialog('move', worker, participation);
  }

  openCancellation(worker: LineStaffingWorker): void {
    const participation = this.participationForStage(worker);
    if (!participation) return;
    const mode: AssignmentDialogMode = participation.assignmentType === 'Temporary' || participation.assignmentType === 'Replacement'
      ? 'cancel-temporary'
      : 'remove-default';
    this.openAssignmentDialog(mode, worker, participation);
  }

  closeAssignmentDialog(force = false): void {
    if (this.assignmentSaving && !force) return;
    this.workerDirectoryRequestVersion++;
    this.assignmentDialogVisible = false;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    this.pendingWorker = null;
    this.pendingParticipation = null;
    this.selectedDefaultWorkerIds = new Set<string>();
    this.dialogWorkers = [];
    this.workerSearch = '';
    this.departmentFilter = '';
    this.workerDirectoryError = '';
    this.workerDirectoryLoading = false;
  }

  selectDialogWorker(worker: LineStaffingWorker): void {
    if (this.workerSelectionUnavailableMessage(worker)) return;
    this.assignmentForm.controls.workerId.setValue(worker.workerId);
    this.assignmentValidationSummary = '';
  }

  isDefaultWorkerSelected(worker: LineStaffingWorker): boolean {
    return this.selectedDefaultWorkerIds.has(worker.workerId) || this.hasNonDefaultParticipationInSelectedStage(worker);
  }

  defaultWorkerSelectionUnavailableMessage(worker: LineStaffingWorker): string | null {
    if (!worker.isOnActiveService) return 'العامل خارج الخدمة ولا يمكن إضافته إلى خطة التسكين.';
    if (this.hasNonDefaultParticipationInSelectedStage(worker)) return 'له مشاركة مؤقتة في هذه المرحلة وتُدار من إجراء مؤقت مستقل.';
    return null;
  }

  defaultWorkerAssignmentStateLabel(worker: LineStaffingWorker): string {
    if (this.hasNonDefaultParticipationInSelectedStage(worker)) return 'مشاركة مؤقتة قائمة — لا تتغير من التسكين الدائم';
    if (this.selectedDefaultWorkerIds.has(worker.workerId)) return 'مشارك دائم في هذه المرحلة';
    return 'متاح للإضافة إلى هذه المرحلة';
  }

  toggleDefaultWorker(worker: LineStaffingWorker, selected: boolean): void {
    if (this.defaultWorkerSelectionUnavailableMessage(worker)) return;
    if (selected) this.selectedDefaultWorkerIds.add(worker.workerId);
    else this.selectedDefaultWorkerIds.delete(worker.workerId);
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
  }

  toggleDefaultWorkerFromRow(worker: LineStaffingWorker, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if ((event.target as HTMLElement | null)?.closest('input, button, a, select, label')) return;
    this.toggleDefaultWorker(worker, !this.isDefaultWorkerSelected(worker));
  }

  onDefaultWorkerCheckboxChange(worker: LineStaffingWorker, event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    const input = event.target as HTMLInputElement;
    this.toggleDefaultWorker(worker, input.checked);
  }

  onAssignmentFormSubmitted(): void {
    // Permanent bulk choices are committed exclusively by the dialog's
    // explicit action. Enter in search/filter controls must not save or close.
    if (this.assignmentDialogMode === 'default') return;
    this.saveAssignment();
  }

  saveAssignment(): void {
    if (this.assignmentSaving || !this.selectedStage) return;
    if (this.assignmentDialogMode === 'default') {
      this.saveDefaultWorkerSelections();
      return;
    }
    const validation = this.formValidation.validate(this.assignmentForm, this.assignmentRequiredRules(), this.assignmentExtraRequirements());
    if (!validation.valid) {
      this.assignmentValidationSummary = validation.summary;
      return;
    }

    const worker = this.assignmentDialogMode === 'replacement'
      ? this.selectedDialogWorker
      : this.pendingWorker ?? this.selectedDialogWorker;
    if (!worker) {
      this.assignmentValidationSummary = 'العامل مطلوب';
      return;
    }

    this.assignmentSaving = true;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    const reason = (this.assignmentForm.controls.reason.value ?? '').trim();
    const targetSubStageId = this.assignmentForm.controls.targetSubStageId.value || this.selectedStage.subStageId;
    const startAtUtc = this.assignmentTimeUtc(this.assignmentForm.controls.startTime.value);
    const endAtUtc = this.assignmentTimeUtc(this.assignmentForm.controls.endTime.value);

    let request$: Observable<unknown>;
    switch (this.assignmentDialogMode) {
      case 'temporary':
        request$ = this.assignments.createTemporaryAssignment({
          workerId: worker.workerId,
          fromSubStageId: this.temporaryParticipationMode === 'TemporaryMove' ? this.assignmentForm.controls.temporarySourceSubStageId.value || null : null,
          toSubStageId: this.selectedStage.subStageId,
          startAtUtc: startAtUtc!,
          endAtUtc: endAtUtc!,
          reason,
          participationMode: this.temporaryParticipationMode
        });
        break;
      case 'replacement':
        request$ = this.assignments.createReplacementAssignment({
          replacementWorkerId: worker.workerId,
          replacedWorkerId: this.pendingWorker!.workerId,
          subStageId: this.selectedStage.subStageId,
          fromSubStageId: this.assignmentForm.controls.temporarySourceSubStageId.value || null,
          startAtUtc: startAtUtc!,
          endAtUtc: endAtUtc!,
          reason
        });
        break;
      case 'move':
        request$ = this.assignments.moveCurrentAssignment({
          workerId: worker.workerId,
          sourceAssignmentId: this.pendingParticipation!.assignmentId,
          fromSubStageId: this.pendingParticipation!.subStageId,
          toSubStageId: targetSubStageId,
          effectiveAtUtc: new Date().toISOString(),
          temporaryEndAtUtc: worker.effectiveAssignmentType === 'Default' ? undefined : worker.temporaryEndsAtUtc ?? undefined,
          reason
        });
        break;
      case 'remove-default':
        request$ = this.assignments.removeDefaultAssignment(worker.workerId, this.pendingParticipation!.subStageId, reason);
        break;
      case 'cancel-temporary':
        request$ = this.assignments.cancelTemporaryAssignment(this.pendingParticipation!.assignmentId, reason);
        break;
    }

    request$
      .pipe(finalize(() => this.assignmentSaving = false), takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.successMessage = 'تم حفظ تغيير التسكين مع الاحتفاظ بسجل التعيينات.';
          this.closeAssignmentDialog(true);
          this.refreshSelectedStageAfterAssignment();
        },
        error: error => this.assignmentDialogError = this.formValidation.serverMessage(error, 'تعذر حفظ تغيير التسكين. راجع البيانات وحاول مرة أخرى.')
      });
  }

  private saveDefaultWorkerSelections(): void {
    const stage = this.selectedStage;
    if (!stage) return;

    this.assignmentSaving = true;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    this.assignments.updateStageDefaultAssignments(stage.subStageId, [...this.selectedDefaultWorkerIds])
      .pipe(finalize(() => this.assignmentSaving = false), takeUntil(this.destroy$))
      .subscribe({
        next: result => {
          const message = `تم تحديث عمال المرحلة: إضافة ${result.addedWorkersCount} وإزالة ${result.removedWorkersCount} من هذه المرحلة فقط.`;
          this.successMessage = message;
          this.closeAssignmentDialog(true);
          this.refreshSelectedStageAfterAssignment();
        },
        error: error => this.assignmentDialogError = this.formValidation.serverMessage(error, 'تعذر حفظ اختيارات عمال المرحلة. راجع البيانات وحاول مرة أخرى.')
      });
  }

  /**
   * Assignment saves change one stage at a time.  Replacing the whole plan here
   * used to recreate the workspace and made the browser reflow the page while
   * its nested scroll positions were being restored.  Keep the existing plan
   * and DOM regions mounted; merge only the selected stage, its participating
   * workers, and the plan-level summary returned by the authoritative refresh.
   */
  private refreshSelectedStageAfterAssignment(): void {
    const currentPlan = this.plan;
    const selectedStageId = this.selectedSubStageId;
    if (!currentPlan || !selectedStageId || !this.hasCompleteContext) return;

    const requestVersion = ++this.planRequestVersion;
    this.assignments.getLineStaffingStageRefresh(
      this.selectedFactoryId,
      this.selectedProductionLineId,
      this.selectedProductModelId,
      selectedStageId,
      this.staffingReferenceDate
    )
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: refreshedStage => {
          if (requestVersion !== this.planRequestVersion || this.plan !== currentPlan) return;
          this.plan = {
            ...currentPlan,
            totalStages: currentPlan.totalStages,
            stagesWithWorkers: refreshedStage.stagesWithWorkers,
            stagesWithoutWorkers: refreshedStage.stagesWithoutWorkers,
            stagesWithTemporaryAssignments: refreshedStage.stagesWithTemporaryAssignments,
            stagesNeedingCompensationReview: refreshedStage.stagesNeedingCompensationReview,
            stagesNeedingStaffingReview: refreshedStage.stagesNeedingStaffingReview,
            overallStaffingStatus: refreshedStage.overallStaffingStatus,
            staffingPlanComplete: refreshedStage.staffingPlanComplete,
            operationalAttendanceChecked: refreshedStage.operationalAttendanceChecked,
            financialConfigurationPending: refreshedStage.financialConfigurationPending,
            stages: refreshedStage.stages,
            workers: refreshedStage.workers
          };
        },
        error: error => {
          if (requestVersion !== this.planRequestVersion || this.plan !== currentPlan) return;
          this.planError = this.formValidation.serverMessage(error, 'تم حفظ التسكين، لكن تعذر تحديث المرحلة المحددة.');
        }
      });
  }

  workerAssignmentLabel(worker: LineStaffingWorker): string {
    const participation = this.participationForStage(worker);
    if (participation) {
      if (participation.assignmentType === 'Temporary') return `مشاركة مؤقتة ${this.temporaryParticipationPeriod(participation)}`;
      if (participation.assignmentType === 'Replacement') return `بديل مؤقت ${this.temporaryParticipationPeriod(participation)}`;
      return 'تعيين دائم فعّال';
    }
    return worker.participations.length ? `مشارك في ${worker.participations.length} مراحل` : 'دون تعيين فعّال';
  }

  workerElsewhereWarning(worker: LineStaffingWorker): string | null {
    const otherStages = this.otherParticipations(worker);
    return otherStages.length
      ? 'للعامل مشاركات مرحلة أخرى ظاهرة ضمن بياناته أعلاه. الإضافة لا تلغي هذه المشاركات؛ النقل فقط إجراء ناقل.'
      : null;
  }

  workerSelectionUnavailableMessage(worker: LineStaffingWorker): string | null {
    if (!worker.isOnActiveService) return 'العامل خارج الخدمة ولا يمكن إضافته إلى خطة التسكين.';
    if (this.assignmentDialogMode === 'temporary' && this.participationForStage(worker))
      return 'العامل مشارك بالفعل في المرحلة المحددة؛ لا يمكن إضافة مشاركة مكررة.';
    if (this.assignmentDialogMode === 'temporary' && this.hasTemporaryPeriodOverlap(worker)) return this.temporaryConflictMessage(worker);
    return null;
  }

  workerTemporaryAssignmentMessage(worker: LineStaffingWorker): string | null {
    if (this.assignmentDialogMode !== 'temporary') return null;
    const temporaryParticipations = worker.participations.filter(participation => participation.assignmentType === 'Temporary' || participation.assignmentType === 'Replacement');
    if (!temporaryParticipations.length) return null;
    return this.hasTemporaryPeriodOverlap(worker)
      ? this.temporaryConflictMessage(worker)
      : `لدى العامل مشاركة مؤقتة فعّالة في ${temporaryParticipations.map(participation => participation.subStageName ?? 'مرحلة أخرى').join('، ')}. أدخل الفترة للتحقق من عدم التداخل في المرحلة نفسها.`;
  }

  private hasNonDefaultParticipationInSelectedStage(worker: LineStaffingWorker): boolean {
    const participation = this.participationForStage(worker);
    return participation !== null && participation.assignmentType !== 'Default';
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

  isMoveTargetUnavailable(subStageId: string): boolean {
    return subStageId === this.pendingParticipation?.subStageId || Boolean(this.pendingWorker?.participations.some(participation => participation.subStageId === subStageId));
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
    this.tabletWorkspaceHeightPx = null;
    this.disconnectSectionVisibilityObserver(false);
    this.releaseTabletWorkspaceScrollLock();
  }

  private openAssignmentDialog(mode: AssignmentDialogMode, worker: LineStaffingWorker | null = null, participation: LineStaffingParticipation | null = null): void {
    if (!this.selectedStage || !this.canManageAssignments) return;
    this.assignmentDialogMode = mode;
    this.pendingWorker = worker;
    this.pendingParticipation = participation;
    this.assignmentDialogError = '';
    this.assignmentValidationSummary = '';
    this.workerSearch = '';
    this.departmentFilter = '';
    this.dialogWorkers = [];
    this.workerDirectoryError = '';
    this.assignmentForm.reset({
      workerId: mode === 'move' || mode === 'remove-default' || mode === 'cancel-temporary' ? worker?.workerId ?? '' : '',
      targetSubStageId: '',
      startTime: mode === 'temporary' || mode === 'replacement' ? new Date() : null,
      endTime: null,
      temporaryParticipationMode: 'AdditionalParticipation',
      temporarySourceSubStageId: mode === 'move' ? participation?.subStageId ?? '' : '',
      reason: ''
    });
    this.selectedDefaultWorkerIds = mode === 'default'
      ? new Set(this.selectedStageWorkers
        .filter(candidate => this.participationForStage(candidate)?.assignmentType === 'Default')
        .map(candidate => candidate.workerId))
      : new Set<string>();
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
      rules.push({ control: 'startTime', message: 'وقت البداية مطلوب', isMissing: () => !this.assignmentForm.controls.startTime.value });
      rules.push({ control: 'endTime', message: 'وقت النهاية مطلوب', isMissing: () => !this.assignmentForm.controls.endTime.value });
    }
    if (this.assignmentDialogMode === 'temporary') {
      rules.push({ control: 'reason', message: 'سبب التعيين المؤقت مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
      if (this.temporaryParticipationMode === 'TemporaryMove') rules.push({ control: 'temporarySourceSubStageId', message: 'مرحلة المصدر للنقل المؤقت مطلوبة' });
    }
    if (this.assignmentDialogMode === 'replacement') {
      rules.push({ control: 'reason', message: 'سبب الاستبدال مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
      rules.push({ control: 'temporarySourceSubStageId', message: 'مرحلة مصدر العامل البديل مطلوبة' });
    }
    if (this.assignmentDialogMode === 'move') rules.push({ control: 'reason', message: 'سبب النقل مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    if (this.assignmentDialogMode === 'remove-default' || this.assignmentDialogMode === 'cancel-temporary') rules.push({ control: 'reason', message: 'سبب الإلغاء مطلوب', isMissing: () => !(this.assignmentForm.controls.reason.value ?? '').trim() });
    return rules;
  }

  private assignmentExtraRequirements(): string[] {
    if (!this.dialogNeedsTemporaryPeriod) return [];
    const start = this.assignmentTimeUtc(this.assignmentForm.controls.startTime.value);
    const end = this.assignmentTimeUtc(this.assignmentForm.controls.endTime.value);
    const requirements = start && end && new Date(end).getTime() <= new Date(start).getTime()
      ? ['وقت البداية يجب أن يسبق وقت النهاية']
      : [];
    const worker = this.selectedDialogWorker;
    const unavailable = worker ? this.workerSelectionUnavailableMessage(worker) : null;
    return unavailable ? [...requirements, unavailable] : requirements;
  }

  private loadActiveStaffingWorkers(): void {
    const requestVersion = ++this.workerDirectoryRequestVersion;
    this.workerDirectoryLoading = true;
    this.workerDirectoryError = '';
    this.assignments.getActiveLineStaffingWorkers(this.staffingReferenceDate)
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
          this.workerDirectoryError = this.formValidation.serverMessage(error, 'تعذر تحميل دليل العمال بالخدمة الفعالة. أعد المحاولة.');
        }
      });
  }

  private navigateStages(direction: -1 | 1, problemsOnly = false): void {
    const stages = this.navigationStages(problemsOnly);
    const index = stages.findIndex(stage => stage.subStageId === this.selectedSubStageId);
    const targetIndex = index < 0 ? (direction > 0 ? 0 : stages.length - 1) : index + direction;
    if (targetIndex < 0 || targetIndex >= stages.length) return;
    this.setSelectedStage(stages[targetIndex].subStageId, true);
  }

  private navigationStages(problemsOnly: boolean): LineStaffingStage[] {
    const stages = this.filteredStages;
    return problemsOnly ? stages.filter(stage => stage.staffingStatus !== 'Staffed' || stage.isFinancialReviewPending) : stages;
  }

  private revealSelectedStageInList(): void {
    if (!this.stageList?.nativeElement || typeof document === 'undefined' || !this.selectedSubStageId) return;
    queueMicrotask(() => {
      const stageList = this.stageList?.nativeElement;
      const stage = document.getElementById(`staffing-stage-${this.selectedSubStageId}`) as HTMLButtonElement | null;
      if (!stageList || !stage) return;
      const listBounds = stageList.getBoundingClientRect();
      const stageBounds = stage.getBoundingClientRect();
      const scrollOffset = stageBounds.top < listBounds.top
        ? stageBounds.top - listBounds.top
        : stageBounds.bottom > listBounds.bottom
          ? stageBounds.bottom - listBounds.bottom
          : 0;
      if (scrollOffset) stageList.scrollBy({ top: scrollOffset, behavior: 'smooth' });
    });
  }

  private workspaceScrollPosition(): WorkspaceScrollPosition {
    return {
      stageList: this.stageList?.nativeElement.scrollTop ?? 0,
      selectedPanel: this.selectedStagePanel?.nativeElement.scrollTop ?? 0
    };
  }

  private restoreWorkspaceScrollPosition(position: WorkspaceScrollPosition): void {
    queueMicrotask(() => {
      if (this.stageList?.nativeElement) this.stageList.nativeElement.scrollTop = position.stageList;
      if (this.selectedStagePanel?.nativeElement) this.selectedStagePanel.nativeElement.scrollTop = position.selectedPanel;
    });
  }

  private scrollContainerToTarget(container?: HTMLElement, target?: HTMLElement): void {
    if (!container || !target) return;
    const containerBounds = container.getBoundingClientRect();
    const targetBounds = target.getBoundingClientRect();
    const targetTop = Math.max(0, targetBounds.top - containerBounds.top + container.scrollTop);
    container.scrollTo({ top: targetTop, behavior: 'smooth' });
  }

  private scrollContainerToStart(container?: HTMLElement): void {
    if (!container) return;
    container.scrollTo({ top: 0, behavior: 'smooth' });
  }

  private observeWorkspaceSectionFragment(): void {
    this.route.fragment
      .pipe(takeUntil(this.destroy$))
      .subscribe(fragment => {
        const section = this.workspaceSectionFromFragment(fragment);
        if (!section) return;
        this.currentRouteFragmentSection = section;
        if (this.pendingVisibilityFragmentSection === section) {
          this.pendingVisibilityFragmentSection = null;
          return;
        }
        if (this.pendingExplicitFragmentSection === section) {
          this.pendingExplicitFragmentSection = null;
          return;
        }
        this.requestFragmentSectionNavigation(section);
      });
  }

  private workspaceSectionFromFragment(fragment: string | null): StaffingSection | null {
    return fragment === 'choices' || fragment === 'summary' || fragment === 'stages' || fragment === 'workers'
      ? fragment
      : null;
  }

  private requestFragmentSectionNavigation(section: StaffingSection, force = false): void {
    if (!force && this.restoredFragmentSection === section && this.pendingFragmentSection === null) return;
    this.pendingFragmentSection = section;
    this.schedulePendingFragmentSectionNavigation();
  }

  private schedulePendingFragmentSectionNavigation(): void {
    if (!this.pendingFragmentSection || typeof window === 'undefined') return;
    const requestVersion = ++this.fragmentNavigationRequestVersion;
    const navigate = () => {
      if (requestVersion !== this.fragmentNavigationRequestVersion || !this.pendingFragmentSection) return;
      const section = this.pendingFragmentSection;
      if (!this.scrollToStaffingSection(section)) return;
      this.restoredFragmentSection = section;
      this.pendingFragmentSection = null;
    };
    if (typeof window.requestAnimationFrame === 'function') {
      window.requestAnimationFrame(navigate);
      return;
    }
    queueMicrotask(navigate);
  }

  private scheduleSectionVisibilityObserver(): void {
    if (typeof window === 'undefined') return;
    const setup = () => this.startSectionVisibilityObserver();
    if (typeof window.requestAnimationFrame === 'function') {
      window.requestAnimationFrame(setup);
      return;
    }
    queueMicrotask(setup);
  }

  private startSectionVisibilityObserver(): void {
    const content = this.tabletContent?.nativeElement;
    const choices = this.staffingChoices?.nativeElement;
    const summary = this.staffingSummary?.nativeElement;
    const stages = this.stageList?.nativeElement;
    const workers = this.selectedStagePanel?.nativeElement;
    if (!this.tabletScrollLockApplied || !content || !choices || !summary || !stages || !workers || typeof IntersectionObserver === 'undefined') {
      this.disconnectSectionVisibilityObserver();
      return;
    }

    this.disconnectSectionVisibilityObserver(false);
    this.observedSectionElements.set(choices, 'choices');
    this.observedSectionElements.set(summary, 'summary');
    this.observedSectionElements.set(stages, 'stages');
    this.observedSectionElements.set(workers, 'workers');
    this.sectionVisibilityObserver = new IntersectionObserver(entries => this.recordSectionVisibility(entries), {
      root: content,
      rootMargin: '0px 0px -30% 0px',
      threshold: [0, .2, .45, .7, 1]
    });
    for (const sectionElement of this.observedSectionElements.keys()) this.sectionVisibilityObserver.observe(sectionElement);

    content.addEventListener('scroll', this.onTabletContentScroll, { passive: true });
    stages.addEventListener('scroll', this.onStageListScroll, { passive: true });
    workers.addEventListener('scroll', this.onSelectedStagePanelScroll, { passive: true });
    this.observedScrollContainers = [content, stages, workers];
  }

  private recordSectionVisibility(entries: IntersectionObserverEntry[]): void {
    // IntersectionObserver provides visibility relative to the bounded content
    // root. Route changes remain scroll-intent driven so layout-only updates
    // (save, refresh, dialog close, browser chrome) cannot change the fragment.
    for (const entry of entries) {
      const section = this.observedSectionElements.get(entry.target as HTMLElement);
      if (section) this.sectionIntersectionRatios.set(section, entry.isIntersecting ? entry.intersectionRatio : 0);
    }
  }

  private scheduleVisibleSectionFragmentUpdate(preferredSection: StaffingSection | null = null): void {
    if (this.fragmentScrollSuppressed || this.sectionVisibilityFrame !== null || typeof window === 'undefined') return;
    const evaluate = () => {
      this.sectionVisibilityFrame = null;
      if (this.fragmentScrollSuppressed) return;
      const section = preferredSection ?? this.strongestVisibleWorkspaceSection();
      if (section) this.stabilizeVisibleSectionFragment(section);
    };
    if (typeof window.requestAnimationFrame === 'function') {
      this.sectionVisibilityFrame = window.requestAnimationFrame(evaluate);
      return;
    }
    queueMicrotask(evaluate);
  }

  private handleInternalSectionScroll(preferredSection: StaffingSection | null = null): void {
    if (this.fragmentScrollSuppressed) {
      this.extendFragmentScrollSuppression();
      return;
    }
    this.scheduleVisibleSectionFragmentUpdate(preferredSection);
  }

  private strongestVisibleWorkspaceSection(): StaffingSection | null {
    const content = this.tabletContent?.nativeElement;
    if (!content) return null;
    const contentBounds = content.getBoundingClientRect();
    const contentHeight = Math.max(1, contentBounds.bottom - contentBounds.top);
    let strongest: { section: StaffingSection; score: number } | null = null;
    for (const [element, section] of this.observedSectionElements) {
      const bounds = element.getBoundingClientRect();
      const visibleHeight = Math.max(0, Math.min(bounds.bottom, contentBounds.bottom) - Math.max(bounds.top, contentBounds.top));
      if (visibleHeight <= 0) continue;
      const meaningfulHeight = Math.max(1, Math.min(bounds.bottom - bounds.top, contentHeight));
      const topDistance = Math.abs(Math.max(bounds.top, contentBounds.top) - contentBounds.top);
      const intersectionRatio = this.sectionIntersectionRatios.get(section) ?? visibleHeight / meaningfulHeight;
      const score = intersectionRatio * .7 + (visibleHeight / meaningfulHeight) * .3 - (topDistance / contentHeight) * .2;
      if (!strongest || score > strongest.score) strongest = { section, score };
    }
    return strongest?.section ?? null;
  }

  private stabilizeVisibleSectionFragment(section: StaffingSection): void {
    if (section === this.currentRouteFragmentSection) {
      this.visibleSectionCandidate = null;
      if (this.sectionVisibilityDebounceTimer) clearTimeout(this.sectionVisibilityDebounceTimer);
      this.sectionVisibilityDebounceTimer = null;
      return;
    }
    if (this.visibleSectionCandidate === section) return;
    this.visibleSectionCandidate = section;
    if (this.sectionVisibilityDebounceTimer) clearTimeout(this.sectionVisibilityDebounceTimer);
    this.sectionVisibilityDebounceTimer = setTimeout(() => {
      if (this.visibleSectionCandidate !== section || this.fragmentScrollSuppressed) return;
      this.visibleSectionCandidate = null;
      this.sectionVisibilityDebounceTimer = null;
      this.updateRouteFragmentFromVisibleSection(section);
    }, 100);
  }

  private updateRouteFragmentFromVisibleSection(section: StaffingSection): void {
    if (section === this.currentRouteFragmentSection) return;
    this.currentRouteFragmentSection = section;
    this.pendingVisibilityFragmentSection = section;
    void this.router.navigate([], {
      relativeTo: this.route,
      fragment: section,
      replaceUrl: true,
      queryParamsHandling: 'preserve'
    });
  }

  private suppressVisibleSectionFragmentUpdates(): void {
    this.fragmentScrollSuppressed = true;
    this.visibleSectionCandidate = null;
    if (this.sectionVisibilityDebounceTimer) clearTimeout(this.sectionVisibilityDebounceTimer);
    if (this.fragmentScrollSuppressionTimer) clearTimeout(this.fragmentScrollSuppressionTimer);
    this.fragmentScrollSuppressionTimer = setTimeout(() => {
      this.fragmentScrollSuppressed = false;
      this.fragmentScrollSuppressionTimer = null;
    }, 450);
  }

  private extendFragmentScrollSuppression(): void {
    if (!this.fragmentScrollSuppressed) return;
    if (this.fragmentScrollSuppressionTimer) clearTimeout(this.fragmentScrollSuppressionTimer);
    this.fragmentScrollSuppressionTimer = setTimeout(() => {
      this.fragmentScrollSuppressed = false;
      this.fragmentScrollSuppressionTimer = null;
    }, 180);
  }

  private disconnectSectionVisibilityObserver(resetFragmentState = true): void {
    this.sectionVisibilityObserver?.disconnect();
    this.sectionVisibilityObserver = null;
    for (const container of this.observedScrollContainers) {
      container.removeEventListener('scroll', this.onTabletContentScroll);
      container.removeEventListener('scroll', this.onStageListScroll);
      container.removeEventListener('scroll', this.onSelectedStagePanelScroll);
    }
    this.observedScrollContainers = [];
    this.observedSectionElements.clear();
    this.sectionIntersectionRatios.clear();
    if (this.sectionVisibilityFrame !== null && typeof window !== 'undefined') window.cancelAnimationFrame(this.sectionVisibilityFrame);
    this.sectionVisibilityFrame = null;
    if (this.sectionVisibilityDebounceTimer) clearTimeout(this.sectionVisibilityDebounceTimer);
    this.sectionVisibilityDebounceTimer = null;
    if (resetFragmentState) {
      if (this.fragmentScrollSuppressionTimer) clearTimeout(this.fragmentScrollSuppressionTimer);
      this.fragmentScrollSuppressionTimer = null;
      this.fragmentScrollSuppressed = false;
      this.visibleSectionCandidate = null;
    }
  }

  private bindTabletWorkspaceMediaQuery(): void {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return;
    this.tabletWorkspaceMediaQuery = window.matchMedia(LineStaffingWorkspacePageComponent.TabletWorkspaceMediaQuery);
    if (typeof this.tabletWorkspaceMediaQuery.addEventListener === 'function') {
      this.tabletWorkspaceMediaQuery.addEventListener('change', this.onTabletWorkspaceBreakpointChange);
    } else if (typeof this.tabletWorkspaceMediaQuery.addListener === 'function') {
      this.tabletWorkspaceMediaQuery.addListener(this.onTabletWorkspaceBreakpointChange);
    }
    window.addEventListener('orientationchange', this.onOrientationChange, { passive: true });
  }

  private unbindTabletWorkspaceMediaQuery(): void {
    if (typeof window === 'undefined') return;
    if (this.tabletWorkspaceMediaQuery) {
      if (typeof this.tabletWorkspaceMediaQuery.removeEventListener === 'function') {
        this.tabletWorkspaceMediaQuery.removeEventListener('change', this.onTabletWorkspaceBreakpointChange);
      } else if (typeof this.tabletWorkspaceMediaQuery.removeListener === 'function') {
        this.tabletWorkspaceMediaQuery.removeListener(this.onTabletWorkspaceBreakpointChange);
      }
    }
    window.removeEventListener('orientationchange', this.onOrientationChange);
    this.tabletWorkspaceMediaQuery = null;
  }

  private scheduleTabletWorkspaceContainment(): void {
    if (typeof window === 'undefined') return;
    const synchronize = () => this.synchronizeTabletWorkspaceContainment();
    if (typeof window.requestAnimationFrame === 'function') {
      window.requestAnimationFrame(synchronize);
      return;
    }
    queueMicrotask(synchronize);
  }

  private synchronizeTabletWorkspaceContainment(): void {
    const shouldContain = Boolean(
      this.plan
      && this.tabletWorkspaceMediaQuery?.matches
      && this.workspace?.nativeElement
      && this.tabletContent?.nativeElement
    );
    if (!shouldContain) {
      this.disconnectSectionVisibilityObserver();
      this.releaseTabletWorkspaceScrollLock();
      return;
    }

    this.applyTabletWorkspaceScrollLock();
    if (this.tabletWorkspaceHeightPx === null) this.measureTabletWorkspaceHeight();
    if (this.pendingFragmentSection) this.schedulePendingFragmentSectionNavigation();
    this.scheduleSectionVisibilityObserver();
  }

  private measureTabletWorkspaceHeight(): void {
    if (typeof window === 'undefined' || !this.tabletContent?.nativeElement) return;
    const viewportHeight = window.visualViewport?.height ?? window.innerHeight;
    const contentTop = this.tabletContent.nativeElement.getBoundingClientRect().top;
    const availableHeight = Math.floor(viewportHeight - Math.max(contentTop, 0) - 12);
    // Capture once per tablet/orientation state. In particular, do not listen
    // to visualViewport resize because Android browser chrome changes it while
    // a finger is scrolling and would resize the scroll owner under that finger.
    this.tabletWorkspaceHeightPx = Math.max(280, availableHeight);
  }

  private applyTabletWorkspaceScrollLock(): void {
    if (this.tabletScrollLockApplied || typeof document === 'undefined') return;
    document.documentElement.classList.add(LineStaffingWorkspacePageComponent.TabletScrollLockClass);
    document.body.classList.add(LineStaffingWorkspacePageComponent.TabletScrollLockClass);
    this.tabletScrollLockApplied = true;
  }

  private releaseTabletWorkspaceScrollLock(): void {
    if (!this.tabletScrollLockApplied || typeof document === 'undefined') return;
    document.documentElement.classList.remove(LineStaffingWorkspacePageComponent.TabletScrollLockClass);
    document.body.classList.remove(LineStaffingWorkspacePageComponent.TabletScrollLockClass);
    this.tabletScrollLockApplied = false;
  }

  private hasTemporaryPeriodOverlap(worker: LineStaffingWorker): boolean {
    const start = this.assignmentTimeUtc(this.assignmentForm.controls.startTime.value);
    const end = this.assignmentTimeUtc(this.assignmentForm.controls.endTime.value);
    if (!start || !end) return false;
    return worker.participations
      .filter(participation => participation.subStageId === this.selectedStage?.subStageId)
      .filter(participation => participation.assignmentType === 'Temporary' || participation.assignmentType === 'Replacement')
      .some(participation => participation.startsAtUtc && participation.endsAtUtc
        && new Date(participation.startsAtUtc).getTime() < new Date(end).getTime()
        && new Date(participation.endsAtUtc).getTime() > new Date(start).getTime());
  }

  private temporaryConflictMessage(worker: LineStaffingWorker): string {
    return `الفترة تتداخل مع مشاركة مؤقتة قائمة للعامل في المرحلة المحددة.`;
  }

  private assignmentTimeUtc(value: Date | null): string | null {
    if (!value || Number.isNaN(value.getTime())) return null;
    const hours = String(value.getHours()).padStart(2, '0');
    const minutes = String(value.getMinutes()).padStart(2, '0');
    const local = new Date(`${this.staffingReferenceDate}T${hours}:${minutes}:00`);
    return Number.isNaN(local.getTime()) ? null : local.toISOString();
  }

  private egyptToday(): string {
    return new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  }

  private displayDate(value: string | null): string {
    return value ? value.slice(0, 10) : 'غير محدد';
  }

  participationForStage(worker: LineStaffingWorker, subStageId = this.selectedSubStageId): LineStaffingParticipation | null {
    return worker.participations.find(participation => participation.subStageId === subStageId) ?? null;
  }

  otherParticipations(worker: LineStaffingWorker): LineStaffingParticipation[] {
    return worker.participations.filter(participation => participation.subStageId !== this.selectedSubStageId);
  }

  workerParticipationStageNames(worker: LineStaffingWorker): string[] {
    return worker.participations.map(participation => participation.subStageName ?? 'مرحلة أخرى');
  }

  temporaryParticipationPeriod(participation: LineStaffingParticipation): string {
    return `من ${this.displayDate(participation.startsAtUtc)} إلى ${this.displayDate(participation.endsAtUtc)}`;
  }
}
