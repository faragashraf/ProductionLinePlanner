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
          { id: 'worker-1', code: 'W-1', fullName: 'عامل تجريبي', employmentStatus: 'Active', isActive: true, photoReference: 'worker-1.png', phone: '01000000000' }
        ],
        totalCount: 1,
        pageNumber: 1,
        pageSize: 1
      }
    });

    http.expectNone(request => request.url.endsWith('/api/workers'));
    expect(workers).toEqual([{ id: 'worker-1', code: 'W-1', fullName: 'عامل تجريبي', state: 'على رأس العمل', employmentStatus: 'Active', isActive: true, photoReference: 'worker-1.png', hasPhoto: true, phone: '01000000000' }]);
  });

  it('retains lightweight photo metadata without exposing image bytes', () => {
    let workers: WorkerPageItem[] = [];
    service.loadWorkers().subscribe(result => workers = result.workers);
    const request = http.expectOne(item => item.url.endsWith('/api/workers'));
    request.flush({ success: true, data: { items: [
      { id: 'with-photo', employeeCode: '119', fullName: 'Worker 119', isActive: true, employmentStatus: 'Active', hasPhoto: true, photoReference: '/api/workers/with-photo/photo?v=abc', photoVersion: 'abc' },
      { id: 'without-photo', employeeCode: '120', fullName: 'No Photo', isActive: true, employmentStatus: 'Active', hasPhoto: false, photoReference: null }
    ] } });

    expect(workers[0]).toEqual(jasmine.objectContaining({ hasPhoto: true, photoVersion: 'abc' }));
    expect(workers[1]).toEqual(jasmine.objectContaining({ hasPhoto: false }));
    expect(JSON.stringify(workers)).not.toContain('base64');
  });

  it('uses an unconstrained query for All, and explicit active/inactive constraints for status filters', () => {
    let allWorkers: WorkerPageItem[] = [];
    service.loadWorkers({ search: 'Ali' }).subscribe(result => allWorkers = result.workers);
    const allRequest = http.expectOne(request => request.url.endsWith('/api/workers'));
    expect(allRequest.request.params.has('isActive')).toBeFalse();
    expect(allRequest.request.params.get('search')).toBe('Ali');
    allRequest.flush({ success: true, data: { items: [
      { id: 'active', employeeCode: 'A-1', fullName: 'Active', isActive: true, employmentStatus: 'Active' },
      { id: 'former', employeeCode: 'F-1', fullName: 'Former', isActive: false, employmentStatus: 'LeftEmployment' }
    ], totalCount: 2, pageNumber: 1, pageSize: 20 } });
    expect(allWorkers.map(worker => worker.code)).toEqual(['A-1', 'F-1']);

    service.loadWorkers({ serviceStatus: 'active' }).subscribe();
    const activeRequest = http.expectOne(request => request.url.endsWith('/api/workers'));
    expect(activeRequest.request.params.get('isActive')).toBe('true');
    activeRequest.flush({ success: true, data: { items: [] } });

    service.loadWorkers({ serviceStatus: 'inactive' }).subscribe();
    const formerRequest = http.expectOne(request => request.url.endsWith('/api/workers'));
    expect(formerRequest.request.params.get('isActive')).toBe('false');
    formerRequest.flush({ success: true, data: { items: [] } });
  });

  it('patches one worker and maps the authoritative response for targeted UI reconciliation', () => {
    let updated: WorkerPageItem | undefined;

    service.updateWorker('worker-1', { fullName: 'عامل محدّث', phone: '01012345678' }).subscribe(value => updated = value);

    const request = http.expectOne(item => item.url.endsWith('/api/workers/worker-1'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ fullName: 'عامل محدّث', phone: '01012345678' });
    request.flush({ success: true, data: { id: 'worker-1', employeeCode: 'E-1', fullName: 'عامل محدّث', phone: '01012345678', isActive: true, employmentStatus: 'Active' } });

    expect(updated).toEqual(jasmine.objectContaining({ id: 'worker-1', code: 'E-1', fullName: 'عامل محدّث', phone: '01012345678' }));
  });

  it('loads one authoritative worker and preserves local photo metadata', () => {
    let worker: WorkerPageItem | undefined;
    service.getWorker('worker-1').subscribe(value => worker = value);
    const request = http.expectOne(item => item.url.endsWith('/api/workers/worker-1'));
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, data: {
      id: 'worker-1', employeeCode: 'E-1', fullName: 'عامل', isActive: true, employmentStatus: 'Active',
      badgeNumber: 'B-1', attendanceUserId: '99', hasPhoto: true, photoReference: '/api/workers/worker-1/photo?v=abc', photoVersion: 'abc'
    } });
    expect(worker).toEqual(jasmine.objectContaining({ badgeNumber: 'B-1', attendanceUserId: '99', photoReference: jasmine.stringContaining('?v=') }));
  });

  it('uses protected worker photo routes for upload, replacement, delete, and local employment status', () => {
    const file = new File(['valid image bytes'], 'worker.png', { type: 'image/png' });
    service.uploadWorkerPhoto('worker-1', file).subscribe();
    const upload = http.expectOne(item => item.url.endsWith('/api/workers/worker-1/photo'));
    expect(upload.request.method).toBe('PUT');
    const uploaded = (upload.request.body as FormData).get('photo') as File;
    expect(uploaded.name).toBe('worker.png');
    expect(uploaded.type).toBe('image/png');
    upload.flush({ success: true, data: { photo: { version: 'a'.repeat(64) } } });

    service.deleteWorkerPhoto('worker-1').subscribe();
    const remove = http.expectOne(item => item.url.endsWith('/api/workers/worker-1/photo'));
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null, { status: 204, statusText: 'No Content' });

    service.setEmploymentStatus('worker-1', { employmentStatus: 'Suspended' }).subscribe();
    const status = http.expectOne(item => item.url.endsWith('/api/workers/worker-1/employment-status'));
    expect(status.request.method).toBe('PATCH');
    expect(status.request.body).toEqual({ employmentStatus: 'Suspended' });
    status.flush({ success: true, data: { id: 'worker-1', employeeCode: 'E-1', fullName: 'عامل', isActive: false, employmentStatus: 'Suspended' } });
  });
});
