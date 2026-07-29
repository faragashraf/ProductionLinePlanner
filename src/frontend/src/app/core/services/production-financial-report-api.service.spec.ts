import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProductionFinancialReportApiService } from './production-financial-report-api.service';

describe('ProductionFinancialReportApiService', () => {
  let service: ProductionFinancialReportApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ProductionFinancialReportApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the dedicated financial endpoint with the same report filters', () => {
    service.query({
      from: '2026-07-18', to: '2026-07-18', factoryId: 'factory-1', productionLineId: 'line-1',
      productModelId: 'model-1', productionOrderId: 'order-1', productModelStageId: 'stage-1', workerId: 'worker-1',
      status: 'Approved', view: 'StageWorkers', page: 2, pageSize: 20, sortBy: 'StageCode', sortDirection: 'Descending'
    }).subscribe();

    const request = http.expectOne(item => item.url.includes('/api/reports/production/financials'));
    expect(request.request.method).toBe('GET');
    expect(request.request.urlWithParams).toContain('workerId=worker-1');
    expect(request.request.urlWithParams).toContain('sortBy=StageCode');
    expect(request.request.urlWithParams).not.toMatch(/salary|baseSalary|workerSalaryHistory/i);
    request.flush({
      success: true,
      data: {
        summary: {
          totalPhysicalProducedQuantity: 500, totalPhysicalAcceptedQuantity: 500, totalPhysicalRejectedQuantity: 0,
          recordCount: 3, stageCount: 3, workerCount: 1, totalProductionEarnings: 375,
          totalStageProductionCost: 750, averageProductionEarningPerWorker: 375, averageCostPerPhysicalUnit: 1.5,
          incompleteFinancialRecordCount: 0, financialDataStatus: 'Complete', currencyCode: 'EGP'
        },
        rows: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0,
        appliedStatus: 'Approved', view: 'StageWorkers', sortBy: 'StageCode', sortDirection: 'Descending'
      }
    });
  });
});
