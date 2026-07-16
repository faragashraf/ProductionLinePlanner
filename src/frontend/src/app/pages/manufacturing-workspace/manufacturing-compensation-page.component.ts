import { Component, OnInit } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { environment } from '../../../environments/environment';
import { STAGE_COST_TERMINOLOGY } from '../../core/config/stage-cost-terminology';
import {
  CompensationMode,
  CompensationModelStageUpdate,
  ManufacturingMasterDataApiService,
  ModelStageItem,
  ProductModelItem
} from '../../core/services/manufacturing-master-data-api.service';

const COMPENSATION_MODES: readonly CompensationMode[] = [
  'SharedPercentage',
  'FullRatePerWorker',
  'FixedAmount'
];

interface CompensationMethodDefinition {
  mode: CompensationMode;
  title: string;
  description: string;
}

interface CompensationStageField {
  key: 'code' | 'name' | 'piecePrice' | 'standardSeconds' | 'method' | 'status';
  label: string;
  emphasis?: 'method';
}

const COMPENSATION_METHODS: readonly CompensationMethodDefinition[] = [
  { mode: 'SharedPercentage', title: 'توزيع نسبي', description: 'تُسجّل كمية المرحلة مرة واحدة ثم توزّع المستحقات بحسب نسب العاملين.' },
  { mode: 'FullRatePerWorker', title: 'سعر كامل لكل عامل', description: 'يحصل كل عامل معيّن على قيمة الكمية المقبولة كاملة من دون مضاعفة الإنتاج.' },
  { mode: 'FixedAmount', title: 'قيمة ثابتة', description: 'يتلقى العامل مبلغًا ثابتًا مستقلًا عن كمية الإنتاج المسجّلة.' }
];

const COMPENSATION_STAGE_FIELDS: readonly CompensationStageField[] = [
  { key: 'code', label: 'الكود' },
  { key: 'name', label: 'المرحلة' },
  { key: 'piecePrice', label: 'سعر القطعة' },
  { key: 'standardSeconds', label: 'الثواني' },
  { key: 'method', label: STAGE_COST_TERMINOLOGY.calculationMethod, emphasis: 'method' },
  { key: 'status', label: 'الحالة' }
];

@Component({
  selector: 'app-manufacturing-compensation-page',
  templateUrl: './manufacturing-compensation-page.component.html',
  styleUrls: ['./manufacturing-compensation-page.component.scss']
})
export class ManufacturingCompensationPageComponent implements OnInit {
  readonly permissions = PERMISSIONS;
  readonly stageCostTerminology = STAGE_COST_TERMINOLOGY;
  readonly compensationModes = COMPENSATION_MODES;
  readonly compensationMethods = COMPENSATION_METHODS;
  readonly stageFields = COMPENSATION_STAGE_FIELDS;
  readonly showDialogDiagnostics = !environment.production;

  models: ProductModelItem[] = [];
  stages: ModelStageItem[] = [];
  selectedModelId = '';
  editingStageId = '';
  editingStage: ModelStageItem | null = null;
  isEditDialogVisible = false;
  editStageEntered = false;
  dialogShowEventReceived = false;
  isLoadingModels = false;
  isLoadingStages = false;
  hasLoadedModels = false;
  hasLoadedStages = false;
  isSaving = false;
  hasError = false;
  errorMessage = 'تعذر تحميل إعدادات تكلفة المراحل، يرجى المحاولة مرة أخرى.';
  successMessage = '';

  readonly stageForm = this.formBuilder.group({
    compensationMode: ['SharedPercentage' as CompensationMode, Validators.required],
    piecePrice: [0, [Validators.required, Validators.min(0)]],
    standardSeconds: [null as number | null, Validators.min(1)]
  });

  constructor(
    private readonly formBuilder: FormBuilder,
    private readonly api: ManufacturingMasterDataApiService,
    private readonly permissionService: PermissionService
  ) {}

  ngOnInit(): void {
    this.loadModels();
  }

  get canManage(): boolean {
    return this.permissionService.hasPermission(this.permissions.compensation.manage);
  }

  get isModelsEmpty(): boolean {
    return this.hasLoadedModels && !this.isLoadingModels && !this.hasError && this.models.length === 0;
  }

  get isStagesEmpty(): boolean {
    return !!this.selectedModelId && this.hasLoadedStages && !this.isLoadingStages && !this.hasError && this.stages.length === 0;
  }

  get dialogExpectedState(): string {
    return this.isEditDialogVisible
      ? 'visible=true: يجب أن تعرض PrimeNG القناع والنافذة الحوارية.'
      : 'visible=false: لا يجب أن تعرض PrimeNG النافذة الحوارية.';
  }

  onModelChange(event: Event): void {
    const modelId = (event.target as HTMLSelectElement).value;
    this.selectedModelId = modelId;
    this.stages = [];
    this.hasLoadedStages = false;
    this.cancelEdit();

    if (modelId) {
      this.loadStages(modelId);
    }
  }

  editStage(stage: ModelStageItem): void {
    this.editStageEntered = true;
    this.dialogShowEventReceived = false;
    this.editingStage = stage;
    this.editingStageId = stage.id;
    this.isEditDialogVisible = true;
    this.stageForm.reset({
      compensationMode: stage.compensationMode,
      piecePrice: stage.piecePrice,
      standardSeconds: stage.standardSeconds ?? null
    });
  }

  cancelEdit(): void {
    this.isEditDialogVisible = false;
    this.editingStage = null;
    this.editingStageId = '';
    this.stageForm.reset({ compensationMode: 'SharedPercentage', piecePrice: 0, standardSeconds: null });
  }

  onDialogShow(): void {
    this.dialogShowEventReceived = true;
  }

  onDialogHide(): void {
    this.dialogShowEventReceived = false;
  }

  saveStage(): void {
    if (!this.selectedModelId || !this.editingStageId || this.stageForm.invalid) {
      this.stageForm.markAllAsTouched();
      return;
    }

    const formValue = this.stageForm.getRawValue();
    const payload: CompensationModelStageUpdate = {
      compensationMode: formValue.compensationMode as CompensationMode,
      piecePrice: Number(formValue.piecePrice),
      standardSeconds: formValue.standardSeconds === null || formValue.standardSeconds === undefined
        ? null
        : Number(formValue.standardSeconds)
    };

    this.isSaving = true;
    this.hasError = false;
    this.successMessage = '';
    this.api.updateCompensationModelStage(this.selectedModelId, this.editingStageId, payload)
      .pipe(finalize(() => this.isSaving = false))
      .subscribe({
        next: updated => {
          this.successMessage = 'تم حفظ إعداد تكلفة المرحلة.';
          this.stages = this.stages.map(stage => stage.id === updated.id ? updated : stage);
          this.cancelEdit();
        },
        error: error => this.setError(error, 'تعذر حفظ إعداد تكلفة المرحلة.')
      });
  }

  onRetry(): void {
    if (this.selectedModelId) {
      this.loadStages(this.selectedModelId);
      return;
    }

    this.loadModels();
  }

  modeLabel(mode: CompensationMode): string {
    switch (mode) {
      case 'SharedPercentage': return 'توزيع نسبي';
      case 'FullRatePerWorker': return 'سعر كامل لكل عامل';
      case 'FixedAmount': return 'قيمة ثابتة';
    }
  }

  stageFieldValue(stage: ModelStageItem, field: CompensationStageField): string | number {
    switch (field.key) {
      case 'code': return stage.subStageCode || '-';
      case 'name': return stage.subStageName || stage.subStageId;
      case 'piecePrice': return stage.piecePrice;
      case 'standardSeconds': return stage.standardSeconds ?? '-';
      case 'method': return this.modeLabel(stage.compensationMode);
      case 'status': return stage.isActive ? 'نشطة' : 'معطلة';
    }
  }

  trackByStageId(_: number, stage: ModelStageItem): string {
    return stage.id;
  }

  private loadModels(): void {
    this.isLoadingModels = true;
    this.hasError = false;
    this.api.compensationModels()
      .pipe(finalize(() => {
        this.isLoadingModels = false;
        this.hasLoadedModels = true;
      }))
      .subscribe({
        next: models => this.models = models.filter(model => model.isActive),
        error: error => {
          this.models = [];
          this.setError(error, 'تعذر تحميل نماذج المنتجات النشطة.');
        }
      });
  }

  private loadStages(modelId: string): void {
    this.isLoadingStages = true;
    this.hasError = false;
    this.api.compensationModelStages(modelId)
      .pipe(finalize(() => {
        this.isLoadingStages = false;
        this.hasLoadedStages = true;
      }))
      .subscribe({
        next: stages => {
          if (this.selectedModelId === modelId) {
            this.stages = stages;
          }
        },
        error: error => {
          if (this.selectedModelId === modelId) {
            this.stages = [];
            this.setError(error, 'تعذر تحميل مراحل الموديل.');
          }
        }
      });
  }

  private setError(error: unknown, fallback: string): void {
    this.hasError = true;
    this.successMessage = '';
    this.errorMessage = error instanceof Error && error.message.trim().length > 0 ? error.message : fallback;
  }
}
