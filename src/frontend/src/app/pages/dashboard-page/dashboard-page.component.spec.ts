import { of, throwError } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { DashboardApiService } from '../../core/services/dashboard-api.service';
import { AttendanceApiService } from '../../core/services/attendance-api.service';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  function permissions(canViewAttendance: boolean, canSyncAttendance = false): jasmine.SpyObj<PermissionService> {
    const service = jasmine.createSpyObj<PermissionService>('PermissionService', ['ensureHydrated', 'hasPermission']);
    service.ensureHydrated.and.returnValue(of([]));
    service.hasPermission.and.callFake((permission) => permission === 'attendance.view' ? canViewAttendance : permission === 'attendance.sync' ? canSyncAttendance : false);
    return service;
  }

  function attendanceApi(): jasmine.SpyObj<AttendanceApiService> {
    return jasmine.createSpyObj<AttendanceApiService>('AttendanceApiService', ['syncToday']);
  }

  it('renders API data without a MockDataService fallback', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 75, totalLines: 1, healthyLines: 0, warningLines: 1, criticalLines: 0, activeWorkers: 2, totalWorkers: 3, attendanceRate: 67 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete', readinessState: 'available', attendanceState: 'available', notificationsState: 'available', hasLoadError: false }));
    const component = new DashboardPageComponent(api, permissions(true), attendanceApi());

    component.ngOnInit();

    expect(component.hasLoadError).toBeFalse();
    expect(component.lineReadinessSummary.overallReadiness).toBe(75);
  });

  it('shows an error state rather than fabricated data when the API fails', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(throwError(() => new Error('offline')));
    const component = new DashboardPageComponent(api, permissions(true), attendanceApi());

    component.ngOnInit();

    expect(component.hasLoadError).toBeTrue();
    expect(component.cards).toEqual([]);
  });

  it('loads the permitted sources without attendance when attendance.view is absent', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 75, totalLines: 1, healthyLines: 0, warningLines: 1, criticalLines: 0, activeWorkers: 2, totalWorkers: 3, attendanceRate: 0 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete', readinessState: 'available', attendanceState: 'not-authorized', notificationsState: 'available', hasLoadError: false }));
    const component = new DashboardPageComponent(api, permissions(false), attendanceApi());

    component.ngOnInit();

    expect(api.loadDashboardData).toHaveBeenCalledWith({ includeAttendance: false });
    expect(component.attendanceUnavailableByPermission).toBeTrue();
    expect(component.hasLoadError).toBeFalse();
  });

  it('does not treat unavailable attendance data as confirmed operational readiness', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 0, totalLines: 1, healthyLines: 0, warningLines: 0, criticalLines: 1, activeWorkers: 0, totalWorkers: 2, attendanceRate: 0 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Unavailable', readinessState: 'available', attendanceState: 'available', notificationsState: 'available', hasLoadError: false }));
    const component = new DashboardPageComponent(api, permissions(true), attendanceApi());

    component.ngOnInit();

    expect(component.hasReadinessData).toBeFalse();
    expect(component.readinessUnavailableMessage).toContain('مزامنة حضور اليوم');
  });

  it('manually syncs today only for attendance.sync and refreshes the dashboard data', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    const initial = { cards: [], lineReadinessSummary: { overallReadiness: 0, totalLines: 1, healthyLines: 0, warningLines: 0, criticalLines: 1, activeWorkers: 0, totalWorkers: 2, attendanceRate: 0 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Unavailable', readinessState: 'available' as const, attendanceState: 'available' as const, notificationsState: 'available' as const, hasLoadError: false };
    const refreshed = { ...initial, lineReadinessSummary: { ...initial.lineReadinessSummary, overallReadiness: 100, activeWorkers: 2, attendanceRate: 100 }, attendanceDataStatus: 'Complete' };
    api.loadDashboardData.and.returnValues(of(initial), of(refreshed));
    const attendance = attendanceApi();
    attendance.syncToday.and.returnValue(of({ syncDateUtc: '2026-07-19T00:00:00Z', sourceUsersCount: 2, sourceCheckInsCount: 2, matchedWorkersCount: 2, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0, insertedRecords: 2, updatedRecords: 0, skippedRecords: 0 }));
    const component = new DashboardPageComponent(api, permissions(true, true), attendance);

    component.ngOnInit();
    component.synchronizeAttendanceToday();

    expect(attendance.syncToday).toHaveBeenCalledTimes(1);
    expect(api.loadDashboardData).toHaveBeenCalledTimes(2);
    expect(component.lineReadinessSummary.overallReadiness).toBe(100);
    expect(component.attendanceSyncMessage).toContain('تمت مزامنة حضور اليوم');
  });

  it('keeps the displayed dashboard data when manual synchronization fails', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 75, totalLines: 1, healthyLines: 0, warningLines: 1, criticalLines: 0, activeWorkers: 2, totalWorkers: 3, attendanceRate: 67 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete', readinessState: 'available', attendanceState: 'available', notificationsState: 'available', hasLoadError: false }));
    const attendance = attendanceApi();
    attendance.syncToday.and.returnValue(throwError(() => new Error('source offline')));
    const component = new DashboardPageComponent(api, permissions(true, true), attendance);

    component.ngOnInit();
    component.synchronizeAttendanceToday();

    expect(component.lineReadinessSummary.overallReadiness).toBe(75);
    expect(component.attendanceSyncFailed).toBeTrue();
    expect(component.attendanceSyncMessage).toContain('لم يتم تغيير المؤشرات');
  });
});
