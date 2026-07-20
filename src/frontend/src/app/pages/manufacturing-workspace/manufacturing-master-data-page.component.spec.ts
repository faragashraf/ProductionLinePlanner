import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;

  const factory = { id: 'factory-1', code: 'FAC', name: 'مصنع الملابس', isActive: true };
  const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'القص', isActive: true };
  const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, name: 'خط القص', lineCode: 'L1', sequenceOrder: 1, isActive: true };
  const stage = { id: 'stage-1', mainStageId: 'legacy-group-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, factoryName: factory.name, departmentNameAr: department.nameAr, productionLineName: line.name, name: 'تجهيز', code: 'STG001', capacity: 2, sequenceOrder: 1, isActive: true };
  const englishStage = { ...stage, id: 'stage-2', name: 'Cutting Line', code: 'CUT-02', sequenceOrder: 2 };
  const firstModel = { id: 'model-1', code: 'MOD-001', name: 'موديل القميص', isActive: true };
  const secondModel = { id: 'model-2', code: 'MOD-002', name: 'Jacket Model', isActive: true };

  beforeEach(async () => {
    api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories', 'departments', 'productionLinesForDepartment', 'operationalStages', 'createOperationalStage', 'updateOperationalStage',
      'stageDependencies', 'deactivateOperationalStage', 'deleteOperationalStage', 'models', 'modelSearchPage', 'searchSubStages', 'modelStages',
      'createModel', 'updateModel', 'setModelActivation', 'addModelStage', 'updateModelStage', 'deactivateModelStage'
    ]);
    api.factories.and.returnValue(of([factory]));
    api.departments.and.returnValue(of([department]));
    api.productionLinesForDepartment.and.returnValue(of([line]));
    api.operationalStages.and.returnValue(of([stage]));
    api.createOperationalStage.and.returnValue(of(stage));
    api.updateOperationalStage.and.returnValue(of(stage));
    api.stageDependencies.and.returnValue(of({ stageId: stage.id, activeBlockers: [], historicalDependencies: [], canDisable: true, canDelete: true, disableMessageAr: '', deleteMessageAr: '' }));
    api.deactivateOperationalStage.and.returnValue(of({ ...stage, isActive: false }));
    api.deleteOperationalStage.and.returnValue(of(void 0));
    api.models.and.returnValue(of([]));
    api.modelSearchPage.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 10 }));
    api.searchSubStages.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 50 }));
    api.modelStages.and.returnValue(of([]));
    api.createModel.and.returnValue(of({ id: 'model-1', code: 'MOD', name: 'موديل', isActive: true }));
    api.updateModel.and.returnValue(of({ id: 'model-1', code: 'MOD', name: 'موديل', isActive: true }));
    api.setModelActivation.and.returnValue(of(void 0));
    api.addModelStage.and.returnValue(of({ id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }));
    api.updateModelStage.and.returnValue(of({ id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }));
    api.deactivateModelStage.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      declarations: [ManufacturingMasterDataPageComponent],
      imports: [FormsModule, ReactiveFormsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: api },
        { provide: ManufacturingRealtimeService, useValue: { watchScreen: () => () => undefined, registerLocalOperation: () => 'local-correlation' } },
        { provide: ActivatedRoute, useValue: { snapshot: { routeConfig: { path: 'stages' } } } }
      ]
    }).overrideComponent(ManufacturingMasterDataPageComponent, { set: { template: '' } }).compileComponents();
    const fixture: ComponentFixture<ManufacturingMasterDataPageComponent> = TestBed.createComponent(ManufacturingMasterDataPageComponent);
    component = fixture.componentInstance;
  });

  it('does not expose legacy grouping forms in the stage catalog state', () => {
    expect((component as never as { mainForm?: unknown }).mainForm).toBeUndefined();
    expect((component as never as { subForm?: unknown }).subForm).toBeUndefined();
    expect(Object.keys(component.stageForm.controls)).not.toContain('mainStageId');
    expect(Object.keys(component.stageForm.controls)).not.toContain('code');
    expect(Object.keys(component.stageForm.controls)).not.toContain('defaultOrder');
  });

  it('loads only the selected factory departments and clears dependent context', () => {
    component.stageForm.patchValue({ departmentId: 'old-department', productionLineId: 'old-line' });
    component.operationalStages = [stage];

    component.selectFactory(factory.id);

    expect(component.stageForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: '', productionLineId: '' }));
    expect(component.operationalStages).toEqual([]);
    expect(api.departments).toHaveBeenCalledWith(factory.id, false);
    expect(component.activeDepartments).toEqual([department]);
  });

  it('loads only the selected department lines and clears the stage list', () => {
    component.stageForm.patchValue({ factoryId: factory.id, productionLineId: 'old-line' });
    component.operationalStages = [stage];

    component.selectDepartment(department.id);

    expect(component.stageForm.getRawValue()).toEqual(jasmine.objectContaining({ departmentId: department.id, productionLineId: '' }));
    expect(component.operationalStages).toEqual([]);
    expect(api.productionLinesForDepartment).toHaveBeenCalledWith(department.id);
    expect(component.activeLines).toEqual([line]);
  });

  it('loads only the selected line operational stages including legacy-parent stages', () => {
    component.stageForm.patchValue({ factoryId: factory.id, departmentId: department.id });

    component.selectLine(line.id);

    expect(api.operationalStages).toHaveBeenCalledWith({ productionLineId: line.id, isActive: undefined, includeInactive: true });
    expect(component.operationalStages).toEqual([stage]);
  });

  it('clears the complete stage context when its selected factory no longer exists after a realtime refresh', () => {
    component.stageForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id, name: '', capacity: 0 });
    component.departments = [department];
    component.lines = [line];
    component.operationalStages = [stage];
    api.factories.and.returnValue(of([]));

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: '', departmentId: '', productionLineId: '' }));
    expect(component.departments).toEqual([]);
    expect(component.lines).toEqual([]);
    expect(component.operationalStages).toEqual([]);
  });

  it('clears the selected department and dependent line when that department no longer exists after a realtime refresh', () => {
    component.stageForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id, name: '', capacity: 0 });
    component.lines = [line];
    component.operationalStages = [stage];
    api.departments.and.returnValue(of([]));

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: '', productionLineId: '' }));
    expect(component.lines).toEqual([]);
    expect(component.operationalStages).toEqual([]);
  });

  it('clears the selected line and its stages when that line no longer exists after a realtime refresh', () => {
    component.stageForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id, name: '', capacity: 0 });
    component.operationalStages = [stage];
    api.productionLinesForDepartment.and.returnValue(of([]));

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: department.id, productionLineId: '' }));
    expect(component.operationalStages).toEqual([]);
  });

  it('creates a stage from its direct production-line context without legacy ordering input', () => {
    component.stageForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id, name: 'تشطيب', capacity: 3 });

    component.saveOperationalStage();

    expect(api.createOperationalStage).toHaveBeenCalledWith({ productionLineId: line.id, name: 'تشطيب', capacity: 3 }, 'local-correlation');
    expect(api.operationalStages).toHaveBeenCalledWith({ productionLineId: line.id, isActive: undefined, includeInactive: true });
    expect(component.operationalStages).toEqual([stage]);
  });

  it('updates the stage row immediately after a successful deactivation without reloading', () => {
    const inactiveStage = { ...stage, isActive: false };
    api.deactivateOperationalStage.and.returnValue(of(inactiveStage));
    component.operationalStages = [stage];
    component.pendingStage = stage;
    component.pendingStageAction = 'disable';
    component.stageDependencySummary = { stageId: stage.id, activeBlockers: [], historicalDependencies: [], canDisable: true, canDelete: true, disableMessageAr: '', deleteMessageAr: '' };

    component.confirmDependencyAction();

    expect(api.deactivateOperationalStage).toHaveBeenCalledOnceWith(stage.id, 'local-correlation');
    expect(component.operationalStages).toEqual([inactiveStage]);
    expect(api.operationalStages).not.toHaveBeenCalled();
    expect(component.error).toBe('');
  });

  it('does not submit a duplicate deactivation while the first request is saving', () => {
    component.saving = true;
    component.pendingStage = stage;
    component.pendingStageAction = 'disable';
    component.stageDependencySummary = { stageId: stage.id, activeBlockers: [], historicalDependencies: [], canDisable: true, canDelete: true, disableMessageAr: '', deleteMessageAr: '' };

    component.confirmDependencyAction();

    expect(api.deactivateOperationalStage).not.toHaveBeenCalled();
  });

  it('keeps model journey price, seconds, and compensation fields on the model-stage form', () => {
    expect(component.modelStageForm.controls.piecePrice).toBeDefined();
    expect(component.modelStageForm.controls.standardSeconds).toBeDefined();
    expect(component.modelStageForm.controls.compensationMode).toBeDefined();
  });

  it('filters stages by Arabic name, partial code, English casing, and ignores outer whitespace without changing order', () => {
    component.operationalStages = [stage, englishStage];

    component.onStageSearch('  جه ');
    expect(component.filteredOperationalStages).toEqual([stage]);
    component.onStageSearch('001');
    expect(component.filteredOperationalStages).toEqual([stage]);
    component.onStageSearch('  cutting  ');
    expect(component.filteredOperationalStages).toEqual([englishStage]);
    component.onStageSearch('');
    expect(component.filteredOperationalStages).toEqual([stage, englishStage]);
  });

  it('reports the stage-search empty state when no stage matches', () => {
    component.operationalStages = [stage];
    component.onStageSearch('غير موجود');

    expect(component.filteredOperationalStages).toEqual([]);
    expect(component.stageEmptyMessage).toBe('لا توجد مراحل مطابقة للبحث.');
  });

  it('requests a server-side model search from the first page after the debounce', fakeAsync(() => {
    (component as { mode: 'stages' | 'models' }).mode = 'models';
    api.modelSearchPage.and.returnValue(of({ items: [firstModel], totalCount: 51, pageNumber: 1, pageSize: 10 }));
    component.ngOnInit();
    api.modelSearchPage.calls.reset();

    component.onModelSearch('  موديل  ');
    tick(250);

    expect(api.modelSearchPage).toHaveBeenCalledWith('  موديل  ', 1, 10);
    expect(component.models).toEqual([firstModel]);
    expect(component.modelTotal).toBe(51);
    expect(component.modelPage).toBe(1);
  }));

  it('loads the requested server page and does not locally filter its rows', () => {
    component.models = [firstModel, secondModel];
    component.modelPage = 1;
    component.modelSearch = 'Jacket';
    api.modelSearchPage.and.returnValue(of({ items: [secondModel], totalCount: 11, pageNumber: 2, pageSize: 10 }));

    component.onModelLazyLoad({ first: 10, rows: 10 });

    expect(api.modelSearchPage).toHaveBeenCalledWith('Jacket', 2, 10);
    expect(component.models).toEqual([secondModel]);
    expect(component.modelTotal).toBe(11);
    expect(component.modelPage).toBe(2);
  });

  it('keeps model and stage searches independent when either value is cleared', () => {
    component.onStageSearch('قص');
    component.onModelSearch('MOD-001');

    expect(component.stageSearch).toBe('قص');
    expect(component.modelSearch).toBe('MOD-001');
    component.onModelSearch('');
    expect(component.stageSearch).toBe('قص');
    component.onStageSearch('');
    expect(component.modelSearch).toBe('');
  });

  it('reports the model-search empty state when the server returns no matching models', () => {
    component.modelSearch = 'موديل غير موجود';
    component.models = [];

    expect(component.modelEmptyMessage).toBe('لا توجد موديلات مطابقة للبحث.');
  });
});
