import { Subject, of, throwError } from 'rxjs';
import { ManufacturingCommandCenterApiService } from '../../core/services/manufacturing-command-center-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingCommandCenter } from '../../shared/models/manufacturing-command-center.model';
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
    first.error(new Error('stale request failed'));
    second.next({ ...sampleData(), scope: { ...sampleData().scope, factoryId: 'factory-new', description: 'new scope' } });
    second.complete();

    expect(component.filters).toEqual(selectedFilters);
    expect(component.data?.scope.factoryId).toBe('factory-new');
    expect(component.hasLoadError).toBeFalse();
    expect(component.ratioText(0)).toBe('0%');
  });
});

function sampleData(): ManufacturingCommandCenter {
  return {
    scope: { productionDate: '2026-07-22', factoryId: null, departmentId: null, productionLineId: null, operationStatus: 'All', description: 'scope' },
    filterCatalog: { factories: [], departments: [], lines: [] },
    workforce: {
      activeWorkers: 3, presentWorkers: 1, presentPermanentlyAssignedWorkers: 0, presentUnassignedWorkers: 1, permanentlyAssignedNotPresentWorkers: 0,
      assignmentCoverage: { numerator: 0, denominator: 0, percentage: null, scope: 'scope', date: '2026-07-22', zeroBehavior: 'NoData' },
      attendanceEvidenceComplete: true, attributionNote: 'note', presentAssignedDetails: [],
      presentUnassignedDetails: [{ workerId: 'w2', workerCode: '2', workerName: 'عامل', attendanceStatus: 'Present', permanentAssignments: [] }], assignedNotPresentDetails: []
    },
    lineSummary: { activeLines: 1, readyLines: 0, staffingShortageLines: 0, journeyNotConfiguredLines: 0, dataIncompleteLines: 0, problemLines: 1, stagesWithoutPresentWorker: 0 },
    operations: { linesWithOperation: 0, linesWithoutOperation: 1, draftOperations: 0, approvedOperations: 0, approvalCancelledOperations: 0, cancelledOperations: 0, approvedRecordedValue: 0, items: [] },
    dataQuality: { modelStagesWithoutPrice: 0, modelStagesWithoutStandardTime: 0, activeJourneyStagesWithoutPresentWorker: 0, activeModelsWithoutJourney: 0, issues: [], modelsWithoutJourneyScopeNote: '' },
    factories: [], calculatedAtUtc: '2026-07-22T08:00:00Z'
  };
}
