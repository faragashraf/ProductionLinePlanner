import { Subject, of, throwError } from 'rxjs';
import { ManufacturingCommandCenterApiService } from '../../core/services/manufacturing-command-center-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingCommandCenter } from '../../shared/models/manufacturing-command-center.model';
import { FactoryMapPageComponent } from './factory-map-page.component';

describe('FactoryMapPageComponent', () => {
  it('maps named operational statuses without deriving decorative efficiency', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    realtime.watchScreen.and.returnValue(() => undefined);
    api.load.and.returnValue(of(sampleMap()));
    const component = new FactoryMapPageComponent(api, realtime);

    component.ngOnInit();

    expect(component.readinessLabel('StaffingShortage')).toBe('نقص عمالة');
    expect(component.operationLabel('Draft')).toBe('مسودة تحتاج استكمالًا');
    expect(component.data?.factories[0].departments[0].lines[0].stagesWithoutPresentWorker).toBe(1);
  });

  it('keeps expanded state during a realtime refresh', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    let refresh: (() => void) | undefined;
    realtime.watchScreen.and.callFake(watch => { refresh = watch.refresh; return () => undefined; });
    api.load.and.returnValue(of(sampleMap()));
    const component = new FactoryMapPageComponent(api, realtime);
    component.ngOnInit();
    component.expandedLines.add('line-1');

    refresh?.();

    expect(component.expandedLines.has('line-1')).toBeTrue();
    expect(api.load).toHaveBeenCalledWith(component.filters);
    expect(api.load).toHaveBeenCalledTimes(2);
  });

  it('shows an API error with no fake hierarchy when the first load fails', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    realtime.watchScreen.and.returnValue(() => undefined);
    api.load.and.returnValue(throwError(() => new Error('offline')));
    const component = new FactoryMapPageComponent(api, realtime);

    component.ngOnInit();

    expect(component.data).toBeNull();
    expect(component.hasLoadError).toBeTrue();
    expect(component.isLoading).toBeFalse();
  });

  it('keeps the selected filters and ignores a stale map response after a filter change', () => {
    const api = jasmine.createSpyObj<ManufacturingCommandCenterApiService>('api', ['load']);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen']);
    const first = new Subject<ManufacturingCommandCenter>();
    const second = new Subject<ManufacturingCommandCenter>();
    realtime.watchScreen.and.returnValue(() => undefined);
    api.load.and.returnValues(first, second);
    const component = new FactoryMapPageComponent(api, realtime);
    component.ngOnInit();

    const selectedFilters = { ...component.filters, productionLineId: 'line-2', factoryId: 'factory-1' };
    component.onFiltersChange(selectedFilters);
    first.next(sampleMap());
    second.next({ ...sampleMap(), scope: { ...sampleMap().scope, factoryId: 'factory-1', productionLineId: 'line-2', description: 'line scope' }, factories: [] });
    second.complete();

    expect(component.filters).toEqual(selectedFilters);
    expect(component.data?.scope.productionLineId).toBe('line-2');
    expect(component.data?.factories).toEqual([]);
    expect(component.hasLoadError).toBeFalse();
  });
});

function sampleMap(): ManufacturingCommandCenter {
  return {
    scope: { productionDate: '2026-07-22', factoryId: null, departmentId: null, productionLineId: null, operationStatus: 'All', description: 'scope' },
    filterCatalog: { factories: [], departments: [], lines: [] },
    workforce: { activeWorkers: 0, presentWorkers: 0, presentPermanentlyAssignedWorkers: 0, presentUnassignedWorkers: 0, permanentlyAssignedNotPresentWorkers: 0, assignmentCoverage: { numerator: 0, denominator: 0, percentage: null, scope: 'scope', date: '2026-07-22', zeroBehavior: 'NoData' }, attendanceEvidenceComplete: true, attributionNote: '', presentAssignedDetails: [], presentUnassignedDetails: [], assignedNotPresentDetails: [] },
    lineSummary: { activeLines: 1, readyLines: 0, staffingShortageLines: 1, journeyNotConfiguredLines: 0, dataIncompleteLines: 0, problemLines: 1, stagesWithoutPresentWorker: 1 },
    operations: { linesWithOperation: 1, linesWithoutOperation: 0, draftOperations: 1, approvedOperations: 0, approvalCancelledOperations: 0, cancelledOperations: 0, approvedRecordedValue: 0, items: [] },
    dataQuality: { modelStagesWithoutPrice: 0, modelStagesWithoutStandardTime: 0, activeJourneyStagesWithoutPresentWorker: 1, activeModelsWithoutJourney: 0, issues: [], modelsWithoutJourneyScopeNote: '' },
    factories: [{ id: 'factory-1', name: 'مصنع', code: 'F', activeDepartments: 1, activeLines: 1, presentPermanentlyAssignedWorkers: 0, problemLines: 1, draftOperations: 1, approvedOperations: 0, departments: [{ id: 'dep-1', name: 'قسم', code: 'D', activeLines: 1, presentPermanentlyAssignedWorkers: 0, permanentlyAssignedWorkers: 0, presentUnassignedWorkers: null, readyLines: 0, notReadyLines: 1, draftOperations: 1, approvedOperations: 0, workforceAttributionNote: '', lines: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'dep-1', name: 'خط', code: 'L', readinessStatus: 'StaffingShortage', permanentlyAssignedWorkers: 0, presentPermanentlyAssignedWorkers: 0, requiredWorkers: 1, journeyStages: 1, stagesCoveredByPresentWorker: 0, stagesWithoutPresentWorker: 1, lastReliableUpdateUtc: '2026-07-22T08:00:00Z', alerts: [], operations: [] }] }] }],
    calculatedAtUtc: '2026-07-22T08:00:00Z'
  };
}
