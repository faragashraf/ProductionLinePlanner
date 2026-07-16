import { FormBuilder } from '@angular/forms';
import { of } from 'rxjs';
import { AssignmentsApiService, LineStaffingPlan } from '../../core/services/assignments-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';
import { LineStaffingWorkspacePageComponent } from './line-staffing-workspace-page.component';

const factoryId = '43dde27f-7ee3-4e90-9f3b-582fc90a3b0';
const lineId = 'c0550d1f-4bf7-432c-b19b-672763d490fc';
const modelId = '46593736-2fe2-450d-84a1-f304b712e07f';
const defaultStageId = 'c0ec408d-74ab-4299-88cd-1a7543cc335b';
const temporaryStageId = 'df19ab2b-49df-445d-a516-4d5d070d8de2';

describe('LineStaffingWorkspacePageComponent', () => {
  let masterData: jasmine.SpyObj<ManufacturingMasterDataApiService>;
  let assignments: jasmine.SpyObj<AssignmentsApiService>;
  let component: LineStaffingWorkspacePageComponent;

  beforeEach(() => {
    masterData = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    assignments = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', ['getLineStaffingPlan', 'getActiveLineStaffingWorkers', 'createDefaultAssignment']);
    masterData.factories.and.returnValue(of([{ id: factoryId, code: 'F1', name: 'المصنع', isActive: true }]));
    masterData.allProductionLines.and.returnValue(of([{ id: lineId, factoryId, name: 'خط الخياطة', sequenceOrder: 1, isActive: true }]));
    masterData.models.and.returnValue(of([{ id: modelId, code: 'GER', name: 'جرومان', isActive: true }]));
    assignments.getLineStaffingPlan.and.returnValue(of(plan()));
    assignments.getActiveLineStaffingWorkers.and.returnValue(of(plan().workers));
    assignments.createDefaultAssignment.and.returnValue(of({ assignmentId: 'assignment', workerId: 'worker-one', assignmentType: 'Default', subStageId: defaultStageId, fromSubStageId: null, toSubStageId: null, startsAtUtc: null, endsAtUtc: null, status: 'Active', replacementForWorkerId: null }));
    component = new LineStaffingWorkspacePageComponent(
      masterData,
      assignments,
      { hasPermission: () => true } as unknown as PermissionService,
      new FormBuilder(),
      new FormSubmissionValidationService()
    );
  });

  it('loads all model stages only after Factory → Line → Model and the explicit load action', () => {
    component.ngOnInit();
    expect(masterData.factories).toHaveBeenCalledTimes(1);
    expect(assignments.getLineStaffingPlan).not.toHaveBeenCalled();

    component.selectFactory(factoryId);
    component.selectProductionLine(lineId);
    component.selectProductModel(modelId);
    expect(assignments.getLineStaffingPlan).not.toHaveBeenCalled();

    component.loadProductStages();

    expect(assignments.getLineStaffingPlan).toHaveBeenCalledWith(factoryId, lineId, modelId, component.referenceDate);
    expect(component.plan?.stages.length).toBe(2);
    expect(component.selectedStage?.subStageId).toBe(defaultStageId);
  });

  it('loads every active staffing worker immediately without an attendance prerequisite and filters stages in place', () => {
    initialize(component);
    component.openDefaultAssignment();

    expect(component.availableWorkers.map(worker => worker.employeeCode)).toEqual(['100', '101']);
    expect(assignments.getActiveLineStaffingWorkers).toHaveBeenCalledWith(component.referenceDate);
    component.stageFilter = 'without-workers';
    expect(component.filteredStages.map(stage => stage.subStageId)).toEqual([temporaryStageId]);

    component.selectStage(temporaryStageId);
    expect(component.selectedStage?.stageName).toBe('تشطيب');
  });

  it('saves a default assignment through the shared assignment capability without navigation', () => {
    initialize(component);
    component.openDefaultAssignment();
    component.selectDialogWorker(component.availableWorkers[1]);
    component.saveAssignment();

    expect(assignments.createDefaultAssignment).toHaveBeenCalledWith({ workerId: 'worker-two', subStageId: defaultStageId, reason: undefined });
    expect(component.successMessage).toContain('تم حفظ تغيير التسكين');
  });

  it('keeps the first permanent assignment reason optional while a permanent change requires one', () => {
    initialize(component);
    component.selectStage(temporaryStageId);
    component.openDefaultAssignment();
    component.selectDialogWorker(component.availableWorkers[1]);

    expect(component.assignmentMissingRequirements).not.toContain('سبب تغيير التعيين الدائم مطلوب');

    component.selectDialogWorker(component.availableWorkers[0]);
    expect(component.assignmentMissingRequirements).toContain('سبب تغيير التعيين الدائم مطلوب');
  });

  it('shows temporary-assignment candidates before an end date is entered instead of silently filtering them away', () => {
    initialize(component);
    component.openTemporaryAssignment();

    expect(assignments.getActiveLineStaffingWorkers).toHaveBeenCalledWith(component.referenceDate);
    expect(component.availableWorkers.map(worker => worker.employeeCode)).toEqual(['100', '101']);
    expect(component.assignmentForm.controls.endAtLocal.value).toBe('');
  });

  it('navigates both directions through the current filter and problem stages without reloading the plan', () => {
    initialize(component);
    component.selectStage(temporaryStageId);

    expect(component.canNavigateStages(-1)).toBeTrue();
    expect(component.canNavigateStages(1)).toBeFalse();
    component.previousStage();
    expect(component.selectedSubStageId).toBe(defaultStageId);
    component.nextStage();
    expect(component.selectedSubStageId).toBe(temporaryStageId);
    expect(component.canNavigateStages(-1, true)).toBeTrue();
    component.previousProblemStage();
    expect(component.selectedSubStageId).toBe(defaultStageId);
    expect(assignments.getLineStaffingPlan).toHaveBeenCalledTimes(1);
  });

  it('labels provisional SharedPercentage setup as a temporary stage-cost configuration', () => {
    initialize(component);

    expect(component.compensationStatusLabel(component.selectedStage!)).toBe('إعداد تكلفة المرحلة مؤقت');
  });
});

function initialize(component: LineStaffingWorkspacePageComponent): void {
  component.ngOnInit();
  component.selectFactory(factoryId);
  component.selectProductionLine(lineId);
  component.selectProductModel(modelId);
  component.loadProductStages();
}

function plan(): LineStaffingPlan {
  return {
    factoryId,
    factoryName: 'المصنع',
    productionLineId: lineId,
    productionLineName: 'خط الخياطة',
    productModelId: modelId,
    productModelCode: 'GER',
    productModelName: 'جرومان',
    staffingReferenceDate: '2026-07-13',
    totalStages: 2,
    stagesWithWorkers: 1,
    stagesWithoutWorkers: 1,
    stagesWithTemporaryAssignments: 0,
    stagesNeedingCompensationReview: 2,
    stagesNeedingStaffingReview: 0,
    overallStaffingStatus: 'NeedsStaffing',
    staffingPlanComplete: false,
    operationalAttendanceChecked: false,
    financialConfigurationPending: true,
    stages: [
      { productModelStageId: 'stage-one', subStageId: defaultStageId, mainStageName: 'تجميع', stageCode: 'S1', stageName: 'تجميع', stageOrder: 1, piecePrice: .38, compensationMode: 'SharedPercentage', compensationConfigurationStatus: 'FinancialReviewPending', isFinancialReviewPending: true, defaultAssignedWorkersCount: 1, effectiveAssignedWorkersCount: 1, temporaryAssignedWorkersCount: 0, requiredWorkers: null, hasAuthoritativeRequiredWorkerCount: false, staffingStatus: 'Staffed', workerStatusText: 'يوجد عامل واحد', effectiveWorkerIds: ['worker-one'] },
      { productModelStageId: 'stage-two', subStageId: temporaryStageId, mainStageName: 'تشطيب', stageCode: 'S2', stageName: 'تشطيب', stageOrder: 2, piecePrice: .38, compensationMode: 'SharedPercentage', compensationConfigurationStatus: 'FinancialReviewPending', isFinancialReviewPending: true, defaultAssignedWorkersCount: 0, effectiveAssignedWorkersCount: 0, temporaryAssignedWorkersCount: 0, requiredWorkers: null, hasAuthoritativeRequiredWorkerCount: false, staffingStatus: 'NeedsStaffing', workerStatusText: 'لا يوجد عمال معينون', effectiveWorkerIds: [] }
    ],
    workers: [
      { workerId: 'worker-one', employeeCode: '100', fullName: 'عامل أول', departmentName: 'التجميع', isOnActiveService: true, hasPhoto: false, photoReference: null, photoVersion: null, defaultSubStageId: defaultStageId, defaultSubStageName: 'تجميع', effectiveAssignmentId: 'default-a', effectiveAssignmentType: 'Default', effectiveSubStageId: defaultStageId, effectiveSubStageName: 'تجميع', fromSubStageId: null, fromSubStageName: null, temporaryStartsAtUtc: null, temporaryEndsAtUtc: null, replacementForWorkerId: null },
      { workerId: 'worker-two', employeeCode: '101', fullName: 'عامل ثان', departmentName: 'التشطيب', isOnActiveService: true, hasPhoto: true, photoReference: '/api/workers/worker-two/photo?v=photo-v1', photoVersion: 'photo-v1', defaultSubStageId: null, defaultSubStageName: null, effectiveAssignmentId: null, effectiveAssignmentType: null, effectiveSubStageId: null, effectiveSubStageName: null, fromSubStageId: null, fromSubStageName: null, temporaryStartsAtUtc: null, temporaryEndsAtUtc: null, replacementForWorkerId: null }
    ]
  };
}
