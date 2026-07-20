import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { By } from '@angular/platform-browser';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { AssignmentsApiService } from '../../core/services/assignments-api.service';
import { ManufacturingMasterDataApiService, SubStageOption } from '../../core/services/manufacturing-master-data-api.service';
import { WorkersApiService } from '../../core/services/workers-api.service';
import { SharedModule } from '../../shared/shared.module';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpTablePaginationDirective } from '../../shared/product/plp-table-pagination.directive';
import { PlpExpandableFormComponent } from '../../shared/product/plp-expandable-form.component';
import { PlpDialogComponent } from '../../shared/product/plp-dialog.component';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';

describe('FactoryStructureFoundationPageComponent', () => {
  function configure(options: { manage?: boolean; record?: boolean; fail?: boolean; empty?: boolean } = {}): ComponentFixture<FactoryStructureFoundationPageComponent> {
    const masterData = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories',
      'departments',
      'allProductionLines',
      'mainStagesForLine',
      'subStagesForMainStage',
      'allMainStages',
      'allSubStages',
      'createFactory',
      'updateFactory',
      'createProductionLine',
      'updateProductionLine',
      'createMain',
      'updateMain',
      'createSub',
      'updateSub',
      'createOperationalStage',
      'updateOperationalStage',
      'stageDependencies',
      'deactivateOperationalStage',
      'deleteOperationalStage'
    ]);
    const assignments = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', [
      'getFactoryStructureSubStageWorkers',
      'createFactoryStructureDefaultAssignment'
    ]);
    const workers = jasmine.createSpyObj<WorkersApiService>('WorkersApiService', [
      'loadFactoryStructureEligibleWorkers',
      'loadWorkers'
    ]);
    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.returnValue(Promise.resolve(true));
    const hydration = new BehaviorSubject<'ready'>('ready');
    const grantedPermissions: string[] = [
      ...(options.manage ? [PERMISSIONS.factoryStructure.manage] : []),
      ...(options.record ? [PERMISSIONS.production.view, PERMISSIONS.production.record] : [])
    ];
    const permissionService = {
      permissions$: of([]),
      hydrationState$: hydration.asObservable(),
      get hydrationState() { return 'ready'; },
      hasPermission: (permission: string) => grantedPermissions.includes(permission),
      hasAccess: (requirement: { permission?: string; requireAll?: string | string[] }) => {
        if (requirement.permission) return grantedPermissions.includes(requirement.permission);
        if (requirement.requireAll) return (Array.isArray(requirement.requireAll) ? requirement.requireAll : [requirement.requireAll]).every(permission => grantedPermissions.includes(permission));
        return true;
      }
    };

    if (options.fail) {
      masterData.factories.and.returnValue(throwError(() => new Error('Factory load failed')));
    } else {
      masterData.factories.and.returnValue(of(options.empty ? [] : [
        { id: 'fac-1', name: 'مصنع رئيسي', code: 'FAC-1', location: 'القاهرة', isActive: true },
        { id: 'fac-2', name: 'مصنع بلا خطوط', code: 'FAC-2', location: 'القاهرة', isActive: true }
      ]));
    }
    masterData.allProductionLines.and.returnValue(of(options.empty ? [] : [
      { id: 'line-1', factoryId: 'fac-1', name: 'خط خياطة 3', lineCode: 'LINE-STITCH-0001', sequenceOrder: 1, isActive: true },
      { id: 'line-2', factoryId: 'fac-1', name: 'خط خياطة 4', lineCode: 'LINE-STITCH-0002', sequenceOrder: 2, isActive: true }
    ]));
    masterData.departments.and.returnValue(of([]));
    masterData.stageDependencies.and.returnValue(of({
      stageId: 'sub-1', activeBlockers: [], historicalDependencies: [], canDisable: true, canDelete: true,
      disableMessageAr: 'يمكن تعطيل المرحلة.', deleteMessageAr: 'يمكن حذف المرحلة.'
    }));
    masterData.deactivateOperationalStage.and.returnValue(of({}));
    masterData.deleteOperationalStage.and.returnValue(of({}));
    masterData.createOperationalStage.and.returnValue(of({ id: 'sub-new', mainStageId: 'main-1', productionLineId: 'line-1', code: 'STG003', name: 'مرحلة جديدة', capacity: 0, sequenceOrder: 1, isActive: true }));
    masterData.updateOperationalStage.and.returnValue(of({ id: 'sub-1', mainStageId: 'main-1', productionLineId: 'line-1', code: 'STG001', name: 'مرحلة', capacity: 0, sequenceOrder: 1, isActive: true }));
    masterData.mainStagesForLine.and.callFake((lineId: string) => of(lineId === 'line-1'
      ? [{ id: 'main-1', productionLineId: 'line-1', name: 'الخياطة', sequenceOrder: 1, isCritical: false, isActive: true }]
      : [{ id: 'main-2', productionLineId: 'line-2', name: 'التجهيز', sequenceOrder: 1, isCritical: false, isActive: true }]));
    masterData.subStagesForMainStage.and.callFake((mainStageId: string) => of(mainStageId === 'main-1'
      ? [{ id: 'sub-1', mainStageId: 'main-1', code: 'STG001', name: 'تحمبل السير', capacity: 0, sequenceOrder: 1, isActive: true }]
      : [{ id: 'sub-2', mainStageId: 'main-2', code: 'STG002', name: 'مرحلة تجهيز', capacity: 0, sequenceOrder: 1, isActive: true }]));
    masterData.allMainStages.and.returnValue(of([]));
    masterData.allSubStages.and.returnValue(of([]));
    masterData.updateFactory.and.returnValue(of({ id: 'fac-1', name: 'مصنع رئيسي', code: 'FAC-1', isActive: true }));
    workers.loadFactoryStructureEligibleWorkers.and.returnValue(of([{
      id: 'worker-1',
      code: 'W-1',
      fullName: 'عامل تجريبي',
      state: 'على رأس العمل',
      employmentStatus: 'Active',
      isActive: true,
      phone: '01000000000'
    }]));
    assignments.getFactoryStructureSubStageWorkers.and.returnValue(of({
      subStageId: 'sub-1',
      workers: [{ id: 'worker-1', code: 'W-1', fullName: 'عامل تجريبي', assignmentType: 'Default', fromSubStageId: null, replacementForWorkerId: null }],
      hasBackendData: true,
      hasUsableBackendData: true
    }));
    assignments.createFactoryStructureDefaultAssignment.and.returnValue(of({
      assignmentId: 'assignment-1',
      workerId: 'worker-1',
      assignmentType: 'Default',
      subStageId: 'sub-1',
      fromSubStageId: null,
      toSubStageId: null,
      startsAtUtc: null,
      endsAtUtc: null,
      status: '',
      replacementForWorkerId: null
    }));

    TestBed.configureTestingModule({
      declarations: [FactoryStructureFoundationPageComponent],
      imports: [FormsModule, SharedModule, ButtonModule, TableModule, PlpResponsiveTableDirective, PlpTablePaginationDirective, PlpExpandableFormComponent, PlpDialogComponent, NoopAnimationsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: masterData },
        { provide: AssignmentsApiService, useValue: assignments },
        { provide: WorkersApiService, useValue: workers },
        { provide: PermissionService, useValue: permissionService },
        { provide: Router, useValue: router }
      ]
    });

    const fixture = TestBed.createComponent(FactoryStructureFoundationPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  function openAssignmentForm(fixture: ComponentFixture<FactoryStructureFoundationPageComponent>): void {
    const trigger = fixture.debugElement.queryAll(By.css('plp-expandable-form button'))
      .find(button => button.nativeElement.textContent.trim() === 'تسكين عامل');

    expect(trigger).toBeDefined();
    trigger!.nativeElement.click();
    fixture.detectChanges();
  }

  it('loads and renders the factory, line, stage, sub-stage, and worker relationships', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('مصنع رئيسي');
    expect(text).toContain('خط خياطة 3');
    expect(text).toContain('الخياطة');
    expect(text).toContain('STG001');
    expect(text).toContain('عامل تجريبي');
  });

  it('shows the dependency summary before disabling an operational stage', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    const stage: SubStageOption = { id: 'sub-1', mainStageId: 'main-1', code: 'STG001', name: 'مرحلة', capacity: 0, sequenceOrder: 1, isActive: true };

    component.openStageDependencyDialog(stage, 'disable');
    fixture.detectChanges();

    expect(masterData.stageDependencies).toHaveBeenCalledWith('sub-1');
    expect(component.stageDependencyDialogVisible).toBeTrue();
    expect(component.canConfirmStageDependencyAction).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('يمكن تعطيل المرحلة.');
  });

  it('clears downstream selections when selecting a factory with no lines', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.selectFactory('fac-2');
    fixture.detectChanges();

    expect(component.visibleLines).toEqual([]);
    expect(component.visibleMainStages).toEqual([]);
    expect(component.visibleSubStages).toEqual([]);
    expect(component.assignedWorkers).toEqual([]);
    expect(component.selectedLineId).toBe('');
    expect(component.selectedMainStageId).toBe('');
    expect(component.selectedSubStageId).toBe('');
  });

  it('loads only main stages for the selected line and clears sub-stage state', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.selectLine('line-2');
    fixture.detectChanges();

    expect(component.visibleMainStages.map(stage => stage.id)).toEqual(['main-2']);
    expect(component.visibleSubStages).toEqual([]);
    expect(component.assignedWorkers).toEqual([]);
  });

  it('auto-selects the only active grouping and hides the selector', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.activeForm = 'sub-stage';
    component.selectLine('line-1');
    fixture.detectChanges();

    expect(component.activeMainStages.map(stage => stage.id)).toEqual(['main-1']);
    expect(component.subStageDraft.mainStageId).toBe('main-1');
    expect(fixture.debugElement.query(By.css('select[name="subMainId"]'))).toBeNull();
  });

  it('ignores an inactive grouping when auto-selecting the only active grouping', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    masterData.mainStagesForLine.and.returnValue(of([
      { id: 'main-active', productionLineId: 'line-1', name: 'نشطة', sequenceOrder: 1, isCritical: false, isActive: true },
      { id: 'main-inactive', productionLineId: 'line-1', name: 'معطلة', sequenceOrder: 2, isCritical: false, isActive: false }
    ]));

    component.activeForm = 'sub-stage';
    component.selectLine('line-1');
    fixture.detectChanges();

    expect(component.activeMainStages.map(stage => stage.id)).toEqual(['main-active']);
    expect(component.subStageDraft.mainStageId).toBe('main-active');
    expect(fixture.debugElement.query(By.css('select[name="subMainId"]'))).toBeNull();
  });

  it('shows the grouping selector only when more than one active grouping exists', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    masterData.mainStagesForLine.and.returnValue(of([
      { id: 'main-a', productionLineId: 'line-1', name: 'نشطة أ', sequenceOrder: 1, isCritical: false, isActive: true },
      { id: 'main-b', productionLineId: 'line-1', name: 'نشطة ب', sequenceOrder: 2, isCritical: false, isActive: true },
      { id: 'main-inactive', productionLineId: 'line-1', name: 'معطلة', sequenceOrder: 3, isCritical: false, isActive: false }
    ]));

    component.activeForm = 'sub-stage';
    component.selectLine('line-1');
    fixture.detectChanges();

    const selector = fixture.debugElement.query(By.css('select[name="subMainId"]'));
    const optionValues = selector.queryAll(By.css('option')).map(option => option.nativeElement.value);

    expect(component.subStageDraft.mainStageId).toBe('');
    expect(optionValues).toEqual(['', 'main-a', 'main-b']);
    expect(selector.nativeElement.textContent).not.toContain('معطلة');
  });

  it('blocks creation with the backend-equivalent message when the line has no active grouping', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    masterData.mainStagesForLine.and.returnValue(of([
      { id: 'main-inactive', productionLineId: 'line-1', name: 'معطلة', sequenceOrder: 1, isCritical: false, isActive: false }
    ]));

    component.activeForm = 'sub-stage';
    component.selectLine('line-1');
    component.subStageDraft.name = 'مرحلة جديدة';
    fixture.detectChanges();
    component.saveSubStage();

    expect(component.activeMainStages).toEqual([]);
    expect(fixture.debugElement.query(By.css('select[name="subMainId"]'))).toBeNull();
    expect(component.errorMessage).toBe('لا يمكن إنشاء مرحلة تشغيلية قبل إنشاء مجموعة مراحل نشطة للخط.');
    expect(masterData.createOperationalStage).not.toHaveBeenCalled();
  });

  it('does not request main stages repeatedly for the same selected line', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;

    component.selectLine('line-1');
    component.selectLine('line-1');
    fixture.detectChanges();

    expect(masterData.mainStagesForLine.calls.allArgs().filter(args => args[0] === 'line-1').length).toBe(1);
  });

  it('loads only sub-stages for the selected main stage and clears assigned workers', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-2');
    component.selectMainStage('main-2');
    fixture.detectChanges();

    expect(component.visibleSubStages.map(stage => stage.id)).toEqual(['sub-2']);
    expect(component.assignedWorkers).toEqual([]);
    expect(component.selectedSubStageId).toBe('');
  });

  it('does not request sub-stages repeatedly for the same selected main stage', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectMainStage('main-1');
    fixture.detectChanges();

    expect(masterData.subStagesForMainStage.calls.allArgs().filter(args => args[0] === 'main-1').length).toBe(1);
  });

  it('reloads sub-stages when a previously selected main stage is selected after switching lines', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectLine('line-2');

    expect(component.selectedMainStageId).toBe('');
    expect(component.selectedSubStageId).toBe('');

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    fixture.detectChanges();

    expect(masterData.subStagesForMainStage.calls.allArgs().filter(args => args[0] === 'main-1').length).toBe(2);
    expect(component.visibleSubStages.map(stage => stage.id)).toEqual(['sub-1']);
  });

  it('loads exactly one new-line sub-stage request and ignores a stale previous-line response', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    const lineASubStages$ = new Subject<SubStageOption[]>();
    const lineBSubStages$ = new Subject<SubStageOption[]>();

    masterData.subStagesForMainStage.and.callFake((mainStageId: string) =>
      mainStageId === 'main-1' ? lineASubStages$.asObservable() : lineBSubStages$.asObservable()
    );

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    lineASubStages$.next([{
      id: 'sub-1', mainStageId: 'main-1', code: 'STG001', name: 'المرحلة الأولى', capacity: 0, sequenceOrder: 1, isActive: true
    }]);

    component.selectLine('line-2');

    expect(component.selectedMainStageId).toBe('');
    expect(component.selectedSubStageId).toBe('');
    expect(component.visibleSubStages).toEqual([]);

    component.selectMainStage('main-2');

    expect(masterData.subStagesForMainStage.calls.allArgs().filter(args => args[0] === 'main-2').length).toBe(1);

    lineASubStages$.next([{
      id: 'stale-sub', mainStageId: 'main-1', code: 'STALE', name: 'قديمة', capacity: 0, sequenceOrder: 1, isActive: true
    }]);
    expect(component.visibleSubStages).toEqual([]);

    lineBSubStages$.next([{
      id: 'sub-2', mainStageId: 'main-2', code: 'STG002', name: 'المرحلة الثانية', capacity: 0, sequenceOrder: 1, isActive: true
    }]);
    fixture.detectChanges();

    expect(component.visibleSubStages.map(stage => stage.id)).toEqual(['sub-2']);
  });

  it('does not request assigned workers repeatedly for the same selected sub-stage', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();

    expect(assignments.getFactoryStructureSubStageWorkers.calls.allArgs().filter(args => args[0] === 'sub-1').length).toBe(1);
  });

  it('requests eligible workers once when selecting a sub-stage', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const workers = TestBed.inject(WorkersApiService) as jasmine.SpyObj<WorkersApiService>;

    expect(workers.loadFactoryStructureEligibleWorkers).not.toHaveBeenCalled();

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();

    expect(workers.loadFactoryStructureEligibleWorkers).toHaveBeenCalledTimes(1);
    expect(workers.loadFactoryStructureEligibleWorkers).toHaveBeenCalledWith('sub-1');
    expect(workers.loadWorkers).not.toHaveBeenCalled();
  });

  it('renders eligible worker options after selecting a sub-stage', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();
    openAssignmentForm(fixture);

    const option = fixture.debugElement.query(By.css('select[name="workerId"] option[value="worker-1"]'));
    expect(option?.nativeElement.textContent).toContain('عامل تجريبي');
  });

  it('stores the selected Worker.id from the rendered selector and enables the rendered assignment button', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();
    openAssignmentForm(fixture);

    const workerSelect = fixture.debugElement.query(By.css('select[name="workerId"]')).nativeElement as HTMLSelectElement;
    const assignButton = fixture.debugElement.query(By.css('#factoryStructureAssignWorkerButton')).nativeElement as HTMLButtonElement;

    expect(assignButton.disabled).toBeTrue();

    workerSelect.value = 'worker-1';
    workerSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(component.selectedWorkerId).toBe('worker-1');
    expect(component.selectedWorkerExistsInOptions).toBeTrue();
    expect(assignButton.disabled).toBeFalse();
  });

  it('calls the factory-structure assignment API when assigning a worker to the selected sub-stage', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.selectedWorkerId = 'worker-1';
    component.assignedWorkers = [];
    component.assignWorker();

    expect(assignments.createFactoryStructureDefaultAssignment).toHaveBeenCalledWith({
      workerId: 'worker-1',
      subStageId: 'sub-1',
      reason: 'Factory structure assignment'
    });
  });

  it('sends one assignment write request from the rendered assign button click', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();
    openAssignmentForm(fixture);

    const workerSelect = fixture.debugElement.query(By.css('select[name="workerId"]')).nativeElement as HTMLSelectElement;
    workerSelect.value = 'worker-1';
    workerSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const assignButton = fixture.debugElement.query(By.css('#factoryStructureAssignWorkerButton')).nativeElement as HTMLButtonElement;
    expect(assignButton.disabled).toBeFalse();
    assignButton.click();

    expect(assignments.createFactoryStructureDefaultAssignment).toHaveBeenCalledTimes(1);
    expect(assignments.createFactoryStructureDefaultAssignment).toHaveBeenCalledWith({
      workerId: 'worker-1',
      subStageId: 'sub-1',
      reason: 'Factory structure assignment'
    });
  });

  it('lets the backend handle duplicate assignment idempotency', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.assignedWorkers = [{ id: 'worker-1', code: 'W-1', fullName: 'عامل تجريبي', assignmentType: 'Default', fromSubStageId: null, replacementForWorkerId: null }];
    component.selectedWorkerId = 'worker-1';
    component.assignWorker();

    expect(assignments.createFactoryStructureDefaultAssignment).toHaveBeenCalledTimes(1);
  });

  it('reloads assignments after save without clearing selected sub-stage or reloading workers', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;
    const workers = TestBed.inject(WorkersApiService) as jasmine.SpyObj<WorkersApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    workers.loadFactoryStructureEligibleWorkers.calls.reset();
    assignments.getFactoryStructureSubStageWorkers.calls.reset();

    component.selectedWorkerId = 'worker-1';
    component.assignWorker();
    fixture.detectChanges();

    expect(component.selectedSubStageId).toBe('sub-1');
    expect(workers.loadFactoryStructureEligibleWorkers).not.toHaveBeenCalled();
    expect(assignments.getFactoryStructureSubStageWorkers).toHaveBeenCalledTimes(1);
    expect(assignments.getFactoryStructureSubStageWorkers).toHaveBeenCalledWith('sub-1');
  });

  it('keeps eligible worker options when an unrelated assignment reload fails', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;
    const workers = TestBed.inject(WorkersApiService) as jasmine.SpyObj<WorkersApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    assignments.getFactoryStructureSubStageWorkers.and.returnValue(throwError(() => new Error('Assignment reload failed')));
    workers.loadFactoryStructureEligibleWorkers.calls.reset();
    component.selectedWorkerId = 'worker-1';
    component.assignWorker();
    fixture.detectChanges();

    expect(component.workers.map(worker => worker.id)).toEqual(['worker-1']);
    expect(workers.loadFactoryStructureEligibleWorkers).not.toHaveBeenCalled();
    expect(workers.loadWorkers).not.toHaveBeenCalled();
    expect(component.selectedSubStageId).toBe('sub-1');
  });

  it('surfaces assignment save errors', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;
    assignments.createFactoryStructureDefaultAssignment.and.returnValue(throwError(() => new Error('Assignment failed')));

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.selectedWorkerId = 'worker-1';
    component.assignWorker();
    fixture.detectChanges();

    expect(component.hasError).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Assignment failed');
  });

  it('blocks a missing or unavailable worker selection with a visible validation error', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;
    const assignments = TestBed.inject(AssignmentsApiService) as jasmine.SpyObj<AssignmentsApiService>;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    component.assignWorker();
    fixture.detectChanges();

    expect(assignments.createFactoryStructureDefaultAssignment).not.toHaveBeenCalled();
    expect(component.hasError).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('اختر العامل والمرحلة الفرعية أولاً.');

    component.hasError = false;
    component.selectedWorkerId = 'worker-not-eligible';
    component.assignWorker();
    fixture.detectChanges();

    expect(assignments.createFactoryStructureDefaultAssignment).not.toHaveBeenCalled();
    expect(component.hasError).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('العامل المحدد غير متاح');
  });

  it('hides management forms without factory-structure.manage', () => {
    const fixture = configure({ manage: false });

    expect(fixture.debugElement.query(By.css('form'))).toBeNull();
  });

  it('shows the contextual production action only for an authorized, complete active stage context', () => {
    const fixture = configure({ record: true });
    const component = fixture.componentInstance;
    const router = TestBed.inject(Router) as jasmine.SpyObj<Router>;

    expect(fixture.debugElement.query(By.css('#factoryStructureRecordProductionButton'))).toBeNull();

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();

    const action = fixture.debugElement.query(By.css('#factoryStructureRecordProductionButton'));
    expect(action).not.toBeNull();
    action.nativeElement.click();

    expect(router.navigate).toHaveBeenCalledWith(['/manufacturing/production-recording'], {
      queryParams: { factoryId: 'fac-1', productionLineId: 'line-1', mainStageId: 'main-1', subStageId: 'sub-1' }
    });
  });

  it('does not expose the contextual production action without recording access', () => {
    const fixture = configure({ manage: true });
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectMainStage('main-1');
    component.selectSubStage('sub-1');
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('#factoryStructureRecordProductionButton'))).toBeNull();
  });

  it('renders empty state when no structure data exists', () => {
    const fixture = configure({ empty: true });

    expect(fixture.nativeElement.textContent).toContain('لا توجد بيانات مصنع');
  });

  it('renders error state when loading fails', () => {
    const fixture = configure({ fail: true });

    expect(fixture.nativeElement.textContent).toContain('تعذر تحميل بنية المصنع');
    expect(fixture.nativeElement.textContent).toContain('Factory load failed');
  });
});
