import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProductionCostRecordingApiService } from './production-cost-recording-api.service';

describe('ProductionCostRecordingApiService', () => {
  let service: ProductionCostRecordingApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ProductionCostRecordingApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('unwraps production lookup pagination envelopes for models and workers', () => {
    let models: unknown[] = []; let workers: unknown[] = [];
    service.listModels().subscribe(value => models = value);
    service.listWorkers().subscribe(value => workers = value);
    http.expectOne(request => request.url.endsWith('/api/production/lookups/models')).flush({ success: true, data: { items: [{ id: 'model-1', code: 'M1', name: 'Model', isActive: true }], totalCount: 1, pageNumber: 1, pageSize: 50 } });
    http.expectOne(request => request.url.endsWith('/api/production/lookups/workers')).flush({ success: true, data: { items: [{ id: 'worker-1', employeeCode: 'W1', fullName: 'Worker', employmentStatus: 'Active', isActive: true }], totalCount: 1, pageNumber: 1, pageSize: 50 } });
    expect(models).toEqual([{ id: 'model-1', code: 'M1', name: 'Model', isActive: true }]);
    expect(workers).toEqual([{ id: 'worker-1', employeeCode: 'W1', fullName: 'Worker', employmentStatus: 'Active', isActive: true }]);
  });
});
