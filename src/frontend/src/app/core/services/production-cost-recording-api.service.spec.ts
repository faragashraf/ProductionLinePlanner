import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { fakeAsync, tick } from '@angular/core/testing';
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

  it('uses POST collection for daily draft creation and PUT item for an existing aggregate', () => {
    const payload = {
      factoryId: 'factory-1', productionLineId: 'line-1', productModelId: 'model-1', productionDate: '2026-07-16',
      lineQuantity: 500, clientRequestId: 'a94f0c35-89ac-4ed4-86b3-2cda09d55aaf', previewToken: 'preview-token',
      stages: [{ productModelStageId: 'stage-1', workers: [{ workerId: 'worker-1', percentage: 100 }] }]
    };
    const updatePayload = {
      ...payload,
      concurrencyToken: 'order-token-1',
      stages: [{ ...payload.stages[0], stageProductionRecordId: 'record-1', concurrencyToken: 'stage-token-1' }]
    };
    let loaded = false; let previewed = false; let created = false; let updated = false;

    service.loadDailyOperations('factory-1', 'line-1', 'model-1', '2026-07-16').subscribe(() => loaded = true);
    service.previewDailyOperations(payload).subscribe(() => previewed = true);
    service.createDailyDraft(payload, 'daily-operation-create-correlation').subscribe(() => created = true);
    service.updateDailyDraft('day-1', updatePayload, 'daily-operation-update-correlation').subscribe(() => updated = true);

    const load = http.expectOne(request => request.method === 'GET' && request.url.includes('/api/production/daily-operations'));
    expect(load.request.method).toBe('GET');
    expect(load.request.urlWithParams).toContain('productionDate=2026-07-16');
    load.flush({ success: true, data: { productionDate: '2026-07-16', stages: [], activeWorkers: [] } });

    const preview = http.expectOne(request => request.url.endsWith('/api/production/daily-operations/preview'));
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual(payload);
    preview.flush({ success: true, data: { previewToken: 'preview-token', stages: [], warnings: [] } });

    const create = http.expectOne(request => request.url.endsWith('/api/production/daily-operations/drafts'));
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual(payload);
    expect(create.request.headers.get('X-Manufacturing-Realtime-Correlation-Id')).toBe('daily-operation-create-correlation');
    create.flush({ success: true, data: { productionOrderId: 'day-1', stages: [] } });

    const update = http.expectOne(request => request.url.endsWith('/api/production/daily-operations/drafts/day-1'));
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual(updatePayload);
    expect(update.request.headers.get('X-Manufacturing-Realtime-Correlation-Id')).toBe('daily-operation-update-correlation');
    update.flush({ success: true, data: { productionOrderId: 'day-1', stages: [] } });

    expect(loaded).toBeTrue();
    expect(previewed).toBeTrue();
    expect(created).toBeTrue();
    expect(updated).toBeTrue();
  });

  it('sends a daily approval cancellation with every stage concurrency token and the realtime correlation id', () => {
    let cancelled = false;

    service.cancelDailyOperationApproval('day-1', [{ stageProductionRecordId: 'record-1', concurrencyToken: 'token-1' }], 'تصحيح تشغيل اليوم', 'cancel-correlation')
      .subscribe(() => cancelled = true);

    const request = http.expectOne(item => item.url.endsWith('/api/production/daily-operations/day-1/cancel-approval'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      stageApprovals: [{ stageProductionRecordId: 'record-1', concurrencyToken: 'token-1' }],
      reason: 'تصحيح تشغيل اليوم'
    });
    expect(request.request.headers.get('X-Manufacturing-Realtime-Correlation-Id')).toBe('cancel-correlation');
    request.flush({ success: true, data: { productionOrderId: 'day-1', orderStatus: 'Draft', cancelledStageCount: 1 } });

    expect(cancelled).toBeTrue();
  });

  it('keeps a full-day unified preview alive past the normal API timeout', fakeAsync(() => {
    const payload = {
      factoryId: 'factory-1', productionLineId: 'line-1', productModelId: 'model-1', productionDate: '2026-07-16',
      lineQuantity: 500, clientRequestId: 'a94f0c35-89ac-4ed4-86b3-2cda09d55aaf', stages: Array.from({ length: 66 }, (_, index) => ({
        productModelStageId: `stage-${index + 1}`,
        workers: Array.from({ length: index < 9 ? 2 : 1 }, (_, workerIndex) => ({ workerId: workerIndex === 0 ? 'worker-repeated' : `worker-${index}`, percentage: workerIndex === 0 && index < 9 ? 50 : 100 }))
      }))
    };
    let completed = false;

    service.previewDailyOperations(payload).subscribe(() => completed = true);
    const previewRequest = http.expectOne(request => request.url.endsWith('/api/production/daily-operations/preview'));
    expect(previewRequest.request.method).toBe('POST');
    expect(previewRequest.request.body).toEqual(payload);
    expect(payload.stages.length).toBe(66);
    expect(payload.stages.reduce((total, stage) => total + stage.workers.length, 0)).toBe(75);

    tick(10_001);
    previewRequest.flush({ success: true, data: { previewToken: 'preview-token', stages: [], workerTotals: [], warnings: [] } });

    expect(completed).toBeTrue();
  }));
});
