import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { TimeoutError } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { ATTENDANCE_SYNC_TIMEOUT_MS, STANDARD_API_TIMEOUT_MS } from '../config/api-timeout.config';
import { AttendanceApiService } from './attendance-api.service';

describe('AttendanceApiService', () => {
  let service: AttendanceApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AttendanceApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify({ ignoreCancelled: true }));

  it('posts the existing today-only synchronization endpoint', () => {
    let result: unknown;
    service.syncToday().subscribe(value => result = value);

    const request = http.expectOne(buildApiUrl('/api/attendance/sync/today'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({});
    request.flush({
      success: true,
      data: {
        syncDateUtc: '2026-07-15T00:00:00Z', sourceUsersCount: 12, sourceCheckInsCount: 8,
        matchedWorkersCount: 7, unmatchedSourceUsersCount: 1, workersWithoutAttendanceCount: 2,
        insertedRecords: 7, updatedRecords: 0, skippedRecords: 0
      }
    });

    expect(result).toEqual(jasmine.objectContaining({ matchedWorkersCount: 7 }));
  });

  it('loads the existing today attendance snapshot', () => {
    let result: unknown;
    service.getToday().subscribe(value => result = value);

    const request = http.expectOne(buildApiUrl('/api/attendance/today'));
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, data: { date: '2026-07-15T00:00:00Z', items: [] } });

    expect(result).toEqual({ date: '2026-07-15T00:00:00Z', items: [] });
  });

  it('posts an explicit historical production-date synchronization request', () => {
    let result: unknown;
    service.syncForProductionDate('2026-07-13').subscribe(value => result = value);

    const request = http.expectOne(buildApiUrl('/api/attendance/sync/production-date/2026-07-13'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({});
    request.flush({ success: true, data: { syncDateUtc: '2026-07-13T00:00:00Z', sourceUsersCount: 1, sourceCheckInsCount: 1, matchedWorkersCount: 1, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0, insertedRecords: 1, updatedRecords: 0, skippedRecords: 0 } });

    expect(result).toEqual(jasmine.objectContaining({ syncDateUtc: '2026-07-13T00:00:00Z' }));
  });

  it('loads attendance for the explicitly selected production date', () => {
    let result: unknown;
    service.getForProductionDate('2026-07-13').subscribe(value => result = value);

    const request = http.expectOne(buildApiUrl('/api/attendance/today?dateUtc=2026-07-13T12%3A00%3A00.000Z'));
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, data: { date: '2026-07-13T00:00:00Z', items: [] } });

    expect(result).toEqual({ date: '2026-07-13T00:00:00Z', items: [] });
  });

  it('does not cancel a manual sync at the normal 10-second API timeout', fakeAsync(() => {
    let result: unknown;
    service.syncToday().subscribe(value => result = value);

    const request = http.expectOne(buildApiUrl('/api/attendance/sync/today'));
    tick(STANDARD_API_TIMEOUT_MS + 1);
    expect(request.cancelled).toBeFalse();

    request.flush({
      success: true,
      data: {
        syncDateUtc: '2026-07-15T00:00:00Z', sourceUsersCount: 1, sourceCheckInsCount: 1,
        matchedWorkersCount: 1, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0,
        insertedRecords: 1, updatedRecords: 0, skippedRecords: 0
      }
    });
    expect(result).toEqual(jasmine.objectContaining({ matchedWorkersCount: 1 }));
  }));

  it('uses the bounded attendance-source timeout for a manual sync', fakeAsync(() => {
    let failure: unknown;
    service.syncToday().subscribe({ error: error => failure = error });

    const request = http.expectOne(buildApiUrl('/api/attendance/sync/today'));
    tick(ATTENDANCE_SYNC_TIMEOUT_MS);

    expect(request.cancelled).toBeTrue();
    expect(failure).toEqual(jasmine.any(TimeoutError));
  }));
});
