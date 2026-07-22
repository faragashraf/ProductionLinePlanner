import { Component, OnDestroy, OnInit, Optional } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, Validators } from '@angular/forms';
import { EMPTY, Subject, debounceTime, distinctUntilChanged, finalize, forkJoin, map, Observable, switchMap, takeUntil } from 'rxjs';
import {
  DepartmentItem,
  ManufacturingMasterDataApiService,
  ModelStageItem,
  ProductModelItem,
  ProductionLineOption,
  StageDependencySummary,
  SubStageOption
} from '../../core/services/manufacturing-master-data-api.service';
import { matchesSearchTerm, normalizeSearchText } from '../../shared/utils/text-search.utils';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { buildFactoryStructureTree, collectExpandedIds, FactoryStructureTreeNode, filterFactoryStructureTree, findFactoryStructureNode } from './factory-structure-tree.adapter';
import { ManufacturingFilterOption } from './manufacturing-filter-card.component';

type StageStatusFilter = 'all' | 'active' | 'inactive';
type ModelStatusFilter = StageStatusFilter;
interface ModelStageLineGroup {
  lineId: string;
  lineName: string;
  lineCode: string;
  structurePath: string;
  stages: ModelStageItem[];
}

@Component({ selector: 'app-manufacturing-master-data-page', templateUrl: './manufacturing-master-data-page.component.html', styleUrls: ['./manufacturing-master-data-page.component.scss'] })
export class ManufacturingMasterDataPageComponent implements OnInit, OnDestroy {
  readonly mode: 'stages' | 'models';
  loading = true;
  saving = false;
  error = '';

  factories: { id: string; code: string; name: string; isActive: boolean }[] = [];
  departments: DepartmentItem[] = [];
  lines: ProductionLineOption[] = [];
  stageEditDepartments: DepartmentItem[] = [];
  stageEditLines: ProductionLineOption[] = [];
  stageFilterTreeNodes: FactoryStructureTreeNode[] = [];
  selectedStageFilterNode: FactoryStructureTreeNode | null = null;
  stageTreeSearch = '';
  operationalStages: SubStageOption[] = [];
  stageStatusFilter: StageStatusFilter = 'all';
  stageSearch = '';
  stageFormVisible = false;
  editStageId = '';
  stageDependencySummary: StageDependencySummary | null = null;
  pendingStage: SubStageOption | null = null;
  pendingStageAction: 'disable' | 'delete' | null = null;
  dependencyDialogVisible = false;

  models: ProductModelItem[] = [];
  modelListSearch = '';
  modelStatusFilter: ModelStatusFilter = 'all';
  modelFilterTreeNodes: FactoryStructureTreeNode[] = [];
  selectedModelFilterNode: FactoryStructureTreeNode | null = null;
  modelScopeLoading = false;
  modelPage = 1;
  modelTotal = 0;
  modelPageSize = 10;
  modelListLoading = false;
  stages: ModelStageItem[] = [];
  selected: ProductModelItem | null = null;
  modelFormVisible = false;
  modelStageFormVisible = false;
  editModelId = '';
  editModelStageId = '';
  modelStageSearch = '';
  availableStagesSearch = '';
  availableStagesLoading = false;
  availableStagesError = '';
  readonly modelStageSavingIds = new Set<string>();
  private availableStageCatalog: SubStageOption[] = [];
  availableStageOptions: SubStageOption[] = [];
  stageDropdownPanelStyle: Record<string, string> = {};
  private readonly availableStageOptionCache = new Map<string, SubStageOption>();
  private readonly modelLineMembership = new Map<string, Set<string>>();
  readonly statusFilterOptions: readonly ManufacturingFilterOption[] = [
    { label: 'الكل', value: 'all' },
    { label: 'نشط', value: 'active' },
    { label: 'غير نشط', value: 'inactive' }
  ];

  readonly stageFiltersForm = this.fb.group({
    factoryId: [''],
    departmentId: [''],
    productionLineId: ['']
  });
  readonly stageEditForm = this.fb.group({
    factoryId: ['', Validators.required],
    departmentId: ['', Validators.required],
    productionLineId: ['', Validators.required],
    name: ['', Validators.required],
    capacity: [0, [Validators.required, Validators.min(0)]]
  });
  readonly modelForm = this.fb.group({ code: ['', Validators.required], name: ['', Validators.required], description: [''] });
  readonly modelStageForm = this.fb.group({ subStageId: ['', Validators.required], stageOrder: [1, Validators.required], piecePrice: [0, Validators.required], standardSeconds: [null as number | null], compensationMode: ['SharedPercentage', Validators.required], isRequired: [true], isActive: [true] });

  private readonly modelListSearch$ = new Subject<string>();
  private readonly destroy$ = new Subject<void>();
  private modelRequestVersion = 0;
  private availableStagesRequestVersion = 0;
  private stageEditHydrationVersion = 0;
  private stageEditHydrating = false;
  private stageEditHierarchy: { factoryId: string; departmentId: string; productionLineId: string } | null = null;
  private editingStageSnapshot: Readonly<SubStageOption> | null = null;
  private stageFilterExpandedIds = new Set<string>();
  private stopRealtime?: () => void;

  constructor(private readonly fb: FormBuilder, private readonly api: ManufacturingMasterDataApiService, route: ActivatedRoute, @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService) {
    this.mode = route.snapshot.routeConfig?.path === 'models' ? 'models' : 'stages';
  }

  ngOnInit(): void {
    this.modelListSearch$.pipe(debounceTime(250), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(search => this.loadModelPage(search, 1));
    this.stopRealtime = this.mode === 'models'
      ? this.manufacturingRealtime?.watchScreen({ screen: 'models', refresh: () => this.refreshModelsFromRealtime() })
      : this.manufacturingRealtime?.watchScreen({
        screen: 'stages',
        matches: change => change.entityType === 'Factory' || change.entityType === 'Department' || change.entityType === 'ProductionLine' || change.productionLineId === this.stageFiltersForm.controls.productionLineId.value,
        refresh: () => this.refreshStagesFromRealtime()
      });
    this.reload();
  }

  ngOnDestroy(): void { this.stopRealtime?.(); this.destroy$.next(); this.destroy$.complete(); }

  get activeStageEditDepartments(): DepartmentItem[] {
    const factoryId = this.stageEditForm.controls.factoryId.value;
    return this.stageEditDepartments.filter(item => item.isActive !== false && (!factoryId || item.factoryId === factoryId));
  }
  get activeStageEditLines(): ProductionLineOption[] {
    const departmentId = this.stageEditForm.controls.departmentId.value;
    return this.stageEditLines.filter(item => item.isActive && (!departmentId || item.departmentId === departmentId));
  }
  get selectedStage(): Readonly<SubStageOption> | null { return this.editingStageSnapshot?.id === this.editStageId ? this.editingStageSnapshot : null; }
  get visibleStageFilterTreeNodes(): FactoryStructureTreeNode[] { return filterFactoryStructureTree(this.stageFilterTreeNodes, this.stageTreeSearch); }
  get selectedStageFilterPath(): string { return this.selectedStageFilterNode ? this.stageFilterPath(this.selectedStageFilterNode).join(' / ') : 'كل المصانع'; }
  get stageFilterResetKey(): string { return `${this.selectedStageFilterNode?.data?.entityType ?? 'all'}:${this.selectedStageFilterNode?.data?.entityId ?? 'all'}:${this.stageStatusFilter}:${this.stageSearch}`; }
  get filteredOperationalStages(): SubStageOption[] { return this.operationalStages.filter(stage => matchesSearchTerm(this.stageSearch, [stage.name, stage.code])); }
  get stageResultCount(): number { return this.filteredOperationalStages.length; }
  get stageEmptyMessage(): string { return normalizeSearchText(this.stageSearch) ? 'لا توجد مراحل مطابقة للبحث.' : 'اختر مصنعًا أو قسمًا أو خط إنتاج لعرض مراحل الإنتاج، أو لا توجد مراحل مطابقة.'; }
  get filteredModels(): ProductModelItem[] {
    const scopeLineIds = this.selectedModelFilterNode ? this.structureLineIds(this.selectedModelFilterNode) : null;
    return this.models.filter(model => {
      if (this.modelStatusFilter === 'active' && !model.isActive) return false;
      if (this.modelStatusFilter === 'inactive' && model.isActive) return false;
      if (!scopeLineIds) return true;
      const memberships = this.modelLineMembership.get(model.id);
      return !!memberships && [...memberships].some(lineId => scopeLineIds.has(lineId));
    });
  }
  get modelResultTotal(): number { return this.selectedModelFilterNode ? this.filteredModels.length : this.modelTotal; }
  get modelFiltersActive(): boolean { return !!this.selectedModelFilterNode || this.modelStatusFilter !== 'all' || !!this.modelListSearch.trim(); }
  get modelEmptyMessage(): string {
    if (this.selectedModelFilterNode) return 'لا توجد موديلات لها مراحل مرتبطة بنطاق المصنع المحدد.';
    return normalizeSearchText(this.modelListSearch) ? 'لا توجد موديلات مطابقة للبحث.' : 'لا توجد موديلات لعرضها.';
  }
  get filteredLinkedStages(): ModelStageItem[] {
    return [...this.stages]
      .filter(item => matchesSearchTerm(this.modelStageSearch, [this.linkedStageName(item), this.linkedStageCode(item)]))
      .sort((left, right) => left.stageOrder - right.stageOrder);
  }
  get linkedStagesEmptyMessage(): string {
    return normalizeSearchText(this.modelStageSearch)
      ? 'توجد مراحل مرتبطة، لكن لا توجد نتائج مطابقة للبحث.'
      : 'لا توجد مراحل مرتبطة بهذا الموديل.';
  }
  get modelJourneyGroups(): ModelStageLineGroup[] {
    const scopeLineIds = this.selectedModelFilterNode ? this.structureLineIds(this.selectedModelFilterNode) : null;
    const grouped = new Map<string, ModelStageLineGroup>();
    this.filteredLinkedStages.forEach(stage => {
      const catalog = this.availableStageOptionCache.get(stage.subStageId);
      const lineId = catalog?.productionLineId ?? 'unknown';
      if (scopeLineIds && !scopeLineIds.has(lineId)) return;
      const line = this.lines.find(item => item.id === lineId);
      const factoryId = catalog?.factoryId ?? line?.factoryId;
      const departmentId = catalog?.departmentId ?? line?.departmentId;
      const factory = this.factories.find(item => item.id === factoryId);
      const department = this.departments.find(item => item.id === departmentId);
      const group = grouped.get(lineId) ?? {
        lineId,
        lineName: catalog?.productionLineName ?? line?.name ?? 'خط غير محدد',
        lineCode: line?.lineCode ?? '—',
        structurePath: [factory?.name, department?.nameAr ?? department?.name, catalog?.productionLineName ?? line?.name].filter(Boolean).join(' ← ') || 'مسار الخط غير متاح',
        stages: []
      };
      group.stages.push(stage);
      grouped.set(lineId, group);
    });
    return [...grouped.values()]
      .map(group => ({ ...group, stages: [...group.stages].sort((left, right) => left.stageOrder - right.stageOrder) }))
      .sort((left, right) => {
        const leftOrder = this.lines.find(line => line.id === left.lineId)?.sequenceOrder ?? Number.MAX_SAFE_INTEGER;
        const rightOrder = this.lines.find(line => line.id === right.lineId)?.sequenceOrder ?? Number.MAX_SAFE_INTEGER;
        return leftOrder - rightOrder || left.lineName.localeCompare(right.lineName, 'ar');
      });
  }
  get availableStageChoices(): SubStageOption[] {
    const selected = this.selectedAvailableStage;
    return selected && !this.availableStageOptions.some(option => option.id === selected.id)
      ? [selected, ...this.availableStageOptions]
      : this.availableStageOptions;
  }
  get selectedAvailableStage(): SubStageOption | null {
    const selectedId = this.modelStageForm.getRawValue().subStageId;
    if (!selectedId) return null;
    const linked = this.stages.find(stage => stage.subStageId === selectedId);
    return this.availableStageOptionCache.get(selectedId)
      ?? (linked ? this.toAvailableOption(linked) : null);
  }
  get canConfirmDependencyAction(): boolean {
    return this.pendingStageAction === 'disable' ? !!this.stageDependencySummary?.canDisable : this.pendingStageAction === 'delete' && !!this.stageDependencySummary?.canDelete;
  }

  reload(): void {
    this.loading = true;
    this.error = '';
    if (this.mode === 'models') {
      forkJoin({
        page: this.api.modelSearchPage('', 1, this.modelPageSize),
        factories: this.api.factories(),
        departments: this.api.departments(undefined, false),
        lines: this.api.allProductionLines(),
        stages: this.api.allSubStages()
      })
        .pipe(finalize(() => this.loading = false), takeUntil(this.destroy$))
        .subscribe({ next: data => {
          this.applyModelPage(data.page);
          this.applyStageStructureData(data);
          this.modelFilterTreeNodes = buildFactoryStructureTree({ factories: this.factories, departments: this.departments, lines: this.lines, eligibility: new Map() });
          data.stages.forEach(stage => this.availableStageOptionCache.set(stage.id, stage));
          this.availableStageCatalog = data.stages;
          this.rebuildAvailableStageOptions();
        }, error: error => this.setError(error) });
      return;
    }
    forkJoin({
      factories: this.api.factories(),
      departments: this.api.departments(undefined, false),
      lines: this.api.allProductionLines()
    }).pipe(finalize(() => this.loading = false), takeUntil(this.destroy$)).subscribe({
      next: data => {
        this.applyStageStructureData(data);
        this.rebuildStageFilterTree();
      },
      error: error => this.setError(error)
    });
  }

  onStageFilterSearch(value: string): void { this.stageTreeSearch = value.trim(); }

  clearStageFilterSearch(): void { this.stageTreeSearch = ''; }

  selectStageFilterNode(node: FactoryStructureTreeNode): void {
    if (node.data?.entityType !== 'line') return;
    this.selectedStageFilterNode = node;
    this.applyStageFilterSelection(node);
    this.loadOperationalStages();
  }

  clearStageTreeFilter(): void {
    this.selectedStageFilterNode = null;
    this.stageFiltersForm.reset({ factoryId: '', departmentId: '', productionLineId: '' }, { emitEvent: false });
    this.operationalStages = [];
  }

  clearStageFilters(): void {
    this.clearStageTreeFilter();
    this.stageStatusFilter = 'all';
    this.stageSearch = '';
  }

  onStageFilterNodeExpand(node: FactoryStructureTreeNode): void {
    const id = node.data?.entityId;
    if (id) this.stageFilterExpandedIds.add(id);
  }

  onStageFilterNodeCollapse(node: FactoryStructureTreeNode): void {
    const id = node.data?.entityId;
    if (id) this.stageFilterExpandedIds.delete(id);
  }

  selectStageEditFactory(factoryId: string): void {
    if (this.stageEditHydrating) return;
    this.stageEditForm.patchValue({ factoryId, departmentId: '', productionLineId: '' });
    this.stageEditDepartments = [];
    this.stageEditLines = [];
    if (!factoryId) return;
    this.api.departments(factoryId, false).pipe(takeUntil(this.destroy$)).subscribe({ next: departments => this.stageEditDepartments = departments.filter(item => item.factoryId === factoryId && item.isActive), error: error => this.setError(error) });
  }

  selectStageEditDepartment(departmentId: string): void {
    if (this.stageEditHydrating) return;
    this.stageEditForm.patchValue({ departmentId, productionLineId: '' });
    this.stageEditLines = [];
    if (!departmentId) return;
    this.api.productionLinesForDepartment(departmentId).pipe(takeUntil(this.destroy$)).subscribe({ next: lines => this.stageEditLines = lines.filter(line => line.departmentId === departmentId && line.isActive), error: error => this.setError(error) });
  }

  selectStageEditLine(productionLineId: string): void {
    if (this.stageEditHydrating) return;
    this.stageEditForm.patchValue({ productionLineId });
  }

  setStageStatusFilter(value: string): void {
    this.stageStatusFilter = value === 'active' || value === 'inactive' ? value : 'all';
    if (this.selectedStageFilterNode) this.loadOperationalStages();
  }

  onStageSearch(value: string): void { this.stageSearch = value; }
  onModelSearch(value: string): void { this.modelListSearch = value; this.modelPage = 1; this.modelListSearch$.next(value); }
  setModelStatusFilter(value: string): void { this.modelStatusFilter = value === 'active' || value === 'inactive' ? value : 'all'; this.loadModelPage(this.modelListSearch, 1); }
  selectModelFilterNode(node: FactoryStructureTreeNode): void { this.selectedModelFilterNode = node; this.loadModelMembership(this.models); }
  clearModelFilters(): void { this.selectedModelFilterNode = null; this.modelStatusFilter = 'all'; this.modelListSearch = ''; this.loadModelPage('', 1); }
  onModelLazyLoad(event: { first?: number | null; rows?: number | null }): void { const page = Math.floor((event.first ?? 0) / (event.rows ?? this.modelPageSize)) + 1; if (page !== this.modelPage) this.loadModelPage(this.modelListSearch, page); }

  loadOperationalStages(): void {
    const filters = this.stageHierarchyFilters();
    if (!filters) return;
    const isActive = this.stageStatusFilter === 'all' ? undefined : this.stageStatusFilter === 'active';
    this.api.operationalStages({ ...filters, isActive, includeInactive: this.stageStatusFilter === 'all' }).pipe(takeUntil(this.destroy$)).subscribe({ next: stages => this.operationalStages = stages, error: error => this.setError(error) });
  }

  openStageForm(): void {
    this.cancelStageEditHydration();
    this.editStageId = '';
    this.editingStageSnapshot = null;
    this.stageFormVisible = true;
    const hierarchy = this.stageFiltersForm.getRawValue();
    this.stageEditDepartments = this.departments.filter(item => item.factoryId === hierarchy.factoryId);
    this.stageEditLines = this.lines.filter(item => item.departmentId === hierarchy.departmentId);
    this.stageEditForm.reset({ ...hierarchy, name: '', capacity: 0 }, { emitEvent: false });
  }

  editOperationalStage(stage: SubStageOption): void {
    const hydrationVersion = ++this.stageEditHydrationVersion;
    const stageLine = stage.productionLineId ? this.lines.find(line => line.id === stage.productionLineId) : undefined;
    const hierarchy = {
      factoryId: stage.factoryId ?? stageLine?.factoryId ?? '',
      departmentId: stage.departmentId ?? stageLine?.departmentId ?? '',
      productionLineId: stage.productionLineId ?? ''
    };
    this.stageEditHydrating = true;
    this.stageEditHierarchy = hierarchy;
    this.editingStageSnapshot = Object.freeze({ ...stage, ...hierarchy });
    this.editStageId = stage.id;
    this.stageFormVisible = true;
    this.stageEditDepartments = [];
    this.stageEditLines = [];
    this.stageEditForm.reset({ ...hierarchy, name: stage.name, capacity: stage.capacity }, { emitEvent: false });

    if (!hierarchy.factoryId) {
      this.stageEditHydrating = false;
      return;
    }

    this.api.departments(hierarchy.factoryId, false).pipe(
      switchMap(departments => {
        if (!this.isCurrentStageEditHydration(hydrationVersion, stage.id)) return EMPTY;
        const currentDepartment = this.departments.find(item => item.id === hierarchy.departmentId);
        this.stageEditDepartments = currentDepartment && !departments.some(item => item.id === currentDepartment.id)
          ? [...departments, currentDepartment]
          : departments;
        this.restoreStageEditHierarchy(hydrationVersion, stage.id);
        return hierarchy.departmentId
          ? this.api.productionLinesForDepartment(hierarchy.departmentId)
          : EMPTY;
      }),
      finalize(() => {
        if (this.isCurrentStageEditHydration(hydrationVersion, stage.id)) this.stageEditHydrating = false;
      }),
      takeUntil(this.destroy$)
    ).subscribe({
      next: lines => {
        if (!this.isCurrentStageEditHydration(hydrationVersion, stage.id)) return;
        const currentLine = this.lines.find(item => item.id === hierarchy.productionLineId);
        this.stageEditLines = currentLine && !lines.some(item => item.id === currentLine.id)
          ? [...lines, currentLine]
          : lines;
        this.restoreStageEditHierarchy(hydrationVersion, stage.id);
      },
      error: error => {
        if (this.isCurrentStageEditHydration(hydrationVersion, stage.id)) this.setError(error);
      }
    });
  }

  saveOperationalStage(): void {
    if (this.stageEditForm.invalid) { this.stageEditForm.markAllAsTouched(); return; }
    const value = this.stageEditForm.getRawValue();
    const correlationId = this.localCorrelation('stages');
    const request = this.editStageId
      ? this.api.updateOperationalStage(this.editStageId, { name: value.name, capacity: value.capacity }, correlationId)
      : this.api.createOperationalStage({ productionLineId: value.productionLineId!, name: value.name!, capacity: value.capacity! }, correlationId);
    this.save(request, () => { this.stageFormVisible = false; this.editStageId = ''; this.cancelStageEditHydration(); this.loadOperationalStages(); });
  }

  closeStageForm(): void { this.stageFormVisible = false; this.editStageId = ''; this.editingStageSnapshot = null; this.cancelStageEditHydration(); }

  openDependencyDialog(stage: SubStageOption, action: 'disable' | 'delete'): void {
    this.pendingStage = stage;
    this.pendingStageAction = action;
    this.stageDependencySummary = null;
    this.api.stageDependencies(stage.id).pipe(takeUntil(this.destroy$)).subscribe({ next: summary => { this.stageDependencySummary = summary; this.dependencyDialogVisible = true; }, error: error => this.setError(error) });
  }

  confirmDependencyAction(): void {
    if (this.saving || !this.pendingStage || !this.pendingStageAction || !this.canConfirmDependencyAction) return;
    if (this.pendingStageAction === 'delete') {
      this.save(this.api.deleteOperationalStage(this.pendingStage.id, this.localCorrelation('stages')), () => { this.closeDependencyDialog(); this.loadOperationalStages(); });
      return;
    }

    this.save(this.api.deactivateOperationalStage(this.pendingStage.id, this.localCorrelation('stages')), stage => {
      const existingStage = this.operationalStages.find(item => item.id === stage.id);
      const updatedStage = existingStage ? { ...existingStage, ...stage } : stage;
      this.operationalStages = this.stageStatusFilter === 'active'
        ? this.operationalStages.filter(item => item.id !== updatedStage.id)
        : this.upsert(this.operationalStages, updatedStage, 'sequenceOrder');
      this.closeDependencyDialog();
    });
  }

  closeDependencyDialog(): void { this.dependencyDialogVisible = false; this.pendingStage = null; this.pendingStageAction = null; this.stageDependencySummary = null; }

  setOperationalStageActive(stage: SubStageOption): void {
    if (stage.isActive) { this.openDependencyDialog(stage, 'disable'); return; }
    this.save(this.api.updateOperationalStage(stage.id, { isActive: true }, this.localCorrelation('stages')), () => this.loadOperationalStages());
  }

  saveModel(): void { if (this.modelForm.valid) { const correlationId = this.localCorrelation('models'); const value = this.modelForm.getRawValue(); this.save(this.editModelId ? this.api.updateModel(this.editModelId, { name: value.name ?? undefined, description: value.description }, correlationId) : this.api.createModel({ code: value.code!, name: value.name!, description: value.description }, correlationId), item => { if (this.selected?.id === item.id) this.selected = { ...this.selected, ...item }; this.editModelId = ''; this.modelFormVisible = false; this.modelForm.reset(); this.modelForm.controls.code.enable({ emitEvent: false }); this.loadModelPage(this.modelListSearch, this.modelPage); }); } }
  editModel(item: ProductModelItem): void { this.editModelId = item.id; this.modelFormVisible = true; this.modelForm.reset(item); this.modelForm.controls.code.disable({ emitEvent: false }); }
  select(item: ProductModelItem): void {
    this.selected = item;
    this.modelStageSearch = '';
    this.api.modelStages(item.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: stages => { this.stages = [...stages].sort((left, right) => left.stageOrder - right.stageOrder); this.rebuildAvailableStageOptions(); },
      error: error => this.setError(error)
    });
    if (!this.availableStageOptions.length && !this.availableStagesLoading) this.loadAvailableStageCatalog();
  }
  saveModelStage(): void { if (!this.selected || this.modelStageForm.invalid) return; const value = this.modelStageForm.getRawValue(); if (this.stages.some(item => item.subStageId === value.subStageId && item.id !== this.editModelStageId) || this.stages.some(item => item.stageOrder === value.stageOrder && item.id !== this.editModelStageId)) { this.error = 'لا يمكن تكرار المرحلة أو ترتيبها داخل الموديل.'; return; } const correlationId = this.localCorrelation('models'); this.save(this.editModelStageId ? this.api.updateModelStage(this.selected.id, this.editModelStageId, value, correlationId) : this.api.addModelStage(this.selected.id, value, correlationId), item => { this.stages = this.upsert(this.stages, item, 'stageOrder'); this.rebuildAvailableStageOptions(); this.editModelStageId = ''; this.modelStageFormVisible = false; }); }
  editModelStage(item: ModelStageItem): void { this.editModelStageId = item.id; this.modelStageFormVisible = true; this.modelStageForm.reset(item); this.rebuildAvailableStageOptions(); }
  isModelStageSaving(id: string): boolean { return this.modelStageSavingIds.has(id); }
  toggleModelStage(item: ModelStageItem): void {
    const selectedModel = this.selected;
    if (!selectedModel || this.isModelStageSaving(item.id)) return;

    this.error = '';
    this.modelStageSavingIds.add(item.id);
    this.api.updateModelStage(
      selectedModel.id,
      item.id,
      { isActive: !item.isActive },
      this.localCorrelation('models')
    ).pipe(
      finalize(() => this.modelStageSavingIds.delete(item.id)),
      takeUntil(this.destroy$)
    ).subscribe({
      next: updated => {
        if (this.selected?.id !== selectedModel.id) return;
        this.stages = this.upsert(this.stages, updated, 'stageOrder');
        this.rebuildAvailableStageOptions();
      },
      error: error => this.setError(error)
    });
  }
  setModelActive(item: ProductModelItem): void { if (confirm(item.isActive ? 'تعطيل الموديل؟' : 'تفعيل الموديل؟')) this.save(this.api.setModelActivation(item.id, !item.isActive, this.localCorrelation('models')), () => this.models = this.models.map(model => model.id === item.id ? { ...model, isActive: !item.isActive } : model)); }
  onModelStageSearch(value: string): void { this.modelStageSearch = value; }
  clearModelStageSearch(): void { this.modelStageSearch = ''; }
  onAvailableStagesFilter(value: string): void {
    this.availableStagesSearch = value.trim();
    this.loadAvailableStageCatalog();
  }
  syncStageDropdownPanelWidth(): void {
    const trigger = document.getElementById('modelStageSubStage')?.closest<HTMLElement>('.p-dropdown');
    const width = trigger?.getBoundingClientRect().width;
    this.stageDropdownPanelStyle = width ? {
      width: `${Math.floor(width)}px`,
      minWidth: `${Math.floor(width)}px`,
      maxWidth: 'calc(100vw - 1rem)',
      boxSizing: 'border-box'
    } : {};
  }
  retryAvailableStages(): void { this.loadAvailableStageCatalog(); }
  onModelFormVisibility(visible: boolean): void { this.modelFormVisible = visible; if (!visible) { this.editModelId = ''; this.modelForm.reset(); this.modelForm.controls.code.enable({ emitEvent: false }); } }
  onModelStageFormVisibility(visible: boolean): void { this.modelStageFormVisible = visible; if (!visible) { this.editModelStageId = ''; this.modelStageForm.reset({ stageOrder: 1, piecePrice: 0, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }); } }
  subName(id: string): string { return this.availableStageOptionCache.get(id)?.name ?? this.stages.find(item => item.subStageId === id)?.subStageName ?? '-'; }
  totalPrice(): number { return this.stages.filter(item => item.isActive).reduce((sum, item) => sum + item.piecePrice, 0); }
  totalSeconds(): number { return this.stages.filter(item => item.isActive).reduce((sum, item) => sum + (item.standardSeconds ?? 0), 0); }
  stageStatusLabel(isActive: boolean): string { return isActive ? 'فعالة' : 'معطلة'; }
  stageStructurePath(stage: Pick<SubStageOption, 'factoryName' | 'departmentNameAr' | 'productionLineName'>): string {
    return [stage.factoryName, stage.departmentNameAr, stage.productionLineName].filter((value): value is string => !!value?.trim()).join(' ← ');
  }

  private save<T>(request: Observable<T>, success?: (result: T) => void): void { this.saving = true; this.error = ''; request.pipe(finalize(() => this.saving = false), takeUntil(this.destroy$)).subscribe({ next: result => success?.(result), error: error => this.setError(error) }); }
  private loadModelPage(search: string, page: number): void { const requestVersion = ++this.modelRequestVersion; this.modelListLoading = true; this.error = ''; this.api.modelSearchPage(search, page, this.modelPageSize, this.modelStatusFilter).pipe(finalize(() => { if (requestVersion === this.modelRequestVersion) this.modelListLoading = false; }), takeUntil(this.destroy$)).subscribe({ next: result => { if (requestVersion !== this.modelRequestVersion) return; const nearestPage = result.totalCount > 0 ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1; if (result.items.length === 0 && page > nearestPage) { this.loadModelPage(search, nearestPage); return; } this.applyModelPage(result); if (this.selectedModelFilterNode) this.loadModelMembership(result.items); }, error: error => { if (requestVersion === this.modelRequestVersion) this.setError(error); } }); }
  private applyModelPage(page: { items: ProductModelItem[]; totalCount: number; pageNumber: number; pageSize: number }): void { this.models = page.items; this.modelTotal = page.totalCount; this.modelPage = page.pageNumber; this.modelPageSize = page.pageSize; }
  private loadModelMembership(models: readonly ProductModelItem[]): void {
    const pending = models.filter(model => !this.modelLineMembership.has(model.id));
    if (!pending.length) return;
    this.modelScopeLoading = true;
    forkJoin(pending.map(model => this.api.modelStages(model.id).pipe(map(stages => ({ modelId: model.id, stages })))))
      .pipe(finalize(() => this.modelScopeLoading = false), takeUntil(this.destroy$))
      .subscribe({
        next: results => results.forEach(result => this.modelLineMembership.set(result.modelId, new Set(result.stages.map(stage => this.availableStageOptionCache.get(stage.subStageId)?.productionLineId).filter((id): id is string => !!id)))),
        error: error => this.setError(error)
      });
  }
  private structureLineIds(node: FactoryStructureTreeNode): Set<string> {
    const data = node.data;
    if (!data) return new Set();
    if (data.entityType === 'line') return new Set([data.entityId]);
    if (data.entityType === 'department') return new Set(this.lines.filter(line => line.departmentId === data.entityId).map(line => line.id));
    return new Set(this.lines.filter(line => line.factoryId === data.entityId).map(line => line.id));
  }
  private loadAvailableStageCatalog(): void {
    const requestVersion = ++this.availableStagesRequestVersion;
    this.availableStagesLoading = true;
    this.availableStagesError = '';
    this.fetchAvailableStageCatalog().pipe(
      finalize(() => { if (requestVersion === this.availableStagesRequestVersion) this.availableStagesLoading = false; }),
      takeUntil(this.destroy$)
    ).subscribe({
      next: options => {
        if (requestVersion !== this.availableStagesRequestVersion) return;
        options.forEach(option => this.availableStageOptionCache.set(option.id, option));
        this.availableStageCatalog = options;
        this.rebuildAvailableStageOptions();
      },
      error: error => {
        if (requestVersion === this.availableStagesRequestVersion) {
          this.availableStagesError = error instanceof Error ? error.message : 'تعذر تحميل المراحل المتاحة للربط.';
        }
      }
    });
  }
  private fetchAvailableStageCatalog(): Observable<SubStageOption[]> {
    return this.api.searchSubStagesByNameOrCode(this.availableStagesSearch, 1, 200).pipe(map(page => page.items ?? []));
  }
  private rebuildAvailableStageOptions(): void {
    const linkedStageIds = new Set(this.stages.filter(stage => stage.id !== this.editModelStageId).map(stage => stage.subStageId));
    const available = this.availableStageCatalog
      .filter(option => !linkedStageIds.has(option.id))
      .sort((left, right) => left.sequenceOrder - right.sequenceOrder || left.name.localeCompare(right.name));
    this.availableStageOptions = available;
  }
  private toAvailableOption(item: ModelStageItem): SubStageOption {
    return { id: item.subStageId, mainStageId: '', code: item.subStageCode || '—', name: item.subStageName || 'مرحلة مرتبطة', capacity: 0, sequenceOrder: item.stageOrder, isActive: item.isActive };
  }
  linkedStageName(item: ModelStageItem): string { return item.subStageName || this.availableStageOptionCache.get(item.subStageId)?.name || '-'; }
  linkedStageCode(item: ModelStageItem): string { return item.subStageCode || this.availableStageOptionCache.get(item.subStageId)?.code || '—'; }
  private upsert<T extends { id: string }>(items: readonly T[], item: T, sortKey?: keyof T): T[] { const next = items.some(candidate => candidate.id === item.id) ? items.map(candidate => candidate.id === item.id ? item : candidate) : [...items, item]; return sortKey ? [...next].sort((left, right) => Number(left[sortKey]) - Number(right[sortKey])) : next; }
  trackByModelStageId(_index: number, item: ModelStageItem): string { return item.id; }
  private setError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      const message = error.error?.error?.message ?? error.error?.message;
      if (typeof message === 'string' && message.trim()) {
        this.error = message;
        return;
      }
      if (error.status === 404) {
        this.error = 'تعذر تحديث مرحلة الموديل لأن العلاقة لم تعد موجودة. حدّث البيانات ثم حاول مرة أخرى.';
        return;
      }
      if (error.status === 409) {
        this.error = 'تعذر تحديث مرحلة الموديل بسبب تعارض مع تعديل متزامن. حدّث البيانات ثم حاول مرة أخرى.';
        return;
      }
      if (error.status === 0) {
        this.error = 'تعذر الاتصال بالخادم أثناء تحديث مرحلة الموديل. لم تتغير الحالة المعروضة.';
        return;
      }
    }
    this.error = error instanceof Error ? error.message : 'تعذر تحميل أو حفظ البيانات.';
  }
  private refreshModelsFromRealtime(): void {
    this.loadModelPage(this.modelListSearch, this.modelPage);
    if (this.selected) this.select(this.selected);
    this.loadAvailableStageCatalog();
  }
  private refreshStagesFromRealtime(): void {
    const selectedId = this.selectedStageFilterNode?.data?.entityId;
    forkJoin({
      factories: this.api.factories(),
      departments: this.api.departments(undefined, false),
      lines: this.api.allProductionLines()
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: data => {
        this.applyStageStructureData(data);
        this.rebuildStageFilterTree();
        this.selectedStageFilterNode = selectedId ? findFactoryStructureNode(this.stageFilterTreeNodes, selectedId) ?? null : null;
        if (this.selectedStageFilterNode) {
          this.applyStageFilterSelection(this.selectedStageFilterNode);
          this.loadOperationalStages();
        } else {
          this.validateStageFilterContextAfterStructureRefresh();
        }
      },
      error: error => this.setError(error)
    });
  }

  private clearStageContext(level: 'factory' | 'department' | 'line'): void {
    if (level === 'factory') {
      this.stageFiltersForm.patchValue({ factoryId: '', departmentId: '', productionLineId: '' });
    } else if (level === 'department') {
      this.stageFiltersForm.patchValue({ departmentId: '', productionLineId: '' });
    } else {
      this.stageFiltersForm.patchValue({ productionLineId: '' });
    }
    this.operationalStages = [];
  }

  private applyStageStructureData(data: { factories: readonly { id: string; code: string; name: string; isActive: boolean }[]; departments: readonly DepartmentItem[]; lines: readonly ProductionLineOption[] }): void {
    this.factories = data.factories.filter(factory => factory.isActive);
    const factoryIds = new Set(this.factories.map(factory => factory.id));
    this.departments = data.departments.filter(item => item.isActive !== false && !!item.factoryId && factoryIds.has(item.factoryId));
    const departmentIds = new Set(this.departments.map(item => item.id).filter(Boolean));
    this.lines = data.lines.filter(line => line.isActive && factoryIds.has(line.factoryId) && (!line.departmentId || departmentIds.has(line.departmentId)));
  }

  private validateStageFilterContextAfterStructureRefresh(): void {
    const factoryId = this.stageFiltersForm.controls.factoryId.value;
    const departmentId = this.stageFiltersForm.controls.departmentId.value;
    const productionLineId = this.stageFiltersForm.controls.productionLineId.value;
    if (factoryId && !this.factories.some(factory => factory.id === factoryId)) {
      this.stageFiltersForm.patchValue({ factoryId: '', departmentId: '', productionLineId: '' });
      this.operationalStages = [];
      return;
    }
    if (departmentId && !this.departments.some(department => department.id === departmentId)) {
      this.stageFiltersForm.patchValue({ departmentId: '', productionLineId: '' });
      this.operationalStages = [];
      return;
    }
    if (productionLineId && !this.lines.some(line => line.id === productionLineId)) {
      this.stageFiltersForm.patchValue({ productionLineId: '' });
      this.operationalStages = [];
      return;
    }
    if (productionLineId) this.loadOperationalStages();
    else this.operationalStages = [];
  }

  private rebuildStageFilterTree(): void {
    this.stageFilterExpandedIds = collectExpandedIds(this.stageFilterTreeNodes).size ? collectExpandedIds(this.stageFilterTreeNodes) : this.stageFilterExpandedIds;
    this.stageFilterTreeNodes = buildFactoryStructureTree({
      factories: this.factories,
      departments: this.departments,
      lines: this.lines,
      eligibility: new Map()
    }, this.stageFilterExpandedIds);
    const selectedId = this.selectedStageFilterNode?.data?.entityId;
    this.selectedStageFilterNode = selectedId ? findFactoryStructureNode(this.stageFilterTreeNodes, selectedId) ?? null : null;
  }

  private applyStageFilterSelection(node: FactoryStructureTreeNode): void {
    const data = node.data;
    if (!data) return;
    if (data.entityType === 'factory') {
      this.stageFiltersForm.patchValue({ factoryId: data.entityId, departmentId: '', productionLineId: '' });
      return;
    }
    if (data.entityType === 'department') {
      this.stageFiltersForm.patchValue({ factoryId: data.parentId ?? '', departmentId: data.entityId, productionLineId: '' });
      return;
    }
    const line = data.source as ProductionLineOption;
    this.stageFiltersForm.patchValue({ factoryId: line.factoryId, departmentId: line.departmentId ?? '', productionLineId: data.entityId });
  }

  private isCurrentStageEditHydration(version: number, stageId: string): boolean {
    return version === this.stageEditHydrationVersion && stageId === this.editStageId && !!this.stageEditHierarchy;
  }

  private restoreStageEditHierarchy(version: number, stageId: string): void {
    if (!this.isCurrentStageEditHydration(version, stageId) || !this.stageEditHierarchy) return;
    this.stageEditForm.patchValue(this.stageEditHierarchy, { emitEvent: false });
  }

  private cancelStageEditHydration(): void {
    this.stageEditHydrationVersion += 1;
    this.stageEditHydrating = false;
    this.stageEditHierarchy = null;
    this.editingStageSnapshot = null;
  }

  private stageHierarchyFilters(): { factoryId?: string; departmentId?: string; productionLineId?: string } | null {
    const data = this.selectedStageFilterNode?.data;
    if (!data) {
      const productionLineId = this.stageFiltersForm.controls.productionLineId.value;
      return productionLineId ? { productionLineId } : null;
    }
    if (data.entityType === 'factory') return { factoryId: data.entityId };
    if (data.entityType === 'department') return { departmentId: data.entityId };
    return { productionLineId: data.entityId };
  }

  private stageFilterPath(node: FactoryStructureTreeNode): string[] {
    const path = [node.data?.name ?? ''];
    let parentId = node.data?.parentId;
    while (parentId) {
      const parent = findFactoryStructureNode(this.stageFilterTreeNodes, parentId);
      if (!parent?.data) break;
      path.unshift(parent.data.name);
      parentId = parent.data.parentId;
    }
    return path.filter(Boolean);
  }

  private localCorrelation(screen: 'models' | 'stages'): string | undefined {
    return this.manufacturingRealtime?.registerLocalOperation(screen);
  }
}
