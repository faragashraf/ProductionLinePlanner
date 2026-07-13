import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormBuilder, Validators } from '@angular/forms';
import { finalize, Observable } from 'rxjs';
import { MainStageOption, ManufacturingMasterDataApiService, ModelStageItem, ProductModelItem, ProductionLineOption, SubStageOption } from '../../core/services/manufacturing-master-data-api.service';

@Component({ selector: 'app-manufacturing-master-data-page', templateUrl: './manufacturing-master-data-page.component.html', styleUrls: ['./manufacturing-master-data-page.component.scss'] })
export class ManufacturingMasterDataPageComponent implements OnInit {
  readonly mode: 'stages' | 'models'; loading = true; saving = false; error = ''; editMainId = ''; editSubId = ''; editModelId = ''; editModelStageId = '';
  lines: ProductionLineOption[] = []; mains: MainStageOption[] = []; subs: SubStageOption[] = []; models: ProductModelItem[] = []; stages: ModelStageItem[] = []; selected: ProductModelItem | null = null;
  readonly mainForm = this.fb.group({ productionLineId: ['', Validators.required], name: ['', Validators.required], sequenceOrder: [1, Validators.required], isCritical: [false] });
  readonly subForm = this.fb.group({ mainStageId: ['', Validators.required], code: ['', Validators.required], name: ['', Validators.required], capacity: [0, Validators.required], sequenceOrder: [1, Validators.required] });
  readonly modelForm = this.fb.group({ code: ['', Validators.required], name: ['', Validators.required], description: [''] });
  readonly stageForm = this.fb.group({ subStageId: ['', Validators.required], stageOrder: [1, Validators.required], piecePrice: [0, Validators.required], standardSeconds: [null as number | null], compensationMode: ['SharedPercentage', Validators.required], isRequired: [true], isActive: [true] });
  constructor(private readonly fb: FormBuilder, private readonly api: ManufacturingMasterDataApiService, route: ActivatedRoute) { this.mode = route.snapshot.routeConfig?.path === 'models' ? 'models' : 'stages'; }
  ngOnInit(): void { this.reload(); }
  reload(): void { this.loading = true; this.error = ''; this.api.subStages().subscribe({ next: x => this.subs = x }); if (this.mode === 'stages') { this.api.productionLines().subscribe({ next: x => this.lines = x }); this.api.mainStages().subscribe({ next: x => this.mains = x }); this.api.subStages().pipe(finalize(() => this.loading = false)).subscribe({ next: x => this.subs = x, error: e => this.error = e.message }); return; } this.api.models().pipe(finalize(() => this.loading = false)).subscribe({ next: x => this.models = x, error: e => this.error = e.message }); }
  saveMain(): void { if (this.mainForm.valid) this.save(this.editMainId ? this.api.updateMain(this.editMainId, this.mainForm.getRawValue()) : this.api.createMain(this.mainForm.getRawValue()), () => { this.editMainId = ''; this.mainForm.reset({ sequenceOrder: 1, isCritical: false }); }); }
  saveSub(): void { if (this.subForm.valid) { const value = this.subForm.getRawValue(); const request = { mainStageId: value.mainStageId, code: value.code, name: value.name, capacity: value.capacity, defaultOrder: value.sequenceOrder }; this.save(this.editSubId ? this.api.updateSub(this.editSubId, request) : this.api.createSub(request), () => { this.editSubId = ''; this.subForm.reset({ capacity: 0, sequenceOrder: 1 }); }); } }
  editMain(item: MainStageOption): void { this.editMainId = item.id; this.mainForm.reset(item); }
  editSub(item: SubStageOption): void { this.editSubId = item.id; this.subForm.reset(item); }
  disableMain(id: string): void { if (confirm('سيتم تعطيل المرحلة دون حذفها.')) this.save(this.api.deactivateMain(id)); }
  disableSub(id: string): void { if (confirm('سيتم تعطيل المرحلة دون حذفها.')) this.save(this.api.deactivateSub(id)); }
  saveModel(): void { if (this.modelForm.valid) this.save(this.editModelId ? this.api.updateModel(this.editModelId, this.modelForm.getRawValue()) : this.api.createModel(this.modelForm.getRawValue()), () => { this.editModelId = ''; this.modelForm.reset(); }); }
  editModel(item: ProductModelItem): void { this.editModelId = item.id; this.modelForm.reset(item); }
  select(item: ProductModelItem): void { this.selected = item; this.api.modelStages(item.id).subscribe({ next: x => this.stages = x, error: e => this.error = e.message }); }
  saveModelStage(): void { if (!this.selected || this.stageForm.invalid) return; const value = this.stageForm.getRawValue(); if (this.stages.some(x => x.subStageId === value.subStageId && x.id !== this.editModelStageId) || this.stages.some(x => x.stageOrder === value.stageOrder && x.id !== this.editModelStageId)) { this.error = 'لا يمكن تكرار المرحلة أو ترتيبها داخل الموديل.'; return; } this.save(this.editModelStageId ? this.api.updateModelStage(this.selected.id, this.editModelStageId, value) : this.api.addModelStage(this.selected.id, value), () => { this.editModelStageId = ''; this.stageForm.reset({ stageOrder: 1, piecePrice: 0, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }); this.select(this.selected!); }); }
  editStage(item: ModelStageItem): void { this.editModelStageId = item.id; this.stageForm.reset(item); }
  disableModelStage(id: string): void { if (this.selected && confirm('سيتم تعطيل إعداد المرحلة.')) this.save(this.api.deactivateModelStage(this.selected.id, id), () => this.select(this.selected!)); }
  setModelActive(item: ProductModelItem): void { if (confirm(item.isActive ? 'تعطيل الموديل؟' : 'تفعيل الموديل؟')) this.save(this.api.setModelActivation(item.id, !item.isActive)); }
  mainName(id: string): string { return this.mains.find(x => x.id === id)?.name ?? '-'; } subName(id: string): string { return this.subs.find(x => x.id === id)?.name ?? '-'; } totalPrice(): number { return this.stages.filter(x => x.isActive).reduce((sum, x) => sum + x.piecePrice, 0); } totalSeconds(): number { return this.stages.filter(x => x.isActive).reduce((sum, x) => sum + (x.standardSeconds ?? 0), 0); }
  private save(request: Observable<unknown>, success?: () => void): void { this.saving = true; request.pipe(finalize(() => this.saving = false)).subscribe({ next: () => { success?.(); this.reload(); }, error: e => this.error = e.message || 'تعذر حفظ التغيير.' }); }
}
