import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProductionQuantitiesReportApiService } from './production-quantities-report-api.service';

describe('ProductionQuantitiesReportApiService', () => {
  let service: ProductionQuantitiesReportApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ProductionQuantitiesReportApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only quantities filters to the secure quantities endpoint', () => {
    service.query({
      from: '2026-07-18', to: '2026-07-18', factoryId: 'factory-1', productionLineId: 'line-1',
      productModelId: 'model-1', workerId: 'worker-1', status: 'Approved', view: 'StageWorkers',
      page: 1, pageSize: 20, sortBy: 'StageCode', sortDirection: 'Ascending'
    }).subscribe();

    const request = http.expectOne(item => item.url.includes('/api/reports/production/quantities'));
    expect(request.request.method).toBe('GET');
    expect(request.request.urlWithParams).toContain('view=StageWorkers');
    expect(request.request.urlWithParams).toContain('workerId=worker-1');
    expect(request.request.urlWithParams).not.toMatch(/salary|price|cost|earning|entitlement|currency|compensation|fixedamount/i);
    request.flush({
      success: true,
      data: {
        summary: { totalPhysicalProducedQuantity: 0, totalPhysicalAcceptedQuantity: 0, totalPhysicalRejectedQuantity: 0, totalStageProducedQuantity: 0, totalAcceptedQuantity: 0, totalRejectedQuantity: 0, recordCount: 0, stageCount: 0, workerCount: 0 },
        rows: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        appliedStatus: 'Approved', view: 'StageWorkers', sortBy: 'StageCode', sortDirection: 'Ascending'
      }
    });
  });

  it('preserves the date range and status while omitting empty optional filters', () => {
    service.query({
      from: '2026-07-01', to: '2026-07-31', factoryId: '', productionLineId: '', productModelId: '',
      productionOrderId: '', productModelStageId: '', workerId: '', status: 'Draft', view: 'ByWorker',
      page: 1, pageSize: 20, sortDirection: 'Ascending'
    }).subscribe();

    const request = http.expectOne(item => item.url.includes('/api/reports/production/quantities'));
    expect(request.request.urlWithParams).toContain('from=2026-07-01');
    expect(request.request.urlWithParams).toContain('to=2026-07-31');
    expect(request.request.urlWithParams).toContain('status=Draft');
    expect(request.request.urlWithParams).toContain('view=ByWorker');
    expect(request.request.urlWithParams).not.toMatch(/factoryId=|productionLineId=|productModelId=|productionOrderId=|productModelStageId=|workerId=/);
    request.flush({
      success: true,
      data: {
        summary: { totalPhysicalProducedQuantity: 0, totalPhysicalAcceptedQuantity: 0, totalPhysicalRejectedQuantity: 0, totalStageProducedQuantity: 0, totalAcceptedQuantity: 0, totalRejectedQuantity: 0, recordCount: 0, stageCount: 0, workerCount: 0 },
        rows: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        appliedStatus: 'Draft', view: 'ByWorker', sortBy: 'WorkerCode', sortDirection: 'Ascending'
      }
    });
  });
});
