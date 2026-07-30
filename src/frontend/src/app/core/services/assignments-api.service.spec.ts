import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { fakeAsync, tick } from '@angular/core/testing';
import { AssignmentsApiService } from './assignments-api.service';
import { WorkersApiService } from './workers-api.service';

const subStageId = 'c0ec408d-74ab-4299-88cd-1a7543cc335b';
const workerId = '7d8f9c5b-09f4-4f2e-9360-16a5e950e2c7';
const factoryId = '43dde27f-7ee3-4e90-9f3b-582fc90a3b0';
const productionLineId = 'c0550d1f-4bf7-432c-b19b-672763d490fc';
const productModelId = '46593736-2fe2-450d-84a1-f304b712e07f';

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
      productionLineId: 'line-1',
      subStageId: 'sub-1',
      reason: 'Factory structure assignment'
    }).subscribe(result => assignmentId = result.assignmentId);

    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'POST' && httpRequest.url.endsWith('/api/factory-structure/assignments/default')
    );
    expect(request.request.body).toEqual({
      workerId: 'worker-1',
      productionLineId: 'line-1',
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

  it('updates permanent selections for one stage in one request', () => {
    let added = -1;

    service.updateStageDefaultAssignments('line-1', subStageId, [workerId, 'a1e4c17a-5ba5-4a56-95d8-02f39b896b2c'])
      .subscribe(result => added = result.addedWorkersCount);

    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'PUT' && httpRequest.url.endsWith(`/api/assignments/default/stages/${subStageId}`)
    );
    expect(request.request.body).toEqual({ productionLineId: 'line-1', workerIds: [workerId, 'a1e4c17a-5ba5-4a56-95d8-02f39b896b2c'] });
    request.flush({
      success: true,
      data: { subStageId, addedWorkersCount: 1, removedWorkersCount: 0, activeWorkerIds: [workerId, 'a1e4c17a-5ba5-4a56-95d8-02f39b896b2c'] }
    });

    expect(added).toBe(1);
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

  it('maps the read-only stage worker context with present, current and available workers', () => {
    let context: { current: number; present: number; available: number; attendance: string } | undefined;

    service.getSubStageWorkerContext(subStageId, '2026-07-13').subscribe(result => {
      context = {
        current: result.currentWorkers.length,
        present: result.presentWorkers.length,
        available: result.availableWorkers.length,
        attendance: result.currentWorkers[0].attendanceStatus
      };
    });

    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'GET' && httpRequest.urlWithParams.endsWith(`/api/assignments/sub-stages/${subStageId}/worker-context?productionDate=2026-07-13`)
    );
    request.flush({
      success: true,
      data: {
        subStageId,
        currentWorkers: [{ workerId, employeeCode: 'W-1', fullName: 'عامل تجريبي', attendanceStatus: 'Present', assignmentType: 'Default', effectiveSubStageId: subStageId, isAvailable: true }],
        presentWorkers: [{ workerId, employeeCode: 'W-1', fullName: 'عامل تجريبي', attendanceStatus: 'Present', assignmentType: 'Default', effectiveSubStageId: subStageId, isAvailable: true }],
        availableWorkers: [{ workerId, employeeCode: 'W-1', fullName: 'عامل تجريبي', attendanceStatus: 'Present', assignmentType: 'Default', effectiveSubStageId: subStageId, isAvailable: true }],
        unavailableWorkersCount: 2
      }
    });

    expect(context).toEqual({ current: 1, present: 1, available: 1, attendance: 'Present' });
  });

  it('does not issue a worker-context request for a stale invalid sub-stage id', () => {
    let resultCount = -1;
    service.getSubStageWorkerContext('stale-id', '2026-07-13').subscribe(result => resultCount = result.currentWorkers.length);
    expect(resultCount).toBe(0);
  });

  it('sends a required removal reason with the current-stage assignment request', () => {
    let status = '';
    service.removeDefaultAssignment(workerId, 'line-1', subStageId, 'انتهت الوردية').subscribe(result => status = result.assignmentType);
    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'DELETE' && httpRequest.url.endsWith(`/api/assignments/default/${workerId}`)
    );
    expect(request.request.params.get('subStageId')).toBe(subStageId);
    expect(request.request.params.get('productionLineId')).toBe('line-1');
    expect(request.request.params.get('reason')).toBe('انتهت الوردية');
    request.flush({ success: true, data: { assignmentId: 'assignment-1', workerId, subStageId, assignmentType: 'Default' } });
    expect(status).toBe('Default');
  });

  it('loads one attendance-free permanent line staffing plan for the selected factory, line and model', () => {
    let planName = '';
    let participationLineId = '';
    service.getLineStaffingPlan(factoryId, productionLineId, productModelId, '2026-07-13').subscribe(plan => {
      planName = plan.productModelName;
      participationLineId = plan.workers[0].participations[0].productionLineId;
    });

    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'GET' &&
      httpRequest.urlWithParams.includes('/api/line-staffing?') &&
      httpRequest.urlWithParams.includes(`factoryId=${factoryId}`) &&
      httpRequest.urlWithParams.includes(`productionLineId=${productionLineId}`) &&
      httpRequest.urlWithParams.includes(`productModelId=${productModelId}`) &&
      httpRequest.urlWithParams.includes('staffingReferenceDate=2026-07-13') &&
      !httpRequest.urlWithParams.includes('asOfUtc=')
    );
    request.flush({
      success: true,
      data: {
        factoryId,
        productionLineId,
        productModelId,
        productModelName: 'جرومان',
        stages: [],
        workers: [{
          workerId,
          employeeCode: '119',
          fullName: 'عامل',
          hasPhoto: false,
          participations: [{
            assignmentId: 'assignment-1',
            assignmentType: 'Default',
            productionLineId,
            subStageId,
            subStageName: 'الترفيع',
            fromSubStageId: null,
            fromSubStageName: null,
            startsAtUtc: '2026-07-10T08:00:00Z',
            endsAtUtc: null,
            replacementForWorkerId: null,
            temporaryParticipationMode: null
          }]
        }]
      }
    });

    expect(planName).toBe('جرومان');
    expect(participationLineId).toBe(productionLineId);
  });

  it('loads the shared active permanent staffing worker source without attendance', () => {
    let workers = 0;
    service.getActiveLineStaffingWorkers('2026-07-13').subscribe(items => workers = items.length);

    const request = http.expectOne(httpRequest =>
      httpRequest.method === 'GET' &&
      httpRequest.urlWithParams.includes('/api/line-staffing/workers?') &&
      httpRequest.urlWithParams.includes('staffingReferenceDate=2026-07-13') &&
      !httpRequest.urlWithParams.includes('asOfUtc=')
    );
    request.flush({
      success: true,
      data: [{ workerId, employeeCode: '119', fullName: 'عامل', isOnActiveService: true, hasPhoto: false }]
    });

    expect(workers).toBe(1);
  });
});
