import { Observable, Subject, of, throwError } from 'rxjs';
import { DailyProductionOperationsPageComponent } from './daily-production-operations-page.component';
import { DailyProductionPreview } from '../../core/services/production-cost-recording-api.service';

describe('DailyProductionOperationsPageComponent unified preview', () => {
  let component: DailyProductionOperationsPageComponent;
  let production: jasmine.SpyObj<any>;
  let masterData: jasmine.SpyObj<any>;
  let attendance: jasmine.SpyObj<any>;

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
    production = jasmine.createSpyObj('ProductionCostRecordingApiService', ['previewDailyOperations', 'loadDailyOperations', 'saveDailyDraft']);
    masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    attendance = jasmine.createSpyObj('AttendanceApiService', ['syncForProductionDate']);
    component = new DailyProductionOperationsPageComponent(
      masterData,
      attendance,
      production,
      { hasPermission: () => true } as any,
      { serverMessage: (error: any, fallback: string) => error?.error?.detail ?? fallback } as any
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
      isFinancialReviewPending: false, isReady: true, originalWorkerIds: ['worker-1'],
      workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', isOnActiveService: true,
        effectiveAssignmentType: 'Permanent', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
        requiresAuthorizedOverride: false, suggestedPercentage: 100, percentage: 100, fixedAmount: null, notes: '', manualOverrideReason: '' }]
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
});
