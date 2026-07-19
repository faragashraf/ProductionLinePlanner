import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, Validators } from '@angular/forms';
import { EMPTY, Subject, catchError, debounceTime, distinctUntilChanged, finalize, forkJoin, map, Observable, switchMap, takeUntil } from 'rxjs';
import { MainStageOption, ManufacturingMasterDataApiService, ModelStageItem, ProductModelItem, ProductionLineOption, SubStageOption } from '../../core/services/manufacturing-master-data-api.service';

@Component({ selector: 'app-manufacturing-master-data-page', templateUrl: './manufacturing-master-data-page.component.html', styleUrls: ['./manufacturing-master-data-page.component.scss'] })
export class ManufacturingMasterDataPageComponent implements OnInit, OnDestroy {
  readonly mode: 'stages' | 'models'; loading = true; saving = false; error = ''; editMainId = ''; editSubId = ''; editModelId = ''; editModelStageId = '';
  stageStatusFilter: 'all' | 'active' | 'inactive' = 'all';
  mainFormVisible = false; subFormVisible = false; modelFormVisible = false; modelStageFormVisible = false;
  lines: ProductionLineOption[] = []; mains: MainStageOption[] = []; subs: SubStageOption[] = []; models: ProductModelItem[] = []; stages: ModelStageItem[] = []; selected: ProductModelItem | null = null;
  modelStageOptions: SubStageOption[] = []; modelStageSearch = ''; modelStagePage = 1; modelStageTotal = 0; modelStagePageSize = 50; modelStageSelectorLoading = false;
  private readonly modelStageSearch$ = new Subject<string>();
  private readonly destroy$ = new Subject<void>();
  readonly mainForm = this.fb.group({ productionLineId: ['', Validators.required], name: ['', Validators.required], sequenceOrder: [1, Validators.required], isCritical: [false] });
  readonly subForm = this.fb.group({ mainStageId: ['', Validators.required], code: ['', Validators.required], name: ['', Validators.required], capacity: [0, Validators.required], sequenceOrder: [1, Validators.required] });
  readonly modelForm = this.fb.group({ code: ['', Validators.required], name: ['', Validators.required], description: [''] });
  readonly stageForm = this.fb.group({ subStageId: ['', Validators.required], stageOrder: [1, Validators.required], piecePrice: [0, Validators.required], standardSeconds: [null as number | null], compensationMode: ['SharedPercentage', Validators.required], isRequired: [true], isActive: [true] });
  constructor(private readonly fb: FormBuilder, private readonly api: ManufacturingMasterDataApiService, route: ActivatedRoute) { this.mode = route.snapshot.routeConfig?.path === 'models' ? 'models' : 'stages'; }
  ngOnInit(): void { this.modelStageSearch$.pipe(debounceTime(250), distinctUntilChanged(), switchMap(search => this.loadModelStageOptions(search, 1)), takeUntil(this.destroy$)).subscribe(); this.reload(); }
  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
  reload(): void {
    this.loading = true;
    this.error = '';
    if (this.mode === 'stages') {
      forkJoin({ lines: this.api.productionLines(), mains: this.api.allMainStages(), subs: this.api.allSubStages() })
        .pipe(finalize(() => this.loading = false))
        .subscribe({
          next: data => {
            this.subs = data.subs;
            this.lines = data.lines;
            this.mains = data.mains;
          },
          error: e => this.error = e.message || 'تعذر تحميل بيانات التصنيع.'
        });
      return;
    }

    forkJoin({ models: this.api.models(), stagePage: this.api.searchSubStages('', 1, this.modelStagePageSize) })
      .pipe(finalize(() => this.loading = false))
      .subscribe({
        next: data => {
          this.modelStageOptions = data.stagePage.items;
          this.modelStageTotal = data.stagePage.totalCount;
          this.models = data.models;
        },
        error: e => this.error = e.message || 'تعذر تحميل بيانات التصنيع.'
      });
  }
  saveMain(): void { if (this.mainForm.valid) this.save(this.editMainId ? this.api.updateMain(this.editMainId, this.mainForm.getRawValue()) : this.api.createMain(this.mainForm.getRawValue()), item => { this.mains = this.upsert(this.mains, item); this.editMainId = ''; this.mainFormVisible = false; this.mainForm.reset({ sequenceOrder: 1, isCritical: false }); }); }
  saveSub(): void { if (this.subForm.valid) { const value = this.subForm.getRawValue(); const request = { mainStageId: value.mainStageId, code: value.code, name: value.name, capacity: value.capacity, defaultOrder: value.sequenceOrder }; this.save(this.editSubId ? this.api.updateSub(this.editSubId, request) : this.api.createSub(request), item => { this.subs = this.upsert(this.subs, item); this.editSubId = ''; this.subFormVisible = false; this.subForm.reset({ capacity: 0, sequenceOrder: 1 }); }); } }
  editMain(item: MainStageOption): void { this.editMainId = item.id; this.mainFormVisible = true; this.mainForm.reset(item); }
  editSub(item: SubStageOption): void { this.editSubId = item.id; this.subFormVisible = true; this.subForm.reset(item); }
  disableMain(id: string): void { if (confirm('سيتم تعطيل المرحلة دون حذفها.')) this.save(this.api.deactivateMain(id), () => this.mains = this.markInactive(this.mains, id)); }
  disableSub(id: string): void { if (confirm('سيتم تعطيل المرحلة دون حذفها.')) this.save(this.api.deactivateSub(id), () => this.subs = this.markInactive(this.subs, id)); }
  setMainActive(item: MainStageOption): void { if (item.isActive) { this.disableMain(item.id); return; } if (confirm('سيتم إعادة تفعيل المرحلة.')) this.save(this.api.setMainActivation(item.id, true), result => this.mains = this.upsert(this.mains, result)); }
  setSubActive(item: SubStageOption): void { if (item.isActive) { this.disableSub(item.id); return; } if (confirm('سيتم إعادة تفعيل المرحلة الفرعية.')) this.save(this.api.setSubActivation(item.id, true), result => this.subs = this.upsert(this.subs, result)); }
  saveModel(): void { if (this.modelForm.valid) this.save(this.editModelId ? this.api.updateModel(this.editModelId, this.modelForm.getRawValue()) : this.api.createModel(this.modelForm.getRawValue()), item => { this.models = this.upsert(this.models, item); this.editModelId = ''; this.modelFormVisible = false; this.modelForm.reset(); }); }
  editModel(item: ProductModelItem): void { this.editModelId = item.id; this.modelFormVisible = true; this.modelForm.reset(item); }
  select(item: ProductModelItem): void { this.selected = item; this.api.modelStages(item.id).subscribe({ next: x => this.stages = x, error: e => this.error = e.message }); }
  saveModelStage(): void { if (!this.selected || this.stageForm.invalid) return; const value = this.stageForm.getRawValue(); if (this.stages.some(x => x.subStageId === value.subStageId && x.id !== this.editModelStageId) || this.stages.some(x => x.stageOrder === value.stageOrder && x.id !== this.editModelStageId)) { this.error = 'لا يمكن تكرار المرحلة أو ترتيبها داخل الموديل.'; return; } this.save(this.editModelStageId ? this.api.updateModelStage(this.selected.id, this.editModelStageId, value) : this.api.addModelStage(this.selected.id, value), item => { this.stages = this.upsert(this.stages, item, 'stageOrder'); this.editModelStageId = ''; this.modelStageFormVisible = false; this.stageForm.reset({ stageOrder: 1, piecePrice: 0, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }); }); }
  editStage(item: ModelStageItem): void { this.editModelStageId = item.id; this.modelStageFormVisible = true; this.stageForm.reset(item); this.ensureSelectedModelStage(item); }
  onModelStageSearch(value: string): void { this.modelStageSearch = value; this.modelStageSearch$.next(value.trim()); }
  changeModelStagePage(offset: number): void { const page = this.modelStagePage + offset; if (page >= 1 && (page - 1) * this.modelStagePageSize < this.modelStageTotal) this.loadModelStageOptions(this.modelStageSearch, page).pipe(takeUntil(this.destroy$)).subscribe(); }
  get modelStageChoices(): SubStageOption[] { const currentId = this.stageForm.getRawValue().subStageId; const existing = this.stages.find(stage => stage.subStageId === currentId); const selected = existing && !this.modelStageOptions.some(option => option.id === existing.subStageId) ? { id: existing.subStageId, mainStageId: '', code: existing.subStageCode || '—', name: existing.subStageName || 'مرحلة مرتبطة', capacity: 0, sequenceOrder: 0, isActive: false } : null; return selected ? [selected, ...this.modelStageOptions] : this.modelStageOptions; }
  onMainFormVisibility(visible: boolean): void { this.mainFormVisible = visible; if (!visible) { this.editMainId = ''; this.mainForm.reset({ sequenceOrder: 1, isCritical: false }); } }
  onSubFormVisibility(visible: boolean): void { this.subFormVisible = visible; if (!visible) { this.editSubId = ''; this.subForm.reset({ capacity: 0, sequenceOrder: 1 }); } }
  onModelFormVisibility(visible: boolean): void { this.modelFormVisible = visible; if (!visible) { this.editModelId = ''; this.modelForm.reset(); } }
  onModelStageFormVisibility(visible: boolean): void { this.modelStageFormVisible = visible; if (!visible) { this.editModelStageId = ''; this.stageForm.reset({ stageOrder: 1, piecePrice: 0, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }); } }
  disableModelStage(id: string): void { if (this.selected && confirm('سيتم تعطيل إعداد المرحلة.')) this.save(this.api.deactivateModelStage(this.selected.id, id), () => this.stages = this.markInactive(this.stages, id)); }
  setModelActive(item: ProductModelItem): void { if (confirm(item.isActive ? 'تعطيل الموديل؟' : 'تفعيل الموديل؟')) this.save(this.api.setModelActivation(item.id, !item.isActive), () => this.models = this.models.map(model => model.id === item.id ? { ...model, isActive: !item.isActive } : model)); }
  mainName(id: string): string { return this.mains.find(x => x.id === id)?.name ?? '-'; } subName(id: string): string { return this.subs.find(x => x.id === id)?.name ?? this.modelStageOptions.find(x => x.id === id)?.name ?? this.stages.find(x => x.subStageId === id)?.subStageName ?? '-'; } totalPrice(): number { return this.stages.filter(x => x.isActive).reduce((sum, x) => sum + x.piecePrice, 0); } totalSeconds(): number { return this.stages.filter(x => x.isActive).reduce((sum, x) => sum + (x.standardSeconds ?? 0), 0); }
  get visibleMains(): MainStageOption[] { return this.filterByStatus(this.mains); }
  get visibleSubs(): SubStageOption[] { return this.filterByStatus(this.subs); }
  get stageResultCount(): number { return this.visibleMains.length + this.visibleSubs.length; }
  setStageStatusFilter(value: string): void { this.stageStatusFilter = value === 'active' || value === 'inactive' ? value : 'all'; }
  stageStatusLabel(isActive: boolean): string { return isActive ? 'فعالة' : 'معطلة'; }
  formatOrder(value: number | null | undefined, minimum = 0): string { return value === null || value === undefined || value < minimum ? 'غير محدد' : String(value); }
  private save<T>(request: Observable<T>, success?: (result: T) => void): void { this.saving = true; this.error = ''; request.pipe(finalize(() => this.saving = false)).subscribe({ next: result => { this.error = ''; success?.(result); }, error: e => this.error = e.message || 'تعذر حفظ التغيير.' }); }
  private filterByStatus<T extends { isActive: boolean }>(items: readonly T[]): T[] { return this.stageStatusFilter === 'all' ? [...items] : items.filter(item => item.isActive === (this.stageStatusFilter === 'active')); }
  private upsert<T extends { id: string }>(items: readonly T[], item: T, sortKey?: keyof T): T[] { const next = items.some(candidate => candidate.id === item.id) ? items.map(candidate => candidate.id === item.id ? item : candidate) : [...items, item]; return sortKey ? [...next].sort((left, right) => Number(left[sortKey]) - Number(right[sortKey])) : next; }
  private markInactive<T extends { id: string; isActive: boolean }>(items: readonly T[], id: string): T[] { return items.map(item => item.id === id ? { ...item, isActive: false } : item); }
  private loadModelStageOptions(search: string, page: number): Observable<void> { this.modelStageSelectorLoading = true; return this.api.searchSubStages(search, page, this.modelStagePageSize).pipe(map(result => { this.error = ''; this.modelStageSearch = search; this.modelStagePage = result.pageNumber; this.modelStageTotal = result.totalCount; this.modelStageOptions = result.items; }), catchError(error => { this.error = error instanceof Error ? error.message : 'تعذر تحميل المراحل.'; return EMPTY; }), finalize(() => this.modelStageSelectorLoading = false)); }
  private ensureSelectedModelStage(item: ModelStageItem): void { if (!this.modelStageOptions.some(option => option.id === item.subStageId)) this.modelStageOptions = [{ id: item.subStageId, mainStageId: '', code: item.subStageCode || '—', name: item.subStageName || 'مرحلة مرتبطة', capacity: 0, sequenceOrder: 0, isActive: false }, ...this.modelStageOptions]; }
}
