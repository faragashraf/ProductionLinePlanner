import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;
  let realtime: jasmine.SpyObj<ManufacturingRealtimeService>;

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
    realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('ManufacturingRealtimeService', ['watchScreen', 'registerLocalOperation']);
    realtime.watchScreen.and.returnValue(() => undefined);
    realtime.registerLocalOperation.and.returnValue('local-correlation');

    await TestBed.configureTestingModule({
      declarations: [ManufacturingMasterDataPageComponent],
      imports: [FormsModule, ReactiveFormsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: api },
        { provide: ManufacturingRealtimeService, useValue: realtime },
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
    expect(realtime.registerLocalOperation).toHaveBeenCalledWith('stages');
    expect(api.operationalStages).toHaveBeenCalledWith({ productionLineId: line.id, isActive: undefined, includeInactive: true });
    expect(component.operationalStages).toEqual([stage]);
  });

  it('uses the local correlation for saving a stage linked to a model', () => {
    component.selected = firstModel;
    component.modelStageForm.setValue({ subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true });

    component.saveModelStage();

    expect(api.addModelStage).toHaveBeenCalledWith(firstModel.id, component.modelStageForm.getRawValue(), 'local-correlation');
    expect(realtime.registerLocalOperation).toHaveBeenCalledWith('models');
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

  it('loads every catalog page before excluding linked stages, so total and visible items stay consistent', () => {
    const catalog = [stage, englishStage, { ...stage, id: 'stage-3', code: 'PACK-03', name: 'التغليف', sequenceOrder: 3 }];
    api.searchSubStages.and.callFake((_search: string, page: number) => of(page === 1
      ? { items: [stage, englishStage], totalCount: 3, pageNumber: 1, pageSize: 2 }
      : { items: [catalog[2]], totalCount: 3, pageNumber: 2, pageSize: 2 }));
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];

    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();

    expect(api.searchSubStages).toHaveBeenCalledWith('', 1, 200);
    expect(api.searchSubStages).toHaveBeenCalledWith('', 2, 2);
    expect(component.availableStagesTotal).toBe(2);
    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id, 'stage-3']);
    expect(component.availableStagesPageCount).toBe(1);
  });

  it('searches available stages by Arabic name and code without mixing linked stages', () => {
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, englishStage];
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];

    component.onAvailableStagesSearch('cutting');
    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id]);
    component.onAvailableStagesSearch('CUT-02');
    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id]);
    component.onAvailableStagesSearch('STG001');
    expect(component.availableStageChoices).toEqual([]);
  });

  it('resets available-stage pagination on search and keeps an explicit selection across pages', () => {
    const catalog = Array.from({ length: 12 }, (_, index) => ({ ...stage, id: `stage-${index + 1}`, code: `STG-${index + 1}`, name: `مرحلة ${index + 1}`, sequenceOrder: index + 1 }));
    (component as never as { availableStageCatalog: typeof catalog }).availableStageCatalog = catalog;
    component.onAvailableStagesSearch('');
    component.changeAvailableStagesPage(1);
    component.selectAvailableStage(catalog[0]);
    component.onAvailableStagesSearch('مرحلة 1');

    expect(component.availableStagesPage).toBe(1);
    expect(component.modelStageForm.controls.subStageId.value).toBe(catalog[0].id);
    expect(component.availableStageChoices[0].id).toBe(catalog[0].id);
  });

  it('keeps the current edit stage visible and selected even when it is already linked', () => {
    const linked = { id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    component.stages = [linked];
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, englishStage];
    component.editModelStage(linked);

    expect(component.modelStageForm.controls.subStageId.value).toBe(stage.id);
    expect(component.availableStageChoices.map(item => item.id)).toContain(stage.id);
  });

  it('ignores a stale catalog response after a newer refresh response is applied', () => {
    const stale = new Subject<any>();
    const current = new Subject<any>();
    api.searchSubStages.and.returnValues(stale, current);

    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();
    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();
    current.next({ items: [englishStage], totalCount: 1, pageNumber: 1, pageSize: 200 });
    stale.next({ items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 });

    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id]);
  });

  it('shows scoped available-stage empty and error states and retries catalog loading', () => {
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage];
    component.onAvailableStagesSearch('غير موجود');
    expect(component.availableStageChoices).toEqual([]);

    api.searchSubStages.and.returnValues(
      throwError(() => new Error('انقطع الاتصال')),
      of({ items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 })
    );
    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();
    expect(component.availableStagesError).toBe('انقطع الاتصال');

    component.onAvailableStagesSearch('');
    component.retryAvailableStages();
    expect(component.availableStagesError).toBe('');
    expect(component.availableStageChoices).toEqual([stage]);
  });

  it('searches linked model stages by name and code, resets the table key, and keeps StageOrder', () => {
    const later = { id: 'model-stage-2', subStageId: englishStage.id, subStageCode: englishStage.code, subStageName: englishStage.name, stageOrder: 2, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    const first = { id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    component.stages = [later, first];
    component.onLinkedStagesSearch('CUT-02');
    expect(component.filteredLinkedStages.map(item => item.id)).toEqual([later.id]);
    component.onLinkedStagesSearch('');
    expect(component.filteredLinkedStages.map(item => item.id)).toEqual([first.id, later.id]);
    component.clearLinkedStagesSearch();
    expect(component.linkedStagesSearch).toBe('');
  });

  it('keeps the linked-stage search independent from the available-stage search and reports a no-match empty state', () => {
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
    component.onAvailableStagesSearch('CUT-02');
    component.onLinkedStagesSearch('غير موجود');

    expect(component.availableStagesSearch).toBe('CUT-02');
    expect(component.filteredLinkedStages).toEqual([]);
    expect(component.linkedStagesEmptyMessage).toBe('توجد مراحل مرتبطة، لكن لا توجد نتائج مطابقة للبحث.');
  });
});
