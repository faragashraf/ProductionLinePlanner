import { Component, OnDestroy, OnInit, Optional } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, Validators } from '@angular/forms';
import { Subject, debounceTime, distinctUntilChanged, finalize, forkJoin, map, Observable, takeUntil } from 'rxjs';
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

type StageStatusFilter = 'all' | 'active' | 'inactive';

@Component({ selector: 'app-manufacturing-master-data-page', templateUrl: './manufacturing-master-data-page.component.html', styleUrls: ['./manufacturing-master-data-page.component.scss'] })
export class ManufacturingMasterDataPageComponent implements OnInit, OnDestroy {
  readonly mode: 'stages' | 'models';
  loading = true;
  saving = false;
  error = '';

  factories: { id: string; code: string; name: string; isActive: boolean }[] = [];
  departments: DepartmentItem[] = [];
  lines: ProductionLineOption[] = [];
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
  modelSearch = '';
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
  linkedStagesSearch = '';
  availableStagesSearch = '';
  availableStagesLoading = false;
  availableStagesError = '';
  private availableStageCatalog: SubStageOption[] = [];
  availableStageOptions: SubStageOption[] = [];
  stageDropdownPanelStyle: Record<string, string> = {};
  private readonly availableStageOptionCache = new Map<string, SubStageOption>();

  readonly stageForm = this.fb.group({
    factoryId: ['', Validators.required],
    departmentId: ['', Validators.required],
    productionLineId: ['', Validators.required],
    name: ['', Validators.required],
    capacity: [0, [Validators.required, Validators.min(0)]]
  });
  readonly modelForm = this.fb.group({ code: ['', Validators.required], name: ['', Validators.required], description: [''] });
  readonly modelStageForm = this.fb.group({ subStageId: ['', Validators.required], stageOrder: [1, Validators.required], piecePrice: [0, Validators.required], standardSeconds: [null as number | null], compensationMode: ['SharedPercentage', Validators.required], isRequired: [true], isActive: [true] });

  private readonly modelSearch$ = new Subject<string>();
  private readonly destroy$ = new Subject<void>();
  private modelRequestVersion = 0;
  private availableStagesRequestVersion = 0;
  private stageFilterExpandedIds = new Set<string>();
  private stopRealtime?: () => void;

  constructor(private readonly fb: FormBuilder, private readonly api: ManufacturingMasterDataApiService, route: ActivatedRoute, @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService) {
    this.mode = route.snapshot.routeConfig?.path === 'models' ? 'models' : 'stages';
  }

  ngOnInit(): void {
    this.modelSearch$.pipe(debounceTime(250), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(search => this.loadModelPage(search, 1));
    this.stopRealtime = this.mode === 'models'
      ? this.manufacturingRealtime?.watchScreen({ screen: 'models', refresh: () => this.refreshModelsFromRealtime() })
      : this.manufacturingRealtime?.watchScreen({
        screen: 'stages',
        matches: change => change.entityType === 'Factory' || change.entityType === 'Department' || change.entityType === 'ProductionLine' || change.productionLineId === this.stageForm.controls.productionLineId.value,
        refresh: () => this.refreshStagesFromRealtime()
      });
    this.reload();
  }

  ngOnDestroy(): void { this.stopRealtime?.(); this.destroy$.next(); this.destroy$.complete(); }

  get activeDepartments(): DepartmentItem[] {
    const factoryId = this.stageForm.controls.factoryId.value;
    return this.departments.filter(item => item.isActive !== false && (!factoryId || item.factoryId === factoryId));
  }
  get activeLines(): ProductionLineOption[] {
    const departmentId = this.stageForm.controls.departmentId.value;
    return this.lines.filter(item => item.isActive && (!departmentId || item.departmentId === departmentId));
  }
  get selectedStage(): SubStageOption | null { return this.operationalStages.find(stage => stage.id === this.editStageId) ?? null; }
  get visibleStageFilterTreeNodes(): FactoryStructureTreeNode[] { return filterFactoryStructureTree(this.stageFilterTreeNodes, this.stageTreeSearch); }
  get selectedStageFilterPath(): string { return this.selectedStageFilterNode ? this.stageFilterPath(this.selectedStageFilterNode).join(' / ') : 'كل المصانع'; }
  get stageFilterResetKey(): string { return `${this.selectedStageFilterNode?.data?.entityType ?? 'all'}:${this.selectedStageFilterNode?.data?.entityId ?? 'all'}:${this.stageStatusFilter}:${this.stageSearch}`; }
  get filteredOperationalStages(): SubStageOption[] { return this.operationalStages.filter(stage => matchesSearchTerm(this.stageSearch, [stage.name, stage.code])); }
  get stageResultCount(): number { return this.filteredOperationalStages.length; }
  get stageEmptyMessage(): string { return normalizeSearchText(this.stageSearch) ? 'لا توجد مراحل مطابقة للبحث.' : 'اختر مصنعًا أو قسمًا أو خط إنتاج لعرض مراحل الإنتاج، أو لا توجد مراحل مطابقة.'; }
  get modelEmptyMessage(): string { return normalizeSearchText(this.modelSearch) ? 'لا توجد موديلات مطابقة للبحث.' : 'لا توجد موديلات لعرضها.'; }
  get filteredLinkedStages(): ModelStageItem[] {
    return [...this.stages]
      .filter(item => matchesSearchTerm(this.linkedStagesSearch, [this.linkedStageName(item), this.linkedStageCode(item)]))
      .sort((left, right) => left.stageOrder - right.stageOrder);
  }
  get linkedStagesEmptyMessage(): string {
    return normalizeSearchText(this.linkedStagesSearch)
      ? 'توجد مراحل مرتبطة، لكن لا توجد نتائج مطابقة للبحث.'
      : 'لا توجد مراحل مرتبطة بهذا الموديل.';
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
      this.api.modelSearchPage('', 1, this.modelPageSize)
        .pipe(finalize(() => this.loading = false), takeUntil(this.destroy$))
        .subscribe({ next: page => { this.applyModelPage(page); this.loadAvailableStageCatalog(); }, error: error => this.setError(error) });
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
    this.stageForm.patchValue({ factoryId: '', departmentId: '', productionLineId: '' });
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

  selectFactory(factoryId: string): void {
    this.stageForm.patchValue({ factoryId, departmentId: '', productionLineId: '' });
    this.departments = [];
    this.lines = [];
    this.operationalStages = [];
    if (!factoryId) return;
    this.api.departments(factoryId, false).pipe(takeUntil(this.destroy$)).subscribe({ next: departments => this.departments = departments.filter(item => item.factoryId === factoryId && item.isActive), error: error => this.setError(error) });
  }

  selectDepartment(departmentId: string): void {
    this.stageForm.patchValue({ departmentId, productionLineId: '' });
    this.lines = [];
    this.operationalStages = [];
    if (!departmentId) return;
    this.api.productionLinesForDepartment(departmentId).pipe(takeUntil(this.destroy$)).subscribe({ next: lines => this.lines = lines.filter(line => line.departmentId === departmentId && line.isActive), error: error => this.setError(error) });
  }

  selectLine(productionLineId: string): void {
    this.stageForm.patchValue({ productionLineId });
    this.operationalStages = [];
    if (productionLineId) this.loadOperationalStages();
  }

  setStageStatusFilter(value: string): void {
    this.stageStatusFilter = value === 'active' || value === 'inactive' ? value : 'all';
    if (this.selectedStageFilterNode) this.loadOperationalStages();
  }

  onStageSearch(value: string): void { this.stageSearch = value; }
  onModelSearch(value: string): void { this.modelSearch = value; this.modelPage = 1; this.modelSearch$.next(value); }
  onModelLazyLoad(event: { first?: number | null; rows?: number | null }): void { const page = Math.floor((event.first ?? 0) / (event.rows ?? this.modelPageSize)) + 1; if (page !== this.modelPage) this.loadModelPage(this.modelSearch, page); }

  loadOperationalStages(): void {
    const filters = this.stageHierarchyFilters();
    if (!filters) return;
    const isActive = this.stageStatusFilter === 'all' ? undefined : this.stageStatusFilter === 'active';
    this.api.operationalStages({ ...filters, isActive, includeInactive: this.stageStatusFilter === 'all' }).pipe(takeUntil(this.destroy$)).subscribe({ next: stages => this.operationalStages = stages, error: error => this.setError(error) });
  }

  openStageForm(): void {
    this.editStageId = '';
    this.stageFormVisible = true;
    this.stageForm.patchValue({ name: '', capacity: 0 });
  }

  editOperationalStage(stage: SubStageOption): void {
    this.editStageId = stage.id;
    this.stageFormVisible = true;
    this.stageForm.reset({ factoryId: stage.factoryId ?? '', departmentId: stage.departmentId ?? '', productionLineId: stage.productionLineId ?? '', name: stage.name, capacity: stage.capacity });
    if (stage.factoryId) this.api.departments(stage.factoryId, false).pipe(takeUntil(this.destroy$)).subscribe(departments => this.departments = departments);
    if (stage.departmentId) this.api.productionLinesForDepartment(stage.departmentId).pipe(takeUntil(this.destroy$)).subscribe(lines => this.lines = lines);
  }

  saveOperationalStage(): void {
    if (this.stageForm.invalid) { this.stageForm.markAllAsTouched(); return; }
    const value = this.stageForm.getRawValue();
    const correlationId = this.localCorrelation('stages');
    const request = this.editStageId
      ? this.api.updateOperationalStage(this.editStageId, { name: value.name, capacity: value.capacity }, correlationId)
      : this.api.createOperationalStage({ productionLineId: value.productionLineId!, name: value.name!, capacity: value.capacity! }, correlationId);
    this.save(request, () => { this.stageFormVisible = false; this.editStageId = ''; this.loadOperationalStages(); });
  }

  closeStageForm(): void { this.stageFormVisible = false; this.editStageId = ''; }

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

  saveModel(): void { if (this.modelForm.valid) { const correlationId = this.localCorrelation('models'); const value = this.modelForm.getRawValue(); this.save(this.editModelId ? this.api.updateModel(this.editModelId, { name: value.name ?? undefined, description: value.description }, correlationId) : this.api.createModel({ code: value.code!, name: value.name!, description: value.description }, correlationId), item => { if (this.selected?.id === item.id) this.selected = { ...this.selected, ...item }; this.editModelId = ''; this.modelFormVisible = false; this.modelForm.reset(); this.modelForm.controls.code.enable({ emitEvent: false }); this.loadModelPage(this.modelSearch, this.modelPage); }); } }
  editModel(item: ProductModelItem): void { this.editModelId = item.id; this.modelFormVisible = true; this.modelForm.reset(item); this.modelForm.controls.code.disable({ emitEvent: false }); }
  select(item: ProductModelItem): void {
    this.selected = item;
    this.linkedStagesSearch = '';
    this.api.modelStages(item.id).pipe(takeUntil(this.destroy$)).subscribe({
      next: stages => { this.stages = [...stages].sort((left, right) => left.stageOrder - right.stageOrder); this.rebuildAvailableStageOptions(); },
      error: error => this.setError(error)
    });
    if (!this.availableStageOptions.length && !this.availableStagesLoading) this.loadAvailableStageCatalog();
  }
  saveModelStage(): void { if (!this.selected || this.modelStageForm.invalid) return; const value = this.modelStageForm.getRawValue(); if (this.stages.some(item => item.subStageId === value.subStageId && item.id !== this.editModelStageId) || this.stages.some(item => item.stageOrder === value.stageOrder && item.id !== this.editModelStageId)) { this.error = 'لا يمكن تكرار المرحلة أو ترتيبها داخل الموديل.'; return; } const correlationId = this.localCorrelation('models'); this.save(this.editModelStageId ? this.api.updateModelStage(this.selected.id, this.editModelStageId, value, correlationId) : this.api.addModelStage(this.selected.id, value, correlationId), item => { this.stages = this.upsert(this.stages, item, 'stageOrder'); this.rebuildAvailableStageOptions(); this.editModelStageId = ''; this.modelStageFormVisible = false; }); }
  editModelStage(item: ModelStageItem): void { this.editModelStageId = item.id; this.modelStageFormVisible = true; this.modelStageForm.reset(item); this.rebuildAvailableStageOptions(); }
  disableModelStage(id: string): void { if (this.selected && confirm('سيتم تعطيل إعداد المرحلة.')) this.save(this.api.deactivateModelStage(this.selected.id, id, this.localCorrelation('models')), () => { this.stages = this.markInactive(this.stages, id); this.rebuildAvailableStageOptions(); }); }
  setModelActive(item: ProductModelItem): void { if (confirm(item.isActive ? 'تعطيل الموديل؟' : 'تفعيل الموديل؟')) this.save(this.api.setModelActivation(item.id, !item.isActive, this.localCorrelation('models')), () => this.models = this.models.map(model => model.id === item.id ? { ...model, isActive: !item.isActive } : model)); }
  onLinkedStagesSearch(value: string): void { this.linkedStagesSearch = value; }
  clearLinkedStagesSearch(): void { this.linkedStagesSearch = ''; }
  onAvailableStagesFilter(value: string): void {
    this.availableStagesSearch = value.trim();
    this.loadAvailableStageCatalog();
  }
  syncStageDropdownPanelWidth(): void {
    const trigger = document.getElementById('modelStageSubStage')?.closest<HTMLElement>('.p-dropdown');
    const width = trigger?.getBoundingClientRect().width;
    this.stageDropdownPanelStyle = width ? { width: `${Math.floor(width)}px`, background: '#fff', opacity: '1' } : {};
  }
  retryAvailableStages(): void { this.loadAvailableStageCatalog(); }
  onModelFormVisibility(visible: boolean): void { this.modelFormVisible = visible; if (!visible) { this.editModelId = ''; this.modelForm.reset(); this.modelForm.controls.code.enable({ emitEvent: false }); } }
  onModelStageFormVisibility(visible: boolean): void { this.modelStageFormVisible = visible; if (!visible) { this.editModelStageId = ''; this.modelStageForm.reset({ stageOrder: 1, piecePrice: 0, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }); } }
  subName(id: string): string { return this.availableStageOptionCache.get(id)?.name ?? this.stages.find(item => item.subStageId === id)?.subStageName ?? '-'; }
  totalPrice(): number { return this.stages.filter(item => item.isActive).reduce((sum, item) => sum + item.piecePrice, 0); }
  totalSeconds(): number { return this.stages.filter(item => item.isActive).reduce((sum, item) => sum + (item.standardSeconds ?? 0), 0); }
  stageStatusLabel(isActive: boolean): string { return isActive ? 'فعالة' : 'معطلة'; }

  private save<T>(request: Observable<T>, success?: (result: T) => void): void { this.saving = true; this.error = ''; request.pipe(finalize(() => this.saving = false), takeUntil(this.destroy$)).subscribe({ next: result => success?.(result), error: error => this.setError(error) }); }
  private loadModelPage(search: string, page: number): void { const requestVersion = ++this.modelRequestVersion; this.modelListLoading = true; this.error = ''; this.api.modelSearchPage(search, page, this.modelPageSize).pipe(finalize(() => { if (requestVersion === this.modelRequestVersion) this.modelListLoading = false; }), takeUntil(this.destroy$)).subscribe({ next: result => { if (requestVersion !== this.modelRequestVersion) return; const nearestPage = result.totalCount > 0 ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1; if (result.items.length === 0 && page > nearestPage) { this.loadModelPage(search, nearestPage); return; } this.applyModelPage(result); }, error: error => { if (requestVersion === this.modelRequestVersion) this.setError(error); } }); }
  private applyModelPage(page: { items: ProductModelItem[]; totalCount: number; pageNumber: number; pageSize: number }): void { this.models = page.items; this.modelTotal = page.totalCount; this.modelPage = page.pageNumber; this.modelPageSize = page.pageSize; }
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
  private markInactive<T extends { id: string; isActive: boolean }>(items: readonly T[], id: string): T[] { return items.map(item => item.id === id ? { ...item, isActive: false } : item); }
  private setError(error: unknown): void { this.error = error instanceof Error ? error.message : 'تعذر تحميل أو حفظ البيانات.'; }
  private refreshModelsFromRealtime(): void {
    this.loadModelPage(this.modelSearch, this.modelPage);
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
          this.validateStageFormContextAfterStructureRefresh();
        }
      },
      error: error => this.setError(error)
    });
  }

  private clearStageContext(level: 'factory' | 'department' | 'line'): void {
    if (level === 'factory') {
      this.stageForm.patchValue({ factoryId: '', departmentId: '', productionLineId: '' });
      this.departments = [];
      this.lines = [];
    } else if (level === 'department') {
      this.stageForm.patchValue({ departmentId: '', productionLineId: '' });
      this.lines = [];
    } else {
      this.stageForm.patchValue({ productionLineId: '' });
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

  private validateStageFormContextAfterStructureRefresh(): void {
    const factoryId = this.stageForm.controls.factoryId.value;
    const departmentId = this.stageForm.controls.departmentId.value;
    const productionLineId = this.stageForm.controls.productionLineId.value;
    if (factoryId && !this.factories.some(factory => factory.id === factoryId)) {
      this.stageForm.patchValue({ factoryId: '', departmentId: '', productionLineId: '' });
      this.operationalStages = [];
      return;
    }
    if (departmentId && !this.departments.some(department => department.id === departmentId)) {
      this.stageForm.patchValue({ departmentId: '', productionLineId: '' });
      this.operationalStages = [];
      return;
    }
    if (productionLineId && !this.lines.some(line => line.id === productionLineId)) {
      this.stageForm.patchValue({ productionLineId: '' });
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
      this.stageForm.patchValue({ factoryId: data.entityId, departmentId: '', productionLineId: '' });
      return;
    }
    if (data.entityType === 'department') {
      this.stageForm.patchValue({ factoryId: data.parentId ?? '', departmentId: data.entityId, productionLineId: '' });
      return;
    }
    const line = data.source as ProductionLineOption;
    this.stageForm.patchValue({ factoryId: line.factoryId, departmentId: line.departmentId ?? '', productionLineId: data.entityId });
  }

  private stageHierarchyFilters(): { factoryId?: string; departmentId?: string; productionLineId?: string } | null {
    const data = this.selectedStageFilterNode?.data;
    if (!data) {
      const productionLineId = this.stageForm.controls.productionLineId.value;
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
