import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;

  const factory = { id: 'factory-1', code: 'FAC', name: 'مصنع الملابس', isActive: true };
  const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'القص', isActive: true };
  const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, name: 'خط القص', lineCode: 'L1', sequenceOrder: 1, isActive: true };
  const stage = { id: 'stage-1', mainStageId: 'legacy-group-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, factoryName: factory.name, departmentNameAr: department.nameAr, productionLineName: line.name, name: 'تجهيز', code: 'STG001', capacity: 2, sequenceOrder: 1, isActive: true };

  beforeEach(async () => {
    api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories', 'departments', 'productionLinesForDepartment', 'operationalStages', 'createOperationalStage', 'updateOperationalStage',
      'stageDependencies', 'deactivateOperationalStage', 'deleteOperationalStage', 'models', 'searchSubStages', 'modelStages',
      'createModel', 'updateModel', 'setModelActivation', 'addModelStage', 'updateModelStage', 'deactivateModelStage'
    ]);
    api.factories.and.returnValue(of([factory]));
    api.departments.and.returnValue(of([department]));
    api.productionLinesForDepartment.and.returnValue(of([line]));
    api.operationalStages.and.returnValue(of([stage]));
    api.createOperationalStage.and.returnValue(of(stage));
    api.updateOperationalStage.and.returnValue(of(stage));
    api.stageDependencies.and.returnValue(of({ stageId: stage.id, activeBlockers: [], historicalDependencies: [], canDisable: true, canDelete: true, disableMessageAr: '', deleteMessageAr: '' }));
    api.deactivateOperationalStage.and.returnValue(of(void 0));
    api.deleteOperationalStage.and.returnValue(of(void 0));
    api.models.and.returnValue(of([]));
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

  it('creates a stage from its direct production-line context without a legacy-group selection or code', () => {
    component.stageForm.setValue({ factoryId: factory.id, departmentId: department.id, productionLineId: line.id, name: 'تشطيب', capacity: 3, defaultOrder: 2 });

    component.saveOperationalStage();

    expect(api.createOperationalStage).toHaveBeenCalledWith({ productionLineId: line.id, name: 'تشطيب', capacity: 3, defaultOrder: 2 });
  });

  it('keeps model journey price, seconds, and compensation fields on the model-stage form', () => {
    expect(component.modelStageForm.controls.piecePrice).toBeDefined();
    expect(component.modelStageForm.controls.standardSeconds).toBeDefined();
    expect(component.modelStageForm.controls.compensationMode).toBeDefined();
  });
});
