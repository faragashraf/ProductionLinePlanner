import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PRODUCTION_RECORD_PREVIEW_ROUTE, ProductionCostRecordingApiService } from './production-cost-recording-api.service';

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

  it('uses the single POST preview contract with the current draft payload', () => {
    const payload = {
      productionOrderId: 'order-1', productModelStageId: 'stage-1', productionDate: '2026-07-15',
      producedQuantity: 500, acceptedQuantity: 500, rejectedQuantity: 0, clientRequestId: 'a94f0c35-89ac-4ed4-86b3-2cda09d55aaf',
      workers: [{ workerId: 'worker-1', percentage: 100 }]
    };
    let result: unknown;

    service.calculatePreview(payload).subscribe(value => result = value);

    const request = http.expectOne(request => request.url.endsWith(PRODUCTION_RECORD_PREVIEW_ROUTE));
    expect(request.request.method).toBe('POST');
    expect(request.request.url).toContain(PRODUCTION_RECORD_PREVIEW_ROUTE);
    expect(request.request.body).toEqual(payload);
    request.flush({ success: true, data: { id: 'preview', totalWorkerEarnings: 190, workers: [{ workerId: 'worker-1', calculatedEarning: 190 }] } });

    expect(result).toEqual(jasmine.objectContaining({ totalWorkerEarnings: 190 }));
  });

  it('loads, previews, and saves the aggregate daily-operations contract without using the legacy single-stage route', () => {
    const payload = {
      factoryId: 'factory-1', productionLineId: 'line-1', productModelId: 'model-1', productionDate: '2026-07-16',
      lineQuantity: 500, clientRequestId: 'a94f0c35-89ac-4ed4-86b3-2cda09d55aaf', previewToken: 'preview-token',
      stages: [{ productModelStageId: 'stage-1', workers: [{ workerId: 'worker-1', percentage: 100 }] }]
    };
    let loaded = false; let previewed = false; let saved = false;

    service.loadDailyOperations('factory-1', 'line-1', 'model-1', '2026-07-16').subscribe(() => loaded = true);
    service.previewDailyOperations(payload).subscribe(() => previewed = true);
    service.saveDailyDraft(payload).subscribe(() => saved = true);

    const load = http.expectOne(request => request.method === 'GET' && request.url.includes('/api/production/daily-operations'));
    expect(load.request.method).toBe('GET');
    expect(load.request.urlWithParams).toContain('productionDate=2026-07-16');
    load.flush({ success: true, data: { productionDate: '2026-07-16', stages: [], activeWorkers: [] } });

    const preview = http.expectOne(request => request.url.endsWith('/api/production/daily-operations/preview'));
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual(payload);
    preview.flush({ success: true, data: { previewToken: 'preview-token', stages: [], warnings: [] } });

    const save = http.expectOne(request => request.url.endsWith('/api/production/daily-operations/drafts'));
    expect(save.request.method).toBe('POST');
    expect(save.request.body).toEqual(payload);
    save.flush({ success: true, data: { productionOrderId: 'day-1', stages: [] } });

    expect(loaded).toBeTrue();
    expect(previewed).toBeTrue();
    expect(saved).toBeTrue();
  });
});
