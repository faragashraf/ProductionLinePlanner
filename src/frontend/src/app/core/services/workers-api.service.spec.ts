import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WorkerPageItem } from '../../shared/models/worker.model';
import { WorkersApiService } from './workers-api.service';

describe('WorkersApiService', () => {
  let service: WorkersApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(WorkersApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('maps the factory-structure eligible-workers envelope into worker options without requesting general workers', () => {
    let workers: WorkerPageItem[] = [];

    service.loadFactoryStructureEligibleWorkers('sub-1').subscribe(value => workers = value);

    http.expectOne(request => request.url.endsWith('/api/factory-structure/sub-stages/sub-1/eligible-workers')).flush({
      success: true,
      data: {
        items: [
          { id: 'worker-1', code: 'W-1', fullName: 'عامل تجريبي', state: 'جاهز', phone: '01000000000' }
        ],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 1
      }
    });

    http.expectNone(request => request.url.endsWith('/api/workers'));
    expect(workers).toEqual([{ id: 'worker-1', code: 'W-1', fullName: 'عامل تجريبي', state: 'جاهز', phone: '01000000000' }]);
  });
});
