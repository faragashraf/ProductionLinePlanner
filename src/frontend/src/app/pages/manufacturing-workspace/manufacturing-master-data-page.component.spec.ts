import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { DropdownModule } from 'primeng/dropdown';
import { Subject, of, throwError } from 'rxjs';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let fixture: ComponentFixture<ManufacturingMasterDataPageComponent>;
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
      'factories', 'departments', 'allProductionLines', 'productionLinesForDepartment', 'operationalStages', 'createOperationalStage', 'updateOperationalStage',
      'stageDependencies', 'deactivateOperationalStage', 'deleteOperationalStage', 'models', 'modelSearchPage', 'searchSubStages', 'searchSubStagesByNameOrCode', 'allSubStages', 'modelStages',
      'createModel', 'updateModel', 'setModelActivation', 'addModelStage', 'updateModelStage', 'deactivateModelStage'
    ]);
    api.factories.and.returnValue(of([factory]));
    api.departments.and.returnValue(of([department]));
    api.allProductionLines.and.returnValue(of([line]));
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
    api.searchSubStagesByNameOrCode.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 50 }));
    api.allSubStages.and.returnValue(of([stage, englishStage]));
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
      imports: [CommonModule, FormsModule, ReactiveFormsModule, DropdownModule, NoopAnimationsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: api },
        { provide: ManufacturingRealtimeService, useValue: realtime },
        { provide: ActivatedRoute, useValue: { snapshot: { routeConfig: { path: 'stages' } } } }
      ],
      schemas: [NO_ERRORS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(ManufacturingMasterDataPageComponent);
    component = fixture.componentInstance;
  });

  it('does not expose legacy grouping forms in the stage catalog state', () => {
    expect((component as never as { mainForm?: unknown }).mainForm).toBeUndefined();
    expect((component as never as { subForm?: unknown }).subForm).toBeUndefined();
    expect(Object.keys(component.stageEditForm.controls)).not.toContain('mainStageId');
    expect(Object.keys(component.stageEditForm.controls)).not.toContain('code');
    expect(Object.keys(component.stageEditForm.controls)).not.toContain('defaultOrder');
    expect(component.stageFiltersForm).not.toBe(component.stageEditForm as never);
  });

  it('keeps the product-model code visible but immutable during edit and omits it from the update request', () => {
    component.editModel(firstModel);
    expect(component.modelForm.controls.code.disabled).toBeTrue();
    component.modelForm.controls.code.enable();
    component.modelForm.patchValue({ code: 'MUTATED-CODE', name: 'اسم محدث' });

    component.saveModel();

    expect(api.updateModel).toHaveBeenCalledWith(firstModel.id, jasmine.objectContaining({ name: 'اسم محدث' }), 'local-correlation');
    expect((api.updateModel.calls.mostRecent().args[1] as Record<string, unknown>)['code']).toBeUndefined();
  });

  it('loads only the selected factory departments and clears dependent context', () => {
    component.stageEditForm.patchValue({ departmentId: 'old-department', productionLineId: 'old-line' });

    component.selectStageEditFactory(factory.id);

    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: '', productionLineId: '' }));
    expect(api.departments).toHaveBeenCalledWith(factory.id, false);
    expect(component.activeStageEditDepartments).toEqual([department]);
  });

  it('loads only the selected department lines and clears the stage list', () => {
    component.stageEditForm.patchValue({ factoryId: factory.id, productionLineId: 'old-line' });

    component.selectStageEditDepartment(department.id);

    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({ departmentId: department.id, productionLineId: '' }));
    expect(api.productionLinesForDepartment).toHaveBeenCalledWith(department.id);
    expect(component.activeStageEditLines).toEqual([line]);
  });

  it('keeps the edit hierarchy stable while dependent options hydrate with an active tree filter', () => {
    const departmentsResponse = new Subject<typeof component.departments>();
    const linesResponse = new Subject<typeof component.lines>();
    component.reload();
    const selectedLine = component.stageFilterTreeNodes[0].children![0].children![0];
    component.selectStageFilterNode(selectedLine as any);
    api.departments.and.returnValue(departmentsResponse);
    api.productionLinesForDepartment.and.returnValue(linesResponse);

    component.editOperationalStage(stage);
    component.clearStageTreeFilter();

    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id,
      name: stage.name,
      capacity: stage.capacity
    }));
    expect(component.stageFiltersForm.getRawValue()).toEqual({ factoryId: '', departmentId: '', productionLineId: '' });
    departmentsResponse.next([department]);
    departmentsResponse.complete();
    expect(api.productionLinesForDepartment).toHaveBeenCalledWith(department.id);
    expect(component.stageEditForm.controls.departmentId.value).toBe(department.id);

    linesResponse.next([line]);
    linesResponse.complete();

    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id
    }));
  });

  it('hydrates an edited row from its own hierarchy while a different line is selected in the filters', () => {
    const otherDepartment = { ...department, id: 'department-2', code: 'SEW', nameAr: 'الخياطة' };
    const otherLine = { ...line, id: 'line-2', departmentId: otherDepartment.id, name: 'خط الخياطة', lineCode: 'L2' };
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: otherDepartment.id, productionLineId: otherLine.id });
    component.departments = [department, otherDepartment];
    component.lines = [line, otherLine];

    component.editOperationalStage(stage);
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: otherDepartment.id, productionLineId: '' });

    expect(component.stageEditForm.getRawValue()).toEqual({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id,
      name: stage.name,
      capacity: stage.capacity
    });
    expect(component.selectedStage?.code).toBe(stage.code);
    expect(component.stageFiltersForm.getRawValue()).toEqual({ factoryId: factory.id, departmentId: otherDepartment.id, productionLineId: '' });
  });

  it('keeps edit changes isolated from the screen filters after hydration', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    component.editOperationalStage(stage);

    component.selectStageEditFactory('another-factory');
    component.selectStageEditDepartment('another-department');

    expect(component.stageFiltersForm.getRawValue()).toEqual({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({
      factoryId: 'another-factory',
      departmentId: 'another-department',
      productionLineId: ''
    }));
  });

  it('does not let a realtime tree refresh overwrite the stage being edited', () => {
    component.reload();
    component.selectStageFilterNode(component.stageFilterTreeNodes[0].children![0].children![0] as any);
    component.editOperationalStage(stage);

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id
    }));
    expect(component.editStageId).toBe(stage.id);
  });

  it('ignores late edit hydration after close and keeps create prefill and dependent resets intact', () => {
    const staleDepartments = new Subject<typeof component.departments>();
    api.departments.and.returnValue(staleDepartments);
    component.editOperationalStage(stage);
    component.closeStageForm();

    component.stageFiltersForm.patchValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    component.stageEditForm.patchValue({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id,
      name: 'قيمة قديمة',
      capacity: 5
    });
    component.openStageForm();
    staleDepartments.next([]);
    staleDepartments.complete();

    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id,
      name: '',
      capacity: 0
    }));

    api.departments.and.returnValue(of([department]));
    component.selectStageEditFactory(factory.id);
    expect(component.stageEditForm.getRawValue()).toEqual(jasmine.objectContaining({ departmentId: '', productionLineId: '' }));
    component.selectStageEditDepartment(department.id);
    expect(component.stageEditForm.controls.productionLineId.value).toBe('');
    expect(component.stageFiltersForm.getRawValue()).toEqual({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
  });

  it('ignores row A hydration responses after row B is opened', () => {
    const rowADepartments = new Subject<typeof component.departments>();
    const rowB = { ...stage, id: 'stage-b', name: 'مرحلة B', code: 'STG-B', capacity: 7 };
    api.departments.and.returnValues(rowADepartments, of([department]));

    component.editOperationalStage(stage);
    component.editOperationalStage(rowB);
    rowADepartments.next([]);
    rowADepartments.complete();

    expect(component.editStageId).toBe(rowB.id);
    expect(component.stageEditForm.getRawValue()).toEqual({
      factoryId: factory.id,
      departmentId: department.id,
      productionLineId: line.id,
      name: rowB.name,
      capacity: rowB.capacity
    });
    expect(component.selectedStage?.code).toBe(rowB.code);
  });

  it('loads only the selected line operational stages including legacy-parent stages', () => {
    component.stageFiltersForm.patchValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });

    component.loadOperationalStages();

    expect(api.operationalStages).toHaveBeenCalledWith({ productionLineId: line.id, isActive: undefined, includeInactive: true });
    expect(component.operationalStages).toEqual([stage]);
  });

  it('builds the stage filter tree from factories, departments, and lines without loading stages', () => {
    component.reload();

    expect(api.factories).toHaveBeenCalled();
    expect(api.departments).toHaveBeenCalledWith(undefined, false);
    expect(api.allProductionLines).toHaveBeenCalled();
    expect(api.operationalStages).not.toHaveBeenCalled();
    expect(component.stageFilterTreeNodes[0].data?.entityType).toBe('factory');
    expect(component.stageFilterTreeNodes[0].children?.[0].data?.entityType).toBe('department');
    expect(component.stageFilterTreeNodes[0].children?.[0].children?.[0].data?.entityType).toBe('line');
  });

  it('does not filter operational stages when selecting a factory node', () => {
    component.reload();
    const factoryNode = component.stageFilterTreeNodes[0];

    component.selectStageFilterNode(factoryNode);

    expect(component.selectedStageFilterNode).toBeNull();
    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: '', departmentId: '', productionLineId: '' }));
    expect(api.operationalStages).not.toHaveBeenCalled();
    expect(component.stageFilterResetKey).toContain('all:all');
  });

  it('does not filter operational stages when selecting a department node', () => {
    component.reload();
    const departmentNode = component.stageFilterTreeNodes[0].children![0];

    component.selectStageFilterNode(departmentNode as any);

    expect(component.selectedStageFilterNode).toBeNull();
    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: '', departmentId: '', productionLineId: '' }));
    expect(api.operationalStages).not.toHaveBeenCalled();
  });

  it('filters operational stages by the selected line node and keeps add-stage context', () => {
    component.reload();
    const lineNode = component.stageFilterTreeNodes[0].children![0].children![0];

    component.selectStageFilterNode(lineNode as any);

    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id }));
    expect(api.operationalStages).toHaveBeenCalledWith({ productionLineId: line.id, isActive: undefined, includeInactive: true });
    expect(component.selectedStageFilterPath).toBe('مصنع الملابس / القص / خط القص');
  });

  it('clears the stage tree filter without changing the stage search box', () => {
    component.reload();
    component.stageSearch = 'STG001';
    component.selectStageFilterNode(component.stageFilterTreeNodes[0]);

    component.clearStageTreeFilter();

    expect(component.selectedStageFilterNode).toBeNull();
    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: '', departmentId: '', productionLineId: '' }));
    expect(component.operationalStages).toEqual([]);
    expect(component.stageSearch).toBe('STG001');
    expect(component.stageFilterResetKey).toContain('all:all');
  });

  it('searches the stage filter tree by name and code while preserving ancestor paths', () => {
    component.reload();

    component.onStageFilterSearch('L1');
    expect(component.visibleStageFilterTreeNodes[0].expanded).toBeTrue();
    expect(component.visibleStageFilterTreeNodes[0].children![0].expanded).toBeTrue();
    expect(component.visibleStageFilterTreeNodes[0].children![0].children![0].data?.entityId).toBe(line.id);

    component.onStageFilterSearch('القص');
    expect(component.visibleStageFilterTreeNodes[0].children![0].data?.entityId).toBe(department.id);
  });

  it('renders the stage filters as one compact grid with the search field as the wide column', () => {
    component.loading = false;
    component.reload();
    component.selectStageFilterNode(component.stageFilterTreeNodes[0].children![0].children![0] as any);
    fixture.detectChanges();

    const filterRow = fixture.nativeElement.querySelector('.sf') as HTMLElement;
    const treeFilter = filterRow.querySelector('.sf-tree') as HTMLElement;
    const statusFilter = filterRow.querySelector('.sf-st') as HTMLElement;
    const searchFilter = filterRow.querySelector('.sf-s') as HTMLElement;
    const clearButton = filterRow.querySelector('.sf-clear') as HTMLElement;

    expect(filterRow.children[0]).toBe(treeFilter);
    expect(filterRow.children[1]).toBe(statusFilter);
    expect(filterRow.children[2]).toBe(searchFilter);
    expect(filterRow.children[3]).toBe(clearButton);
    expect(getComputedStyle(filterRow).display).toBe('grid');
    expect(getComputedStyle(searchFilter).minWidth).toBe('0px');
  });

  it('shows the selected hierarchy path under the selected node name and keeps clear controls secondary', () => {
    component.loading = false;
    component.reload();
    component.selectStageFilterNode(component.stageFilterTreeNodes[0].children![0].children![0] as any);
    fixture.detectChanges();

    const trigger = fixture.nativeElement.querySelector('.sf-tr') as HTMLElement;
    const selectedName = trigger.querySelector('strong') as HTMLElement;
    const selectedPath = trigger.querySelector('small') as HTMLElement;
    const scopedClear = fixture.nativeElement.querySelector('.sf-x') as HTMLElement;
    const allClear = fixture.nativeElement.querySelector('.sf-clear') as HTMLElement;

    expect(selectedName.textContent?.trim()).toBe(line.name);
    expect(selectedPath.textContent?.trim()).toBe('مصنع الملابس / القص / خط القص');
    expect(scopedClear.classList).toContain('p-button-sm');
    expect(scopedClear.classList).toContain('p-button-text');
    expect(allClear.classList).toContain('p-button-sm');
    expect(allClear.classList).toContain('p-button-outlined');
  });

  it('clears all visible stage filters from the compact filter row', () => {
    component.reload();
    component.selectStageFilterNode(component.stageFilterTreeNodes[0]);
    component.stageStatusFilter = 'inactive';
    component.stageSearch = 'STG';

    component.clearStageFilters();

    expect(component.selectedStageFilterNode).toBeNull();
    expect(component.stageStatusFilter).toBe('all');
    expect(component.stageSearch).toBe('');
    expect(component.operationalStages).toEqual([]);
  });

  it('clears the complete stage context when its selected factory no longer exists after a realtime refresh', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    component.departments = [department];
    component.lines = [line];
    component.operationalStages = [stage];
    api.factories.and.returnValue(of([]));

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: '', departmentId: '', productionLineId: '' }));
    expect(component.departments).toEqual([]);
    expect(component.lines).toEqual([]);
    expect(component.operationalStages).toEqual([]);
  });

  it('clears the selected department and dependent line when that department no longer exists after a realtime refresh', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    component.lines = [line];
    component.operationalStages = [stage];
    api.departments.and.returnValue(of([]));

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: '', productionLineId: '' }));
    expect(component.lines).toEqual([]);
    expect(component.operationalStages).toEqual([]);
  });

  it('clears the selected line and its stages when that line no longer exists after a realtime refresh', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    component.operationalStages = [stage];
    api.allProductionLines.and.returnValue(of([]));

    (component as never as { refreshStagesFromRealtime(): void }).refreshStagesFromRealtime();

    expect(component.stageFiltersForm.getRawValue()).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: department.id, productionLineId: '' }));
    expect(component.operationalStages).toEqual([]);
  });

  it('creates a stage from its direct production-line context without legacy ordering input', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id });
    component.stageEditForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id, name: 'تشطيب', capacity: 3 });

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

  it('toggles only the selected ProductModelStage after the server confirms the saved state', () => {
    const target = { id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    const untouched = { id: 'model-stage-2', productModelId: firstModel.id, subStageId: englishStage.id, stageOrder: 2, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    const saved = { ...target, isActive: false };
    component.selected = firstModel;
    component.stages = [target, untouched];
    api.updateModelStage.and.returnValue(of(saved));

    component.toggleModelStage(target);

    expect(api.updateModelStage).toHaveBeenCalledOnceWith(firstModel.id, target.id, { isActive: false }, 'local-correlation');
    expect(component.stages).toEqual([saved, untouched]);
    expect(component.isModelStageSaving(target.id)).toBeFalse();
  });

  it('reactivates an inactive ProductModelStage through its relationship id', () => {
    const inactive = { id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: false };
    const saved = { ...inactive, isActive: true };
    component.selected = firstModel;
    component.stages = [inactive];
    api.updateModelStage.and.returnValue(of(saved));

    component.toggleModelStage(inactive);

    expect(api.updateModelStage).toHaveBeenCalledOnceWith(firstModel.id, inactive.id, { isActive: true }, 'local-correlation');
    expect(component.stages).toEqual([saved]);
  });

  it('does not optimistically change a model-stage toggle and blocks duplicate clicks for that row', () => {
    const pending = new Subject<any>();
    const target = { id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    component.selected = firstModel;
    component.stages = [target];
    api.updateModelStage.and.returnValue(pending);

    component.toggleModelStage(target);
    component.toggleModelStage(target);

    expect(api.updateModelStage).toHaveBeenCalledTimes(1);
    expect(component.isModelStageSaving(target.id)).toBeTrue();
    expect(component.stages).toEqual([target]);
    pending.next({ ...target, isActive: false });
    pending.complete();
    expect(component.isModelStageSaving(target.id)).toBeFalse();
    expect(component.stages).toEqual([{ ...target, isActive: false }]);
  });

  [404, 409, 0].forEach(status => it(`keeps the saved model-stage state when toggle request fails with ${status}`, () => {
    const target = { id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    component.selected = firstModel;
    component.stages = [target];
    api.updateModelStage.and.returnValue(throwError(() => new HttpErrorResponse({ status, statusText: status === 0 ? 'Network Error' : 'Request failed' })));

    component.toggleModelStage(target);

    expect(component.stages).toEqual([target]);
    expect(component.isModelStageSaving(target.id)).toBeFalse();
    expect(component.error).toContain(status === 404 ? 'لم تعد موجودة' : status === 409 ? 'تعارض' : 'تعذر الاتصال');
  }));

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

    expect(api.modelSearchPage).toHaveBeenCalledWith('  موديل  ', 1, 10, 'all');
    expect(component.models).toEqual([firstModel]);
    expect(component.modelTotal).toBe(51);
    expect(component.modelPage).toBe(1);
  }));

  it('loads the requested server page and does not locally filter its rows', () => {
    component.models = [firstModel, secondModel];
    component.modelPage = 1;
    component.modelListSearch = 'Jacket';
    api.modelSearchPage.and.returnValue(of({ items: [secondModel], totalCount: 11, pageNumber: 2, pageSize: 10 }));

    component.onModelLazyLoad({ first: 10, rows: 10 });

    expect(api.modelSearchPage).toHaveBeenCalledWith('Jacket', 2, 10, 'all');
    expect(component.models).toEqual([secondModel]);
    expect(component.modelTotal).toBe(11);
    expect(component.modelPage).toBe(2);
  });

  it('keeps model and stage searches independent when either value is cleared', () => {
    component.onStageSearch('قص');
    component.onModelSearch('MOD-001');

    expect(component.stageSearch).toBe('قص');
    expect(component.modelListSearch).toBe('MOD-001');
    component.onModelSearch('');
    expect(component.stageSearch).toBe('قص');
    component.onStageSearch('');
    expect(component.modelListSearch).toBe('');
  });

  it('keeps model-list and model-stage searches independent and clears only the stage search when selecting another model', () => {
    component.modelListSearch = 'MOD';
    component.modelStageSearch = 'STG';
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
    api.modelStages.and.returnValue(of([]));

    component.onModelStageSearch('غير موجود');

    expect(component.modelListSearch).toBe('MOD');
    expect(component.filteredLinkedStages).toEqual([]);
    expect(component.linkedStagesEmptyMessage).toContain('لا توجد نتائج مطابقة للبحث');

    component.select(secondModel);

    expect(component.modelListSearch).toBe('MOD');
    expect(component.modelStageSearch).toBe('');
  });

  it('groups the selected model journey by production line and sorts stages by StageOrder', () => {
    const sewingLine = { ...line, id: 'line-2', lineCode: 'L2', name: 'خط الخياطة', sequenceOrder: 2 };
    const sewingStage = { ...englishStage, id: 'stage-2', productionLineId: sewingLine.id, productionLineName: sewingLine.name, sequenceOrder: 2 };
    component.factories = [factory];
    component.departments = [department];
    component.lines = [sewingLine, line];
    const cache = (component as unknown as { availableStageOptionCache: Map<string, typeof stage> }).availableStageOptionCache;
    cache.set(stage.id, stage);
    cache.set(sewingStage.id, sewingStage);
    component.stages = [
      { id: 'map-3', subStageId: sewingStage.id, stageOrder: 3, piecePrice: 2, standardSeconds: 30, compensationMode: 'SharedPercentage', isRequired: true, isActive: true },
      { id: 'map-2', subStageId: stage.id, stageOrder: 2, piecePrice: 1, standardSeconds: 20, compensationMode: 'FixedAmount', isRequired: true, isActive: true },
      { id: 'map-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 10, compensationMode: 'FixedAmount', isRequired: true, isActive: true }
    ];

    expect(component.modelJourneyGroups.map(group => group.lineId)).toEqual([line.id, sewingLine.id]);
    expect(component.modelJourneyGroups[0].stages.map(item => item.stageOrder)).toEqual([1, 2]);
    expect(component.modelJourneyGroups[0].structurePath).toContain(factory.name);
    expect(component.modelJourneyGroups[0].structurePath).toContain(department.nameAr);
    expect(component.modelJourneyGroups[0].structurePath).toContain(line.name);
  });

  it('filters models by the selected structure scope and clears all model filters', () => {
    component.factories = [factory];
    component.departments = [department];
    component.lines = [line];
    component.models = [firstModel, secondModel];
    component.modelFilterTreeNodes = buildTreeForTest();
    component.selectedModelFilterNode = component.modelFilterTreeNodes[0].children![0].children![0];
    const membership = (component as unknown as { modelLineMembership: Map<string, Set<string>> }).modelLineMembership;
    membership.set(firstModel.id, new Set([line.id]));
    membership.set(secondModel.id, new Set(['another-line']));

    expect(component.filteredModels).toEqual([firstModel]);
    component.modelListSearch = 'MOD';
    component.modelStatusFilter = 'inactive';
    component.clearModelFilters();
    expect(component.selectedModelFilterNode).toBeNull();
    expect(component.modelListSearch).toBe('');
    expect(component.modelStatusFilter).toBe('all');
  });

  it('reports the model-search empty state when the server returns no matching models', () => {
    component.modelListSearch = 'موديل غير موجود';
    component.models = [];

    expect(component.modelEmptyMessage).toBe('لا توجد موديلات مطابقة للبحث.');
  });

  it('loads a bounded server-side catalog and excludes already linked stages', () => {
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
    api.searchSubStagesByNameOrCode.and.returnValue(of({ items: [stage, englishStage], totalCount: 2, pageNumber: 1, pageSize: 200 }));

    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();

    expect(api.searchSubStagesByNameOrCode).toHaveBeenCalledWith('', 1, 200);
    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id]);
  });

  it('applies the shared full-width searchable select contract to the model-stage dropdown', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    component.selected = firstModel;
    component.availableStageOptions = [stage];
    fixture.detectChanges();

    const dropdown = fixture.nativeElement.querySelector('p-dropdown') as HTMLElement;
    expect(dropdown.getAttribute('styleclass')).toContain('app-full-width-select');
    expect(dropdown.getAttribute('styleclass')).toContain('app-searchable-select');
    expect(dropdown.getAttribute('panelstyleclass')).toContain('app-searchable-select-panel');
    expect(dropdown.getAttribute('filterplaceholder')).toBe('ابحث باسم أو كود المرحلة');
  });

  it('clears a selected stage without opening the dropdown and keeps long text in the label lane', () => {
    const longStage = { ...stage, name: 'مرحلة تشغيل ذات اسم طويل جدًا للتحقق من عدم تداخل النص مع أزرار الحقل' };
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    component.selected = firstModel;
    component.availableStageOptions = [longStage];
    component.modelStageForm.controls.subStageId.setValue(longStage.id);
    const onShow = spyOn(component, 'syncStageDropdownPanelWidth').and.callThrough();
    fixture.detectChanges();

    const dropdown = fixture.nativeElement.querySelector('.p-dropdown') as HTMLElement;
    const label = dropdown.querySelector('.p-dropdown-label') as HTMLElement;
    const clear = dropdown.querySelector('.p-dropdown-clear-icon') as HTMLElement;
    const trigger = dropdown.querySelector('.p-dropdown-trigger') as HTMLElement;

    expect(clear).not.toBeNull();
    expect(trigger).not.toBeNull();
    expect(getComputedStyle(dropdown).display).toBe('grid');
    expect(getComputedStyle(label).minWidth).toBe('0px');
    expect(getComputedStyle(clear).position).toBe('static');

    clear.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(component.modelStageForm.controls.subStageId.value).toBeNull();
    expect(onShow).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.p-dropdown-panel')).toBeNull();
    expect(fixture.nativeElement.querySelector('.p-dropdown-clear-icon')).toBeNull();
  });

  it('keeps the model-stage overlay at least as wide as its trigger and caps it to the viewport', () => {
    const trigger = document.createElement('div');
    const input = document.createElement('input');
    trigger.className = 'p-dropdown';
    input.id = 'modelStageSubStage';
    trigger.appendChild(input);
    document.body.appendChild(trigger);
    spyOn(trigger, 'getBoundingClientRect').and.returnValue({ width: 436 } as DOMRect);

    component.syncStageDropdownPanelWidth();

    expect(component.stageDropdownPanelStyle).toEqual(jasmine.objectContaining({
      width: '436px',
      minWidth: '436px',
      maxWidth: 'calc(100vw - 1rem)',
      boxSizing: 'border-box'
    }));
    trigger.remove();
  });

  it('renders the available-stage hierarchy path without changing the stage relationship', () => {
    expect(component.stageStructurePath(stage)).toBe('مصنع الملابس ← القص ← خط القص');
    expect(component.stageStructurePath({ factoryName: null, departmentNameAr: 'القص', productionLineName: 'خط القص' })).toBe('القص ← خط القص');
  });

  it('filters the dropdown by name and code without mixing linked stages', () => {
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, englishStage];
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
    (component as never as { rebuildAvailableStageOptions(): void }).rebuildAvailableStageOptions();

    component.onAvailableStagesFilter('cutting');
    expect(api.searchSubStagesByNameOrCode).toHaveBeenCalledWith('cutting', 1, 200);
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, englishStage];
    (component as never as { rebuildAvailableStageOptions(): void }).rebuildAvailableStageOptions();
    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id]);
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
    api.searchSubStagesByNameOrCode.and.returnValues(stale, current);

    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();
    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();
    current.next({ items: [englishStage], totalCount: 1, pageNumber: 1, pageSize: 200 });
    stale.next({ items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 });

    expect(component.availableStageChoices.map(item => item.id)).toEqual([englishStage.id]);
  });

  it('shows scoped available-stage error state and retries catalog loading', () => {
    api.searchSubStagesByNameOrCode.and.returnValues(
      throwError(() => new Error('انقطع الاتصال')),
      of({ items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 })
    );
    (component as never as { loadAvailableStageCatalog(): void }).loadAvailableStageCatalog();
    expect(component.availableStagesError).toBe('انقطع الاتصال');

    component.retryAvailableStages();
    expect(component.availableStagesError).toBe('');
    expect(component.availableStageChoices).toEqual([stage]);
  });

  it('searches linked model stages by name and code, resets the table key, and keeps StageOrder', () => {
    const later = { id: 'model-stage-2', subStageId: englishStage.id, subStageCode: englishStage.code, subStageName: englishStage.name, stageOrder: 2, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    const first = { id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    component.stages = [later, first];
    component.onModelStageSearch('CUT-02');
    expect(component.filteredLinkedStages.map(item => item.id)).toEqual([later.id]);
    component.onModelStageSearch('');
    expect(component.filteredLinkedStages.map(item => item.id)).toEqual([first.id, later.id]);
    component.clearModelStageSearch();
    expect(component.modelStageSearch).toBe('');
  });

  it('keeps the linked-stage search independent from the available-stage search and reports a no-match empty state', () => {
    component.stages = [{ id: 'model-stage-1', subStageId: stage.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
    component.onAvailableStagesFilter('CUT-02');
    component.onModelStageSearch('غير موجود');

    expect(component.availableStagesSearch).toBe('CUT-02');
    expect(component.filteredLinkedStages).toEqual([]);
    expect(component.linkedStagesEmptyMessage).toBe('توجد مراحل مرتبطة، لكن لا توجد نتائج مطابقة للبحث.');
  });
});

function buildTreeForTest() {
  return [{
    key: 'factory:factory-1', data: { entityId: 'factory-1', entityType: 'factory' as const, name: 'مصنع الملابس', code: 'FAC', isActive: true, source: { id: 'factory-1', code: 'FAC', name: 'مصنع الملابس', isActive: true }, canDelete: false }, children: [{
      key: 'department:department-1', data: { entityId: 'department-1', entityType: 'department' as const, parentId: 'factory-1', name: 'القص', code: 'CUT', isActive: true, source: { id: 'department-1', factoryId: 'factory-1', nameAr: 'القص', isActive: true }, canDelete: false }, children: [{
        key: 'line:line-1', leaf: true, data: { entityId: 'line-1', entityType: 'line' as const, parentId: 'department-1', name: 'خط القص', code: 'L1', isActive: true, source: { id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط القص', sequenceOrder: 1, isActive: true }, canDelete: false }
      }]
    }]
  }];
}
