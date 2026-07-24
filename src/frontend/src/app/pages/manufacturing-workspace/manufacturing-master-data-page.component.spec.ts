import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { DropdownModule } from 'primeng/dropdown';
import { TableModule } from 'primeng/table';
import { TooltipModule } from 'primeng/tooltip';
import { TreeModule } from 'primeng/tree';
import { ContextMenuModule } from 'primeng/contextmenu';
import { Subject, of, throwError } from 'rxjs';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let fixture: ComponentFixture<ManufacturingMasterDataPageComponent>;
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;
  let realtime: jasmine.SpyObj<ManufacturingRealtimeService>;
  let grantedPermissions: Set<string>;

  const factory = { id: 'factory-1', code: 'FAC', name: 'مصنع الملابس', isActive: true };
  const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'القص', isActive: true };
  const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, name: 'خط القص', lineCode: 'L1', sequenceOrder: 1, isActive: true };
  const stage = { id: 'stage-1', mainStageId: 'legacy-group-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, factoryName: factory.name, departmentNameAr: department.nameAr, productionLineName: line.name, name: 'تجهيز', code: 'STG001', capacity: 2, sequenceOrder: 1, isActive: true };
  const englishStage = { ...stage, id: 'stage-2', name: 'Cutting Line', code: 'CUT-02', sequenceOrder: 2 };
  const firstModel = { id: 'model-1', code: 'MOD-001', name: 'موديل القميص', isActive: true };
  const secondModel = { id: 'model-2', code: 'MOD-002', name: 'Jacket Model', isActive: true };

  beforeEach(async () => {
    grantedPermissions = new Set([PERMISSIONS.models.manage, PERMISSIONS.stages.manage, PERMISSIONS.stages.delete]);
    api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories', 'departments', 'allProductionLines', 'productionLinesForDepartment', 'operationalStages', 'createOperationalStage', 'updateOperationalStage',
      'stageDependencies', 'deactivateOperationalStage', 'deleteOperationalStage', 'models', 'modelSearchPage', 'searchSubStages', 'allSubStages', 'modelStages',
      'createModel', 'updateModel', 'setModelActivation', 'deleteModel', 'modelDeleteEligibility', 'addModelStage', 'updateModelStage', 'deactivateModelStage'
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
    api.allSubStages.and.returnValue(of([stage, englishStage]));
    api.modelStages.and.returnValue(of([]));
    api.createModel.and.returnValue(of({ id: 'model-1', code: 'MOD', name: 'موديل', isActive: true }));
    api.updateModel.and.returnValue(of({ id: 'model-1', code: 'MOD', name: 'موديل', isActive: true }));
    api.setModelActivation.and.returnValue(of(void 0));
    api.deleteModel.and.returnValue(of(void 0));
    api.modelDeleteEligibility.and.returnValue(of({ modelId: firstModel.id, canDelete: true, messageAr: 'يمكن حذف الموديل من الكتالوج التشغيلي.' }));
    api.addModelStage.and.returnValue(of({ id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }));
    api.updateModelStage.and.returnValue(of({ id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }));
    api.deactivateModelStage.and.returnValue(of(void 0));
    realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('ManufacturingRealtimeService', ['watchScreen', 'registerLocalOperation']);
    realtime.watchScreen.and.returnValue(() => undefined);
    realtime.registerLocalOperation.and.returnValue('local-correlation');

    await TestBed.configureTestingModule({
      declarations: [ManufacturingMasterDataPageComponent],
      imports: [CommonModule, FormsModule, ReactiveFormsModule, DropdownModule, TableModule, TooltipModule, TreeModule, ContextMenuModule, NoopAnimationsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: api },
        { provide: ManufacturingRealtimeService, useValue: realtime },
        { provide: PermissionService, useValue: { hasPermission: (permission: string) => grantedPermissions.has(permission) } },
        { provide: ActivatedRoute, useValue: { snapshot: { routeConfig: { path: 'stages' } } } }
      ],
      schemas: [NO_ERRORS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(ManufacturingMasterDataPageComponent);
    component = fixture.componentInstance;
  });

  function selectSingleStageModelContext(): void {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    component.factories = [factory];
    component.models = [firstModel];
    component.departments = [department];
    component.lines = [line];
    component.selected = firstModel;
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage];
    component.selectModelStageFactory(factory.id);
    component.selectModelStageDepartment(department.id);
    component.selectModelStageProductionLine(line.id);
  }

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
    expect((api.updateModel.calls.mostRecent().args[1] as Record<string, unknown>)['productionLineId']).toBeUndefined();
    expect((api.updateModel.calls.mostRecent().args[1] as Record<string, unknown>)['departmentId']).toBeUndefined();
  });

  it('creates a product model from its general attributes without a production-line field', () => {
    component.modelForm.setValue({ code: 'MOD-INDEPENDENT', name: 'موديل مستقل', description: 'لا يتبع خطًا واحدًا' });

    component.saveModel();

    const request = api.createModel.calls.mostRecent().args[0] as unknown as Record<string, unknown>;
    expect(request).toEqual({ code: 'MOD-INDEPENDENT', name: 'موديل مستقل', description: 'لا يتبع خطًا واحدًا' });
    expect(request['productionLineId']).toBeUndefined();
    expect(request['departmentId']).toBeUndefined();
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
  });

  it('builds one nested factory → model → department → line context tree', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    const otherDepartment = { ...department, id: 'department-2', code: 'SEW', nameAr: 'الخياطة' };
    const otherLine = { ...line, id: 'line-2', departmentId: otherDepartment.id, lineCode: 'L2', name: 'خط الخياطة' };
    api.models.and.returnValue(of([firstModel, secondModel]));
    api.departments.and.returnValue(of([department, otherDepartment]));
    api.allProductionLines.and.returnValue(of([line, otherLine]));

    component.reload();

    const factoryNode = component.modelStageContextNodes[0];
    const modelNode = factoryNode.children![0];
    const departmentNode = modelNode.children![0];
    const lineNode = departmentNode.children![0];
    expect(factoryNode.data.contextType).toBe('factory');
    expect(modelNode.data.contextType).toBe('model');
    expect(departmentNode.data.contextType).toBe('department');
    expect(lineNode.data.contextType).toBe('line');
  });

  it('requires a final line node before the context is complete and resets dependent selections', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    api.models.and.returnValue(of([firstModel]));
    api.modelStages.and.returnValue(of([]));
    component.reload();
    const factoryNode = component.modelStageContextNodes[0];
    const modelNode = factoryNode.children![0];
    const departmentNode = modelNode.children![0];
    const lineNode = departmentNode.children![0];

    component.selectModelStageContextNode(factoryNode);
    expect(component.modelStageContextMessage).toBe('اختر موديلًا لعرض علاقات مراحله.');
    component.selectModelStageContextNode(modelNode);
    expect(component.modelStageContextMessage).toBe('اختر قسمًا تابعًا للمصنع.');
    component.selectModelStageContextNode(departmentNode);
    expect(component.modelStageContextMessage).toBe('اختر خط إنتاج تابعًا للقسم.');
    component.selectModelStageContextNode(lineNode);
    expect(component.hasModelStageContext).toBeTrue();

    component.selectModelStageContextNode(factoryNode);
    expect(component.selected).toBeNull();
    expect(component.selectedModelStageDepartmentId).toBe('');
    expect(component.selectedModelStageProductionLineId).toBe('');
  });

  it('shows only the selected line stages and all three relationship states', () => {
    const unlinkedStage = { ...englishStage, id: 'stage-2', productionLineId: line.id, productionLineName: line.name, sequenceOrder: 2 };
    const inactiveLinkStage = { ...englishStage, id: 'stage-3', productionLineId: line.id, productionLineName: line.name, sequenceOrder: 3 };
    const activeLink = { id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    const inactiveLink = { id: 'model-stage-2', subStageId: inactiveLinkStage.id, stageOrder: 2, piecePrice: 2, standardSeconds: 30, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: false };
    component.factories = [factory];
    component.models = [firstModel];
    component.departments = [department];
    component.lines = [line];
    component.selected = firstModel;
    component.stages = [activeLink, inactiveLink];
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, unlinkedStage, inactiveLinkStage];

    component.selectModelStageFactory(factory.id);
    component.selectModelStageDepartment(department.id);
    component.selectModelStageProductionLine(line.id);

    const rows = component.availableModelStageRows;
    expect(rows.map(row => component.modelStageRelationshipLabel(row))).toEqual(['مرتبطة وفعالة', 'غير مرتبطة', 'مرتبطة ومعطلة']);
    expect(rows.map(row => component.modelStageRelationshipStatus(row))).toEqual(['ready', 'info', 'warning']);
  });

  it('changing the selected line does not change product-stage relationships on other lines', () => {
    const secondLine = { ...line, id: 'line-2', lineCode: 'L2', name: 'خط الخياطة', sequenceOrder: 2 };
    const secondStage = { ...englishStage, id: 'stage-2', productionLineId: secondLine.id, productionLineName: secondLine.name, sequenceOrder: 1 };
    const cuttingLink = { id: 'model-stage-1', subStageId: stage.id, stageOrder: 1, piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    const sewingLink = { id: 'model-stage-2', subStageId: secondStage.id, stageOrder: 2, piecePrice: 2, standardSeconds: 30, compensationMode: 'SharedPercentage' as const, isRequired: true, isActive: true };
    component.factories = [factory];
    component.models = [firstModel];
    component.departments = [department];
    component.lines = [line, secondLine];
    component.selected = firstModel;
    component.stages = [cuttingLink, sewingLink];
    (component as never as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, secondStage];

    component.selectModelStageFactory(factory.id);
    component.selectModelStageDepartment(department.id);
    component.selectModelStageProductionLine(secondLine.id);

    expect(component.availableModelStageRows.map(row => row.stage.id)).toEqual([secondStage.id]);
    expect(component.stages.map(item => item.id)).toEqual([cuttingLink.id, sewingLink.id]);
  });

  it('keeps the model catalog independent from the selected context path', () => {
    component.factories = [factory];
    component.models = [firstModel, secondModel];
    component.departments = [department];
    component.lines = [line];

    component.selectModelStageFactory(factory.id);
    component.selectModelStageDepartment(department.id);
    component.selectModelStageProductionLine(line.id);

    expect(component.filteredModels).toEqual([firstModel, secondModel]);
  });

  it('renders a single table and a breadcrumb only after selecting a line tree node', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    api.models.and.returnValue(of([firstModel]));
    api.modelStages.and.returnValue(of([]));
    component.reload();
    const factoryNode = component.modelStageContextNodes[0];
    const lineNode = factoryNode.children![0].children![0].children![0];
    factoryNode.expanded = true;
    factoryNode.children![0].expanded = true;
    factoryNode.children![0].children![0].expanded = true;

    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('p-table').length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('p-tree').length).toBe(1);
    expect(fixture.nativeElement.querySelectorAll('p-dropdown').length).toBe(0);
    const contextTree = fixture.nativeElement.querySelector('.master-page__model-context') as HTMLElement;
    expect(contextTree.getAttribute('dir')).toBe('rtl');
    expect(getComputedStyle(contextTree).overflowX).toBe('clip');
    expect(contextTree.textContent).toContain('مصنع الملابس');
    expect(contextTree.querySelector('.pi-chevron-left')).not.toBeNull();
    (contextTree.querySelector('.p-tree-toggler') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(contextTree.querySelector('.pi-chevron-down')).not.toBeNull();
    expect(contextTree.textContent).toContain('MOD-001 — موديل القميص');

    component.selectModelStageContextNode(lineNode);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('p-table').length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('مصنع الملابس ← MOD-001 — موديل القميص ← القص ← خط القص');
  });

  function buildModelTree(): { factoryNode: any; modelNode: any; lineNode: any } {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    fixture.detectChanges();
    component.factories = [factory];
    component.models = [firstModel];
    component.departments = [department];
    component.lines = [line];
    (component as never as { rebuildModelStageContextTree: () => void }).rebuildModelStageContextTree();
    const factoryNode = component.modelStageContextNodes[0];
    const modelNode = factoryNode.children![0];
    const lineNode = modelNode.children![0].children![0];
    factoryNode.expanded = true;
    modelNode.expanded = true;
    modelNode.children![0].expanded = true;
    fixture.detectChanges();
    return { factoryNode, modelNode, lineNode };
  }

  it('keeps the line-stage table free of an ellipsis context-menu action', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    api.models.and.returnValue(of([firstModel]));
    fixture.detectChanges();
    selectSingleStageModelContext();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('button[aria-label="إجراءات المرحلة"]').length).toBe(0);
    const actionGroups = fixture.nativeElement.querySelectorAll('.master-page__model-stages-table .master-page__model-stage-row-actions');
    const addButton = fixture.nativeElement.querySelector('button[aria-label="إضافة المرحلة إلى الموديل"]') as HTMLButtonElement;
    expect(actionGroups.length).toBe(1);
    expect(addButton).not.toBeNull();
    expect(addButton.getAttribute('style')).toBeNull();
    expect(addButton.classList).toContain('p-button-outlined');
    expect(addButton.classList).toContain('plp-action-group__full');
  });

  it('uses matching two-column text actions for a linked model stage', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    api.models.and.returnValue(of([firstModel]));
    fixture.detectChanges();
    selectSingleStageModelContext();
    component.stages = [{
      id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1,
      piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true
    }];
    fixture.detectChanges();

    const group = fixture.nativeElement.querySelector('.master-page__model-stages-table .master-page__model-stage-row-actions') as HTMLElement;
    const edit = fixture.nativeElement.querySelector('button[aria-label="تعديل إعدادات الارتباط"]') as HTMLButtonElement;
    const toggle = fixture.nativeElement.querySelector('button[aria-label="تعطيل الارتباط بالموديل"]') as HTMLButtonElement;
    expect(group.classList).toContain('plp-action-group--equal-actions');
    expect(getComputedStyle(group).display).toBe('grid');
    expect(edit.getAttribute('label')).toBe('تعديل');
    expect(edit.classList).not.toContain('p-button-icon-only');
    expect(edit.getAttribute('style')).toBeNull();
    expect(toggle.getAttribute('style')).toBeNull();
  });

  it('defaults the line-stage relationship filter to all and updates its result count locally', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    fixture.detectChanges();
    selectSingleStageModelContext();
    (component as unknown as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, englishStage];
    component.stages = [{
      id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1,
      piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true
    }];
    const selectedTreeNode = { key: 'selected-line' } as any;
    component.selectedModelStageContextNode = selectedTreeNode;

    expect(component.modelStageRelationshipFilter).toBe('all');
    expect(component.filteredAvailableModelStageRows.length).toBe(2);
    component.setModelStageRelationshipFilter('linked');
    expect(component.filteredAvailableModelStageRows.map(row => row.stage.id)).toEqual([stage.id]);
    component.setModelStageRelationshipFilter('unlinked');
    expect(component.filteredAvailableModelStageRows.map(row => row.stage.id)).toEqual([englishStage.id]);
    expect(component.selectedModelStageContextNode).toBe(selectedTreeNode);

    fixture.detectChanges();
    const toolbar = fixture.nativeElement.querySelector('[aria-label="فلتر حالة مراحل الخط"]') as HTMLElement;
    expect(toolbar).not.toBeNull();
    expect(toolbar.closest('[dir="rtl"]')).not.toBeNull();
    expect(toolbar.getAttribute('style')).toBeNull();
    expect((toolbar.querySelector('plp-status-badge') as unknown as { label: string }).label).toBe('النتائج: 1');
  });

  it('uses the relationship-specific empty state without reloading the model tree', () => {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    fixture.detectChanges();
    selectSingleStageModelContext();
    const modelStageRequests = api.modelStages.calls.count();

    component.setModelStageRelationshipFilter('linked');
    expect(component.modelStageFilterEmptyMessage).toBe('لا توجد مراحل مرتبطة بهذا الموديل على الخط المختار.');
    component.setModelStageRelationshipFilter('unlinked');
    expect(component.modelStageFilterEmptyMessage).toBe('لا توجد مراحل غير مرتبطة متاحة على الخط المختار.');
    component.setModelStageRelationshipFilter('all');
    expect(component.modelStageFilterEmptyMessage).toBe('الخط المختار لا يحتوي مراحل.');
    expect(api.modelStages.calls.count()).toBe(modelStageRequests);
  });

  it('recalculates relationship-filtered rows when the selected line changes', () => {
    const secondLine = { ...line, id: 'line-2', name: 'خط القص الثاني', lineCode: 'L2' };
    const secondLineStage = { ...englishStage, id: 'stage-line-2', productionLineId: secondLine.id, productionLineName: secondLine.name };
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    fixture.detectChanges();
    component.factories = [factory];
    component.models = [firstModel];
    component.departments = [department];
    component.lines = [line, secondLine];
    component.selected = firstModel;
    (component as unknown as { availableStageCatalog: typeof stage[] }).availableStageCatalog = [stage, secondLineStage];
    component.stages = [{
      id: 'model-stage-1', productModelId: firstModel.id, subStageId: stage.id, stageOrder: 1,
      piecePrice: 1, standardSeconds: 20, compensationMode: 'SharedPercentage', isRequired: true, isActive: true
    }];
    component.selectModelStageFactory(factory.id);
    component.selectModelStageDepartment(department.id);
    component.selectModelStageProductionLine(line.id);
    component.setModelStageRelationshipFilter('linked');

    expect(component.filteredAvailableModelStageRows.length).toBe(1);
    component.selectModelStageProductionLine(secondLine.id);
    expect(component.modelStageRelationshipFilter).toBe('linked');
    expect(component.filteredAvailableModelStageRows.length).toBe(0);
    component.setModelStageRelationshipFilter('unlinked');
    expect(component.filteredAvailableModelStageRows.map(row => row.stage.id)).toEqual([secondLineStage.id]);
  });

  it('renders exactly one RTL model action button and the requested model actions', () => {
    const { modelNode } = buildModelTree();

    expect(component.canManageModels).withContext(`permissions=${[...grantedPermissions].join(',')}`).toBeTrue();
    expect(component.hasModelContextActions(modelNode)).withContext(`node=${JSON.stringify(modelNode.data)}`).toBeTrue();
    expect(fixture.nativeElement.querySelectorAll('button[aria-label="إجراءات الموديل"]').length).toBe(1);
    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    expect(component.modelContextMenuItems.map(item => item.label).filter(Boolean)).toEqual(['إضافة موديل', 'تعديل الموديل', 'حذف الموديل']);
    expect(component.modelContextMenuItems.some(item => item.separator)).toBeTrue();
    const tree = fixture.nativeElement.querySelector('.master-page__model-context') as HTMLElement;
    expect(tree.getAttribute('dir')).toBe('rtl');
  });

  it('opens the existing model form for add and edit without line or department ownership', () => {
    const { modelNode } = buildModelTree();
    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    const add = component.modelContextMenuItems.find(item => item.label === 'إضافة موديل');
    expect(add).toBeDefined();
    add?.command?.({} as never);
    expect(component.modelFormVisible).toBeTrue();
    expect(component.editModelId).toBe('');
    expect(Object.keys(component.modelForm.controls)).not.toContain('productionLineId');
    expect(Object.keys(component.modelForm.controls)).not.toContain('departmentId');

    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    component.modelContextMenuItems.find(item => item.label === 'تعديل الموديل')?.command?.({} as never);
    expect(component.editModelId).toBe(firstModel.id);
    expect(component.modelForm.getRawValue().name).toBe(firstModel.name);
  });

  it('asks for confirmation then soft-deletes an unlinked model and clears its selected context', () => {
    const { modelNode, lineNode } = buildModelTree();
    component.selectModelStageContextNode(lineNode);
    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    component.modelContextMenuItems.find(item => item.label === 'حذف الموديل')?.command?.({} as never);
    expect(component.modelDeleteDialogVisible).toBeTrue();
    expect(component.pendingModelDeletion?.name).toBe(firstModel.name);

    component.confirmModelDeletion();

    expect(api.deleteModel).toHaveBeenCalledWith(firstModel.id, 'local-correlation');
    expect(component.models).toEqual([]);
    expect(component.selected).toBeNull();
    expect(component.hasModelStageContext).toBeFalse();
  });

  it('keeps the delete dialog open and shows the backend dependency reason when deletion is blocked', () => {
    const { modelNode } = buildModelTree();
    api.modelDeleteEligibility.and.returnValue(of({ modelId: firstModel.id, canDelete: false, messageAr: 'لا يمكن حذف الموديل لأنه مرتبط بتشغيل إنتاج.' }));
    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    const deleteItem = component.modelContextMenuItems.find(item => item.label === 'حذف الموديل');

    expect(deleteItem?.disabled).toBeTrue();
    expect(deleteItem?.tooltip).toBe('لا يمكن حذف الموديل لأنه مرتبط بتشغيل إنتاج.');
    expect(component.models).toEqual([firstModel]);
  });

  it('hides the model action button without models.manage and does not change expanded state when opening it', () => {
    const { modelNode } = buildModelTree();
    const wasExpanded = modelNode.expanded;
    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    expect(modelNode.expanded).toBe(wasExpanded);
    grantedPermissions.clear();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('button[aria-label="إجراءات الموديل"]').length).toBe(0);
  });

  it('preserves the selected tree context and breadcrumb after editing the selected model', () => {
    const { modelNode, lineNode } = buildModelTree();
    component.selectModelStageContextNode(lineNode);
    api.updateModel.and.returnValue(of({ ...firstModel, name: 'موديل القميص المحدث' }));
    component.openModelContextMenu(new MouseEvent('click'), modelNode);
    component.modelContextMenuItems.find(item => item.label === 'تعديل الموديل')?.command?.({} as never);
    component.modelForm.patchValue({ name: 'موديل القميص المحدث' });
    component.saveModel();

    expect(component.selected?.name).toBe('موديل القميص المحدث');
    expect(component.selectedModelStageContextNode?.key).toBe(lineNode.key);
    expect(component.modelStageContextBreadcrumb).toContain('موديل القميص المحدث');
  });

  it('uses a native keyboard-focusable action trigger without inline styles or a page reload', () => {
    buildModelTree();
    const actionButton = fixture.nativeElement.querySelector('button[aria-label="إجراءات الموديل"]') as HTMLButtonElement;
    expect(actionButton.tagName).toBe('BUTTON');
    expect(actionButton.getAttribute('aria-haspopup')).toBe('menu');
    expect(actionButton.getAttribute('style')).toBeNull();
  });
});
