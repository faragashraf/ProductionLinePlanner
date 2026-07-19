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
    http.expectOne(request => request.url.endsWith('/api/sub-stages')).flush(page([{ id: 'sub-1', defaultOrder: 4 }]));
    http.expectOne(request => request.url.endsWith('/api/production-lines')).flush(page([{ id: 'line-1' }]));
    http.expectOne(request => request.url.endsWith('/api/product-models?includeInactive=true')).flush(page([{ id: 'model-1', isActive: false }]));
    expect(values).toEqual([[{ id: 'main-1' }], [{ id: 'sub-1', defaultOrder: 4, sequenceOrder: 4 }], [{ id: 'line-1' }], [{ id: 'model-1', isActive: false }]]);
  });

  it('loads active and inactive sub-stages for administration and maps DefaultOrder once at the API boundary', () => {
    let stages: unknown[] = [];
    service.allSubStages().subscribe(value => stages = value);
    const page = (items: unknown[]) => ({ success: true, data: { items } });
    http.expectOne(request => request.url.endsWith('/api/sub-stages?isActive=true&pageSize=200')).flush(page([{ id: 'active', defaultOrder: 1, isActive: true }]));
    http.expectOne(request => request.url.endsWith('/api/sub-stages?isActive=false&pageSize=200')).flush(page([{ id: 'inactive', defaultOrder: 2, isActive: false }]));
    expect(stages).toEqual([{ id: 'active', defaultOrder: 1, sequenceOrder: 1, isActive: true }, { id: 'inactive', defaultOrder: 2, sequenceOrder: 2, isActive: false }]);
  });

  it('unwraps department responses from the existing API envelope', () => {
    let departments: unknown[] = [];

    service.departments().subscribe(value => departments = value);

    http.expectOne(request => request.url.endsWith('/api/departments')).flush({
      success: true,
      data: {
        items: [
          { departmentId: 4, name: 'Challenger' }
        ]
      }
    });

    expect(departments).toEqual([{ departmentId: 4, name: 'Challenger' }]);
  });

  it('maps active compensation model options from the endpoint envelope', () => {
    let models: unknown[] = [];
    service.compensationModels().subscribe(value => models = value);

    const request = http.expectOne(item => item.method === 'GET' && item.urlWithParams.includes('/api/compensation/models?includeInactive=false'));
    request.flush({ success: true, data: { items: [{ id: 'model-grm001', code: 'GRM001', name: 'جرومان', isActive: true }] } });

    expect(models).toEqual([{ id: 'model-grm001', code: 'GRM001', name: 'جرومان', isActive: true }]);
  });

  it('maps stage configuration and sends only compensation-editable fields on save', () => {
    let stageCount = 0;
    service.compensationModelStages('model-grm001').subscribe(value => stageCount = value.length);
    http.expectOne(item => item.method === 'GET' && item.url.endsWith('/api/compensation/models/model-grm001/stages')).flush({
      success: true,
      data: [{ id: 'stage-1', productModelId: 'model-grm001', subStageId: 'sub-1', subStageCode: 'STG001', subStageName: 'تجهيز', stageOrder: 1, piecePrice: 0.5, standardSeconds: 22, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }]
    });

    service.updateCompensationModelStage('model-grm001', 'stage-1', {
      compensationMode: 'FullRatePerWorker',
      piecePrice: 0.75,
      standardSeconds: 18
    }).subscribe();
    const request = http.expectOne(item => item.method === 'PATCH' && item.url.endsWith('/api/compensation/models/model-grm001/stages/stage-1'));
    expect(request.request.body).toEqual({ compensationMode: 'FullRatePerWorker', piecePrice: 0.75, standardSeconds: 18 });
    request.flush({ success: true, data: { id: 'stage-1', subStageId: 'sub-1', stageOrder: 1, piecePrice: 0.75, standardSeconds: 18, compensationMode: 'FullRatePerWorker', isRequired: true, isActive: true } });

    expect(stageCount).toBe(1);
  });
});
