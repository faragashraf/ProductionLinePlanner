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
});
