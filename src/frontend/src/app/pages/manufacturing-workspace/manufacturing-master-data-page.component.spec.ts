import { CommonModule } from '@angular/common';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ContextMenuModule } from 'primeng/contextmenu';
import { TableModule } from 'primeng/table';
import { TreeModule } from 'primeng/tree';
import { of } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { ManufacturingMasterDataApiService, ModelStageItem, SubStageOption } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { PermissionService } from '../../core/services/permission.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent department stage catalog', () => {
  let fixture: ComponentFixture<ManufacturingMasterDataPageComponent>;
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;

  const factory = { id: 'factory-1', code: 'FAC', name: 'مصنع الملابس', isActive: true };
  const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'القص', isActive: true };
  const otherDepartment = { ...department, id: 'department-2', code: 'SEW', nameAr: 'الخياطة' };
  const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, name: 'خط القص', lineCode: 'CUT-1', sequenceOrder: 1, isActive: true };
  const otherLine = { id: 'line-2', factoryId: factory.id, departmentId: otherDepartment.id, name: 'خط الخياطة', lineCode: 'SEW-1', sequenceOrder: 1, isActive: true };
  const stage: SubStageOption = {
    id: 'stage-1',
    mainStageId: 'main-1',
    mainStageName: 'التجهيز',
    departmentId: department.id,
    factoryId: factory.id,
    factoryName: factory.name,
    departmentNameAr: department.nameAr,
    name: 'تجهيز',
    code: 'STG001',
    capacity: 2,
    sequenceOrder: 1,
    isActive: true
  };
  const otherStage: SubStageOption = { ...stage, id: 'stage-2', departmentId: otherDepartment.id, departmentNameAr: otherDepartment.nameAr, code: 'STG002' };
  const model = { id: 'model-1', code: 'MOD-001', name: 'موديل القميص', isActive: true };
  const otherModel = { id: 'model-2', code: 'MOD-002', name: 'موديل آخر', isActive: true };
  const relationship: ModelStageItem = {
    id: 'model-stage-1',
    productModelId: model.id,
    productionLineId: line.id,
    subStageId: stage.id,
    departmentId: department.id,
    subStageCode: stage.code,
    subStageName: stage.name,
    stageOrder: 1,
    piecePrice: 1,
    standardSeconds: 20,
    compensationMode: 'SharedPercentage',
    isRequired: true,
    isActive: true
  };

  beforeEach(async () => {
    api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories', 'departments', 'allProductionLines', 'operationalStages', 'createOperationalStage', 'updateOperationalStage',
      'stageDependencies', 'deactivateOperationalStage', 'deleteOperationalStage', 'models', 'modelSearchPage',
      'allSubStages', 'modelStages', 'createModel', 'updateModel', 'setModelActivation', 'deleteModel',
      'modelDeleteEligibility', 'addModelStage', 'updateModelStage', 'copyModelStages'
    ]);
    api.factories.and.returnValue(of([factory]));
    api.departments.and.returnValue(of([department, otherDepartment]));
    api.allProductionLines.and.returnValue(of([line, otherLine]));
    api.operationalStages.and.returnValue(of([stage]));
    api.createOperationalStage.and.returnValue(of(stage));
    api.updateOperationalStage.and.returnValue(of(stage));
    api.stageDependencies.and.returnValue(of({ stageId: stage.id, activeBlockers: [], historicalDependencies: [], canDisable: true, canDelete: true, disableMessageAr: '', deleteMessageAr: '' }));
    api.deactivateOperationalStage.and.returnValue(of({ ...stage, isActive: false }));
    api.deleteOperationalStage.and.returnValue(of(void 0));
    api.models.and.returnValue(of([model, otherModel]));
    api.modelSearchPage.and.returnValue(of({ items: [model, otherModel], totalCount: 2, pageNumber: 1, pageSize: 10 }));
    api.allSubStages.and.returnValue(of([stage, otherStage]));
    api.modelStages.and.returnValue(of([relationship]));
    api.createModel.and.returnValue(of(model));
    api.updateModel.and.returnValue(of(model));
    api.setModelActivation.and.returnValue(of(void 0));
    api.deleteModel.and.returnValue(of(void 0));
    api.modelDeleteEligibility.and.returnValue(of({ modelId: model.id, canDelete: true, messageAr: 'يمكن الحذف.' }));
    api.addModelStage.and.returnValue(of(relationship));
    api.updateModelStage.and.returnValue(of(relationship));
    api.copyModelStages.and.returnValue(of({ sourceFactoryId: factory.id, sourceDepartmentId: department.id, sourceProductionLineId: line.id, sourceProductModelId: model.id, targetFactoryId: factory.id, targetDepartmentId: otherDepartment.id, targetProductionLineId: otherLine.id, targetProductModelId: otherModel.id, isPreview: true, requestedCount: 1, addedCount: 1, skippedCount: 0, failedCount: 0, addedStageIds: [], plannedStages: [], skippedStages: [], failedStages: [], validationErrors: [] }));

    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('ManufacturingRealtimeService', ['watchScreen', 'registerLocalOperation']);
    realtime.watchScreen.and.returnValue(() => undefined);
    realtime.registerLocalOperation.and.returnValue('local-correlation');

    await TestBed.configureTestingModule({
      declarations: [ManufacturingMasterDataPageComponent],
      imports: [CommonModule, FormsModule, ReactiveFormsModule, TableModule, TreeModule, ContextMenuModule, NoopAnimationsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: api },
        { provide: ManufacturingRealtimeService, useValue: realtime },
        { provide: PermissionService, useValue: { hasPermission: (permission: string) => permission === PERMISSIONS.models.manage || permission === PERMISSIONS.stages.manage || permission === PERMISSIONS.stages.delete } },
        { provide: ActivatedRoute, useValue: { snapshot: { routeConfig: { path: 'stages' } } } }
      ],
      schemas: [NO_ERRORS_SCHEMA]
    }).compileComponents();

    fixture = TestBed.createComponent(ManufacturingMasterDataPageComponent);
    component = fixture.componentInstance;
  });

  function selectDepartmentModelContext(): void {
    (component as unknown as { mode: 'stages' | 'models' }).mode = 'models';
    component.factories = [factory];
    component.departments = [department, otherDepartment];
    component.lines = [line, otherLine];
    component.models = [model, otherModel];
    component.selected = model;
    (component as unknown as { availableStageCatalog: SubStageOption[] }).availableStageCatalog = [stage, otherStage];
    component.selectModelStageFactory(factory.id);
    component.selectModelStageModel(model.id);
    component.selectModelStageDepartment(department.id);
    component.selectedModelStageProductionLineId = line.id;
    component.stages = [relationship];
  }

  it('removes ProductionLine from stage filters and edit contracts', () => {
    expect(Object.keys(component.stageFiltersForm.controls)).toEqual(['factoryId', 'departmentId']);
    expect(Object.keys(component.stageEditForm.controls)).toEqual(['factoryId', 'departmentId', 'name', 'capacity']);
  });

  it('builds a Factory to Department stage tree without line nodes', () => {
    component.reload();

    expect(component.stageFilterTreeNodes.length).toBe(1);
    const departmentNode = component.stageFilterTreeNodes[0].children![0];
    expect(departmentNode.data?.entityType).toBe('department');
    expect(departmentNode.children).toBeUndefined();
    expect(departmentNode.leaf).toBeTrue();
  });

  it('loads stages by the selected Department and preserves status filtering', () => {
    component.reload();
    const departmentNode = component.stageFilterTreeNodes[0].children!.find(node => node.data?.entityId === department.id)!;
    component.selectStageFilterNode(departmentNode);

    expect(component.stageFiltersForm.getRawValue()).toEqual({ factoryId: factory.id, departmentId: department.id });
    expect(api.operationalStages).toHaveBeenCalledWith({ departmentId: department.id, isActive: undefined, includeInactive: true });
  });

  it('creates a stage from Department context only', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: department.id });
    component.stageEditForm.setValue({ factoryId: factory.id, departmentId: department.id, name: 'تشطيب', capacity: 3 });

    component.saveOperationalStage();

    expect(api.createOperationalStage).toHaveBeenCalledWith({ departmentId: department.id, name: 'تشطيب', capacity: 3 }, 'local-correlation');
  });

  it('hydrates edit context from the stage Department without changing the active filter', () => {
    component.stageFiltersForm.setValue({ factoryId: factory.id, departmentId: otherDepartment.id });
    component.departments = [department, otherDepartment];

    component.editOperationalStage(stage);

    expect(component.stageEditForm.getRawValue()).toEqual({ factoryId: factory.id, departmentId: department.id, name: stage.name, capacity: stage.capacity });
    expect(component.stageFiltersForm.getRawValue()).toEqual({ factoryId: factory.id, departmentId: otherDepartment.id });
  });

  it('requires a ProductionLine leaf to complete model-stage context', () => {
    selectDepartmentModelContext();
    (component as unknown as { rebuildModelStageContextTree(): void }).rebuildModelStageContextTree();

    expect(component.hasModelStageContext).toBeTrue();
    const departmentNode = component.modelStageContextNodes[0].children![0].children![0];
    expect(departmentNode.data.contextType).toBe('department');
    expect(departmentNode.leaf).toBeFalsy();
    expect(departmentNode.children![0].data.contextType).toBe('line');
    expect(departmentNode.children![0].leaf).toBeTrue();

    component.selectModelStageDepartment(department.id);
    expect(component.hasModelStageContext).toBeFalse();
  });

  it('shows only catalog stages owned by the selected Department', () => {
    selectDepartmentModelContext();

    expect(component.availableModelStageRows.map(row => row.stage.id)).toEqual([stage.id]);
    expect(component.availableModelStageRows[0].relationship?.id).toBe(relationship.id);
  });

  it('uses explicit source and target line scope in bulk-copy preview', () => {
    selectDepartmentModelContext();
    component.stages = [relationship];
    component.toggleModelStageSelection(relationship.id, true);
    component.openBulkCopyDialog();
    component.setBulkCopyTargetModel(otherModel.id);
    component.setBulkCopyTargetDepartment(otherDepartment.id);
    component.setBulkCopyTargetLine(otherLine.id);

    component.submitBulkCopyDialog();

    expect(api.copyModelStages).toHaveBeenCalledWith(model.id, line.id, {
      sourceFactoryId: factory.id,
      sourceDepartmentId: department.id,
      sourceProductionLineId: line.id,
      targetModelId: otherModel.id,
      targetFactoryId: factory.id,
      targetDepartmentId: otherDepartment.id,
      targetProductionLineId: otherLine.id,
      sourceProductModelStageIds: [relationship.id],
      previewOnly: true
    }, 'local-correlation');
  });

  it('rejects the same model and ProductionLine as a bulk-copy target', () => {
    selectDepartmentModelContext();
    component.stages = [relationship];
    component.toggleModelStageSelection(relationship.id, true);
    component.openBulkCopyDialog();
    component.setBulkCopyTargetModel(model.id);
    component.setBulkCopyTargetDepartment(department.id);
    component.setBulkCopyTargetLine(line.id);

    expect(component.bulkCopyTargetSameAsSource).toBeTrue();
    expect(component.bulkCopyCanPreview).toBeFalse();
  });

  it('renders Department language and no line-owned stage controls', () => {
    component.reload();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('القسم');
    expect(text).not.toContain('اختر خط الإنتاج أولًا');
    expect((fixture.nativeElement as HTMLElement).querySelector('[formControlName="productionLineId"]')).toBeNull();
  });
});
