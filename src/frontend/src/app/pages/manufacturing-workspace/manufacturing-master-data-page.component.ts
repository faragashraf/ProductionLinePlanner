import { Component, OnDestroy, OnInit, Optional } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, Validators } from '@angular/forms';
import { EMPTY, Subject, catchError, debounceTime, distinctUntilChanged, finalize, forkJoin, map, Observable, switchMap, takeUntil } from 'rxjs';
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
  modelStageOptions: SubStageOption[] = [];
  modelStageSearch = '';
  modelStagePage = 1;
  modelStageTotal = 0;
  readonly modelStagePageSize = 50;
  modelStageSelectorLoading = false;

  readonly stageForm = this.fb.group({
    factoryId: ['', Validators.required],
    departmentId: ['', Validators.required],
    productionLineId: ['', Validators.required],
    name: ['', Validators.required],
    capacity: [0, [Validators.required, Validators.min(0)]]
  });
  readonly modelForm = this.fb.group({ code: ['', Validators.required], name: ['', Validators.required], description: [''] });
  readonly modelStageForm = this.fb.group({ subStageId: ['', Validators.required], stageOrder: [1, Validators.required], piecePrice: [0, Validators.required], standardSeconds: [null as number | null], compensationMode: ['SharedPercentage', Validators.required], isRequired: [true], isActive: [true] });

  private readonly modelStageSearch$ = new Subject<string>();
  private readonly modelSearch$ = new Subject<string>();
  private readonly destroy$ = new Subject<void>();
  private modelRequestVersion = 0;
  private stopRealtime?: () => void;

  constructor(private readonly fb: FormBuilder, private readonly api: ManufacturingMasterDataApiService, route: ActivatedRoute, @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService) {
    this.mode = route.snapshot.routeConfig?.path === 'models' ? 'models' : 'stages';
  }

  ngOnInit(): void {
    this.modelStageSearch$.pipe(debounceTime(250), distinctUntilChanged(), switchMap(search => this.loadModelStageOptions(search, 1)), takeUntil(this.destroy$)).subscribe();
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

  get activeDepartments(): DepartmentItem[] { return this.departments.filter(item => item.isActive); }
  get activeLines(): ProductionLineOption[] { return this.lines.filter(item => item.isActive); }
  get selectedStage(): SubStageOption | null { return this.operationalStages.find(stage => stage.id === this.editStageId) ?? null; }
  get filteredOperationalStages(): SubStageOption[] { return this.operationalStages.filter(stage => matchesSearchTerm(this.stageSearch, [stage.name, stage.code])); }
  get stageResultCount(): number { return this.filteredOperationalStages.length; }
  get stageEmptyMessage(): string { return normalizeSearchText(this.stageSearch) ? 'لا توجد مراحل مطابقة للبحث.' : 'اختر خط إنتاج لعرض مراحل الإنتاج، أو لا توجد مراحل مطابقة.'; }
  get modelEmptyMessage(): string { return normalizeSearchText(this.modelSearch) ? 'لا توجد موديلات مطابقة للبحث.' : 'لا توجد موديلات لعرضها.'; }
  get canConfirmDependencyAction(): boolean {
    return this.pendingStageAction === 'disable' ? !!this.stageDependencySummary?.canDisable : this.pendingStageAction === 'delete' && !!this.stageDependencySummary?.canDelete;
  }

  reload(): void {
    this.loading = true;
    this.error = '';
    if (this.mode === 'models') {
      forkJoin({ modelPage: this.api.modelSearchPage('', 1, this.modelPageSize), stagePage: this.api.searchSubStages('', 1, this.modelStagePageSize) })
        .pipe(finalize(() => this.loading = false), takeUntil(this.destroy$))
        .subscribe({ next: data => { this.applyModelPage(data.modelPage); this.modelStageOptions = data.stagePage.items; this.modelStageTotal = data.stagePage.totalCount; }, error: error => this.setError(error) });
      return;
    }
    this.api.factories().pipe(finalize(() => this.loading = false), takeUntil(this.destroy$)).subscribe({
      next: factories => this.factories = factories.filter(factory => factory.isActive),
      error: error => this.setError(error)
    });
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
    if (this.stageForm.controls.productionLineId.value) this.loadOperationalStages();
  }

  onStageSearch(value: string): void { this.stageSearch = value; }
  onModelSearch(value: string): void { this.modelSearch = value; this.modelPage = 1; this.modelSearch$.next(value); }
  onModelLazyLoad(event: { first?: number | null; rows?: number | null }): void { const page = Math.floor((event.first ?? 0) / (event.rows ?? this.modelPageSize)) + 1; if (page !== this.modelPage) this.loadModelPage(this.modelSearch, page); }

  loadOperationalStages(): void {
    const productionLineId = this.stageForm.controls.productionLineId.value;
    if (!productionLineId) return;
    const isActive = this.stageStatusFilter === 'all' ? undefined : this.stageStatusFilter === 'active';
    this.api.operationalStages({ productionLineId, isActive, includeInactive: this.stageStatusFilter === 'all' }).pipe(takeUntil(this.destroy$)).subscribe({ next: stages => this.operationalStages = stages, error: error => this.setError(error) });
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

  saveModel(): void { if (this.modelForm.valid) { const correlationId = this.localCorrelation('models'); this.save(this.editModelId ? this.api.updateModel(this.editModelId, this.modelForm.getRawValue(), correlationId) : this.api.createModel(this.modelForm.getRawValue(), correlationId), item => { if (this.selected?.id === item.id) this.selected = { ...this.selected, ...item }; this.editModelId = ''; this.modelFormVisible = false; this.modelForm.reset(); this.loadModelPage(this.modelSearch, this.modelPage); }); } }
  editModel(item: ProductModelItem): void { this.editModelId = item.id; this.modelFormVisible = true; this.modelForm.reset(item); }
  select(item: ProductModelItem): void { this.selected = item; this.api.modelStages(item.id).pipe(takeUntil(this.destroy$)).subscribe({ next: stages => this.stages = stages, error: error => this.setError(error) }); }
  saveModelStage(): void { if (!this.selected || this.modelStageForm.invalid) return; const value = this.modelStageForm.getRawValue(); if (this.stages.some(item => item.subStageId === value.subStageId && item.id !== this.editModelStageId) || this.stages.some(item => item.stageOrder === value.stageOrder && item.id !== this.editModelStageId)) { this.error = 'لا يمكن تكرار المرحلة أو ترتيبها داخل الموديل.'; return; } const correlationId = this.localCorrelation('models'); this.save(this.editModelStageId ? this.api.updateModelStage(this.selected.id, this.editModelStageId, value, correlationId) : this.api.addModelStage(this.selected.id, value, correlationId), item => { this.stages = this.upsert(this.stages, item, 'stageOrder'); this.editModelStageId = ''; this.modelStageFormVisible = false; }); }
  editModelStage(item: ModelStageItem): void { this.editModelStageId = item.id; this.modelStageFormVisible = true; this.modelStageForm.reset(item); this.ensureSelectedModelStage(item); }
  disableModelStage(id: string): void { if (this.selected && confirm('سيتم تعطيل إعداد المرحلة.')) this.save(this.api.deactivateModelStage(this.selected.id, id, this.localCorrelation('models')), () => this.stages = this.markInactive(this.stages, id)); }
  setModelActive(item: ProductModelItem): void { if (confirm(item.isActive ? 'تعطيل الموديل؟' : 'تفعيل الموديل؟')) this.save(this.api.setModelActivation(item.id, !item.isActive, this.localCorrelation('models')), () => this.models = this.models.map(model => model.id === item.id ? { ...model, isActive: !item.isActive } : model)); }
  onModelStageSearch(value: string): void { this.modelStageSearch = value; this.modelStageSearch$.next(value.trim()); }
  changeModelStagePage(offset: number): void { const page = this.modelStagePage + offset; if (page >= 1 && (page - 1) * this.modelStagePageSize < this.modelStageTotal) this.loadModelStageOptions(this.modelStageSearch, page).pipe(takeUntil(this.destroy$)).subscribe(); }
  get modelStageChoices(): SubStageOption[] { const currentId = this.modelStageForm.getRawValue().subStageId; const existing = this.stages.find(stage => stage.subStageId === currentId); const selected = existing && !this.modelStageOptions.some(option => option.id === existing.subStageId) ? { id: existing.subStageId, mainStageId: '', code: existing.subStageCode || '—', name: existing.subStageName || 'مرحلة مرتبطة', capacity: 0, sequenceOrder: 0, isActive: false } : null; return selected ? [selected, ...this.modelStageOptions] : this.modelStageOptions; }
  onModelFormVisibility(visible: boolean): void { this.modelFormVisible = visible; if (!visible) { this.editModelId = ''; this.modelForm.reset(); } }
  onModelStageFormVisibility(visible: boolean): void { this.modelStageFormVisible = visible; if (!visible) { this.editModelStageId = ''; this.modelStageForm.reset({ stageOrder: 1, piecePrice: 0, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }); } }
  subName(id: string): string { return this.modelStageOptions.find(item => item.id === id)?.name ?? this.stages.find(item => item.subStageId === id)?.subStageName ?? '-'; }
  totalPrice(): number { return this.stages.filter(item => item.isActive).reduce((sum, item) => sum + item.piecePrice, 0); }
  totalSeconds(): number { return this.stages.filter(item => item.isActive).reduce((sum, item) => sum + (item.standardSeconds ?? 0), 0); }
  stageStatusLabel(isActive: boolean): string { return isActive ? 'فعالة' : 'معطلة'; }

  private save<T>(request: Observable<T>, success?: (result: T) => void): void { this.saving = true; this.error = ''; request.pipe(finalize(() => this.saving = false), takeUntil(this.destroy$)).subscribe({ next: result => success?.(result), error: error => this.setError(error) }); }
  private loadModelPage(search: string, page: number): void { const requestVersion = ++this.modelRequestVersion; this.modelListLoading = true; this.error = ''; this.api.modelSearchPage(search, page, this.modelPageSize).pipe(finalize(() => { if (requestVersion === this.modelRequestVersion) this.modelListLoading = false; }), takeUntil(this.destroy$)).subscribe({ next: result => { if (requestVersion !== this.modelRequestVersion) return; const nearestPage = result.totalCount > 0 ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1; if (result.items.length === 0 && page > nearestPage) { this.loadModelPage(search, nearestPage); return; } this.applyModelPage(result); }, error: error => { if (requestVersion === this.modelRequestVersion) this.setError(error); } }); }
  private applyModelPage(page: { items: ProductModelItem[]; totalCount: number; pageNumber: number; pageSize: number }): void { this.models = page.items; this.modelTotal = page.totalCount; this.modelPage = page.pageNumber; this.modelPageSize = page.pageSize; }
  private loadModelStageOptions(search: string, page: number): Observable<void> { this.modelStageSelectorLoading = true; return this.api.searchSubStages(search, page, this.modelStagePageSize).pipe(map(result => { this.modelStageSearch = search; this.modelStagePage = result.pageNumber; this.modelStageTotal = result.totalCount; this.modelStageOptions = result.items; }), catchError(error => { this.setError(error); return EMPTY; }), finalize(() => this.modelStageSelectorLoading = false)); }
  private ensureSelectedModelStage(item: ModelStageItem): void { if (!this.modelStageOptions.some(option => option.id === item.subStageId)) this.modelStageOptions = [{ id: item.subStageId, mainStageId: '', code: item.subStageCode || '—', name: item.subStageName || 'مرحلة مرتبطة', capacity: 0, sequenceOrder: 0, isActive: false }, ...this.modelStageOptions]; }
  private upsert<T extends { id: string }>(items: readonly T[], item: T, sortKey?: keyof T): T[] { const next = items.some(candidate => candidate.id === item.id) ? items.map(candidate => candidate.id === item.id ? item : candidate) : [...items, item]; return sortKey ? [...next].sort((left, right) => Number(left[sortKey]) - Number(right[sortKey])) : next; }
  private markInactive<T extends { id: string; isActive: boolean }>(items: readonly T[], id: string): T[] { return items.map(item => item.id === id ? { ...item, isActive: false } : item); }
  private setError(error: unknown): void { this.error = error instanceof Error ? error.message : 'تعذر تحميل أو حفظ البيانات.'; }
  private refreshModelsFromRealtime(): void {
    this.loadModelPage(this.modelSearch, this.modelPage);
    if (this.selected) this.select(this.selected);
    this.loadModelStageOptions(this.modelStageSearch, this.modelStagePage).pipe(takeUntil(this.destroy$)).subscribe();
  }
  private refreshStagesFromRealtime(): void {
    const factoryId = this.stageForm.controls.factoryId.value;
    const departmentId = this.stageForm.controls.departmentId.value;
    const productionLineId = this.stageForm.controls.productionLineId.value;
    if (!factoryId) return;
    this.api.factories().pipe(takeUntil(this.destroy$)).subscribe({
      next: factories => {
        this.factories = factories.filter(factory => factory.isActive);
        if (!this.factories.some(factory => factory.id === factoryId)) {
          this.clearStageContext('factory');
          return;
        }
        this.api.departments(factoryId, false).pipe(takeUntil(this.destroy$)).subscribe({
          next: departments => {
            this.departments = departments.filter(item => item.factoryId === factoryId && item.isActive);
            if (departmentId && !this.departments.some(department => department.id === departmentId)) {
              this.clearStageContext('department');
              return;
            }
            if (!departmentId) {
              this.lines = [];
              this.operationalStages = [];
              return;
            }
            this.api.productionLinesForDepartment(departmentId).pipe(takeUntil(this.destroy$)).subscribe({
              next: lines => {
                this.lines = lines.filter(line => line.departmentId === departmentId && line.isActive);
                if (productionLineId && !this.lines.some(line => line.id === productionLineId)) {
                  this.clearStageContext('line');
                  return;
                }
                if (!productionLineId) {
                  this.operationalStages = [];
                  return;
                }
                this.loadOperationalStages();
              },
              error: error => this.setError(error)
            });
          },
          error: error => this.setError(error)
        });
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

  private localCorrelation(screen: 'models' | 'stages'): string | undefined {
    return this.manufacturingRealtime?.registerLocalOperation(screen);
  }
}
