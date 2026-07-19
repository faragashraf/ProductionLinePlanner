import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { buildApiUrl } from '../config/api.config';
import { FactoryMapApiService } from './factory-map-api.service';

describe('FactoryMapApiService', () => {
  let service: FactoryMapApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(FactoryMapApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds the map from the existing factory hierarchy and readiness APIs', () => {
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [{ id: 'factory-1', name: 'مصنع 1' }] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [{ id: 'line-1', factoryId: 'factory-1', name: 'خط 1' }] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'main-1', productionLineId: 'line-1', name: 'تجهيز' }] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'فحص', capacity: 3 }] } });
    http.expectOne(buildApiUrl('/api/readiness/factory')).flush({ success: true, data: { readinessPercent: 80, presentWorkers: 2, requiredWorkers: 3 } });
    http.expectOne(buildApiUrl('/api/readiness/production-lines')).flush({ success: true, data: { items: [{ scopeEntityId: 'line-1', lineName: 'خط 1', readinessPercent: 80, presentWorkers: 2, requiredWorkers: 3 }] } });

    expect(result.hasUsableBackendData).toBeTrue();
    expect(result.layout.lines[0].name).toBe('خط 1');
    expect(result.layout.lines[0].stages[0].subStages[0].name).toBe('فحص');
    expect(result.layout.lines[0].readinessPercent).toBe(80);
  });

  it('returns an empty real-data layout when the factory API has no records', () => {
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/readiness/factory')).flush({ success: true, data: {} });
    http.expectOne(buildApiUrl('/api/readiness/production-lines')).flush({ success: true, data: { items: [] } });

    expect(result.hasBackendData).toBeFalse();
    expect(result.layout.lines).toEqual([]);
    expect(result.fallbackReason).toBe('incomplete');
  });
});
