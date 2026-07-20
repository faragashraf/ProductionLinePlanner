import { FormBuilder } from '@angular/forms';
import { fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { AssignmentsApiService, LineStaffingPlan } from '../../core/services/assignments-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';
import { LineStaffingWorkspacePageComponent } from './line-staffing-workspace-page.component';

const factoryId = '43dde27f-7ee3-4e90-9f3b-582fc90a3b0';
const departmentId = '3adcd4d8-06da-4e9a-a2d8-5c1ac48274d9';
const lineId = 'c0550d1f-4bf7-432c-b19b-672763d490fc';
const modelId = '46593736-2fe2-450d-84a1-f304b712e07f';
const defaultStageId = 'c0ec408d-74ab-4299-88cd-1a7543cc335b';
const temporaryStageId = 'df19ab2b-49df-445d-a516-4d5d070d8de2';

describe('LineStaffingWorkspacePageComponent', () => {
  let masterData: jasmine.SpyObj<ManufacturingMasterDataApiService>;
  let assignments: jasmine.SpyObj<AssignmentsApiService>;
  let router: jasmine.SpyObj<Router>;
  let route: ActivatedRoute;
  let routeFragments: BehaviorSubject<string | null>;
  let component: LineStaffingWorkspacePageComponent;

  beforeEach(() => {
    masterData = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', ['factories', 'departments', 'productionLinesForDepartment', 'models']);
    assignments = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', ['getLineStaffingPlan', 'getLineStaffingStageRefresh', 'getActiveLineStaffingWorkers', 'updateStageDefaultAssignments', 'removeDefaultAssignment']);
    masterData.factories.and.returnValue(of([{ id: factoryId, code: 'F1', name: 'المصنع', isActive: true }]));
    masterData.departments.and.returnValue(of([{ id: departmentId, factoryId, code: 'SEW', nameAr: 'الخياطة', isActive: true }]));
    masterData.productionLinesForDepartment.and.returnValue(of([{ id: lineId, factoryId, departmentId, name: 'خط الخياطة', sequenceOrder: 1, isActive: true }]));
    masterData.models.and.returnValue(of([{ id: modelId, code: 'GER', name: 'جرومان', isActive: true }]));
    assignments.getLineStaffingPlan.and.callFake(() => of(plan()));
    assignments.getLineStaffingStageRefresh.and.callFake(() => of(stageRefresh()));
    assignments.getActiveLineStaffingWorkers.and.returnValue(of(plan().workers));
    assignments.updateStageDefaultAssignments.and.returnValue(of({ subStageId: defaultStageId, addedWorkersCount: 1, removedWorkersCount: 0, activeWorkerIds: ['worker-one', 'worker-two'] }));
    assignments.removeDefaultAssignment.and.returnValue(of({} as any));
    routeFragments = new BehaviorSubject<string | null>(null);
    route = { fragment: routeFragments.asObservable() } as ActivatedRoute;
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.returnValue(Promise.resolve(true));
    component = new LineStaffingWorkspacePageComponent(
      masterData,
      assignments,
      { hasPermission: () => true } as unknown as PermissionService,
      new FormBuilder(),
      new FormSubmissionValidationService(),
      route,
      router
    );
  });

  afterEach(() => {
    document.documentElement.classList.remove('plp-line-staffing-tablet-scroll-lock');
    document.body.classList.remove('plp-line-staffing-tablet-scroll-lock');
  });

  it('loads model stages only after Factory → Department → Line → Model and the explicit load action', () => {
    component.ngOnInit();
    expect(masterData.factories).toHaveBeenCalledTimes(1);
    expect(assignments.getLineStaffingPlan).not.toHaveBeenCalled();

    component.selectFactory(factoryId);
    component.selectDepartment(departmentId);
    component.selectProductionLine(lineId);
    component.selectProductModel(modelId);
    expect(assignments.getLineStaffingPlan).not.toHaveBeenCalled();

    component.loadProductStages();

    expect(assignments.getLineStaffingPlan).toHaveBeenCalledWith(factoryId, lineId, modelId, component.staffingReferenceDate);
    expect(component.plan?.stages.length).toBe(2);
    expect(component.selectedStage?.subStageId).toBe(defaultStageId);
  });

  it('loads only the selected factory departments and only the selected department lines', () => {
    component.ngOnInit();
    component.selectFactory(factoryId);

    expect(masterData.departments).toHaveBeenCalledWith(factoryId, false);
    expect(component.activeDepartments.map(department => department.id)).toEqual([departmentId]);
    expect(component.visibleProductionLines).toEqual([]);

    component.selectDepartment(departmentId);

    expect(masterData.productionLinesForDepartment).toHaveBeenCalledWith(departmentId);
    expect(component.visibleProductionLines.map(line => line.id)).toEqual([lineId]);
  });

  it('clears dependent context and a loaded journey when a higher context value changes', () => {
    initialize(component);
    expect(component.plan).not.toBeNull();

    component.selectDepartment('');

    expect(component.selectedProductionLineId).toBe('');
    expect(component.selectedProductModelId).toBe('');
    expect(component.selectedSubStageId).toBe('');
    expect(component.plan).toBeNull();
  });

  it('does not include unassigned lines in the staffing choices', () => {
    masterData.productionLinesForDepartment.and.returnValue(of([
      { id: lineId, factoryId, departmentId, name: 'خط الخياطة', sequenceOrder: 1, isActive: true },
      { id: 'legacy-line', factoryId, departmentId: null, name: 'خط قديم', sequenceOrder: 2, isActive: true }
    ]));
    component.ngOnInit();
    component.selectFactory(factoryId);
    component.selectDepartment(departmentId);

    expect(component.visibleProductionLines.map(line => line.id)).toEqual([lineId]);
  });

  it('keeps the model journey empty state distinct when the selected model has no configured stages', () => {
    assignments.getLineStaffingPlan.and.returnValue(of({ ...plan(), stages: [], totalStages: 0, stagesWithWorkers: 0, stagesWithoutWorkers: 0 }));
    initialize(component);

    expect(component.plan).not.toBeNull();
    expect(component.hasLoadedModelJourney).toBeFalse();
    expect(component.selectedStage).toBeNull();
  });

  it('keeps the staffing form permanent-only with no temporary fields or mode selector state', () => {
    expect(component.assignmentForm.contains('workerId')).toBeTrue();
    expect(component.assignmentForm.contains('startTime')).toBeFalse();
    expect(component.assignmentForm.contains('endTime')).toBeFalse();
    expect(component.assignmentForm.contains('temporaryParticipationMode')).toBeFalse();
  });

  it('loads every active staffing worker immediately without an attendance prerequisite and filters stages in place', () => {
    initialize(component);
    component.openDefaultAssignment();

    expect(component.availableWorkers.map(worker => worker.employeeCode)).toEqual(['100', '101']);
    expect(assignments.getActiveLineStaffingWorkers).toHaveBeenCalledWith(component.staffingReferenceDate);
    component.stageFilter = 'without-workers';
    expect(component.filteredStages.map(stage => stage.subStageId)).toEqual([temporaryStageId]);

    component.selectStage(temporaryStageId);
    expect(component.selectedStage?.stageName).toBe('تشطيب');
  });

  it('saves checked permanent workers together, closes the dialog, and refreshes only the selected stage', () => {
    initialize(component);
    const unchangedStage = component.plan!.stages.find(stage => stage.subStageId === temporaryStageId)!;
    const previousSelectedStage = component.selectedStage;
    component.openDefaultAssignment();
    expect(component.isDefaultWorkerSelected(component.availableWorkers[0])).toBeTrue();
    component.toggleDefaultWorker(component.availableWorkers[1], true);
    component.saveAssignment();

    expect(assignments.updateStageDefaultAssignments).toHaveBeenCalledWith(defaultStageId, ['worker-one', 'worker-two']);
    expect(component.assignmentDialogVisible).toBeFalse();
    expect(component.successMessage).toContain('تم تحديث عمال المرحلة');
    expect(component.selectedSubStageId).toBe(defaultStageId);
    expect(component.selectedStage).not.toBe(previousSelectedStage);
    expect(component.selectedStageWorkers.map(worker => worker.workerId)).toEqual(['worker-one', 'worker-two']);
    expect(component.plan!.stages.find(stage => stage.subStageId === temporaryStageId)).toEqual(unchangedStage);
    expect(assignments.getLineStaffingPlan).toHaveBeenCalledTimes(1);
    expect(assignments.getLineStaffingStageRefresh).toHaveBeenCalledWith(factoryId, lineId, modelId, defaultStageId, component.staffingReferenceDate);
    expect(assignments.getActiveLineStaffingWorkers).toHaveBeenCalledTimes(1);
  });

  it('keeps the bulk dialog open while first, successive, and reversed selections only update local state', () => {
    initialize(component);
    const save = spyOn(component, 'saveAssignment');
    const firstWorker = component.plan!.workers[0];
    const secondWorker = component.plan!.workers[1];
    component.openDefaultAssignment();
    const workerDirectoryRequests = assignments.getActiveLineStaffingWorkers.calls.count();

    component.onDefaultWorkerCheckboxChange(secondWorker, selectionEvent(true));
    component.toggleDefaultWorkerFromRow(firstWorker, rowSelectionEvent());
    component.onDefaultWorkerCheckboxChange(secondWorker, selectionEvent(false));

    expect(component.assignmentDialogVisible).toBeTrue();
    expect(component.selectedDefaultWorkersCount).toBe(0);
    expect(component.selectedSubStageId).toBe(defaultStageId);
    expect(save).not.toHaveBeenCalled();
    expect(assignments.updateStageDefaultAssignments).not.toHaveBeenCalled();
    expect(assignments.getLineStaffingPlan).toHaveBeenCalledTimes(1);
    expect(assignments.getLineStaffingStageRefresh).not.toHaveBeenCalled();
    expect(assignments.getActiveLineStaffingWorkers).toHaveBeenCalledTimes(workerDirectoryRequests);
  });

  it('exposes only permanent assignment and permanent cancellation through the shared sheet', () => {
    initialize(component);
    const assignedWorker = component.plan!.workers[0];

    component.openDefaultAssignment();
    expect(component.assignmentDialogVisible).toBeTrue();
    expect(component.assignmentDialogMode).toBe('default');
    component.closeAssignmentDialog();

    component.openCancellation(assignedWorker);
    expect(component.assignmentDialogVisible).toBeTrue();
    expect(component.assignmentDialogMode).toBe('remove-default');
  });

  it('does not submit the permanent bulk dialog from the form while filtering or selecting workers', () => {
    initialize(component);
    const save = spyOn(component, 'saveAssignment');
    component.openDefaultAssignment();
    component.workerSearch = 'عامل';
    component.departmentFilter = 'التشطيب';
    component.onAssignmentFormSubmitted();

    expect(component.assignmentDialogVisible).toBeTrue();
    expect(component.workerSearch).toBe('عامل');
    expect(component.departmentFilter).toBe('التشطيب');
    expect(save).not.toHaveBeenCalled();
  });

  it('keeps the bulk dialog and selected workers on a failed save', () => {
    initialize(component);
    const save = new Subject<{ subStageId: string; addedWorkersCount: number; removedWorkersCount: number; activeWorkerIds: string[] }>();
    assignments.updateStageDefaultAssignments.and.returnValue(save);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);
    component.saveAssignment();
    save.error(new Error('network'));

    expect(component.assignmentDialogVisible).toBeTrue();
    expect(component.isDefaultWorkerSelected(component.availableWorkers[1])).toBeTrue();
    expect(component.assignmentDialogError).toContain('network');
    expect(assignments.getLineStaffingStageRefresh).not.toHaveBeenCalled();
  });

  it('closes the bulk dialog on cancel without saving and discards local selections', () => {
    initialize(component);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);
    component.closeAssignmentDialog();

    expect(component.assignmentDialogVisible).toBeFalse();
    expect(component.selectedDefaultWorkersCount).toBe(0);
    expect(assignments.updateStageDefaultAssignments).not.toHaveBeenCalled();
  });

  it('submits the permanent bulk save once while the request is active', () => {
    initialize(component);
    const save = new Subject<{ subStageId: string; addedWorkersCount: number; removedWorkersCount: number; activeWorkerIds: string[] }>();
    assignments.updateStageDefaultAssignments.and.returnValue(save);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);

    component.saveAssignment();
    component.saveAssignment();

    expect(assignments.updateStageDefaultAssignments).toHaveBeenCalledTimes(1);
    expect(component.assignmentSaving).toBeTrue();
  });

  it('unchecking a preselected worker removes only that worker from the selected stage selection', () => {
    initialize(component);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[0], false);
    component.saveAssignment();

    expect(assignments.updateStageDefaultAssignments).toHaveBeenCalledWith(defaultStageId, []);
  });

  it('does not require a reason when adding a permanent participation to another stage', () => {
    initialize(component);
    component.selectStage(temporaryStageId);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);

    expect(component.assignmentMissingRequirements).not.toContain('سبب تغيير التعيين الدائم مطلوب');

    component.toggleDefaultWorker(component.availableWorkers[1], false);
    expect(component.assignmentMissingRequirements).not.toContain('سبب تغيير التعيين الدائم مطلوب');
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

  it('keeps normal stage selection local without scrolling or focusing the document', fakeAsync(() => {
    initialize(component);
    const getElementById = spyOn(document, 'getElementById');

    component.selectStage(temporaryStageId);
    flushMicrotasks();

    expect(component.selectedSubStageId).toBe(temporaryStageId);
    expect(getElementById).not.toHaveBeenCalled();
  }));

  it('does not move the page when an assignment dialog closes or a selected stage refreshes', fakeAsync(() => {
    initialize(component);
    const scrollTo = spyOn(window, 'scrollTo');
    component.openDefaultAssignment();
    component.closeAssignmentDialog();
    component.loadProductStages(true);
    flushMicrotasks();

    expect(scrollTo).not.toHaveBeenCalled();
    expect(component.selectedSubStageId).toBe(defaultStageId);
  }));

  it('locks document scrolling only while a loaded tablet workspace is active and restores it on destroy', () => {
    activateTabletWorkspace(component);

    expect(document.documentElement.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeTrue();
    expect(document.body.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeTrue();
    expect(component.tabletWorkspaceHeightPx).toBeGreaterThan(0);

    component.ngOnDestroy();

    expect(document.documentElement.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeFalse();
    expect(document.body.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeFalse();
  });

  it('keeps the measured tablet workspace height stable through browser-toolbar viewport changes', () => {
    const { content } = activateTabletWorkspace(component);
    const initialHeight = component.tabletWorkspaceHeightPx;
    content.getBoundingClientRect = () => ({ top: 320 } as DOMRect);

    synchronizeTabletWorkspace(component);

    expect(component.tabletWorkspaceHeightPx).toBe(initialHeight);
  });

  it('moves every requested section through bounded containers without document or window scrolling', () => {
    const { content, workspace } = activateTabletWorkspace(component);
    const contentScrollTo = jasmine.createSpy('contentScrollTo');
    const stageScrollTo = jasmine.createSpy('stageScrollTo');
    const workerScrollTo = jasmine.createSpy('workerScrollTo');
    content.scrollTop = 15;
    content.scrollTo = contentScrollTo;
    workspace.getBoundingClientRect = () => ({ top: 650 } as DOMRect);
    const choices = scrollContainer(150);
    const summary = scrollContainer(400);
    const stageList = scrollContainer(0);
    const selectedPanel = scrollContainer(0);
    stageList.scrollTo = stageScrollTo;
    selectedPanel.scrollTo = workerScrollTo;
    setPrivateElementRef(component, 'staffingChoices', choices);
    setPrivateElementRef(component, 'staffingSummary', summary);
    setPrivateElementRef(component, 'stageList', stageList);
    setPrivateElementRef(component, 'selectedStagePanel', selectedPanel);
    const documentScroll = spyOn(document.documentElement, 'scrollTo');
    const windowScroll = spyOn(window, 'scrollTo');

    navigateWithinWorkspace(component, 'choices');
    navigateWithinWorkspace(component, 'summary');
    navigateWithinWorkspace(component, 'stages');
    navigateWithinWorkspace(component, 'workers');

    expect(contentScrollTo).toHaveBeenCalledWith({ top: 45, behavior: 'smooth' });
    expect(contentScrollTo).toHaveBeenCalledWith({ top: 295, behavior: 'smooth' });
    expect(contentScrollTo).toHaveBeenCalledWith({ top: 545, behavior: 'smooth' });
    expect(stageScrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
    expect(workerScrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
    expect(documentScroll).not.toHaveBeenCalled();
    expect(windowScroll).not.toHaveBeenCalled();
  });

  it('replaces the route fragment for every explicit section request without page scrolling', fakeAsync(() => {
    const { content, workspace } = activateTabletWorkspace(component);
    const contentScrollTo = jasmine.createSpy('contentScrollTo');
    content.scrollTo = contentScrollTo;
    workspace.getBoundingClientRect = () => ({ top: 650 } as DOMRect);
    setPrivateElementRef(component, 'staffingChoices', scrollContainer(150));
    setPrivateElementRef(component, 'staffingSummary', scrollContainer(400));
    setPrivateElementRef(component, 'stageList', scrollContainer(0));
    setPrivateElementRef(component, 'selectedStagePanel', scrollContainer(0));
    const documentScroll = spyOn(document.documentElement, 'scrollTo');
    const windowScroll = spyOn(window, 'scrollTo');

    for (const section of ['choices', 'summary', 'stages', 'workers'] as const) {
      component.requestStaffingSection(section);
      tick(17);
    }

    expect(router.navigate.calls.allArgs().map(([, extras]) => (extras as { fragment: string }).fragment)).toEqual(['choices', 'summary', 'stages', 'workers']);
    expect(router.navigate.calls.allArgs().every(([, extras]) => {
      const navigation = extras as { relativeTo: ActivatedRoute; replaceUrl: boolean; queryParamsHandling: string };
      return navigation.relativeTo === route && navigation.replaceUrl && navigation.queryParamsHandling === 'preserve';
    })).toBeTrue();
    triggerManualSectionScroll(component, 'content');
    tick(200);
    expect(router.navigate).toHaveBeenCalledTimes(4);
    expect(contentScrollTo).toHaveBeenCalled();
    expect(documentScroll).not.toHaveBeenCalled();
    expect(windowScroll).not.toHaveBeenCalled();
    tick(450);
  }));

  it('restores direct #stages once through the stage-list container and does not replay after normal updates', fakeAsync(() => {
    routeFragments.next('stages');
    component.ngOnInit();
    const { content, workspace } = activateTabletWorkspace(component);
    const contentScrollTo = jasmine.createSpy('contentScrollTo');
    const stageScrollTo = jasmine.createSpy('stageScrollTo');
    const workerScrollTo = jasmine.createSpy('workerScrollTo');
    content.scrollTo = contentScrollTo;
    workspace.getBoundingClientRect = () => ({ top: 650 } as DOMRect);
    const stageList = scrollContainer(0);
    const selectedPanel = scrollContainer(0);
    stageList.scrollTo = stageScrollTo;
    selectedPanel.scrollTo = workerScrollTo;
    setPrivateElementRef(component, 'stageList', stageList);
    setPrivateElementRef(component, 'selectedStagePanel', selectedPanel);

    tick(17);

    expect(contentScrollTo).toHaveBeenCalledWith({ top: 530, behavior: 'smooth' });
    expect(stageScrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
    expect(workerScrollTo).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();

    const scrollCount = contentScrollTo.calls.count();
    component.loadProductStages(true);
    component.selectStage(temporaryStageId);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);
    component.saveAssignment();
    component.closeAssignmentDialog();
    tick(17);
    expect(contentScrollTo).toHaveBeenCalledTimes(scrollCount);
    tick(450);
  }));

  it('restores direct #workers once and keeps fragment scrolling stable through Android browser-chrome viewport changes', fakeAsync(() => {
    routeFragments.next('workers');
    component.ngOnInit();
    const { content, workspace } = activateTabletWorkspace(component);
    const contentScrollTo = jasmine.createSpy('contentScrollTo');
    const workerScrollTo = jasmine.createSpy('workerScrollTo');
    content.scrollTo = contentScrollTo;
    workspace.getBoundingClientRect = () => ({ top: 650 } as DOMRect);
    const selectedPanel = scrollContainer(0);
    selectedPanel.scrollTo = workerScrollTo;
    setPrivateElementRef(component, 'selectedStagePanel', selectedPanel);

    tick(17);

    expect(contentScrollTo).toHaveBeenCalledWith({ top: 530, behavior: 'smooth' });
    expect(workerScrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'smooth' });
    const measuredHeight = component.tabletWorkspaceHeightPx;
    content.scrollTop = 184;
    content.getBoundingClientRect = () => ({ top: 320 } as DOMRect);
    synchronizeTabletWorkspace(component);
    tick(17);

    expect(routeFragments.value).toBe('workers');
    expect(component.tabletWorkspaceHeightPx).toBe(measuredHeight);
    expect(content.scrollTop).toBe(184);
    expect(contentScrollTo).toHaveBeenCalledTimes(1);
    expect(workerScrollTo).toHaveBeenCalledTimes(1);
    tick(450);
  }));

  it('ignores unknown fragments without moving the workspace or document', fakeAsync(() => {
    routeFragments.next('not-a-staffing-section');
    component.ngOnInit();
    const { content } = activateTabletWorkspace(component);
    const contentScrollTo = jasmine.createSpy('contentScrollTo');
    content.scrollTo = contentScrollTo;
    const documentScroll = spyOn(document.documentElement, 'scrollTo');
    const windowScroll = spyOn(window, 'scrollTo');

    tick(17);

    expect(contentScrollTo).not.toHaveBeenCalled();
    expect(documentScroll).not.toHaveBeenCalled();
    expect(windowScroll).not.toHaveBeenCalled();
  }));

  it('updates fragments from each manually scrolled internal section without triggering internal scrolling', fakeAsync(() => {
    component.ngOnInit();
    const { content } = activateTabletWorkspace(component);
    const choices = scrollContainer(110, 330);
    const summary = scrollContainer(620, 840);
    const stageList = scrollContainer(860, 1180);
    const selectedPanel = scrollContainer(860, 1180);
    content.getBoundingClientRect = () => ({ top: 100, bottom: 500 } as DOMRect);
    setPrivateElementRef(component, 'staffingChoices', choices);
    setPrivateElementRef(component, 'staffingSummary', summary);
    setPrivateElementRef(component, 'stageList', stageList);
    setPrivateElementRef(component, 'selectedStagePanel', selectedPanel);
    synchronizeTabletWorkspace(component);
    tick(17);

    expect((component as unknown as { sectionVisibilityObserver: IntersectionObserver | null }).sectionVisibilityObserver).not.toBeNull();
    expect((component as unknown as { observedSectionElements: Map<HTMLElement, string> }).observedSectionElements.size).toBe(4);

    triggerManualSectionScroll(component, 'content');
    tick(117);
    summary.getBoundingClientRect = () => ({ top: 110, bottom: 330 } as DOMRect);
    choices.getBoundingClientRect = () => ({ top: -320, bottom: -80 } as DOMRect);
    triggerManualSectionScroll(component, 'content');
    tick(117);
    stageList.getBoundingClientRect = () => ({ top: 110, bottom: 430 } as DOMRect);
    summary.getBoundingClientRect = () => ({ top: -320, bottom: -80 } as DOMRect);
    triggerManualSectionScroll(component, 'content');
    tick(117);
    triggerManualSectionScroll(component, 'workers');
    tick(117);

    expect(router.navigate.calls.allArgs().map(([, extras]) => (extras as { fragment: string }).fragment)).toEqual(['choices', 'summary', 'stages', 'workers']);
    expect(router.navigate.calls.allArgs().every(([, extras]) => {
      const navigation = extras as { relativeTo: ActivatedRoute; replaceUrl: boolean; queryParamsHandling: string };
      return navigation.relativeTo === route && navigation.replaceUrl && navigation.queryParamsHandling === 'preserve';
    })).toBeTrue();
    expect(content.scrollTo).not.toHaveBeenCalled();
    expect(stageList.scrollTo).not.toHaveBeenCalled();
    expect(selectedPanel.scrollTo).not.toHaveBeenCalled();

    routeFragments.next('workers');
    tick(17);
    expect(content.scrollTo).not.toHaveBeenCalled();
    expect(stageList.scrollTo).not.toHaveBeenCalled();
    expect(selectedPanel.scrollTo).not.toHaveBeenCalled();
  }));

  it('stabilizes adjacent visible sections and ignores browser-toolbar-only viewport changes', fakeAsync(() => {
    component.ngOnInit();
    const { content } = activateTabletWorkspace(component);
    const choices = scrollContainer(620, 840);
    const summary = scrollContainer(110, 330);
    const stageList = scrollContainer(860, 1180);
    const selectedPanel = scrollContainer(860, 1180);
    content.getBoundingClientRect = () => ({ top: 100, bottom: 500 } as DOMRect);
    setPrivateElementRef(component, 'staffingChoices', choices);
    setPrivateElementRef(component, 'staffingSummary', summary);
    setPrivateElementRef(component, 'stageList', stageList);
    setPrivateElementRef(component, 'selectedStagePanel', selectedPanel);
    synchronizeTabletWorkspace(component);
    tick(17);

    triggerManualSectionScroll(component, 'content');
    tick(17);
    choices.getBoundingClientRect = () => ({ top: 110, bottom: 330 } as DOMRect);
    summary.getBoundingClientRect = () => ({ top: 620, bottom: 840 } as DOMRect);
    triggerManualSectionScroll(component, 'content');
    tick(117);

    expect(router.navigate.calls.allArgs().map(([, extras]) => (extras as { fragment: string }).fragment)).toEqual(['choices']);
    const navigationCount = router.navigate.calls.count();
    const measuredHeight = component.tabletWorkspaceHeightPx;
    content.getBoundingClientRect = () => ({ top: 260, bottom: 660 } as DOMRect);
    synchronizeTabletWorkspace(component);
    tick(117);

    expect(component.tabletWorkspaceHeightPx).toBe(measuredHeight);
    expect(router.navigate).toHaveBeenCalledTimes(navigationCount);
  }));

  it('does not move a section after stage selection, assignment save, or dialog close', () => {
    const { content } = activateTabletWorkspace(component);
    const contentScrollTo = jasmine.createSpy('contentScrollTo');
    const stageList = scrollContainer(0);
    const selectedPanel = scrollContainer(0);
    content.scrollTo = contentScrollTo;
    setPrivateElementRef(component, 'stageList', stageList);
    setPrivateElementRef(component, 'selectedStagePanel', selectedPanel);

    component.selectStage(temporaryStageId);
    component.selectStage(defaultStageId);
    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);
    component.saveAssignment();

    expect(contentScrollTo).not.toHaveBeenCalled();
    expect(stageList.scrollTo).not.toHaveBeenCalled();
    expect(selectedPanel.scrollTo).not.toHaveBeenCalled();
    expect(component.assignmentDialogVisible).toBeFalse();
    expect(document.documentElement.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeTrue();
  });

  it('keeps the tablet page lock active through explicit section navigation and PrimeNG dialog state', () => {
    const { content } = activateTabletWorkspace(component);
    setPrivateElementRef(component, 'staffingChoices', scrollContainer(150));
    component.selectedSubStageId = defaultStageId;
    navigateWithinWorkspace(component, 'choices');
    component.openDefaultAssignment();
    component.closeAssignmentDialog();

    expect(component.assignmentDialogVisible).toBeFalse();
    expect(document.documentElement.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeTrue();
    expect(document.body.classList.contains('plp-line-staffing-tablet-scroll-lock')).toBeTrue();

    component.ngOnDestroy();
  });

  it('preserves page, list, and worker-panel state when an assignment refreshes the selected stage', fakeAsync(() => {
    initialize(component);
    const stageList = { scrollTop: 138 } as HTMLElement;
    const selectedPanel = { scrollTop: 74 } as HTMLElement;
    (component as unknown as { stageList: { nativeElement: HTMLElement }; selectedStagePanel: { nativeElement: HTMLElement } }).stageList = { nativeElement: stageList };
    (component as unknown as { selectedStagePanel: { nativeElement: HTMLElement } }).selectedStagePanel = { nativeElement: selectedPanel };
    component.stageFilter = 'default';
    component.stageSearch = 'تجميع';
    const documentScroll = spyOn(document.documentElement, 'scrollTo');
    const windowScroll = spyOn(window, 'scrollTo');
    const getElementById = spyOn(document, 'getElementById');

    component.openDefaultAssignment();
    component.toggleDefaultWorker(component.availableWorkers[1], true);
    component.saveAssignment();
    flushMicrotasks();

    expect(component.assignmentDialogVisible).toBeFalse();
    expect(component.selectedSubStageId).toBe(defaultStageId);
    expect(component.stageFilter).toBe('default');
    expect(component.stageSearch).toBe('تجميع');
    expect(stageList.scrollTop).toBe(138);
    expect(selectedPanel.scrollTop).toBe(74);
    expect(documentScroll).not.toHaveBeenCalled();
    expect(windowScroll).not.toHaveBeenCalled();
    expect(getElementById).not.toHaveBeenCalled();
    expect(assignments.getLineStaffingPlan).toHaveBeenCalledTimes(1);
    expect(assignments.getActiveLineStaffingWorkers).toHaveBeenCalledTimes(1);
    expect(assignments.getLineStaffingStageRefresh).toHaveBeenCalledTimes(1);
  }));

  it('removes a cancelled last worker from details and updates the selected stage without a refresh', () => {
    initialize(component);
    assignments.getLineStaffingStageRefresh.and.returnValue(of(cancelledStageRefresh()));
    const worker = component.selectedStageWorkers[0];

    component.openCancellation(worker);
    component.assignmentForm.controls.reason.setValue('انتهاء المشاركة');
    component.saveAssignment();

    expect(component.selectedSubStageId).toBe(defaultStageId);
    expect(component.selectedStageWorkers).toEqual([]);
    expect(component.selectedStage?.effectiveAssignedWorkersCount).toBe(0);
    expect(component.selectedStage?.staffingStatus).toBe('NeedsStaffing');
    expect(component.selectedStage?.workerStatusText).toBe('لا يوجد عمال مسكنون');
  });

  it('keeps the previous worker details when cancellation fails', () => {
    initialize(component);
    assignments.removeDefaultAssignment.and.returnValue(throwError(() => ({ error: { detail: 'فشل الإلغاء' } })));
    const worker = component.selectedStageWorkers[0];

    component.openCancellation(worker);
    component.assignmentForm.controls.reason.setValue('اختبار الفشل');
    component.saveAssignment();

    expect(component.selectedStageWorkers.map(item => item.workerId)).toEqual(['worker-one']);
    expect(component.selectedStage?.effectiveAssignedWorkersCount).toBe(1);
    expect(component.assignmentDialogError).toContain('فشل الإلغاء');
    expect(assignments.getLineStaffingStageRefresh).not.toHaveBeenCalled();
  });

  it('keeps a worker assigned to another stage selectable for permanent assignment', () => {
    initialize(component);
    component.selectStage(temporaryStageId);
    component.openDefaultAssignment();
    const worker = component.availableWorkers.find(candidate => candidate.workerId === 'worker-one')!;

    expect(component.workerSelectionUnavailableMessage(worker)).toBeNull();
    expect(component.workerParticipationStageNames(worker)).toEqual(['تجميع']);
  });

  it('keeps permanent worker search behavior without opening a non-permanent dialog', () => {
    initialize(component);
    component.openDefaultAssignment();
    component.workerSearch = '101';

    expect(component.availableWorkers.map(worker => worker.workerId)).toEqual(['worker-two']);
    expect(component.assignmentDialogMode).toBe('default');
  });

  it('uses only the independently scrollable stage list for explicit next-stage navigation without moving focus', fakeAsync(() => {
    initialize(component);
    const scrollBy = jasmine.createSpy('scrollBy');
    const stageList = {
      scrollBy,
      getBoundingClientRect: () => ({ top: 0, bottom: 80 } as DOMRect)
    } as unknown as HTMLElement;
    const targetStage = document.createElement('button');
    targetStage.id = `staffing-stage-${temporaryStageId}`;
    document.body.appendChild(targetStage);
    spyOn(targetStage, 'getBoundingClientRect').and.returnValue({ top: 120, bottom: 160 } as DOMRect);
    const focus = spyOn(targetStage, 'focus');
    (component as unknown as { stageList: { nativeElement: HTMLElement } }).stageList = { nativeElement: stageList };

    component.nextStage();
    flushMicrotasks();

    expect(component.selectedSubStageId).toBe(temporaryStageId);
    expect(scrollBy).toHaveBeenCalledWith({ top: 80, behavior: 'smooth' });
    expect(focus).not.toHaveBeenCalled();
    targetStage.remove();
  }));

  it('restores stage-list and selected-panel scroll positions after an in-place refresh', fakeAsync(() => {
    initialize(component);
    const stageList = { scrollTop: 138 } as HTMLElement;
    const selectedPanel = { scrollTop: 74 } as HTMLElement;
    (component as unknown as { stageList: { nativeElement: HTMLElement }; selectedStagePanel: { nativeElement: HTMLElement } }).stageList = { nativeElement: stageList };
    (component as unknown as { selectedStagePanel: { nativeElement: HTMLElement } }).selectedStagePanel = { nativeElement: selectedPanel };
    const refresh = new Subject<LineStaffingPlan>();
    assignments.getLineStaffingPlan.and.returnValue(refresh);

    component.loadProductStages(true);
    stageList.scrollTop = 0;
    selectedPanel.scrollTop = 0;
    refresh.next(plan());
    flushMicrotasks();

    expect(component.selectedSubStageId).toBe(defaultStageId);
    expect(stageList.scrollTop).toBe(138);
    expect(selectedPanel.scrollTop).toBe(74);
  }));

  it('labels provisional SharedPercentage setup as a stage-cost configuration', () => {
    initialize(component);

    expect(component.compensationStatusLabel(component.selectedStage!)).toBe('إعداد تكلفة المرحلة مؤقت');
  });
});

function initialize(component: LineStaffingWorkspacePageComponent): void {
  component.ngOnInit();
  component.selectFactory(factoryId);
  component.selectDepartment(departmentId);
  component.selectProductionLine(lineId);
  component.selectProductModel(modelId);
  component.loadProductStages();
}

function selectionEvent(checked: boolean): Event {
  return {
    target: { checked },
    preventDefault: jasmine.createSpy('preventDefault'),
    stopPropagation: jasmine.createSpy('stopPropagation')
  } as unknown as Event;
}

function rowSelectionEvent(): Event {
  return {
    target: document.createElement('td'),
    preventDefault: jasmine.createSpy('preventDefault'),
    stopPropagation: jasmine.createSpy('stopPropagation')
  } as unknown as Event;
}

function stageRefresh() {
  const source = plan();
  const assignedWorker = {
    ...source.workers[1],
    defaultSubStageId: defaultStageId,
    defaultSubStageName: 'تجميع',
    effectiveAssignmentId: 'default-b',
    effectiveAssignmentType: 'Default' as const,
    effectiveSubStageId: defaultStageId,
    effectiveSubStageName: 'تجميع',
    participations: [{ assignmentId: 'default-b', assignmentType: 'Default' as const, subStageId: defaultStageId, subStageName: 'تجميع', fromSubStageId: null, fromSubStageName: null, startsAtUtc: null, endsAtUtc: null, replacementForWorkerId: null, temporaryParticipationMode: null }]
  };
  return {
    stage: { ...source.stages[0], defaultAssignedWorkersCount: 2, effectiveAssignedWorkersCount: 2, workerStatusText: 'يوجد عاملان', effectiveWorkerIds: ['worker-one', 'worker-two'] },
    stages: [{ ...source.stages[0], defaultAssignedWorkersCount: 2, effectiveAssignedWorkersCount: 2, workerStatusText: 'يوجد عاملان', effectiveWorkerIds: ['worker-one', 'worker-two'] }, source.stages[1]],
    workers: [source.workers[0], assignedWorker],
    stagesWithWorkers: 1,
    stagesWithoutWorkers: 1,
    stagesWithTemporaryAssignments: 0,
    stagesNeedingCompensationReview: 2,
    stagesNeedingStaffingReview: 0,
    overallStaffingStatus: 'NeedsStaffing',
    staffingPlanComplete: false,
    operationalAttendanceChecked: false,
    financialConfigurationPending: true
  };
}

function cancelledStageRefresh() {
  const source = plan();
  const clearedWorker = {
    ...source.workers[0],
    defaultSubStageId: null,
    defaultSubStageName: null,
    effectiveAssignmentId: null,
    effectiveAssignmentType: null,
    effectiveSubStageId: null,
    effectiveSubStageName: null,
    participations: []
  };
  const stage = {
    ...source.stages[0],
    defaultAssignedWorkersCount: 0,
    effectiveAssignedWorkersCount: 0,
    temporaryAssignedWorkersCount: 0,
    staffingStatus: 'NeedsStaffing',
    workerStatusText: 'لا يوجد عمال مسكنون',
    effectiveWorkerIds: []
  };
  return {
    stage,
    stages: [stage, source.stages[1]],
    workers: [clearedWorker, source.workers[1]],
    stagesWithWorkers: 0,
    stagesWithoutWorkers: 2,
    stagesWithTemporaryAssignments: 0,
    stagesNeedingCompensationReview: 2,
    stagesNeedingStaffingReview: 0,
    overallStaffingStatus: 'NeedsStaffing',
    staffingPlanComplete: false,
    operationalAttendanceChecked: false,
    financialConfigurationPending: true
  };
}

function activateTabletWorkspace(component: LineStaffingWorkspacePageComponent): { workspace: HTMLElement; content: HTMLElement } {
  const workspace = scrollContainer(650);
  const content = scrollContainer(120);
  component.plan = plan();
  component.selectedSubStageId = defaultStageId;
  setPrivateElementRef(component, 'workspace', workspace);
  setPrivateElementRef(component, 'tabletContent', content);
  (component as unknown as { tabletWorkspaceMediaQuery: { matches: boolean } }).tabletWorkspaceMediaQuery = { matches: true };
  synchronizeTabletWorkspace(component);
  return { workspace, content };
}

function scrollContainer(top: number, bottom = top + 100): HTMLElement {
  const element = document.createElement('div');
  Object.defineProperty(element, 'scrollTop', { configurable: true, writable: true, value: 0 });
  element.scrollTo = jasmine.createSpy('scrollTo') as unknown as typeof element.scrollTo;
  element.getBoundingClientRect = () => ({ top, bottom } as DOMRect);
  return element;
}

function setPrivateElementRef(component: LineStaffingWorkspacePageComponent, property: string, nativeElement: HTMLElement): void {
  (component as unknown as Record<string, unknown>)[property] = { nativeElement };
}

function navigateWithinWorkspace(component: LineStaffingWorkspacePageComponent, section: 'choices' | 'summary' | 'stages' | 'workers'): boolean {
  return (component as unknown as { scrollToStaffingSection: (target: typeof section) => boolean }).scrollToStaffingSection(section);
}

function triggerManualSectionScroll(component: LineStaffingWorkspacePageComponent, section: 'content' | 'workers'): void {
  const privateComponent = component as unknown as {
    onTabletContentScroll: () => void;
    onSelectedStagePanelScroll: () => void;
  };
  if (section === 'workers') privateComponent.onSelectedStagePanelScroll();
  else privateComponent.onTabletContentScroll();
}

function synchronizeTabletWorkspace(component: LineStaffingWorkspacePageComponent): void {
  (component as unknown as { synchronizeTabletWorkspaceContainment: () => void }).synchronizeTabletWorkspaceContainment();
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
      { productModelStageId: 'stage-two', subStageId: temporaryStageId, mainStageName: 'تشطيب', stageCode: 'S2', stageName: 'تشطيب', stageOrder: 2, piecePrice: .38, compensationMode: 'SharedPercentage', compensationConfigurationStatus: 'FinancialReviewPending', isFinancialReviewPending: true, defaultAssignedWorkersCount: 0, effectiveAssignedWorkersCount: 0, temporaryAssignedWorkersCount: 0, requiredWorkers: null, hasAuthoritativeRequiredWorkerCount: false, staffingStatus: 'NeedsStaffing', workerStatusText: 'لا يوجد عمال مسكنون', effectiveWorkerIds: [] }
    ],
    workers: [
      { workerId: 'worker-one', employeeCode: '100', fullName: 'عامل أول', departmentName: 'التجميع', isOnActiveService: true, hasPhoto: false, photoReference: null, photoVersion: null, defaultSubStageId: defaultStageId, defaultSubStageName: 'تجميع', effectiveAssignmentId: 'default-a', effectiveAssignmentType: 'Default', effectiveSubStageId: defaultStageId, effectiveSubStageName: 'تجميع', fromSubStageId: null, fromSubStageName: null, temporaryStartsAtUtc: null, temporaryEndsAtUtc: null, replacementForWorkerId: null, participations: [{ assignmentId: 'default-a', assignmentType: 'Default', subStageId: defaultStageId, subStageName: 'تجميع', fromSubStageId: null, fromSubStageName: null, startsAtUtc: null, endsAtUtc: null, replacementForWorkerId: null, temporaryParticipationMode: null }] },
      { workerId: 'worker-two', employeeCode: '101', fullName: 'عامل ثان', departmentName: 'التشطيب', isOnActiveService: true, hasPhoto: true, photoReference: '/api/workers/worker-two/photo?v=photo-v1', photoVersion: 'photo-v1', defaultSubStageId: null, defaultSubStageName: null, effectiveAssignmentId: null, effectiveAssignmentType: null, effectiveSubStageId: null, effectiveSubStageName: null, fromSubStageId: null, fromSubStageName: null, temporaryStartsAtUtc: null, temporaryEndsAtUtc: null, replacementForWorkerId: null, participations: [] }
    ]
  };
}
