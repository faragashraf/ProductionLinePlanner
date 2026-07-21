import { Observable, Subject, of, throwError } from 'rxjs';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { DailyProductionOperationsPageComponent } from './daily-production-operations-page.component';
import { DailyProductionPreview } from '../../core/services/production-cost-recording-api.service';
import { ProductionCostRecordingApiService } from '../../core/services/production-cost-recording-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { AttendanceApiService } from '../../core/services/attendance-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';
import { ManufacturingWorkspaceModule } from './manufacturing-workspace.module';

describe('DailyProductionOperationsPageComponent unified preview', () => {
  let component: DailyProductionOperationsPageComponent;
  let production: jasmine.SpyObj<any>;
  let masterData: jasmine.SpyObj<any>;
  let attendance: jasmine.SpyObj<any>;
  let realtime: jasmine.SpyObj<any>;
  let stopRealtime: jasmine.Spy;
  let watchConfig: { refresh: () => void; matches?: (change: any) => boolean };
  let grantedPermissions: Set<string>;

  const preview: DailyProductionPreview = {
    productionDate: '2026-07-16',
    lineQuantity: 500,
    previewToken: 'preview-token',
    totalWorkerEntitlements: 250,
    stages: [{
      productModelStageId: 'stage-1', stageCode: 'S1', stageName: 'مرحلة 1', stageQuantity: 500,
      stageCost: 250, compensationMode: 'SharedPercentage', warnings: [],
      workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', percentage: 100, equivalentQuantity: 500, calculatedEarning: 250 }]
    }],
    workerTotals: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', totalEntitlement: 250 }],
    warnings: []
  };

  beforeEach(() => {
    grantedPermissions = new Set([
      PERMISSIONS.production.view,
      PERMISSIONS.production.record,
      PERMISSIONS.production.approve,
      PERMISSIONS.assignments.manage
    ]);
    production = jasmine.createSpyObj('ProductionCostRecordingApiService', ['previewDailyOperations', 'loadDailyOperations', 'saveDailyDraft', 'approveDailyOperation', 'cancelDailyOperationApproval']);
    masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    attendance = jasmine.createSpyObj('AttendanceApiService', ['syncForProductionDate']);
    stopRealtime = jasmine.createSpy('stopRealtime');
    realtime = jasmine.createSpyObj('ManufacturingRealtimeService', ['watchScreen', 'registerLocalOperation']);
    realtime.watchScreen.and.callFake((config: any) => {
      watchConfig = config;
      return stopRealtime;
    });
    component = new DailyProductionOperationsPageComponent(
      masterData,
      attendance,
      production,
      { hasPermission: (permission: string) => grantedPermissions.has(permission) } as any,
      { serverMessage: (error: any, fallback: string) => error?.error?.detail ?? fallback } as any,
      realtime
    );
    component.operations = {
      factoryId: 'factory-1', factoryName: 'Factory', productionLineId: 'line-1', productionLineName: 'Line',
      productModelId: 'model-1', productModelCode: 'M1', productModelName: 'Model', productionDate: '2026-07-16',
      staffingContextVersion: 'context', totalStages: 1, readyStages: 1, stagesWithAbsentWorkers: 0,
      stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0,
      activeWorkers: [], stages: []
    };
    component.selectedFactoryId = 'factory-1';
    component.selectedProductionLineId = 'line-1';
    component.selectedProductModelId = 'model-1';
    component.productionDate = '2026-07-16';
    component.lineQuantity = 500;
    component.stages = [{
      productModelStageId: 'stage-1', subStageId: 'sub-stage-1', mainStageName: 'Main', stageCode: 'S1',
      stageName: 'مرحلة 1', stageOrder: 1, piecePrice: .5, compensationMode: 'SharedPercentage',
      staffingStatus: 'Staffed', attendanceStatus: 'Ready', hasAbsentWorkers: false, hasNoSourceCheckInWorkers: false,
      isFinancialReviewPending: false, isReady: true,
      workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', isOnActiveService: true,
        effectiveAssignmentType: 'Permanent', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
        requiresAuthorizedOverride: false, suggestedPercentage: 100, contributionStartsAtUtc: '2026-07-16T05:00:00Z', contributionEndsAtUtc: '2026-07-16T13:00:00Z', workerMinutes: 480, isProductionReady: true,
        isAssignedWorker: true, isDailyOverride: false, includedInProduction: true,
        percentage: 100, quantity: 500, fixedAmount: null, notes: '', manualOverrideReason: '' }]
    } as any];
  });

  it('starts attendance synchronization only from the explicit manual action and blocks duplicate clicks', () => {
    const pending = new Subject<any>();
    masterData.factories.and.returnValue(of([]));
    attendance.syncForProductionDate.and.returnValue(pending);

    component.ngOnInit();
    expect(attendance.syncForProductionDate).not.toHaveBeenCalled();

    component.synchronizeAttendance();
    component.synchronizeAttendance();

    expect(attendance.syncForProductionDate).toHaveBeenCalledTimes(1);
    expect(component.attendanceSyncing).toBeTrue();
    pending.complete();
    expect(component.attendanceSyncing).toBeFalse();
  });

  it('reloads current operations for a matching daily-production realtime event without local edits', () => {
    masterData.factories.and.returnValue(of([]));
    production.loadDailyOperations.and.returnValue(of(component.operations));
    component.attendanceSyncedForDate = component.productionDate;

    component.ngOnInit();
    expect(watchConfig.matches?.(dailyChange())).toBeTrue();
    watchConfig.refresh();

    expect(production.loadDailyOperations).toHaveBeenCalledTimes(1);
  });

  it('does not reload current operations for a different daily-production context', () => {
    masterData.factories.and.returnValue(of([]));
    production.loadDailyOperations.and.returnValue(of(component.operations));
    component.attendanceSyncedForDate = component.productionDate;

    component.ngOnInit();
    expect(watchConfig.matches?.(dailyChange({ productionLineId: 'another-line' }))).toBeFalse();
    expect(watchConfig.matches?.(dailyChange({ productionDate: '2000-01-01' }))).toBeFalse();

    expect(production.loadDailyOperations).not.toHaveBeenCalled();
  });

  it('shows a reload notice instead of overwriting matching local edits', () => {
    masterData.factories.and.returnValue(of([]));
    component.attendanceSyncedForDate = component.productionDate;
    component.stageChanged();

    component.ngOnInit();
    expect(watchConfig.matches?.(dailyChange())).toBeTrue();
    watchConfig.refresh();

    expect(component.hasPendingRemoteUpdate).toBeTrue();
    expect(component.remoteUpdateMessage).toContain('تعديلاتك غير المحفوظة');
    expect(production.loadDailyOperations).not.toHaveBeenCalled();
  });

  it('releases the daily-production realtime watcher on destroy', () => {
    masterData.factories.and.returnValue(of([]));

    component.ngOnInit();
    component.ngOnDestroy();

    expect(stopRealtime).toHaveBeenCalledTimes(1);
  });

  it('sends exactly one request for repeated clicks while preview is active and renders its response without a page reload', () => {
    const pending = new Subject<DailyProductionPreview>();
    production.previewDailyOperations.and.returnValue(pending.asObservable());

    component.calculatePreview();
    component.calculatePreview();

    expect(production.previewDailyOperations).toHaveBeenCalledTimes(1);
    expect(masterData.factories).not.toHaveBeenCalled();
    expect(production.loadDailyOperations).not.toHaveBeenCalled();
    pending.next(preview);
    pending.complete();

    expect(component.preview).toEqual(preview);
    expect(component.previewing).toBeFalse();
  });

  it('does not cancel the active preview when a material UI change invalidates it', () => {
    let teardownCalls = 0;
    production.previewDailyOperations.and.returnValue(new Observable<DailyProductionPreview>(() => () => teardownCalls++));

    component.calculatePreview();
    component.lineQuantity = 501;
    component.lineQuantityChanged();

    expect(teardownCalls).toBe(0);
    expect(component.previewing).toBeTrue();
  });

  it('keeps the same ClientRequestId when a failed preview is retried with unchanged inputs', () => {
    production.previewDailyOperations.and.returnValues(
      throwError(() => ({ error: { detail: 'تعذر الاتصال بالخادم.' } })),
      of(preview)
    );

    component.calculatePreview();
    expect(component.error).toBe('تعذر الاتصال بالخادم.');
    component.calculatePreview();

    const firstRequest = production.previewDailyOperations.calls.argsFor(0)[0];
    const retryRequest = production.previewDailyOperations.calls.argsFor(1)[0];
    expect(firstRequest.clientRequestId).toBe(retryRequest.clientRequestId);
    expect(component.preview).toEqual(preview);
  });

  function dailyChange(overrides: Record<string, unknown> = {}): Record<string, unknown> {
    return {
      eventId: 'event-1',
      entityType: 'ProductionOrder',
      changeType: 'Updated',
      entityId: 'order-1',
      occurredAtUtc: new Date().toISOString(),
      actorUserId: null,
      correlationId: null,
      factoryId: component.selectedFactoryId,
      departmentId: null,
      productionLineId: component.selectedProductionLineId,
      mainStageId: null,
      productModelId: component.selectedProductModelId,
      subStageId: null,
      productionDate: component.productionDate,
      ...overrides
    };
  }

  it('updates the minute-weighted quantity preview immediately when line quantity changes', () => {
    const stage = component.stages[0];
    stage.workers[0].percentage = 25;
    component.lineQuantity = 200;

    component.lineQuantityChanged();

    expect(component.calculatedWorkerQuantity(stage, stage.workers[0])).toBe(50);
    expect(component.exclusionReasonLabel('NoTemporalIntersection')).toBe('لا يوجد تقاطع زمني');
  });

  it('imports assigned ready, absent, and incomplete-attendance workers into the stage without manual selection', () => {
    component.attendanceSyncedForDate = component.productionDate;
    production.loadDailyOperations.and.returnValue(of({
      ...component.operations,
      stages: [{
        ...component.stages[0],
        workers: [
          { ...component.stages[0].workers[0], isProductionReady: true, exclusionReason: null },
          { ...component.stages[0].workers[0], workerId: 'worker-2', workerCode: 'W2', workerName: 'عامل غائب', attendanceStatus: 'Absent', hasSourceCheckIn: false, isPresent: false, isProductionReady: false, workerMinutes: 0, suggestedPercentage: null, exclusionReason: 'Absent' },
          { ...component.stages[0].workers[0], workerId: 'worker-3', workerCode: 'W3', workerName: 'حضور غير مكتمل', isProductionReady: false, workerMinutes: 0, suggestedPercentage: null, contributionEndsAtUtc: null, exclusionReason: 'IncompleteAttendance' }
        ]
      }]
    }));

    component.loadTodayOperations();

    expect(production.loadDailyOperations).toHaveBeenCalledTimes(1);
    expect(component.stages[0].workers.map(worker => worker.workerId)).toEqual(['worker-1', 'worker-2', 'worker-3']);
    expect(component.stages[0].workers.map(worker => worker.includedInProduction)).toEqual([true, false, false]);
    expect(component.dailyStaffingLabel(component.stages[0].workers[1])).toBe('مسكن — غائب');
    expect(component.dailyStaffingLabel(component.stages[0].workers[2])).toBe('مسكن — حضور غير مكتمل');
  });

  it('deduplicates assigned workers by stable id and keeps daily overrides separate from permanent staffing', () => {
    const stage = component.stages[0];
    const override = {
      ...stage.workers[0],
      workerId: 'worker-override',
      workerCode: 'OVR',
      workerName: 'بديل اليوم',
      isAssignedWorker: false,
      isDailyOverride: false,
      suggestedPercentage: null,
      workerMinutes: 240
    };
    component.operations!.activeWorkers = [override];
    component.selectedStageId = stage.productModelStageId;
    component.replacementWorkerId = override.workerId;

    component.addReplacementWorker();
    const added = stage.workers.find(worker => worker.workerId === override.workerId)!;
    added.manualOverrideReason = 'بديل لهذا اليوم فقط';
    production.previewDailyOperations.and.returnValue(of(preview));
    component.calculatePreview();

    expect(stage.workers[0].isAssignedWorker).toBeTrue();
    expect(added.isDailyOverride).toBeTrue();
    const request = production.previewDailyOperations.calls.mostRecent().args[0];
    expect(request.stages[0].workers.map((worker: any) => worker.workerId)).toEqual(['worker-1', 'worker-override']);
  });

  it('recomputes shared percentages from ready worker minutes after a daily participant change', () => {
    const stage = component.stages[0];
    stage.workers = [
      { ...stage.workers[0], workerId: 'worker-a', workerMinutes: 300 },
      { ...stage.workers[0], workerId: 'worker-b', workerMinutes: 180 }
    ];
    component.lineQuantity = 500;

    component.applyEqualDistribution(stage, false);

    expect(stage.workers.map(worker => worker.percentage)).toEqual([62.5, 37.5]);
    expect(stage.workers.reduce((total, worker) => total + component.calculatedWorkerQuantity(stage, worker), 0)).toBe(500);
  });

  it('edits shared allocation bidirectionally and updates the paired value immediately', () => {
    const stage = component.stages[0];
    const worker = stage.workers[0];

    component.updateWorkerPercentage(stage, worker, 25);
    expect(worker.percentage).toBe(25);
    expect(worker.quantity).toBe(125);

    component.updateWorkerQuantity(stage, worker, 200);
    expect(worker.quantity).toBe(200);
    expect(worker.percentage).toBe(40);
  });

  it('reconciles rounding when entered worker quantities equal the stage quantity', () => {
    const stage = component.stages[0];
    stage.workers = [
      { ...stage.workers[0], workerId: 'worker-a', percentage: null, quantity: null },
      { ...stage.workers[0], workerId: 'worker-b', percentage: null, quantity: null },
      { ...stage.workers[0], workerId: 'worker-c', percentage: null, quantity: null }
    ];

    component.updateWorkerQuantity(stage, stage.workers[0], 166.667);
    component.updateWorkerQuantity(stage, stage.workers[1], 166.667);
    component.updateWorkerQuantity(stage, stage.workers[2], 166.666);

    expect(component.stageAllocationQuantity(stage)).toBe(500);
    expect(component.stageAllocationPercentage(stage)).toBe(100);
    expect(component.stageAllocationStatusLabel(stage)).toBe('التوزيع متوازن');
  });

  it('blocks a worker quantity that exceeds the stage quantity before preview', () => {
    const stage = component.stages[0];
    component.updateWorkerQuantity(stage, stage.workers[0], 501);

    component.calculatePreview();

    expect(production.previewDailyOperations).not.toHaveBeenCalled();
    expect(component.validationMessages.join(' ')).toContain('راجع قيم العمال');
  });

  it('builds stage and worker views from one id-based allocation projection', () => {
    component.stages[0].workers.push({
      ...component.stages[0].workers[0],
      workerId: 'worker-excluded',
      workerCode: 'WX',
      workerName: 'عامل غير جاهز',
      isProductionReady: false,
      includedInProduction: false,
      percentage: null,
      quantity: null,
      workerMinutes: 0,
      exclusionReason: 'IncompleteAttendance'
    });

    component.preview = preview;

    const stageRow = component.stageAllocationRows[0];
    const workerRow = component.workerAllocationRows[0];
    expect(stageRow.stageId).toBe('stage-1');
    expect(stageRow.workers).toHaveSize(2);
    expect(stageRow.workers.find(worker => worker.workerId === 'worker-excluded')?.isCalculated).toBeFalse();
    expect(stageRow.participantCount).toBe(1);
    expect(stageRow.totalEntitlement).toBe(250);
    expect(workerRow.workerId).toBe('worker-1');
    expect(workerRow.stages[0].stageId).toBe(stageRow.stageId);
    expect(workerRow.totalEntitlement).toBe(stageRow.totalEntitlement);
    expect(workerRow.contributionStartsAtUtc).toBe(stageRow.workers[0].contributionStartsAtUtc);
    expect(workerRow.contributionEndsAtUtc).toBe(stageRow.workers[0].contributionEndsAtUtc);
    expect(workerRow.workerMinutes).toBe(stageRow.workers[0].workerMinutes);
    expect(workerRow.participationType).toBe(stageRow.workers[0].participationType);
  });

  it('keeps stage and worker totals consistent across multiple stages', () => {
    component.stages.push({
      ...component.stages[0],
      productModelStageId: 'stage-2',
      stageCode: 'S2',
      stageName: 'مرحلة 2',
      workers: [{ ...component.stages[0].workers[0] }]
    });
    component.preview = {
      ...preview,
      totalWorkerEntitlements: 400,
      stages: [
        preview.stages[0],
        { ...preview.stages[0], productModelStageId: 'stage-2', stageCode: 'S2', stageName: 'مرحلة 2', stageCost: 150,
          workers: [{ ...preview.stages[0].workers[0], equivalentQuantity: 300, calculatedEarning: 150 }] }
      ],
      workerTotals: [{ ...preview.workerTotals[0], totalEntitlement: 400 }]
    };

    const stageTotal = component.stageAllocationRows.reduce((total, stage) => total + stage.totalEntitlement, 0);
    const workerTotal = component.workerAllocationRows.reduce((total, worker) => total + worker.totalEntitlement, 0);
    expect(stageTotal).toBe(400);
    expect(workerTotal).toBe(stageTotal);
    expect(component.workerAllocationRows[0].stageCount).toBe(2);
    expect(component.workerAllocationRows[0].totalAllocatedQuantity).toBe(800);
  });

  it('keeps the technical draft id internally without exposing it in the success copy', () => {
    component.savedDraft = {
      productionOrderId: 'order-1', orderNumber: 'DLY-20260716-001', productionDate: '2026-07-16',
      recordedAtUtc: '2026-07-16T13:00:00Z', lineQuantity: 500, wasAlreadySaved: false,
      stages: [{}, {}] as any
    };

    expect(component.savedDraft.orderNumber).toBe('DLY-20260716-001');
    expect(component.savedDraftTitle).toContain('تم حفظ مسودة تشغيل يوم');
    expect(component.savedDraftTitle).not.toContain('DLY-');
    expect(component.savedDraftDetail).toContain('تم حفظ 2 مرحلة');
  });

  it('allows an explicit correction of the existing daily draft without creating a second draft', () => {
    component.operations!.existingDraft = {
      productionOrderId: 'order-1', orderNumber: 'DLY-1', productionDate: component.productionDate,
      recordedAtUtc: '2026-07-16T13:00:00Z', lineQuantity: 500, wasAlreadySaved: true,
      stages: [{ id: 'record-1', concurrencyToken: 'token-1', status: 'Draft' }] as any
    };
    component.preview = preview;
    (component as any).previewRevision = (component as any).revision;
    production.saveDailyDraft.and.returnValue(of({ ...component.operations!.existingDraft, wasAlreadySaved: false }));

    component.saveDailyDraft();

    expect(production.saveDailyDraft).toHaveBeenCalledTimes(1);
    expect(component.savedDraft?.productionOrderId).toBe('order-1');
  });

  it('keeps navigation available for a view-only user while blocking data edits', () => {
    grantedPermissions = new Set([PERMISSIONS.production.view]);
    component.selectedStageId = '';
    const worker = component.stages[0].workers[0];

    component.selectStage('stage-1');
    component.stageSearch = 'S1';
    component.updateWorkerPercentage(component.stages[0], worker, 50);

    expect(component.canView).toBeTrue();
    expect(component.isReadOnly).toBeTrue();
    expect(component.selectedStage?.productModelStageId).toBe('stage-1');
    expect(component.filteredStages).toHaveSize(1);
    expect(worker.percentage).toBe(100);
  });

  it('lets an approver-only user cancel an approved operation without enabling draft edits', () => {
    grantedPermissions = new Set([PERMISSIONS.production.approve]);
    component.savedDraft = approvedDailyDraft();
    const worker = component.stages[0].workers[0];

    component.openDailyApprovalCancellationDialog();
    component.updateWorkerQuantity(component.stages[0], worker, 250);
    component.saveDailyDraft();

    expect(component.canView).toBeTrue();
    expect(component.canEditDraft).toBeFalse();
    expect(component.canCancelDailyOperationApproval).toBeTrue();
    expect(component.dailyApprovalCancellationDialogVisible).toBeTrue();
    expect(worker.quantity).toBe(500);
    expect(production.saveDailyDraft).not.toHaveBeenCalled();
  });

  it('allows an editor to change a draft but prevents direct edits after approval', () => {
    grantedPermissions = new Set([PERMISSIONS.production.view, PERMISSIONS.production.record]);
    const worker = component.stages[0].workers[0];

    component.updateWorkerPercentage(component.stages[0], worker, 50);
    expect(component.canEditDraft).toBeTrue();
    expect(worker.percentage).toBe(50);

    component.savedDraft = approvedDailyDraft();
    component.updateWorkerPercentage(component.stages[0], worker, 25);

    expect(component.isApproved).toBeTrue();
    expect(component.isReadOnly).toBeTrue();
    expect(worker.percentage).toBe(50);
  });

  it('requires approved-stage concurrency data before exposing cancellation', () => {
    grantedPermissions = new Set([PERMISSIONS.production.approve]);
    component.savedDraft = { ...approvedDailyDraft(), stages: [{ id: 'record-1', concurrencyToken: '', status: 'Approved' }] as any };

    expect(component.canCancelDailyOperationApproval).toBeFalse();
  });

  it('shows daily approval cancellation only for an approved daily operation and requires a reason', () => {
    component.savedDraft = approvedDailyDraft();

    expect(component.canCancelDailyOperationApproval).toBeTrue();
    component.openDailyApprovalCancellationDialog();
    component.confirmDailyApprovalCancellation();

    expect(component.dailyApprovalCancellationDialogVisible).toBeTrue();
    expect(component.error).toContain('سبب إلغاء اعتماد تشغيل اليوم مطلوب');
    expect(production.cancelDailyOperationApproval).not.toHaveBeenCalled();
  });

  it('cancels all approved daily stages once and reloads the corrected daily context', () => {
    component.savedDraft = approvedDailyDraft();
    component.attendanceSyncedForDate = component.productionDate;
    production.cancelDailyOperationApproval.and.returnValue(of({ productionOrderId: 'order-1', orderStatus: 'Draft', cancelledAtUtc: '2026-07-16T14:00:00Z', cancelledStageCount: 1 }));
    production.loadDailyOperations.and.returnValue(of(component.operations));

    component.openDailyApprovalCancellationDialog();
    component.dailyApprovalCancellationReason = 'تصحيح كمية التشغيل';
    component.confirmDailyApprovalCancellation();
    component.confirmDailyApprovalCancellation();

    expect(production.cancelDailyOperationApproval).toHaveBeenCalledTimes(1);
    expect(production.cancelDailyOperationApproval).toHaveBeenCalledWith(
      'order-1',
      [{ stageProductionRecordId: 'record-1', concurrencyToken: 'approved-token' }],
      'تصحيح كمية التشغيل',
      undefined
    );
    expect(production.loadDailyOperations).toHaveBeenCalledTimes(1);
  });

  function approvedDailyDraft() {
    return {
      productionOrderId: 'order-1', orderNumber: 'DLY-1', productionDate: '2026-07-16',
      recordedAtUtc: '2026-07-16T13:00:00Z', lineQuantity: 500, wasAlreadySaved: false,
      stages: [{ id: 'record-1', concurrencyToken: 'approved-token', status: 'Approved' }] as any
    };
  }
});

describe('DailyProductionOperationsPageComponent visual hierarchy', () => {
  it('uses contained stage scrolling and structured stage, worker, preview, and entitlement regions', () => {
    const production = jasmine.createSpyObj('ProductionCostRecordingApiService', ['previewDailyOperations', 'loadDailyOperations', 'saveDailyDraft']);
    const masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    const attendance = jasmine.createSpyObj('AttendanceApiService', ['syncForProductionDate']);
    masterData.factories.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: ProductionCostRecordingApiService, useValue: production },
        { provide: ManufacturingMasterDataApiService, useValue: masterData },
        { provide: AttendanceApiService, useValue: attendance },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: FormSubmissionValidationService, useValue: { serverMessage: (_: unknown, fallback: string) => fallback } }
      ]
    });

    const fixture = TestBed.createComponent(DailyProductionOperationsPageComponent);
    const component = fixture.componentInstance;
    const stage = {
      productModelStageId: 'stage-1', subStageId: 'sub-stage-1', mainStageName: 'التجميع', stageCode: 'ST-01',
      stageName: 'تجميع الكتف', stageOrder: 1, piecePrice: .5, compensationMode: 'SharedPercentage',
      staffingStatus: 'Staffed', attendanceStatus: 'Ready', hasAbsentWorkers: false, hasNoSourceCheckInWorkers: false,
      isFinancialReviewPending: false, isReady: true,
      workers: [{ workerId: 'worker-1', workerCode: 'W-001', workerName: 'Worker One', isOnActiveService: true,
        effectiveAssignmentType: 'Default', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
          requiresAuthorizedOverride: false, suggestedPercentage: 100, contributionStartsAtUtc: '2026-07-17T04:33:00Z', contributionEndsAtUtc: '2026-07-17T16:07:00Z', workerMinutes: 694, isProductionReady: true,
          isAssignedWorker: true, isDailyOverride: false, includedInProduction: true,
          percentage: 100, quantity: 500, fixedAmount: null, notes: '', manualOverrideReason: '' },
        { workerId: 'worker-2', workerCode: 'W-002', workerName: 'عامل بحضور قديم', isOnActiveService: true,
          effectiveAssignmentType: 'Default', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
          requiresAuthorizedOverride: false, suggestedPercentage: null, contributionStartsAtUtc: null, contributionEndsAtUtc: null,
          workerMinutes: 0, isProductionReady: false, exclusionReason: 'IncompleteAttendance', isAssignedWorker: true,
          isDailyOverride: false, includedInProduction: false, percentage: null, quantity: null, fixedAmount: null, notes: '', manualOverrideReason: '' }]
    } as any;
    component.operations = {
      factoryId: 'factory-1', factoryName: 'مصنع القاهرة', productionLineId: 'line-1', productionLineName: 'خط التجميع',
      productModelId: 'model-1', productModelCode: 'M-01', productModelName: 'موديل اختبار', productionDate: '2026-07-17',
      staffingContextVersion: 'context', totalStages: 24, readyStages: 24, stagesWithAbsentWorkers: 0,
      stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0, activeWorkers: [], stages: []
    };
    component.stages = Array.from({ length: 24 }, (_, index) => ({
      ...stage,
      productModelStageId: `stage-${index + 1}`,
      stageCode: `ST-${String(index + 1).padStart(2, '0')}`,
      stageName: index === 0
        ? 'مرحلة تجميع طويلة لاختبار التفاف الاسم داخل البطاقة دون قص أو تداخل'
        : `مرحلة ${index + 1}`,
      stageOrder: index + 1
    }));
    component.selectedStageId = 'stage-1';
    component.lineQuantity = 500;
    component.preview = {
      productionDate: '2026-07-17', lineQuantity: 500, previewToken: 'preview-token', totalWorkerEntitlements: 250,
      stages: [{ productModelStageId: 'stage-1', stageCode: 'ST-01', stageName: 'تجميع الكتف', stageQuantity: 500,
        stageCost: 250, compensationMode: 'SharedPercentage', warnings: [], workers: [
          { workerId: 'worker-1', workerCode: 'W-001', workerName: 'Worker One', percentage: 100, equivalentQuantity: 500, calculatedEarning: 250 },
          { workerId: '', workerCode: '', workerName: '', equivalentQuantity: 0, calculatedEarning: 0 }
        ] }],
      workerTotals: [{ workerId: 'worker-1', workerCode: 'W-001', workerName: 'Worker One', totalEntitlement: 250 }],
      warnings: []
    };
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    const stageButtons = fixture.nativeElement.querySelectorAll('.daily-production-operations__stage') as NodeListOf<HTMLButtonElement>;
    const workerTotalRow = fixture.nativeElement.querySelector('.daily-production-operations__worker-totals tbody tr') as HTMLTableRowElement;

    expect(fixture.nativeElement.querySelector('.plp-bounded-workspace--viewport')).toBeNull();
    const workspace = fixture.nativeElement.querySelector('[data-workspace-layout="stage-master-detail"]') as HTMLElement;
    expect(workspace.getAttribute('data-tablet-priority')).toBe('workers');
    expect(fixture.nativeElement.querySelector('.plp-bounded-workspace__panel--viewport')).toBeNull();
    expect(fixture.nativeElement.querySelector('.daily-production-operations__stage-panel')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.daily-production-operations__detail-panel')).not.toBeNull();
    const stageList = fixture.nativeElement.querySelector('.daily-production-operations__stage-list.plp-contained-scroll-list') as HTMLElement;
    expect(stageList).not.toBeNull();
    expect(getComputedStyle(stageList).overflowY).toBe('auto');
    expect(stageButtons).toHaveSize(24);
    expect(stageButtons[23].textContent).toContain('مرحلة 24');
    const firstStage = stageButtons[0];
    const secondStage = stageButtons[1];
    const stageSecondaryRow = firstStage.querySelector('.plp-entity-secondary-row') as HTMLElement;
    const firstStageRect = firstStage.getBoundingClientRect();
    const secondStageRect = secondStage.getBoundingClientRect();
    const secondaryRowRect = stageSecondaryRow.getBoundingClientRect();
    const firstStageStyle = getComputedStyle(firstStage);

    expect(firstStage.querySelector('plp-responsive-entity-row')).not.toBeNull();
    expect(firstStage.classList).toContain('daily-production-operations__stage--compact');
    expect(firstStage.querySelectorAll('.plp-compact-facts > div')).toHaveSize(4);
    expect(firstStage.classList).toContain('plp-structured-card--content-sized');
    expect(firstStage.style.blockSize).toBe('');
    expect(firstStageStyle.position).not.toBe('absolute');
    expect(firstStageStyle.minBlockSize).not.toBe('0px');
    expect(firstStageStyle.overflow).toBe('visible');
    expect(stageSecondaryRow).not.toBeNull();
    expect(stageSecondaryRow.querySelectorAll('plp-status-badge')).toHaveSize(2);
    expect(firstStage.querySelector('[plp-entity-status]')).toBeNull();
    expect(secondaryRowRect.bottom)
      .withContext(JSON.stringify({ card: firstStageRect.toJSON(), secondary: secondaryRowRect.toJSON(), display: firstStageStyle.display, blockSize: firstStageStyle.blockSize }))
      .toBeLessThanOrEqual(firstStageRect.bottom + 1);
    expect(secondStageRect.top).toBeGreaterThanOrEqual(firstStageRect.bottom);
    expect(firstStage.classList).toContain('is-selected');

    [800, 1280, 1440].forEach((viewportWidth) => {
      stageList.style.inlineSize = `${viewportWidth}px`;
      const cardRect = firstStage.getBoundingClientRect();
      const metadataRect = stageSecondaryRow.getBoundingClientRect();
      const nextCardRect = secondStage.getBoundingClientRect();

      expect(metadataRect.left).withContext(`viewport ${viewportWidth}`).toBeGreaterThanOrEqual(cardRect.left - 1);
      expect(metadataRect.right).withContext(`viewport ${viewportWidth}`).toBeLessThanOrEqual(cardRect.right + 1);
      expect(metadataRect.bottom).withContext(`viewport ${viewportWidth}`).toBeLessThanOrEqual(cardRect.bottom + 1);
      expect(nextCardRect.top).withContext(`viewport ${viewportWidth}`).toBeGreaterThanOrEqual(cardRect.bottom);
    });
    expect(fixture.nativeElement.querySelector('.daily-production-operations__worker plp-responsive-entity-row')).not.toBeNull();
    expect(getComputedStyle(fixture.nativeElement.querySelector('.daily-production-operations__worker-list')).overflowY).toBe('auto');
    expect(fixture.nativeElement.querySelectorAll('.daily-production-operations__allocation-editor input')).toHaveSize(2);
    const stableFooter = fixture.nativeElement.querySelector('[data-stable-footer="true"]') as HTMLElement;
    expect(stableFooter).not.toBeNull();
    expect(stableFooter.querySelectorAll('.daily-production-operations__stage-summary > div')).toHaveSize(4);
    expect(stableFooter.querySelector('.daily-production-operations__stage-preview')).not.toBeNull();
    expect(stableFooter.textContent).toContain('استعادة توزيع دقائق العمل');
    const contextFields = fixture.nativeElement.querySelectorAll('.daily-production-operations__context-field') as NodeListOf<HTMLElement>;
    expect(contextFields).toHaveSize(6);
    contextFields.forEach(field => expect(field.querySelector('.daily-production-operations__field-label')).not.toBeNull());
    expect(getComputedStyle(fixture.nativeElement.querySelector('.daily-production-operations')).overflowX).toBe('clip');
    expect(fixture.nativeElement.querySelectorAll('.daily-production-operations__staffing-badge')).toHaveSize(2);
    expect(text).toContain('مسكن وجاهز');
    expect(text).toContain('مسكن — حضور غير مكتمل');
    const stageTable = fixture.nativeElement.querySelector('.daily-production-operations__preview-stages p-table');
    expect(stageTable).not.toBeNull();
    expect(stageTable.querySelectorAll('.plp-table-expander')).toHaveSize(1);
    const stageSummaryRow = stageTable.querySelector('.daily-production-operations__stage-summary-row') as HTMLElement;
    expect(stageSummaryRow).not.toBeNull();
    expect(stageSummaryRow.querySelectorAll('.daily-production-operations__stage-summary-metrics > div')).toHaveSize(4);
    expect(stageSummaryRow.querySelector('.daily-production-operations__stage-summary-status')).not.toBeNull();
    expect(stageTable.querySelectorAll('thead th')).toHaveSize(0);
    expect(fixture.nativeElement.querySelector('.daily-production-operations__worker-totals p-table')).not.toBeNull();
    const stageAllocationTable = fixture.nativeElement.querySelector('.daily-production-operations__preview-stages') as HTMLElement;
    const stageExpander = stageAllocationTable.querySelector('.plp-table-expander') as HTMLButtonElement;
    if (stageExpander.getAttribute('aria-expanded') !== 'true') stageExpander.click();
    fixture.detectChanges();

    const stageExpansion = fixture.nativeElement.querySelector('.plp-expansion-surface--workers') as HTMLElement;
    const stageScroll = stageExpansion.querySelector('[data-expansion-scroll="stage-workers"]') as HTMLElement;
    const workerRowsContainer = stageExpansion.querySelector('.plp-expansion-worker-rows') as HTMLElement;
    const workerRows = stageExpansion.querySelectorAll('.plp-expansion-worker-row') as NodeListOf<HTMLElement>;
    const firstWorkerRow = workerRows.item(0);

    expect(stageExpansion.querySelector('.plp-expansion-worker-grid')).toBeNull();
    expect(stageExpansion.querySelector('.plp-expansion-worker-item')).toBeNull();
    expect(workerRowsContainer.children).toHaveSize(2);
    expect(workerRows).toHaveSize(2);
    expect(Array.from(workerRowsContainer.children).every(child => child.matches('.plp-expansion-worker-row'))).toBeTrue();
    workerRows.forEach(workerRow => {
      expect(workerRow.dataset['workerId']?.trim()).toBeTruthy();
      expect(workerRow.querySelector('.plp-expansion-identity strong')?.textContent?.trim()).toBeTruthy();
      expect(workerRow.querySelector('.plp-entity-code')?.textContent?.trim()).toBeTruthy();
    });

    expect(getComputedStyle(stageScroll).overflowY).toBe('auto');
    expect(['clip', 'hidden']).toContain(getComputedStyle(stageScroll).overflowX);
    expect(stageExpansion.querySelector('[data-sticky-summary]')).toBeNull();
    expect(stageExpansion.querySelector('plp-progressive-disclosure')).toBeNull();
    expect(stageExpansion.querySelector('[aria-expanded]')).toBeNull();
    expect(stageExpansion.querySelector('.plp-expansion-deep-metadata')).toBeNull();
    expect(firstWorkerRow.querySelector('.plp-expansion-identity[title]')).not.toBeNull();
    const firstWorkerMeta = firstWorkerRow.querySelector('[data-worker-meta="time"]') as HTMLSpanElement;
    const firstWorkerTime = firstWorkerMeta.querySelector('.plp-expansion-worker-time') as HTMLSpanElement;
    expect(firstWorkerRow.querySelector('.plp-expansion-key-values')?.children).toHaveSize(3);
    expect(firstWorkerRow.querySelectorAll('[data-worker-meta]')).toHaveSize(3);
    expect(firstWorkerMeta.closest('.plp-expansion-worker-row__identity')).not.toBeNull();
    expect(firstWorkerRow.textContent).toContain(component.contributionDuration(component.stages[0].workers[0].workerMinutes));
    expect(component.contributionDuration(687)).toBe('11 ساعة 27 دقيقة');
    expect(component.contributionDuration(60)).toBe('1 ساعة');
    expect(component.contributionDuration(27)).toBe('27 دقيقة');
    expect(firstWorkerTime.getAttribute('dir')).toBe('ltr');
    expect(getComputedStyle(firstWorkerTime).unicodeBidi).toBe('isolate');
    expect(Array.from(firstWorkerTime.children).map(element => element.getAttribute('data-time') ?? element.getAttribute('data-time-arrow')))
      .toEqual(['check-in', '', 'check-out']);
    expect(firstWorkerTime.textContent).toContain('→');
    expect(firstWorkerTime.textContent).not.toContain('←');
    const contributionRange = firstWorkerTime.textContent?.split('→').map(part => part.trim()) ?? [];
    const expectedCheckIn = component.contributionTime(component.stages[0].workers[0].contributionStartsAtUtc);
    const expectedCheckOut = component.contributionTime(component.stages[0].workers[0].contributionEndsAtUtc);
    expect(contributionRange).toHaveSize(2);
    expect(contributionRange[0]).toContain(expectedCheckIn);
    expect(contributionRange[1]).toContain(expectedCheckOut);
    expect(firstWorkerTime.textContent?.replace(/\s+/g, '')).toBe('07:33→19:07');
    expect(firstWorkerRow.textContent).not.toContain('تفاصيل الحضور');
    const notReadyWorkers = stageExpansion.querySelectorAll('[data-worker-ready="false"]');
    const readinessWarnings = stageExpansion.querySelectorAll('.plp-expansion-worker-warning');
    expect(readinessWarnings.length).toBe(notReadyWorkers.length);
    expect(stageExpansion.querySelector('[data-worker-ready="true"] .plp-expansion-worker-warning')).toBeNull();

    const secondWorker = component.stages[0].workers[1];
    component.stages[0].workers = [component.stages[0].workers[0]];
    component.expandedStageRows = { 'stage-1': true };
    component.preview = component.preview;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.plp-expansion-worker-row')).toHaveSize(1);

    component.stages[0].workers.push(secondWorker);
    component.preview = component.preview;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.plp-expansion-worker-row')).toHaveSize(2);

    component.stages[0].workers.push({
      ...component.stages[0].workers[0],
      workerId: 'worker-3',
      workerCode: 'W-003',
      workerName: 'عامل ثالث'
    });
    component.expandedStageRows = { 'stage-1': true };
    component.preview = component.preview;
    fixture.detectChanges();
    const threeWorkerRowsContainer = fixture.nativeElement.querySelector('.plp-expansion-worker-rows') as HTMLElement;
    const threeWorkerRows = threeWorkerRowsContainer.querySelectorAll('.plp-expansion-worker-row') as NodeListOf<HTMLElement>;
    expect(threeWorkerRowsContainer.children).toHaveSize(3);
    expect(threeWorkerRows).toHaveSize(3);
    expect(Array.from(threeWorkerRowsContainer.children).every(child => child.matches('.plp-expansion-worker-row'))).toBeTrue();
    threeWorkerRows.forEach(workerRow => {
      expect(workerRow.dataset['workerId']?.trim()).toBeTruthy();
      expect(workerRow.querySelector('.plp-expansion-identity strong')?.textContent?.trim()).toBeTruthy();
      expect(workerRow.querySelector('.plp-entity-code')?.textContent?.trim()).toBeTruthy();
    });

    const workerAllocationTable = fixture.nativeElement.querySelector('.daily-production-operations__worker-totals') as HTMLElement;
    const workerExpander = workerAllocationTable.querySelector('.plp-table-expander') as HTMLButtonElement;
    if (workerExpander.getAttribute('aria-expanded') !== 'true') workerExpander.click();
    fixture.detectChanges();

    const workerExpansion = fixture.nativeElement.querySelector('.plp-expansion-surface--stages') as HTMLElement;
    const workerScroll = workerExpansion.querySelector('[data-expansion-scroll="worker-stages"]') as HTMLElement;
    const stageRecords = workerExpansion.querySelectorAll('.plp-expansion-stage-record') as NodeListOf<HTMLElement>;
    const firstStageRecord = stageRecords.item(0);

    expect(stageRecords.length).toBeGreaterThan(0);
    expect(getComputedStyle(workerScroll).overflowY).toBe('auto');
    expect(['clip', 'hidden']).toContain(getComputedStyle(workerScroll).overflowX);
    expect(workerExpansion.querySelector('[data-sticky-summary]')).toBeNull();
    expect(workerExpansion.querySelector('plp-progressive-disclosure')).toBeNull();
    expect(workerExpansion.querySelector('button')).toBeNull();
    expect(workerExpansion.querySelector('[aria-expanded]')).toBeNull();
    expect(workerExpansion.querySelector('.plp-expansion-deep-metadata')).toBeNull();
    expect(firstStageRecord.querySelector('.plp-expansion-identity[title]')).not.toBeNull();
    expect(firstStageRecord.querySelector('.plp-expansion-key-values')?.children).toHaveSize(3);
    expect(firstStageRecord.querySelector('.plp-expansion-stage-meta')).not.toBeNull();
    expect(firstStageRecord.textContent).not.toContain('كمية المرحلة');
    expect(firstStageRecord.textContent).not.toContain('المرحلة الرئيسية');
    expect(firstStageRecord.textContent).not.toContain('سعر المرحلة');

    const allocationSnapshot = component.stageAllocationRows.map(stage => ({
      totalEntitlement: stage.totalEntitlement,
      workers: stage.workers.map(worker => ({
        percentage: worker.percentage,
        allocatedQuantity: worker.allocatedQuantity,
        calculatedEarning: worker.calculatedEarning
      }))
    }));
    fixture.detectChanges();
    expect(component.stageAllocationRows.map(stage => ({
      totalEntitlement: stage.totalEntitlement,
      workers: stage.workers.map(worker => ({
        percentage: worker.percentage,
        allocatedQuantity: worker.allocatedQuantity,
        calculatedEarning: worker.calculatedEarning
      }))
    }))).toEqual(allocationSnapshot);

    expect(workerTotalRow.querySelector('.plp-responsive-entity-row__title')?.textContent).toContain('Worker One');
    expect(workerTotalRow.querySelector('.plp-responsive-entity-row__code')?.textContent).toContain('W-001');
    expect(workerTotalRow.querySelector('.daily-production-operations__entitlement')?.textContent).toContain('250.00');
    const presenceHeading = Array.from(
      workerAllocationTable.querySelectorAll('thead th') as NodeListOf<HTMLElement>
    ).find(heading => heading.textContent?.trim() === 'الحضور والتسكين');
    const presenceCell = workerTotalRow.querySelector('.daily-production-operations__worker-presence-cell') as HTMLElement;
    const presenceTime = presenceCell.querySelector('.plp-contribution-time-range') as HTMLElement;
    const presenceTimeParts = Array.from(presenceTime.children) as HTMLElement[];
    expect(presenceHeading).toBeTruthy();
    expect(presenceTime.getAttribute('dir')).toBe('ltr');
    expect(presenceTimeParts[0].getAttribute('data-time')).toBe('check-in');
    expect(presenceTimeParts[0].textContent?.trim()).toBe('07:33');
    expect(presenceTimeParts[1].hasAttribute('data-time-arrow')).toBeTrue();
    expect(presenceTimeParts[1].textContent?.trim()).toBe('→');
    expect(presenceTimeParts[2].getAttribute('data-time')).toBe('check-out');
    expect(presenceTimeParts[2].textContent?.trim()).toBe('19:07');
    expect(presenceTime.textContent?.replace(/\s+/g, '')).toBe('07:33→19:07');
    expect(presenceCell.textContent).toContain('11 ساعة 34 دقيقة');
    expect(presenceCell.textContent).toContain('تسكين أساسي');
    expect(text).not.toContain('SharedPercentage');
    expect(text).not.toContain('Default');
    expect(text).not.toContain('Ready');

    component.workerAllocationRows = [{
      ...component.workerAllocationRows[0],
      contributionEndsAtUtc: null,
      workerMinutes: 0,
      participationType: 'إضافة يومية'
    }];
    fixture.detectChanges();
    const missingPresenceCell = fixture.nativeElement.querySelector(
      '.daily-production-operations__worker-presence-cell'
    ) as HTMLElement;
    expect(missingPresenceCell.querySelector('.plp-contribution-time-range')?.textContent?.replace(/\s+/g, '')).toBe('07:33→—');
    expect(missingPresenceCell.textContent).toContain('0 دقيقة');
    expect(missingPresenceCell.textContent).toContain('إضافة يومية');
  });

  it('expands both projection tables with touch and keyboard accessible controls', () => {
    const production = jasmine.createSpyObj('ProductionCostRecordingApiService', ['previewDailyOperations', 'loadDailyOperations', 'saveDailyDraft']);
    const masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    const attendance = jasmine.createSpyObj('AttendanceApiService', ['syncForProductionDate']);
    masterData.factories.and.returnValue(of([]));
    TestBed.configureTestingModule({
      imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: ProductionCostRecordingApiService, useValue: production },
        { provide: ManufacturingMasterDataApiService, useValue: masterData },
        { provide: AttendanceApiService, useValue: attendance },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: FormSubmissionValidationService, useValue: { serverMessage: (_: unknown, fallback: string) => fallback } }
      ]
    });
    const fixture = TestBed.createComponent(DailyProductionOperationsPageComponent);
    const component = fixture.componentInstance;
    const readyWorker = {
      workerId: 'worker-1', workerCode: 'W-001', workerName: 'عامل 1', isOnActiveService: true,
      effectiveAssignmentType: 'Default', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
      requiresAuthorizedOverride: false, suggestedPercentage: 100, contributionStartsAtUtc: '2026-07-17T05:00:00Z',
      contributionEndsAtUtc: '2026-07-17T13:00:00Z', workerMinutes: 480, isProductionReady: true,
      isAssignedWorker: true, isDailyOverride: false, includedInProduction: true, percentage: 100, quantity: 500,
      fixedAmount: null, notes: '', manualOverrideReason: ''
    } as any;
    component.operations = {
      factoryId: 'factory-1', factoryName: 'مصنع', productionLineId: 'line-1', productionLineName: 'خط',
      productModelId: 'model-1', productModelCode: 'M1', productModelName: 'موديل', productionDate: '2026-07-17',
      staffingContextVersion: 'context', totalStages: 1, readyStages: 1, stagesWithAbsentWorkers: 0,
      stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0, activeWorkers: [], stages: []
    };
    component.stages = [{
      productModelStageId: 'stage-1', subStageId: 'sub-1', mainStageName: 'تجميع', stageCode: 'S1', stageName: 'مرحلة 1',
      stageOrder: 1, piecePrice: .5, compensationMode: 'SharedPercentage', staffingStatus: 'Staffed', attendanceStatus: 'Ready',
      hasAbsentWorkers: false, hasNoSourceCheckInWorkers: false, isFinancialReviewPending: false, isReady: true, workers: [readyWorker]
    } as any];
    component.selectedStageId = 'stage-1';
    component.lineQuantity = 500;
    component.preview = {
      productionDate: '2026-07-17', lineQuantity: 500, previewToken: 'preview-token', totalWorkerEntitlements: 250,
      stages: [{ productModelStageId: 'stage-1', stageCode: 'S1', stageName: 'مرحلة 1', stageQuantity: 500,
        stageCost: 250, compensationMode: 'SharedPercentage', warnings: [], workers: [{ workerId: 'worker-1',
          workerCode: 'W-001', workerName: 'عامل 1', percentage: 100, equivalentQuantity: 500, calculatedEarning: 250 }] }],
      workerTotals: [{ workerId: 'worker-1', workerCode: 'W-001', workerName: 'عامل 1', totalEntitlement: 250 }],
      warnings: []
    };
    fixture.detectChanges();

    const expanders = fixture.nativeElement.querySelectorAll('.plp-table-expander') as NodeListOf<HTMLButtonElement>;
    expect(expanders).toHaveSize(2);
    expanders.forEach(button => {
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(parseFloat(getComputedStyle(button).minHeight)).toBeGreaterThanOrEqual(44);
    });

    expanders[0].click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.plp-expansion-surface')).not.toBeNull();
    expect(expanders[0].getAttribute('aria-expanded')).toBe('true');
  });
});
