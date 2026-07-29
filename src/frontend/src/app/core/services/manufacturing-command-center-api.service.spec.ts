import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ManufacturingCommandCenterApiService } from './manufacturing-command-center-api.service';
import { defaultCommandCenterFilters } from '../../shared/models/manufacturing-command-center.model';

describe('ManufacturingCommandCenterApiService', () => {
  let service: ManufacturingCommandCenterApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ManufacturingCommandCenterApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends date and every active scope filter in one request without fallback mapping', () => {
    const filters = { ...defaultCommandCenterFilters(), operationDate: '2026-07-22', factoryId: 'f1', departmentId: 'd1', productionLineId: 'l1', operationStatus: 'Draft' as const };
    service.load(filters).subscribe();

    const request = http.expectOne(req => req.url.endsWith('/api/manufacturing-command-center'));
    expect(request.request.params.get('productionDate')).toBe('2026-07-22');
    expect(request.request.params.get('factoryId')).toBe('f1');
    expect(request.request.params.get('departmentId')).toBe('d1');
    expect(request.request.params.get('productionLineId')).toBe('l1');
    expect(request.request.params.get('operationStatus')).toBe('Draft');
    request.flush({ success: true, data: {} });
  });
});
