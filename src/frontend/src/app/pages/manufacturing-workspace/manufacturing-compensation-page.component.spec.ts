import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TableModule } from 'primeng/table';
import { DialogModule } from 'primeng/dialog';
import { ReactiveFormsModule } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import {
  CompensationMode,
  CompensationModelStageUpdate,
  ManufacturingMasterDataApiService,
  ModelStageItem,
  ProductModelItem
} from '../../core/services/manufacturing-master-data-api.service';
import { SharedModule } from '../../shared/shared.module';
import { ManufacturingCompensationPageComponent } from './manufacturing-compensation-page.component';

const models: ProductModelItem[] = [
  { id: 'model-grm001', code: 'GRM001', name: 'جرومان', isActive: true },
  { id: 'model-inactive', code: 'OLD001', name: 'موديل معطل', isActive: false }
];

const stages: ModelStageItem[] = [
  { id: 'stage-shared', productModelId: 'model-grm001', subStageId: 'sub-1', subStageCode: 'STG001', subStageName: 'تجهيز', stageOrder: 1, piecePrice: 0.5, standardSeconds: 22, compensationMode: 'SharedPercentage', isRequired: true, isActive: true },
  { id: 'stage-full', productModelId: 'model-grm001', subStageId: 'sub-2', subStageCode: 'STG002', subStageName: 'خياطة', stageOrder: 2, piecePrice: 0.75, standardSeconds: 18, compensationMode: 'FullRatePerWorker', isRequired: true, isActive: true },
  { id: 'stage-fixed', productModelId: 'model-grm001', subStageId: 'sub-3', subStageCode: 'STG003', subStageName: 'تشطيب', stageOrder: 3, piecePrice: 1.25, standardSeconds: null, compensationMode: 'FixedAmount', isRequired: true, isActive: false }
];

describe('ManufacturingCompensationPageComponent', () => {
  function createComponent(options: {
    manage?: boolean;
    models$?: Observable<ProductModelItem[]>;
    stages$?: Observable<ModelStageItem[]>;
  } = {}): ComponentFixture<ManufacturingCompensationPageComponent> {
    const api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'compensationModels',
      'compensationModelStages',
      'updateCompensationModelStage'
    ]);
    api.compensationModels.and.returnValue(options.models$ ?? of(models));
    api.compensationModelStages.and.returnValue(options.stages$ ?? of(stages));
    api.updateCompensationModelStage.and.callFake((_modelId: string, stageId: string, update: CompensationModelStageUpdate) => of({
      ...stages.find(stage => stage.id === stageId)!,
      ...update
    }));

    const hydration = new BehaviorSubject<'ready'>('ready');
    const permissionService = {
      permissions$: of([]),
      hydrationState$: hydration.asObservable(),
      get hydrationState() { return 'ready'; },
      hasPermission: (permission: string) => permission === PERMISSIONS.compensation.manage && options.manage === true,
      hasAccess: (requirement: { permission?: string }) => requirement.permission !== PERMISSIONS.compensation.manage || options.manage === true
    };

    TestBed.configureTestingModule({
      declarations: [ManufacturingCompensationPageComponent],
      imports: [ReactiveFormsModule, SharedModule, CardModule, ButtonModule, TableModule, DialogModule, NoopAnimationsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: api },
        { provide: PermissionService, useValue: permissionService }
      ]
    });

    const fixture = TestBed.createComponent(ManufacturingCompensationPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('loads active models and renders ProductModelStage rows from the compensation contract', () => {
    const fixture = createComponent({ manage: true });
    const component = fixture.componentInstance;
    const modelSelect = fixture.debugElement.query(By.css('#compensationModel')).nativeElement as HTMLSelectElement;

    expect(component.models.map(model => model.code)).toEqual(['GRM001']);

    modelSelect.value = 'model-grm001';
    modelSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('STG001');
    expect(text).toContain('تجهيز');
    expect(text).toContain('توزيع نسبي');
    expect(text).toContain('سعر كامل لكل عامل');
    expect(text).toContain('قيمة ثابتة');
  });

  it('opens the rendered PrimeNG dialog with the selected stage context and values', () => {
    const fixture = createComponent({ manage: true });
    const component = fixture.componentInstance;
    component.onModelChange({ target: { value: 'model-grm001' } } as unknown as Event);
    fixture.detectChanges();

    const editButton = fixture.debugElement.queryAll(By.css('button'))
      .find(button => button.nativeElement.textContent.trim() === 'تعديل');
    expect(editButton).toBeDefined();
    editButton!.nativeElement.click();
    fixture.detectChanges();

    const dialog = fixture.debugElement.query(By.css('p-dialog'));
    expect(dialog).not.toBeNull();
    expect(component.isEditDialogVisible).toBeTrue();
    expect(component.editingStageId).toBe('stage-shared');
    expect(component.stageForm.getRawValue()).toEqual({ compensationMode: 'SharedPercentage', piecePrice: 0.5, standardSeconds: 22 });
    expect(fixture.nativeElement.textContent).toContain('STG001');
    expect(fixture.nativeElement.textContent).toContain('تجهيز');
    expect(document.body.querySelector('.p-dialog-mask')).not.toBeNull();
    expect(document.body.querySelector('.p-dialog.compensation-edit-dialog')).not.toBeNull();
    expect(fixture.debugElement.queryAll(By.css('p-card')).length).toBe(1);
  });

  ([
    ['SharedPercentage', 0.5, 22],
    ['FullRatePerWorker', 0.75, 18],
    ['FixedAmount', 1.25, null]
  ] as Array<[CompensationMode, number, number | null]>).forEach(([mode, piecePrice, standardSeconds]) => {
    it(`saves ${mode} with valid price and standard-seconds values then reloads persisted stages`, () => {
      const fixture = createComponent({ manage: true });
      const component = fixture.componentInstance;
      const api = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;

      component.onModelChange({ target: { value: 'model-grm001' } } as unknown as Event);
      component.editStage(stages[0]);
      component.stageForm.reset({ compensationMode: mode, piecePrice, standardSeconds });
      api.compensationModelStages.calls.reset();

      component.saveStage();
      fixture.detectChanges();

      expect(api.updateCompensationModelStage).toHaveBeenCalledTimes(1);
      expect(api.updateCompensationModelStage).toHaveBeenCalledWith('model-grm001', 'stage-shared', { compensationMode: mode, piecePrice, standardSeconds });
      expect(api.compensationModelStages).toHaveBeenCalledTimes(1);
      expect(component.selectedModelId).toBe('model-grm001');
      expect(component.editingStageId).toBe('');
      expect(component.isEditDialogVisible).toBeFalse();
    });
  });

  it('saves from the rendered dialog once, closes it after success, and reloads persisted rows', () => {
    const fixture = createComponent({ manage: true });
    const component = fixture.componentInstance;
    const api = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    component.onModelChange({ target: { value: 'model-grm001' } } as unknown as Event);
    component.editStage(stages[1]);
    api.compensationModelStages.calls.reset();
    fixture.detectChanges();

    fixture.debugElement.query(By.css('.compensation-edit-dialog__form button[type="submit"]')).nativeElement.click();
    fixture.detectChanges();

    expect(api.updateCompensationModelStage).toHaveBeenCalledTimes(1);
    expect(api.updateCompensationModelStage).toHaveBeenCalledWith('model-grm001', 'stage-full', {
      compensationMode: 'FullRatePerWorker', piecePrice: 0.75, standardSeconds: 18
    });
    expect(api.compensationModelStages).toHaveBeenCalledTimes(1);
    expect(component.isEditDialogVisible).toBeFalse();
    expect(component.selectedModelId).toBe('model-grm001');
  });

  it('keeps the dialog open and displays the save error when persistence fails', () => {
    const fixture = createComponent({ manage: true });
    const component = fixture.componentInstance;
    const api = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    api.updateCompensationModelStage.and.returnValue(throwError(() => new Error('Save failed')));
    component.onModelChange({ target: { value: 'model-grm001' } } as unknown as Event);
    component.editStage(stages[0]);
    fixture.detectChanges();

    fixture.debugElement.query(By.css('.compensation-edit-dialog__form button[type="submit"]')).nativeElement.click();
    fixture.detectChanges();

    expect(api.updateCompensationModelStage).toHaveBeenCalledTimes(1);
    expect(component.isEditDialogVisible).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Save failed');
  });

  it('cancels the rendered dialog without saving and resets values when another row is opened', () => {
    const fixture = createComponent({ manage: true });
    const component = fixture.componentInstance;
    const api = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    component.onModelChange({ target: { value: 'model-grm001' } } as unknown as Event);
    component.editStage(stages[0]);
    component.stageForm.patchValue({ piecePrice: 99 });
    fixture.detectChanges();

    fixture.debugElement.query(By.css('.compensation-edit-dialog__form button.p-button-text')).nativeElement.click();
    fixture.detectChanges();
    expect(api.updateCompensationModelStage).not.toHaveBeenCalled();
    expect(component.isEditDialogVisible).toBeFalse();

    component.editStage(stages[2]);
    fixture.detectChanges();
    expect(component.editingStageId).toBe('stage-fixed');
    expect(component.stageForm.getRawValue()).toEqual({ compensationMode: 'FixedAmount', piecePrice: 1.25, standardSeconds: null });
  });

  it('hides management controls without compensation.manage while retaining read-only stage data', () => {
    const fixture = createComponent({ manage: false });
    const component = fixture.componentInstance;

    component.onModelChange({ target: { value: 'model-grm001' } } as unknown as Event);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('صلاحية العرض فقط');
    expect(fixture.nativeElement.textContent).toContain('STG001');
    expect(fixture.debugElement.queryAll(By.css('button')).map(button => button.nativeElement.textContent.trim())).not.toContain('تعديل');
  });

  it('renders the empty state when no active models are returned', () => {
    const fixture = createComponent({ models$: of([]) });
    expect(fixture.nativeElement.textContent).toContain('لا توجد نماذج نشطة');
  });

  it('renders the error state when compensation model loading fails', () => {
    const fixture = createComponent({ models$: throwError(() => new Error('Compensation models failed')) });

    expect(fixture.nativeElement.textContent).toContain('تعذر تحميل إعدادات التعويض');
    const errorFixture = fixture;
    expect(errorFixture.nativeElement.textContent).toContain('Compensation models failed');
  });
});
