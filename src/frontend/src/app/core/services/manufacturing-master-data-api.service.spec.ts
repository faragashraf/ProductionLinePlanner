import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ManufacturingMasterDataApiService } from './manufacturing-master-data-api.service';

describe('ManufacturingMasterDataApiService', () => {
  let service: ManufacturingMasterDataApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ManufacturingMasterDataApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('unwraps paginated stage, line, and model responses including inactive models', () => {
    let values: unknown[][] = [];
    service.mainStages().subscribe(value => values.push(value)); service.subStages().subscribe(value => values.push(value)); service.productionLines().subscribe(value => values.push(value)); service.models().subscribe(value => values.push(value));
    const page = (items: unknown[]) => ({ success: true, data: { items, totalCount: items.length, pageNumber: 1, pageSize: 50 } });
    http.expectOne(request => request.url.endsWith('/api/main-stages')).flush(page([{ id: 'main-1' }]));
    http.expectOne(request => request.url.endsWith('/api/sub-stages')).flush(page([{ id: 'sub-1' }]));
    http.expectOne(request => request.url.endsWith('/api/production-lines')).flush(page([{ id: 'line-1' }]));
    http.expectOne(request => request.url.endsWith('/api/product-models?includeInactive=true')).flush(page([{ id: 'model-1', isActive: false }]));
    expect(values).toEqual([[{ id: 'main-1' }], [{ id: 'sub-1' }], [{ id: 'line-1' }], [{ id: 'model-1', isActive: false }]]);
  });
});
