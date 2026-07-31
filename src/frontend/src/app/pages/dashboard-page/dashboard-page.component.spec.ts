import { Subject, of, throwError } from 'rxjs';
import { ManufacturingCommandCenterApiService } from '../../core/services/manufacturing-command-center-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { CommandCenterLine, CommandCenterOperation, ManufacturingCommandCenter } from '../../shared/models/manufacturing-command-center.model';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  it('keeps one filter scope for every metric and exposes matching drill-down items', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    realtime.watchScreen.and.returnValue(() => undefined);
    api.load.and.returnValue(of(sampleData()));
    const component = new DashboardPageComponent(api, realtime);

    component.ngOnInit();
    component.selectDetail('present-unassigned');

    expect(api.load).toHaveBeenCalledWith(component.filters);
    expect(component.data?.workforce.assignmentCoverage.percentage).toBeNull();
    expect(component.ratioText(null)).toBe('لا توجد بيانات');
    expect(component.detailWorkers.map(worker => worker.workerId)).toEqual(['w2']);
  });

  it('preserves the previous response when realtime refresh fails', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    let refresh: (() => void) | undefined;
    realtime.watchScreen.and.callFake(watch => { refresh = watch.refresh; return () => undefined; });
    api.load.and.returnValues(of(sampleData()), throwError(() => new Error('offline')));
    const component = new DashboardPageComponent(api, realtime);
    component.ngOnInit();

    refresh?.();

    expect(component.data?.scope.productionDate).toBe('2026-07-22');
    expect(component.hasLoadError).toBeTrue();
    expect(api.load).toHaveBeenCalledTimes(2);
  });

  it('coalesces realtime events while a refresh is running instead of cancelling it repeatedly', async () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    const runningRefresh = new Subject<ManufacturingCommandCenter>();
    let refresh: (() => void) | undefined;
    realtime.watchScreen.and.callFake(watch => { refresh = watch.refresh; return () => undefined; });
    api.load.and.returnValues(of(sampleData()), runningRefresh, of(sampleData()));
    const component = new DashboardPageComponent(api, realtime);
    component.ngOnInit();

    refresh?.();
    refresh?.();
    refresh?.();

    expect(api.load).toHaveBeenCalledTimes(2);
    runningRefresh.next(sampleData());
    runningRefresh.complete();
    await Promise.resolve();
    expect(api.load).toHaveBeenCalledTimes(3);
  });

  it('shows an explicit initial API error instead of fallback figures', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    realtime.watchScreen.and.returnValue(() => undefined);
    api.load.and.returnValue(throwError(() => new Error('offline')));
    const component = new DashboardPageComponent(api, realtime);

    component.ngOnInit();

    expect(component.data).toBeNull();
    expect(component.hasLoadError).toBeTrue();
    expect(component.isLoading).toBeFalse();
  });

  it('cancels a stale scope request so it cannot overwrite the newest dashboard response', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    const first = new Subject<ManufacturingCommandCenter>();
    const second = new Subject<ManufacturingCommandCenter>();
    realtime.watchScreen.and.returnValue(() => undefined);
    api.load.and.returnValues(first, second);
    const component = new DashboardPageComponent(api, realtime);
    component.ngOnInit();

    const selectedFilters = { ...component.filters, factoryId: 'factory-new' };
    component.onFiltersChange(selectedFilters);
    expect(component.dataIsCurrent).toBeFalse();
    expect(component.problemLines).toEqual([]);
    first.error(new Error('stale request failed'));
    second.next({ ...sampleData(), scope: { ...sampleData().scope, factoryId: 'factory-new', description: 'new scope' } });
    second.complete();

    expect(component.filters).toEqual(selectedFilters);
    expect(component.data?.scope.factoryId).toBe('factory-new');
    expect(component.hasLoadError).toBeFalse();
    expect(component.ratioText(0)).toBe('0%');
  });

  it('shows only intervention lines, orders them by severity, and exposes four independent status dimensions', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    realtime.watchScreen.and.returnValue(() => undefined);
    const healthy = line('healthy', 'Ready', [operation('Approved')]);
    const draft = line('draft', 'Ready', [operation('Draft')]);
    const cancelled = line('cancelled', 'Ready', [operation('Cancelled')]);
    const response = sampleData();
    response.factories = [{
      id: 'factory-1', name: 'مصنع', code: 'F', activeDepartments: 1, activeLines: 3,
      presentPermanentlyAssignedWorkers: 3, problemLines: 2, draftOperations: 1, approvedOperations: 1,
      departments: [{
        id: 'department-1', name: 'قسم', code: 'D', activeLines: 3,
        presentPermanentlyAssignedWorkers: 3, permanentlyAssignedWorkers: 3, presentUnassignedWorkers: 0,
        readyLines: 3, notReadyLines: 0, draftOperations: 1, approvedOperations: 1,
        workforceAttributionNote: '', lines: [healthy, draft, cancelled]
      }]
    }];
    api.load.and.returnValue(of(response));
    const component = new DashboardPageComponent(api, realtime);

    component.ngOnInit();

    expect(component.problemLines.map(problem => problem.line.id)).toEqual(['cancelled', 'draft']);
    expect(component.problemLines[0].reasons).toContain('يوجد تشغيل ملغى');
    expect(component.lineDimensions(draft).map(dimension => dimension.key)).toEqual(['execution', 'route', 'staffing', 'data']);
    expect(component.lineDimensions(draft).find(dimension => dimension.key === 'execution')?.value).toBe('مسودة تحتاج استكمالًا');
  });

  it('turns attendance-derived indicators into unknown values when attendance is untrusted', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    realtime.watchScreen.and.returnValue(() => undefined);
    const response = sampleData();
    response.attendanceSync = { ...response.attendanceSync, status: 'Stale', isTrusted: false, ageMinutes: 30 };
    response.workforce.presentWorkers = null;
    response.workforce.permanentlyAssignedNotPresentWorkers = null;
    response.lineSummary.stagesWithoutPresentWorker = null;
    response.dataQuality.activeJourneyStagesWithoutPresentWorker = null;
    api.load.and.returnValue(of(response));
    const component = new DashboardPageComponent(api, realtime);

    component.ngOnInit();

    expect(component.metricText(component.data!.workforce.presentWorkers)).toBe('غير مؤكد');
    expect(component.decisionIndicators[0].value).toBe('غير مؤكدة');
    expect(component.decisionIndicators[0].context).toContain('مزامنة حضور موثوقة');
  });
});

function operation(status: CommandCenterOperation['status']): CommandCenterOperation {
  return {
    productionOrderId: `order-${status}`, productionLineId: 'line', productModelId: 'model', productModelCode: 'M', productModelName: 'موديل',
    status, finalLineQuantity: 10, recordedStageValue: 20, registeredStages: 1, journeyStages: 1,
    stageRegistrationCoverage: { numerator: 1, denominator: 1, percentage: 100, scope: 'scope', date: '2026-07-22', zeroBehavior: 'Calculated' },
    lastReliableUpdateUtc: '2026-07-22T08:00:00Z',
    stages: [{ productModelStageId: 'pms', subStageId: 'stage', mainStageName: 'رئيسية', stageCode: 'S', stageName: 'مرحلة', stageOrder: 1, requiredWorkers: 1, permanentlyAssignedWorkers: 1, presentPermanentlyAssignedWorkers: 1, hasPrice: true, hasStandardTime: true, isRegistered: true, alerts: [] }]
  };
}

function line(id: string, readinessStatus: CommandCenterLine['readinessStatus'], operations: CommandCenterOperation[]): CommandCenterLine {
  return {
    id, factoryId: 'factory-1', departmentId: 'department-1', name: id, code: id, readinessStatus,
    permanentlyAssignedWorkers: 1, presentPermanentlyAssignedWorkers: 1, requiredWorkers: 1,
    journeyStages: 1, stagesCoveredByPresentWorker: 1, stagesWithoutPresentWorker: 0,
    lastReliableUpdateUtc: '2026-07-22T08:00:00Z', alerts: [], operations
  };
}

function sampleData(): ManufacturingCommandCenter {
  return {
    scope: { productionDate: '2026-07-22', factoryId: null, departmentId: null, productionLineId: null, operationStatus: 'All', description: 'scope' },
    filterCatalog: { factories: [], departments: [], lines: [] },
    attendanceSync: { status: 'Fresh', isTrusted: true, lastAttemptAtUtc: '2026-07-22T08:00:00Z', lastSuccessfulAtUtc: '2026-07-22T08:00:00Z', lastErrorCode: null, ageMinutes: 0 },
    workforce: {
      activeWorkers: 3, presentWorkers: 1, presentPermanentlyAssignedWorkers: 0, presentUnassignedWorkers: 1, permanentlyAssignedNotPresentWorkers: 0,
      assignmentCoverage: { numerator: 0, denominator: 0, percentage: null, scope: 'scope', date: '2026-07-22', zeroBehavior: 'NoData' },
      attendanceEvidenceComplete: true, attributionNote: 'note', presentAssignedDetails: [],
      presentUnassignedDetails: [{ workerId: 'w2', workerCode: '2', workerName: 'عامل', attendanceStatus: 'Present', permanentAssignments: [] }], assignedNotPresentDetails: []
    },
    lineSummary: { activeLines: 1, readyLines: 0, noOperationLines: 1, staffingShortageLines: 0, journeyNotConfiguredLines: 0, dataIncompleteLines: 0, attendanceUntrustedLines: 0, problemLines: 1, stagesWithoutPresentWorker: 0 },
    operations: { linesWithOperation: 0, linesWithoutOperation: 1, draftOperations: 0, approvedOperations: 0, approvalCancelledOperations: 0, cancelledOperations: 0, approvedRecordedValue: 0, items: [] },
    dataQuality: { modelStagesWithoutPrice: 0, modelStagesWithoutStandardTime: 0, activeJourneyStagesWithoutPresentWorker: 0, activeModelsWithoutJourney: 0, issues: [], modelsWithoutJourneyScopeNote: '' },
    factories: [], calculatedAtUtc: '2026-07-22T08:00:00Z'
  };
}
