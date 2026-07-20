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

  it('unwraps paginated stage, line, and general model responses without search-stage payloads', () => {
    let values: unknown[][] = [];
    service.mainStages().subscribe(value => values.push(value)); service.subStages().subscribe(value => values.push(value)); service.productionLines().subscribe(value => values.push(value)); service.models().subscribe(value => values.push(value));
    const page = (items: unknown[]) => ({ success: true, data: { items, totalCount: items.length, pageNumber: 1, pageSize: 50 } });
    http.expectOne(request => request.url.endsWith('/api/main-stages')).flush(page([{ id: 'main-1' }]));
    http.expectOne(request => request.url.endsWith('/api/stages')).flush(page([{ id: 'sub-1', defaultOrder: 4 }]));
    http.expectOne(request => request.url.endsWith('/api/production-lines')).flush(page([{ id: 'line-1' }]));
    http.expectOne(request => request.url.endsWith('/api/product-models?includeInactive=true')).flush(page([{ id: 'model-1', isActive: false }]));
    expect(values).toEqual([[{ id: 'main-1' }], [{ id: 'sub-1', defaultOrder: 4, sequenceOrder: 4 }], [{ id: 'line-1' }], [{ id: 'model-1', isActive: false }]]);
  });

  it('keeps management-model search pagination metadata and sends the opt-in stage summary request', () => {
    let page: unknown;
    service.modelSearchList('مرحلة الخياطة', 3, 20).subscribe(value => page = value);

    const request = http.expectOne(item => item.method === 'GET' && item.urlWithParams.includes('/api/product-models?'));
    expect(request.request.urlWithParams).toContain('includeInactive=true');
    expect(request.request.urlWithParams).toContain('includeStageSearchSummaries=true');
    expect(request.request.urlWithParams).toContain('search=%D9%85%D8%B1%D8%AD%D9%84%D8%A9%20%D8%A7%D9%84%D8%AE%D9%8A%D8%A7%D8%B7%D8%A9');
    expect(request.request.urlWithParams).toContain('page=3');
    expect(request.request.urlWithParams).toContain('pageSize=20');
    request.flush({ success: true, data: { items: [{ id: 'model-51', code: 'M-51', name: 'موديل لاحق', isActive: true, stages: [{ subStageId: 'stage-1', code: 'SEW', name: 'الخياطة' }] }], totalCount: 51, pageNumber: 3, pageSize: 20 } });

    expect(page).toEqual(jasmine.objectContaining({ totalCount: 51, pageNumber: 3, pageSize: 20 }));
  });

  it('loads active and inactive operational stages and maps DefaultOrder once at the API boundary', () => {
    let stages: unknown[] = [];
    service.allSubStages().subscribe(value => stages = value);
    const page = (items: unknown[]) => ({ success: true, data: { items } });
    http.expectOne(request => request.url.endsWith('/api/stages?includeInactive=true&pageSize=200')).flush(page([
      { id: 'active', defaultOrder: 1, isActive: true },
      { id: 'inactive', defaultOrder: 2, isActive: false }
    ]));
    expect(stages).toEqual([{ id: 'active', defaultOrder: 1, sequenceOrder: 1, isActive: true }, { id: 'inactive', defaultOrder: 2, sequenceOrder: 2, isActive: false }]);
  });

  it('keeps operational and attendance department endpoints separate', () => {
    let departments: unknown[] = [];
    let attendanceDepartments: unknown[] = [];

    service.departments().subscribe(value => departments = value);
    service.attendanceDepartments().subscribe(value => attendanceDepartments = value);

    http.expectOne(request => request.url.endsWith('/api/departments')).flush({
      success: true,
      data: {
        items: [
          { id: 'department-1', factoryId: 'factory-1', code: 'CUT', nameAr: 'القص' }
        ]
      }
    });
    http.expectOne(request => request.url.endsWith('/api/attendance/departments')).flush({
      success: true,
      data: { items: [{ departmentId: 4, name: 'Challenger' }] }
    });

    expect(departments).toEqual([{ id: 'department-1', factoryId: 'factory-1', code: 'CUT', nameAr: 'القص' }]);
    expect(attendanceDepartments).toEqual([{ departmentId: 4, name: 'Challenger' }]);
  });

  it('loads only production lines belonging to the selected operational department', () => {
    let lines: unknown[] = [];
    service.productionLinesForDepartment('department-1').subscribe(value => lines = value);

    http.expectOne(request => request.url.endsWith('/api/production-lines?departmentId=department-1&pageSize=200')).flush({
      success: true,
      data: { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط الخياطة', sequenceOrder: 1, isActive: true }] }
    });

    expect(lines).toEqual([{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط الخياطة', sequenceOrder: 1, isActive: true }]);
  });

  it('loads operational stages through the factory, department, line, and status filters', () => {
    let stages: unknown[] = [];
    service.operationalStages({ factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-1', isActive: false, includeInactive: true }).subscribe(value => stages = value);

    const request = http.expectOne(item => item.method === 'GET' && item.urlWithParams.includes('/api/stages?'));
    expect(request.request.urlWithParams).toContain('factoryId=factory-1');
    expect(request.request.urlWithParams).toContain('departmentId=department-1');
    expect(request.request.urlWithParams).toContain('productionLineId=line-1');
    expect(request.request.urlWithParams).toContain('isActive=false');
    expect(request.request.urlWithParams).toContain('includeInactive=true');
    request.flush({ success: true, data: { items: [{ id: 'stage-1', mainStageId: 'legacy-group', productionLineId: 'line-1', name: 'تجهيز', code: 'STG001', capacity: 2, defaultOrder: 1, isActive: false }], totalCount: 1, pageNumber: 1, pageSize: 200 } });

    expect(stages).toEqual([{ id: 'stage-1', mainStageId: 'legacy-group', productionLineId: 'line-1', name: 'تجهيز', code: 'STG001', capacity: 2, defaultOrder: 1, sequenceOrder: 1, isActive: false }]);
  });
});
