import { of, throwError } from 'rxjs';
import { AssignmentsApiService } from '../../core/services/assignments-api.service';
import { FactoryMapApiService } from '../../core/services/factory-map-api.service';
import { FactoryMapPageComponent } from './factory-map-page.component';

describe('FactoryMapPageComponent', () => {
  const changeDetector = { markForCheck: jasmine.createSpy('markForCheck') } as any;

  it('uses the real layout supplied by the API service', () => {
    const mapApi = jasmine.createSpyObj<FactoryMapApiService>('FactoryMapApiService', ['loadFactoryMapData']);
    const assignments = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', ['getFactoryStructureSubStageWorkers']);
    mapApi.loadFactoryMapData.and.returnValue(of({ hasBackendData: true, hasUsableBackendData: true, layout: { id: 'factory-1', type: 'factory', name: 'مصنع 1', status: 'ready', readinessPercent: 80, workersCurrent: 2, workersRequired: 3, lines: [] } }));
    const component = new FactoryMapPageComponent(mapApi, assignments, changeDetector);

    component.ngOnInit();

    expect(component.layout.name).toBe('مصنع 1');
    expect(component.showFallbackWarning).toBeFalse();
  });

  it('keeps an empty layout instead of falling back to mock data when the API fails', () => {
    const mapApi = jasmine.createSpyObj<FactoryMapApiService>('FactoryMapApiService', ['loadFactoryMapData']);
    const assignments = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', ['getFactoryStructureSubStageWorkers']);
    mapApi.loadFactoryMapData.and.returnValue(throwError(() => new Error('offline')));
    const component = new FactoryMapPageComponent(mapApi, assignments, changeDetector);

    component.ngOnInit();

    expect(component.layout.lines).toEqual([]);
    expect(component.showFallbackWarning).toBeTrue();
  });

  it('keeps the batch staffing summary unchanged when lazy worker details load', () => {
    const mapApi = jasmine.createSpyObj<FactoryMapApiService>('FactoryMapApiService', ['loadFactoryMapData']);
    const assignments = jasmine.createSpyObj<AssignmentsApiService>('AssignmentsApiService', ['getFactoryStructureSubStageWorkers']);
    mapApi.loadFactoryMapData.and.returnValue(of({
      hasBackendData: true,
      hasUsableBackendData: true,
      layout: {
        id: 'factory-1', type: 'factory', name: 'مصنع 1', status: 'ready', readinessPercent: 100, workersCurrent: 2, workersRequired: 2,
        lines: [{
          id: 'line-1', type: 'line', name: 'خط 1', status: 'ready', readinessPercent: 100, statusText: 'مغطى', activeStageId: 'main-1', activeStageName: 'مرحلة 1', workersCurrent: 2, workersRequired: 2,
          stages: [{
            id: 'main-1', type: 'main-stage', name: 'مرحلة 1', status: 'ready', readinessPercent: 100, workersCurrent: 2, workersRequired: 2,
            subStages: [{ id: 'sub-1', type: 'sub-stage', name: 'مرحلة فرعية', status: 'ready', readinessPercent: 100, workersCurrent: 2, workersRequired: 2, workers: [] }]
          }]
        }]
      }
    }));
    assignments.getFactoryStructureSubStageWorkers.and.returnValue(of({
      subStageId: 'sub-1', workers: [{ id: 'worker-1', fullName: 'عامل 1', code: 'W1', assignmentType: 'Default' }]
    } as any));
    const component = new FactoryMapPageComponent(mapApi, assignments, changeDetector);

    component.ngOnInit();
    component.onLineSelected('line-1');
    component.onMainStageSelected('main-1');
    component.onSubStageSelected('sub-1');

    expect(component.selectedSubStage?.workers).toHaveSize(1);
    expect(component.selectedSubStage?.workersCurrent).toBe(2);
  });
});
