import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { buildApiUrl } from '../config/api.config';
import { OperationalReadinessApiService } from './operational-readiness-api.service';

describe('OperationalReadinessApiService', () => {
  let service: OperationalReadinessApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(OperationalReadinessApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the initial snapshot and lazy stage and worker resources', () => {
    let loadedSnapshot: unknown;
    let loadedStages: unknown;
    let loadedWorkers: unknown;
    service.loadSnapshot('factory 1').subscribe(value => loadedSnapshot = value);
    const snapshot = http.expectOne(request => request.url === buildApiUrl('/operational-readiness') && request.params.get('factoryId') === 'factory 1');
    snapshot.flush({ success: true, data: { factories: [] } });

    service.loadStages('line/1', 'model/1').subscribe(value => loadedStages = value);
    http.expectOne(request => request.url === buildApiUrl('/operational-readiness/lines/line%2F1/stages') && request.params.get('productModelId') === 'model/1')
      .flush({ success: true, data: { stages: [] } });

    service.loadWorkers('line/1', 'stage/1').subscribe(value => loadedWorkers = value);
    http.expectOne(buildApiUrl('/operational-readiness/lines/line%2F1/stages/stage%2F1/workers')).flush({ success: true, data: { workers: [] } });

    expect(loadedSnapshot).toEqual(jasmine.objectContaining({ factories: [] }));
    expect(loadedStages).toEqual(jasmine.objectContaining({ stages: [] }));
    expect(loadedWorkers).toEqual(jasmine.objectContaining({ workers: [] }));
  });

  it('rejects an unsuccessful response instead of fabricating readiness data', () => {
    let error: Error | undefined;
    service.loadSnapshot().subscribe({ error: value => error = value });

    http.expectOne(buildApiUrl('/operational-readiness')).flush({ success: false, data: null, error: { message: 'غير متاح' } });

    expect(error?.message).toBe('غير متاح');
  });

  it('forces a fresh snapshot request for freshness refresh', () => {
    let loadedSnapshot: unknown;
    service.loadSnapshot(undefined, true).subscribe(value => loadedSnapshot = value);

    const request = http.expectOne(request => request.url === buildApiUrl('/operational-readiness') && request.params.has('_'));
    expect(request.request.params.get('_')).toBeTruthy();
    request.flush({ success: true, data: { factories: [] } });

    expect(loadedSnapshot).toEqual(jasmine.objectContaining({ factories: [] }));
  });
});
