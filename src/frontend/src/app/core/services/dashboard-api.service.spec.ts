import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { buildApiUrl } from '../config/api.config';
import { DashboardApiService } from './dashboard-api.service';

describe('DashboardApiService', () => {
  let service: DashboardApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(DashboardApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('maps the current readiness and attendance contracts without mock values', () => {
    let result: ReturnType<DashboardApiService['loadDashboardData']> extends import('rxjs').Observable<infer T> ? T | undefined : never;
    service.loadDashboardData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/readiness/factory')).flush({ success: true, data: {
      readinessPercent: 75, assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete', requiredWorkers: 10, presentWorkers: 6
    } });
    http.expectOne(buildApiUrl('/api/readiness/production-lines')).flush({ success: true, data: {
      items: [{ scopeEntityId: 'line-1', lineName: 'خط 1', requiredWorkers: 10, presentWorkers: 6, readinessPercent: 75 }]
    } });
    http.expectOne(buildApiUrl('/api/attendance/today')).flush({ success: true, data: {
      items: [{ attendanceStatus: 'Present' }, { attendanceStatus: 'Late' }, { attendanceStatus: 'Absent' }]
    } });
    http.expectOne(buildApiUrl('/api/notifications/unread-count')).flush({ success: true, data: { unreadCount: 2 } });

    expect(result!.lineReadinessSummary.overallReadiness).toBe(75);
    expect(result!.lineReadinessSummary.attendanceRate).toBe(67);
    expect(result!.attendanceIndicators.map((item) => item.value)).toEqual([2, 1, 1]);
    expect(result!.cards.find((card) => card.title === 'الإشعارات غير المقروءة')?.value).toBe('2');
  });

  it('does not request attendance when the caller lacks attendance.view', () => {
    let result: any;
    service.loadDashboardData({ includeAttendance: false }).subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/readiness/factory')).flush({ success: true, data: { readinessPercent: 75, assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete' } });
    http.expectOne(buildApiUrl('/api/readiness/production-lines')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/notifications/unread-count')).flush({ success: true, data: { unreadCount: 1 } });

    expect(http.match(buildApiUrl('/api/attendance/today'))).toEqual([]);
    expect(result.attendanceState).toBe('not-authorized');
    expect(result.attendanceIndicators).toEqual([]);
    expect(result.cards.some((card: any) => card.title === 'العاملون الحاضرون')).toBeFalse();
  });

  it('keeps permitted dashboard sources when attendance returns a server error', () => {
    let result: any;
    service.loadDashboardData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/readiness/factory')).flush({ success: true, data: { readinessPercent: 75, assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete' } });
    http.expectOne(buildApiUrl('/api/readiness/production-lines')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/attendance/today')).flush({ error: 'unavailable' }, { status: 500, statusText: 'Server Error' });
    http.expectOne(buildApiUrl('/api/notifications/unread-count')).flush({ success: true, data: { unreadCount: 2 } });

    expect(result.readinessState).toBe('available');
    expect(result.attendanceState).toBe('error');
    expect(result.hasLoadError).toBeTrue();
    expect(result.cards.find((card: any) => card.title === 'الإشعارات غير المقروءة')?.value).toBe('2');
  });

  it('does not present operational readiness as confirmed when attendance data is unavailable', () => {
    let result: any;
    service.loadDashboardData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/readiness/factory')).flush({ success: true, data: {
      readinessPercent: 0, assignmentCoveragePercent: 100, attendanceDataStatus: 'Unavailable', requiredWorkers: 2, presentWorkers: 0
    } });
    http.expectOne(buildApiUrl('/api/readiness/production-lines')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/attendance/today')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/notifications/unread-count')).flush({ success: true, data: { unreadCount: 0 } });

    expect(result.attendanceDataStatus).toBe('Unavailable');
    expect(result.cards.some((card: any) => card.title === 'جاهزية المصنع')).toBeFalse();
  });
});
