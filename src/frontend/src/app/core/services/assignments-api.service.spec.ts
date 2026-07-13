import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { fakeAsync, tick } from '@angular/core/testing';
import { AssignmentsApiService } from './assignments-api.service';
import { WorkersApiService } from './workers-api.service';

const subStageId = 'c0ec408d-74ab-4299-88cd-1a7543cc335b';
const workerId = '7d8f9c5b-09f4-4f2e-9360-16a5e950e2c7';

describe('AssignmentsApiService', () => {
  let service: AssignmentsApiService;
  let workersApi: WorkersApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AssignmentsApiService);
    workersApi = TestBed.inject(WorkersApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts the Factory Structure default assignment payload to the dedicated endpoint', () => {
    let assignmentId = '';

    service.createFactoryStructureDefaultAssignment({
      workerId: 'worker-1',
      subStageId: 'sub-1',
      reason: 'Factory structure assignment'
    }).subscribe(result => assignmentId = result.assignmentId);

    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'POST' && httpRequest.url.endsWith('/api/factory-structure/assignments/default')
    );
    expect(request.request.body).toEqual({
      workerId: 'worker-1',
      subStageId: 'sub-1',
      reason: 'Factory structure assignment'
    });
    request.flush({
      success: true,
      data: {
        assignmentId: 'assignment-1',
        workerId: 'worker-1',
        subStageId: 'sub-1',
        assignmentType: 'Default',
        startsAt: '2026-07-13T00:00:00Z'
      }
    });

    expect(assignmentId).toBe('assignment-1');
  });

  it('keeps assigned-workers active while eligible-workers completes beyond the former 1.5 second deadline', fakeAsync(() => {
    let eligibleWorkers = 0;
    let assignedWorkers = 0;

    workersApi.loadFactoryStructureEligibleWorkers(subStageId).subscribe(workers => eligibleWorkers = workers.length);
    service.getFactoryStructureSubStageWorkers(subStageId).subscribe(data => assignedWorkers = data.workers.length);

    const eligibleRequest = http.expectOne(request =>
      request.method === 'GET' && request.url.endsWith(`/api/factory-structure/sub-stages/${subStageId}/eligible-workers`)
    );
    const assignedRequest = http.expectOne(request =>
      request.method === 'GET' && request.url.endsWith(`/api/factory-structure/sub-stages/${subStageId}/workers`)
    );

    eligibleRequest.flush({
      success: true,
      data: {
        items: [{ id: workerId, code: 'W-1', fullName: 'عامل تجريبي', state: 'جاهز', phone: '01000000000' }]
      }
    });
    tick(1501);

    expect(eligibleWorkers).toBe(1);
    expect(assignedRequest.cancelled).toBeFalse();

    assignedRequest.flush({
      success: true,
      data: {
        subStageId,
        workers: [{ workerId, employeeCode: 'W-1', fullName: 'عامل تجريبي', assignmentType: 'Default' }]
      }
    });

    expect(assignedWorkers).toBe(1);
  }));

  it('keeps eligible-workers active when assigned-workers completes first', () => {
    let eligibleWorkers = 0;
    let assignedWorkers = 0;

    workersApi.loadFactoryStructureEligibleWorkers(subStageId).subscribe(workers => eligibleWorkers = workers.length);
    service.getFactoryStructureSubStageWorkers(subStageId).subscribe(data => assignedWorkers = data.workers.length);

    const eligibleRequest = http.expectOne(request =>
      request.method === 'GET' && request.url.endsWith(`/api/factory-structure/sub-stages/${subStageId}/eligible-workers`)
    );
    const assignedRequest = http.expectOne(request =>
      request.method === 'GET' && request.url.endsWith(`/api/factory-structure/sub-stages/${subStageId}/workers`)
    );

    assignedRequest.flush({
      success: true,
      data: { subStageId, workers: [] }
    });

    expect(assignedWorkers).toBe(0);
    expect(eligibleRequest.cancelled).toBeFalse();

    eligibleRequest.flush({
      success: true,
      data: {
        items: [{ id: workerId, code: 'W-1', fullName: 'عامل تجريبي', state: 'جاهز', phone: '01000000000' }]
      }
    });

    expect(eligibleWorkers).toBe(1);
  });
});
