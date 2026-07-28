import { Component, OnDestroy, OnInit, Optional, ViewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, Validators } from '@angular/forms';
import { MenuItem, MessageService, TreeNode } from 'primeng/api';
import { ContextMenu } from 'primeng/contextmenu';
import { EMPTY, Subject, debounceTime, distinctUntilChanged, finalize, forkJoin, Observable, switchMap, takeUntil } from 'rxjs';
import {
  DepartmentItem,
  CopyModelStagesRequest,
  CopyModelStagesSummary,
  ManufacturingMasterDataApiService,
  ModelStageItem,
  ProductModelItem,
  ProductModelDeleteEligibility,
  ProductionLineOption,
  StageDependencySummary,
  SubStageOption
} from '../../core/services/manufacturing-master-data-api.service';
import { matchesSearchTerm, normalizeSearchText } from '../../shared/utils/text-search.utils';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { buildFactoryStructureTree, collectExpandedIds, FactoryStructureTreeNode, filterFactoryStructureTree, findFactoryStructureNode } from './factory-structure-tree.adapter';
import { ManufacturingFilterOption } from './manufacturing-filter-card.component';
import { buildModelContextMenu, ModelContextAction } from './model-context-menu.builder';

type StageStatusFilter = 'all' | 'active' | 'inactive';
type ModelStatusFilter = StageStatusFilter;
type ModelStageRelationshipFilter = 'all' | 'linked' | 'unlinked';
interface ModelStageLineGroup {
  lineId: string;
  lineName: string;
  lineCode: string;
  structurePath: string;
  stages: ModelStageItem[];
}

interface ModelStageAvailabilityRow {
  stage: SubStageOption;
  lineName: string;
  relationship: ModelStageItem | null;
}

type ModelStageContextNodeType = 'factory' | 'model' | 'department' | 'line';
interface ModelStageContextNodeData {
  contextType: ModelStageContextNodeType;
  factoryId: string;
  modelId?: string;
  departmentId?: string;
  productionLineId?: string;
  code?: string;
  name: string;
}

@Component({ selector: 'app-manufacturing-master-data-page', templateUrl: './manufacturing-master-data-page.component.html', styleUrls: ['./manufacturing-master-data-page.component.scss'] })
export class ManufacturingMasterDataPageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;
  @ViewChild('modelContextMenu') private modelContextMenu?: ContextMenu;
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
  modelStageRelationshipFilter: ModelStageRelationshipFilter = 'all';
  selectedModelStageFactoryId = '';
  selectedModelStageDepartmentId = '';
  selectedModelStageProductionLineId = '';
  modelStageContextNodes: TreeNode[] = [];
  selectedModelStageContextNode: TreeNode | null = null;
  modelStagesLoading = false;
  modelContextMenuItems: MenuItem[] = [];
  modelDeleteDialogVisible = false;
  pendingModelDeletion: ProductModelItem | null = null;
  availableStagesLoading = false;
  availableStagesError = '';
  readonly modelStageSavingIds = new Set<string>();
  selectedModelStageIds = new Set<string>();
  bulkCopyDialogVisible = false;
  bulkCopyStep: 'configure' | 'confirm' = 'configure';
  bulkCopyBusy = false;
  bulkCopyError = '';
  bulkCopyTargetFactoryId = '';
  bulkCopyTargetModelId = '';
  bulkCopyTargetModels: ProductModelItem[] = [];
  bulkCopyTargetsLoading = false;
  bulkCopyTargetDepartmentId = '';
  bulkCopyTargetProductionLineId = '';
  bulkCopyPreview: CopyModelStagesSummary | null = null;
  private availableStageCatalog: SubStageOption[] = [];
  availableStageOptions: SubStageOption[] = [];
  stageDropdownPanelStyle: Record<string, string> = {};
  private readonly availableStageOptionCache = new Map<string, SubStageOption>();
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
  private contextModelNode: TreeNode | null = null;
  private modelContextMenuBusy = false;
  private contextModelDeleteEligibility: ProductModelDeleteEligibility | null = null;
  private stopRealtime?: () => void;

  constructor(private readonly fb: FormBuilder, private readonly api: ManufacturingMasterDataApiService, route: ActivatedRoute, private readonly permissionService: PermissionService, @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService, @Optional() private readonly messageService?: MessageService) {
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
  get filteredModels(): ProductModelItem[] { return this.models; }
  get modelResultTotal(): number { return this.modelTotal; }
  get modelFiltersActive(): boolean { return this.modelStatusFilter !== 'all' || !!this.modelListSearch.trim(); }
  get modelEmptyMessage(): string { return normalizeSearchText(this.modelListSearch) ? 'لا توجد موديلات مطابقة للبحث.' : 'لا توجد موديلات لعرضها.'; }
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
    const grouped = new Map<string, ModelStageLineGroup>();
    this.filteredLinkedStages.forEach(stage => {
      const catalog = this.availableStageOptionCache.get(stage.subStageId);
      const lineId = catalog?.productionLineId ?? 'unknown';
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
  get modelStageFactoryOptions(): { id: string; code: string; name: string; isActive: boolean }[] {
    return this.factories.filter(factory => factory.isActive);
  }
  get modelStageDepartmentOptions(): DepartmentItem[] {
    return this.departments.filter(department => department.isActive !== false
      && !!department.id
      && department.factoryId === this.selectedModelStageFactoryId);
  }
  get modelStageLineOptions(): ProductionLineOption[] {
    if (!this.selectedModelStageDepartmentId) return [];
    return this.lines
      .filter(line => line.isActive
        && line.factoryId === this.selectedModelStageFactoryId
        && line.departmentId === this.selectedModelStageDepartmentId)
      .sort((left, right) => left.sequenceOrder - right.sequenceOrder || left.name.localeCompare(right.name, 'ar'));
  }
  get availableModelStageRows(): ModelStageAvailabilityRow[] {
    if (!this.hasModelStageContext) return [];
    const relationships = new Map(this.stages.map(stage => [stage.subStageId, stage]));
    return this.availableStageCatalog
      .filter(stage => stage.isActive
        && this.factoryIdForStage(stage) === this.selectedModelStageFactoryId
        && this.departmentIdForStage(stage) === this.selectedModelStageDepartmentId
        && stage.productionLineId === this.selectedModelStageProductionLineId)
      .map(stage => ({ stage, lineName: this.lineNameForStage(stage), relationship: relationships.get(stage.id) ?? null }))
      .sort((left, right) => this.lineOrderForStage(left.stage) - this.lineOrderForStage(right.stage)
        || left.stage.sequenceOrder - right.stage.sequenceOrder
        || left.stage.name.localeCompare(right.stage.name, 'ar'));
  }
  get filteredAvailableModelStageRows(): ModelStageAvailabilityRow[] {
    return this.availableModelStageRows.filter(row => this.modelStageRelationshipFilter === 'all'
      || (this.modelStageRelationshipFilter === 'linked' ? !!row.relationship : !row.relationship));
  }
  get modelStageFilterEmptyMessage(): string {
    if (this.modelStageRelationshipFilter === 'linked') return 'لا توجد مراحل مرتبطة بهذا الموديل على الخط المختار.';
    if (this.modelStageRelationshipFilter === 'unlinked') return 'لا توجد مراحل غير مرتبطة متاحة على الخط المختار.';
    return 'الخط المختار لا يحتوي مراحل.';
  }
  get selectableModelStageRows(): ModelStageAvailabilityRow[] {
    return this.filteredAvailableModelStageRows.filter(row => !!row.relationship);
  }
  get selectedModelStageRelationships(): ModelStageItem[] {
    const selectedIds = this.selectedModelStageIds;
    return this.availableModelStageRows
      .map(row => row.relationship)
      .filter((relationship): relationship is ModelStageItem => !!relationship && selectedIds.has(relationship.id))
      .sort((left, right) => left.stageOrder - right.stageOrder);
  }
  get allVisibleModelStagesSelected(): boolean {
    return this.selectableModelStageRows.length > 0
      && this.selectableModelStageRows.every(row => this.selectedModelStageIds.has(row.relationship!.id));
  }
  get bulkCopyTargetFactoryOptions(): { id: string; code: string; name: string; isActive: boolean }[] {
    return this.modelStageFactoryOptions;
  }
  get bulkCopyTargetModelOptions(): ProductModelItem[] {
    return [...this.bulkCopyTargetModels].sort((left, right) => left.code.localeCompare(right.code, 'ar'));
  }
  get bulkCopyTargetDepartmentOptions(): DepartmentItem[] {
    return this.departments
      .filter(department => department.isActive !== false && department.factoryId === this.bulkCopyTargetFactoryId)
      .sort((left, right) => (left.sequenceOrder ?? 0) - (right.sequenceOrder ?? 0));
  }
  get bulkCopyTargetLineOptions(): ProductionLineOption[] {
    return this.lines
      .filter(line => line.isActive
        && line.factoryId === this.bulkCopyTargetFactoryId
        && line.departmentId === this.bulkCopyTargetDepartmentId)
      .sort((left, right) => left.sequenceOrder - right.sequenceOrder || left.name.localeCompare(right.name, 'ar'));
  }
  get bulkCopyTargetSameAsSource(): boolean {
    return this.bulkCopyTargetModelId === this.selected?.id
      && this.bulkCopyTargetProductionLineId === this.selectedModelStageProductionLineId;
  }
  get bulkCopyCanPreview(): boolean {
    return !!this.bulkCopyTargetFactoryId
      && !!this.bulkCopyTargetModelId
      && !!this.bulkCopyTargetDepartmentId
      && !!this.bulkCopyTargetProductionLineId
      && !this.bulkCopyTargetSameAsSource
      && this.selectedModelStageRelationships.length > 0
      && !this.bulkCopyTargetsLoading
      && !this.bulkCopyBusy;
  }
  get bulkCopySourceLabel(): string {
    return `${this.selected?.code ?? '—'} — ${this.lines.find(line => line.id === this.selectedModelStageProductionLineId)?.name ?? '—'}`;
  }
  get bulkCopyTargetLabel(): string {
    const model = this.bulkCopyTargetModels.find(item => item.id === this.bulkCopyTargetModelId);
    const line = this.lines.find(item => item.id === this.bulkCopyTargetProductionLineId);
    return `${model?.code ?? '—'} — ${line?.name ?? '—'}`;
  }
  get hasModelStageContext(): boolean {
    return !!this.selectedModelStageFactoryId
      && !!this.selected
      && !!this.selectedModelStageDepartmentId
      && !!this.selectedModelStageProductionLineId;
  }
  get modelStageContextMessage(): string {
    if (!this.selectedModelStageFactoryId) return 'اختر مصنعًا أولًا.';
    if (!this.selected) return 'اختر موديلًا لعرض علاقات مراحله.';
    if (!this.selectedModelStageDepartmentId) return 'اختر قسمًا تابعًا للمصنع.';
    if (!this.selectedModelStageProductionLineId) return 'اختر خط إنتاج تابعًا للقسم.';
    return '';
  }
  get modelStageContextBreadcrumb(): string {
    const factory = this.factories.find(item => item.id === this.selectedModelStageFactoryId)?.name ?? 'المصنع';
    const model = this.selected ? `${this.selected.code} — ${this.selected.name}` : 'الموديل';
    const department = this.departments.find(item => item.id === this.selectedModelStageDepartmentId)?.nameAr
      ?? this.departments.find(item => item.id === this.selectedModelStageDepartmentId)?.name
      ?? 'القسم';
    const line = this.lines.find(item => item.id === this.selectedModelStageProductionLineId)?.name ?? 'خط الإنتاج';
    return `${factory} ← ${model} ← ${department} ← ${line}`;
  }
  get canManageModels(): boolean { return this.permissionService.hasPermission(this.permissions.models.manage); }
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
        models: this.api.models(),
        factories: this.api.factories(),
        departments: this.api.departments(undefined, false),
        lines: this.api.allProductionLines(),
        stages: this.api.allSubStages()
      })
        .pipe(finalize(() => this.loading = false), takeUntil(this.destroy$))
        .subscribe({ next: data => {
          this.models = data.models;
          this.modelTotal = data.models.length;
          this.applyStageStructureData(data);
          data.stages.forEach(stage => this.availableStageOptionCache.set(stage.id, stage));
          this.availableStageCatalog = data.stages;
          this.rebuildModelStageContextTree();
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
  setModelStageRelationshipFilter(value: string): void { this.modelStageRelationshipFilter = value === 'linked' || value === 'unlinked' ? value : 'all'; }
  isModelStageSelected(stageId: string): boolean { return this.selectedModelStageIds.has(stageId); }
  toggleModelStageSelection(stageId: string, selected: boolean): void {
    const next = new Set(this.selectedModelStageIds);
    selected ? next.add(stageId) : next.delete(stageId);
    this.selectedModelStageIds = next;
  }
  toggleVisibleModelStageSelection(selected: boolean): void {
    const next = new Set(this.selectedModelStageIds);
    this.selectableModelStageRows.forEach(row => selected ? next.add(row.relationship!.id) : next.delete(row.relationship!.id));
    this.selectedModelStageIds = next;
  }
  clearModelFilters(): void { this.modelStatusFilter = 'all'; this.modelListSearch = ''; this.loadModelPage('', 1); }
  selectModelStageFactory(factoryId: string): void {
    this.selectedModelStageFactoryId = this.modelStageFactoryOptions.some(factory => factory.id === factoryId) ? factoryId : '';
    this.selectedModelStageDepartmentId = '';
    this.selectedModelStageProductionLineId = '';
    this.clearModelStageSelection();
    this.rebuildAvailableStageOptions();
  }
  selectModelStageModel(modelId: string): void {
    const model = this.models.find(item => item.id === modelId) ?? null;
    if (!model) {
      this.selected = null;
      this.stages = [];
      this.clearModelStageSelection();
      this.rebuildAvailableStageOptions();
      return;
    }
    this.select(model);
  }
  selectModelStageDepartment(departmentId: string): void {
    const department = this.modelStageDepartmentOptions.find(item => item.id === departmentId);
    this.selectedModelStageDepartmentId = department?.id ?? '';
    this.selectedModelStageProductionLineId = '';
    this.clearModelStageSelection();
    this.rebuildAvailableStageOptions();
  }
  selectModelStageProductionLine(productionLineId: string): void {
    const previousLineId = this.selectedModelStageProductionLineId;
    this.selectedModelStageProductionLineId = this.modelStageLineOptions.some(line => line.id === productionLineId)
      ? productionLineId
      : '';
    if (previousLineId !== this.selectedModelStageProductionLineId) this.clearModelStageSelection();
    this.rebuildAvailableStageOptions();
  }
  selectModelStageContextNode(node: TreeNode): void {
    const context = node.data as ModelStageContextNodeData | undefined;
    if (!context) return;
    this.selectedModelStageContextNode = node;
    const previousModelId = this.selected?.id;
    const previousLineId = this.selectedModelStageProductionLineId;

    if (context.contextType === 'factory') {
      this.selectModelStageFactory(context.factoryId);
      this.selected = null;
      this.stages = [];
      this.modelStageFormVisible = false;
      this.clearModelStageSelection();
      return;
    }

    this.selectedModelStageFactoryId = context.factoryId;
    if (context.modelId && this.selected?.id !== context.modelId) this.selectModelStageModel(context.modelId);

    if (context.contextType === 'model') {
      this.selectedModelStageDepartmentId = '';
      this.selectedModelStageProductionLineId = '';
      this.modelStageFormVisible = false;
      if (previousModelId !== context.modelId || previousLineId) this.clearModelStageSelection();
      this.rebuildAvailableStageOptions();
      return;
    }

    this.selectedModelStageDepartmentId = context.departmentId ?? '';
    if (context.contextType === 'department') {
      this.selectedModelStageProductionLineId = '';
      this.modelStageFormVisible = false;
      if (previousModelId !== context.modelId || previousLineId) this.clearModelStageSelection();
      this.rebuildAvailableStageOptions();
      return;
    }

    this.selectedModelStageProductionLineId = context.productionLineId ?? '';
    if (previousModelId !== context.modelId || previousLineId !== this.selectedModelStageProductionLineId) this.clearModelStageSelection();
    this.rebuildAvailableStageOptions();
  }
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
    this.save(request, stage => {
      this.stageFormVisible = false;
      this.editStageId = '';
      this.cancelStageEditHydration();
      this.loadOperationalStages();
    });
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
      const deletedStageId = this.pendingStage.id;
      this.save(this.api.deleteOperationalStage(deletedStageId, this.localCorrelation('stages')), () => {
        this.closeDependencyDialog();
        this.loadOperationalStages();
      });
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

  saveModel(): void { if (this.modelForm.valid) { const correlationId = this.localCorrelation('models'); const value = this.modelForm.getRawValue(); this.save(this.editModelId ? this.api.updateModel(this.editModelId, { name: value.name ?? undefined, description: value.description }, correlationId) : this.api.createModel({ code: value.code!, name: value.name!, description: value.description }, correlationId), item => { const selectedNodeKey = this.selectedModelStageContextNode?.key; const expandedKeys = this.modelStageContextExpandedKeys(); this.models = this.upsert(this.models, item); if (this.selected?.id === item.id) this.selected = { ...this.selected, ...item }; this.editModelId = ''; this.modelFormVisible = false; this.modelForm.reset(); this.modelForm.controls.code.enable({ emitEvent: false }); this.rebuildModelStageContextTree(expandedKeys); this.restoreModelStageContextNode(selectedNodeKey); }); } }
  editModel(item: ProductModelItem): void { this.editModelId = item.id; this.modelFormVisible = true; this.modelForm.reset(item); this.modelForm.controls.code.disable({ emitEvent: false }); }
  openAddModelFromTree(): void {
    this.editModelId = '';
    this.modelFormVisible = true;
    this.modelForm.reset({ code: '', name: '', description: '' });
    this.modelForm.controls.code.enable({ emitEvent: false });
  }
  hasModelContextActions(node: TreeNode): boolean {
    return this.canManageModels && (node.data as ModelStageContextNodeData | undefined)?.contextType === 'model';
  }
  openModelContextMenu(event: MouseEvent, node: TreeNode): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.modelContextMenuBusy || this.saving || !this.hasModelContextActions(node)) return;
    const modelId = (node.data as ModelStageContextNodeData | undefined)?.modelId;
    if (!modelId) return;
    this.contextModelNode = node;
    this.modelContextMenuBusy = true;
    this.api.modelDeleteEligibility(modelId).pipe(finalize(() => this.modelContextMenuBusy = false), takeUntil(this.destroy$)).subscribe({
      next: eligibility => {
        this.contextModelDeleteEligibility = eligibility;
        this.modelContextMenuItems = buildModelContextMenu(this.canManageModels, this.saving, eligibility.canDelete, eligibility.canDelete ? null : eligibility.messageAr, action => this.runModelContextAction(action));
        if (event.target) this.modelContextMenu?.show(event);
      },
      error: error => this.setError(error)
    });
  }
  private runModelContextAction(action: ModelContextAction): void {
    const modelId = (this.contextModelNode?.data as ModelStageContextNodeData | undefined)?.modelId;
    const model = modelId ? this.models.find(item => item.id === modelId) : null;
    if (!model || this.modelContextMenuBusy || this.saving) return;
    this.modelContextMenuBusy = true;
    try {
      if (action === 'add') {
        this.openAddModelFromTree();
        return;
      }
      if (action === 'edit') {
        this.editModel(model);
        return;
      }
      if (!this.contextModelDeleteEligibility?.canDelete) return;
      this.pendingModelDeletion = model;
      this.modelDeleteDialogVisible = true;
    } finally {
      this.modelContextMenuBusy = false;
    }
  }
  confirmModelDeletion(): void {
    const model = this.pendingModelDeletion;
    if (!model || this.saving) return;
    this.save(this.api.deleteModel(model.id, this.localCorrelation('models')), () => {
      const deletedWasSelected = this.selected?.id === model.id;
      this.models = this.models.filter(item => item.id !== model.id);
      this.modelTotal = this.models.length;
      this.rebuildModelStageContextTree(this.modelStageContextExpandedKeys());
      if (deletedWasSelected) this.clearDeletedModelContext();
      this.closeModelDeletionDialog();
    });
  }
  closeModelDeletionDialog(): void { this.modelDeleteDialogVisible = false; this.pendingModelDeletion = null; }
  select(item: ProductModelItem): void {
    if (this.selected?.id !== item.id) this.clearModelStageSelection();
    this.selected = item;
    this.modelStageSearch = '';
    this.modelStagesLoading = true;
    this.api.modelStages(item.id).pipe(finalize(() => this.modelStagesLoading = false), takeUntil(this.destroy$)).subscribe({
      next: stages => { this.stages = [...stages].sort((left, right) => left.stageOrder - right.stageOrder); this.rebuildAvailableStageOptions(); },
      error: error => this.setError(error)
    });
    if (!this.availableStageCatalog.length && !this.availableStagesLoading) this.loadAvailableStageCatalog();
  }
  beginAddModelStage(stage: SubStageOption): void {
    if (!this.selected || !stage.isActive || this.stages.some(item => item.subStageId === stage.id)) return;
    this.editModelStageId = '';
    this.modelStageFormVisible = true;
    this.modelStageForm.reset({
      subStageId: stage.id,
      stageOrder: Math.max(0, ...this.stages.map(item => item.stageOrder)) + 1,
      piecePrice: 0,
      standardSeconds: null,
      compensationMode: 'SharedPercentage',
      isRequired: true,
      isActive: true
    });
  }
  openBulkCopyDialog(): void {
    if (!this.canManageModels || !this.hasModelStageContext || this.selectedModelStageRelationships.length === 0) return;
    this.bulkCopyStep = 'configure';
    this.bulkCopyError = '';
    this.bulkCopyPreview = null;
    this.bulkCopyTargetFactoryId = this.selectedModelStageFactoryId;
    this.bulkCopyTargetDepartmentId = this.selectedModelStageDepartmentId;
    this.bulkCopyTargetProductionLineId = '';
    this.bulkCopyTargetModelId = '';
    this.bulkCopyTargetModels = [...this.models];
    this.bulkCopyDialogVisible = true;
    this.bulkCopyTargetsLoading = true;
    this.api.models().pipe(finalize(() => this.bulkCopyTargetsLoading = false), takeUntil(this.destroy$)).subscribe({
      next: models => this.bulkCopyTargetModels = models,
      error: error => this.bulkCopyError = this.errorMessage(error)
    });
  }
  closeBulkCopyDialog(): void {
    if (this.bulkCopyBusy) return;
    this.bulkCopyDialogVisible = false;
    this.bulkCopyStep = 'configure';
    this.bulkCopyPreview = null;
    this.bulkCopyError = '';
  }
  setBulkCopyTargetFactory(factoryId: string): void {
    this.bulkCopyTargetFactoryId = this.bulkCopyTargetFactoryOptions.some(factory => factory.id === factoryId) ? factoryId : '';
    this.bulkCopyTargetDepartmentId = '';
    this.bulkCopyTargetProductionLineId = '';
    this.resetBulkCopyPreview();
  }
  setBulkCopyTargetModel(modelId: string): void {
    this.bulkCopyTargetModelId = this.bulkCopyTargetModelOptions.some(model => model.id === modelId) ? modelId : '';
    this.resetBulkCopyPreview();
  }
  setBulkCopyTargetDepartment(departmentId: string): void {
    this.bulkCopyTargetDepartmentId = this.bulkCopyTargetDepartmentOptions.some(department => department.id === departmentId) ? departmentId : '';
    this.bulkCopyTargetProductionLineId = '';
    this.resetBulkCopyPreview();
  }
  setBulkCopyTargetLine(productionLineId: string): void {
    this.bulkCopyTargetProductionLineId = this.bulkCopyTargetLineOptions.some(line => line.id === productionLineId) ? productionLineId : '';
    this.resetBulkCopyPreview();
  }
  submitBulkCopyDialog(): void {
    if (this.bulkCopyBusy) return;
    if (this.bulkCopyStep === 'confirm') {
      this.executeBulkCopy();
      return;
    }
    if (!this.bulkCopyCanPreview) return;
    this.bulkCopyBusy = true;
    this.bulkCopyError = '';
    this.api.copyModelStages(
      this.selected!.id,
      this.buildBulkCopyRequest(true),
      this.localCorrelation('models')
    ).pipe(finalize(() => this.bulkCopyBusy = false), takeUntil(this.destroy$)).subscribe({
      next: summary => {
        this.bulkCopyPreview = summary;
        this.bulkCopyStep = 'confirm';
        if (summary.failedCount > 0 || summary.validationErrors.length > 0) {
          this.bulkCopyError = summary.validationErrors.join(' ')
            || summary.failedStages.map(stage => stage.reason).join(' ')
            || 'توجد مراحل لا يمكن نسخها إلى السياق الهدف.';
          return;
        }
      },
      error: error => this.bulkCopyError = this.errorMessage(error)
    });
  }
  returnToBulkCopyConfiguration(): void {
    if (this.bulkCopyBusy) return;
    this.bulkCopyStep = 'configure';
    this.bulkCopyPreview = null;
    this.bulkCopyError = '';
  }
  private executeBulkCopy(): void {
    if (!this.bulkCopyPreview || this.bulkCopyBusy || !this.selected) return;
    const sourceModelId = this.selected.id;
    const targetModelId = this.bulkCopyTargetModelId;
    this.bulkCopyBusy = true;
    this.bulkCopyError = '';
    this.api.copyModelStages(
      sourceModelId,
      this.buildBulkCopyRequest(false),
      this.localCorrelation('models')
    ).pipe(finalize(() => this.bulkCopyBusy = false), takeUntil(this.destroy$)).subscribe({
      next: summary => {
        if (summary.failedCount > 0 || summary.validationErrors.length > 0) {
          this.bulkCopyError = summary.validationErrors.join(' ') || 'تعذر نسخ بعض المراحل.';
          return;
        }
        this.messageService?.add({
          severity: summary.failedCount > 0 ? 'warn' : 'success',
          summary: 'اكتمل نسخ المراحل',
          detail: `تم نسخ ${summary.addedCount} مرحلة، وتم تخطي ${summary.skippedCount} مرحلة، وفشلت ${summary.failedCount} مرحلة.`
        });
        this.selectedModelStageIds = new Set<string>();
        this.bulkCopyDialogVisible = false;
        this.bulkCopyPreview = null;
        this.bulkCopyStep = 'configure';
        if (this.selected?.id === targetModelId) this.reloadSelectedModelStages(targetModelId);
      },
      error: error => this.bulkCopyError = this.errorMessage(error)
    });
  }
  private buildBulkCopyRequest(previewOnly: boolean): CopyModelStagesRequest {
    return {
      sourceProductionLineId: this.selectedModelStageProductionLineId,
      targetModelId: this.bulkCopyTargetModelId,
      targetProductionLineId: this.bulkCopyTargetProductionLineId,
      sourceProductModelStageIds: this.selectedModelStageRelationships.map(stage => stage.id),
      previewOnly
    };
  }
  private resetBulkCopyPreview(): void {
    this.bulkCopyPreview = null;
    this.bulkCopyError = '';
  }
  private reloadSelectedModelStages(modelId: string): void {
    this.modelStagesLoading = true;
    this.api.modelStages(modelId).pipe(finalize(() => this.modelStagesLoading = false), takeUntil(this.destroy$)).subscribe({
      next: stages => {
        if (this.selected?.id !== modelId) return;
        this.stages = [...stages].sort((left, right) => left.stageOrder - right.stageOrder);
        this.rebuildAvailableStageOptions();
      },
      error: error => this.setError(error)
    });
  }
  saveModelStage(): void { if (!this.selected || this.modelStageForm.invalid) return; const value = this.modelStageForm.getRawValue(); if (this.stages.some(item => item.subStageId === value.subStageId && item.id !== this.editModelStageId) || this.stages.some(item => item.stageOrder === value.stageOrder && item.id !== this.editModelStageId)) { this.error = 'لا يمكن تكرار المرحلة أو ترتيبها داخل الموديل.'; return; } const correlationId = this.localCorrelation('models'); this.save(this.editModelStageId ? this.api.updateModelStage(this.selected.id, this.editModelStageId, value, correlationId) : this.api.addModelStage(this.selected.id, value, correlationId), item => { this.stages = this.upsert(this.stages, item, 'stageOrder'); this.rebuildAvailableStageOptions(); this.editModelStageId = ''; this.modelStageFormVisible = false; }); }
  editModelStage(item: ModelStageItem): void { this.editModelStageId = item.id; this.modelStageFormVisible = true; this.modelStageForm.reset(item); this.rebuildAvailableStageOptions(); }
  isModelStageSaving(id: string): boolean { return this.modelStageSavingIds.has(id); }
  modelStageRelationshipLabel(row: ModelStageAvailabilityRow): string {
    return row.relationship ? (row.relationship.isActive ? 'مرتبطة وفعالة' : 'مرتبطة ومعطلة') : 'غير مرتبطة';
  }
  modelStageRelationshipStatus(row: ModelStageAvailabilityRow): 'ready' | 'warning' | 'info' {
    return row.relationship ? (row.relationship.isActive ? 'ready' : 'warning') : 'info';
  }
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
  private loadModelPage(search: string, page: number): void { const requestVersion = ++this.modelRequestVersion; this.modelListLoading = true; this.error = ''; this.api.modelSearchPage(search, page, this.modelPageSize, this.modelStatusFilter).pipe(finalize(() => { if (requestVersion === this.modelRequestVersion) this.modelListLoading = false; }), takeUntil(this.destroy$)).subscribe({ next: result => { if (requestVersion !== this.modelRequestVersion) return; const nearestPage = result.totalCount > 0 ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1; if (result.items.length === 0 && page > nearestPage) { this.loadModelPage(search, nearestPage); return; } this.applyModelPage(result); }, error: error => { if (requestVersion === this.modelRequestVersion) this.setError(error); } }); }
  private applyModelPage(page: { items: ProductModelItem[]; totalCount: number; pageNumber: number; pageSize: number }): void { this.models = page.items; this.modelTotal = page.totalCount; this.modelPage = page.pageNumber; this.modelPageSize = page.pageSize; }
  private loadAvailableStageCatalog(): void {
    const requestVersion = ++this.availableStagesRequestVersion;
    this.availableStagesLoading = true;
    this.availableStagesError = '';
    this.api.allSubStages().pipe(
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
  private rebuildAvailableStageOptions(): void {
    const linkedStageIds = new Set(this.stages.filter(stage => stage.id !== this.editModelStageId).map(stage => stage.subStageId));
    const available = this.availableStageCatalog
      .filter(option => option.isActive
        && this.departmentIdForStage(option) === this.selectedModelStageDepartmentId
        && (!this.selectedModelStageProductionLineId || option.productionLineId === this.selectedModelStageProductionLineId)
        && !linkedStageIds.has(option.id))
      .sort((left, right) => this.lineOrderForStage(left) - this.lineOrderForStage(right)
        || left.sequenceOrder - right.sequenceOrder
        || left.name.localeCompare(right.name, 'ar'));
    this.availableStageOptions = available;
  }
  private rebuildModelStageContextTree(expandedKeys = new Set<string>()): void {
    const activeModels = this.models;
    const activeFactories = this.modelStageFactoryOptions;
    this.modelStageContextNodes = activeFactories.map(factory => ({
      key: `factory:${factory.id}`,
      label: `${factory.code} — ${factory.name}`,
      data: { contextType: 'factory', factoryId: factory.id, code: factory.code, name: factory.name } satisfies ModelStageContextNodeData,
      selectable: true,
      expanded: expandedKeys.has(`factory:${factory.id}`),
      children: activeModels.map(model => ({
        key: `factory:${factory.id}:model:${model.id}`,
        label: `${model.code} — ${model.name}`,
        data: { contextType: 'model', factoryId: factory.id, modelId: model.id, code: model.code, name: model.name } satisfies ModelStageContextNodeData,
        selectable: true,
        expanded: expandedKeys.has(`factory:${factory.id}:model:${model.id}`),
        children: this.departments
          .filter(department => department.factoryId === factory.id && department.isActive !== false && !!department.id)
          .map(department => ({
            key: `factory:${factory.id}:model:${model.id}:department:${department.id}`,
            label: `${department.code || '—'} — ${department.nameAr || department.name || 'القسم'}`,
            data: { contextType: 'department', factoryId: factory.id, modelId: model.id, departmentId: department.id!, code: department.code, name: department.nameAr || department.name || 'القسم' } satisfies ModelStageContextNodeData,
            selectable: true,
            expanded: expandedKeys.has(`factory:${factory.id}:model:${model.id}:department:${department.id}`),
            children: this.lines
              .filter(line => line.factoryId === factory.id && line.departmentId === department.id && line.isActive)
              .sort((left, right) => left.sequenceOrder - right.sequenceOrder || left.name.localeCompare(right.name, 'ar'))
              .map(line => ({
                key: `factory:${factory.id}:model:${model.id}:department:${department.id}:line:${line.id}`,
                label: `${line.lineCode || '—'} — ${line.name}`,
                data: { contextType: 'line', factoryId: factory.id, modelId: model.id, departmentId: department.id!, productionLineId: line.id, code: line.lineCode, name: line.name } satisfies ModelStageContextNodeData,
                selectable: true,
                leaf: true
              }))
          }))
      }))
    }));
  }
  private modelStageContextExpandedKeys(): Set<string> {
    const keys = new Set<string>();
    const collect = (nodes: readonly TreeNode[]) => nodes.forEach(node => {
      if (node.expanded && node.key) keys.add(node.key);
      if (node.children) collect(node.children);
    });
    collect(this.modelStageContextNodes);
    return keys;
  }
  private restoreModelStageContextNode(key: string | undefined): void {
    if (!key) return;
    const find = (nodes: readonly TreeNode[]): TreeNode | null => {
      for (const node of nodes) {
        if (node.key === key) return node;
        const nested = node.children ? find(node.children) : null;
        if (nested) return nested;
      }
      return null;
    };
    this.selectedModelStageContextNode = find(this.modelStageContextNodes);
  }
  private clearDeletedModelContext(): void {
    this.selected = null;
    this.stages = [];
    this.selectedModelStageDepartmentId = '';
    this.selectedModelStageProductionLineId = '';
    this.selectedModelStageContextNode = this.modelStageContextNodes.find(node => node.data?.factoryId === this.selectedModelStageFactoryId) ?? null;
    this.modelStageFormVisible = false;
    this.clearModelStageSelection();
    this.rebuildAvailableStageOptions();
  }
  private clearModelStageSelection(): void {
    this.selectedModelStageIds = new Set<string>();
    if (this.bulkCopyDialogVisible) this.closeBulkCopyDialog();
  }
  private departmentIdForStage(stage: SubStageOption): string | null {
    return stage.departmentId ?? this.lines.find(line => line.id === stage.productionLineId)?.departmentId ?? null;
  }
  private factoryIdForStage(stage: SubStageOption): string | null {
    return stage.factoryId ?? this.lines.find(line => line.id === stage.productionLineId)?.factoryId ?? null;
  }
  private lineNameForStage(stage: SubStageOption): string {
    return stage.productionLineName ?? this.lines.find(line => line.id === stage.productionLineId)?.name ?? 'خط غير محدد';
  }
  private lineOrderForStage(stage: SubStageOption): number {
    return this.lines.find(line => line.id === stage.productionLineId)?.sequenceOrder ?? Number.MAX_SAFE_INTEGER;
  }
  private toAvailableOption(item: ModelStageItem): SubStageOption {
    return { id: item.subStageId, mainStageId: '', code: item.subStageCode || '—', name: item.subStageName || 'مرحلة مرتبطة', capacity: 0, sequenceOrder: item.stageOrder, isActive: item.isActive };
  }
  linkedStageName(item: ModelStageItem): string { return item.subStageName || this.availableStageOptionCache.get(item.subStageId)?.name || '-'; }
  linkedStageCode(item: ModelStageItem): string { return item.subStageCode || this.availableStageOptionCache.get(item.subStageId)?.code || '—'; }
  private upsert<T extends { id: string }>(items: readonly T[], item: T, sortKey?: keyof T): T[] { const next = items.some(candidate => candidate.id === item.id) ? items.map(candidate => candidate.id === item.id ? item : candidate) : [...items, item]; return sortKey ? [...next].sort((left, right) => Number(left[sortKey]) - Number(right[sortKey])) : next; }
  trackByModelStageId(_index: number, item: ModelStageItem): string { return item.id; }
  private setError(error: unknown): void {
    this.error = this.errorMessage(error);
  }
  private errorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const message = error.error?.error?.message ?? error.error?.message;
      if (typeof message === 'string' && message.trim()) {
        return message;
      }
      if (error.status === 404) {
        return 'تعذر تحديث مرحلة الموديل لأن العلاقة لم تعد موجودة. حدّث البيانات ثم حاول مرة أخرى.';
      }
      if (error.status === 409) {
        return 'تعذر تحديث مرحلة الموديل بسبب تعارض مع تعديل متزامن. حدّث البيانات ثم حاول مرة أخرى.';
      }
      if (error.status === 0) {
        return 'تعذر الاتصال بالخادم أثناء تحديث مرحلة الموديل. لم تتغير الحالة المعروضة.';
      }
    }
    return error instanceof Error ? error.message : 'تعذر تحميل أو حفظ البيانات.';
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
