import { of, throwError } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { DashboardApiService } from '../../core/services/dashboard-api.service';
import { DashboardPageComponent } from './dashboard-page.component';

describe('DashboardPageComponent', () => {
  function permissions(canViewAttendance: boolean): jasmine.SpyObj<PermissionService> {
    const service = jasmine.createSpyObj<PermissionService>('PermissionService', ['ensureHydrated', 'hasPermission']);
    service.ensureHydrated.and.returnValue(of([]));
    service.hasPermission.and.returnValue(canViewAttendance);
    return service;
  }

  it('renders API data without a MockDataService fallback', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 75, totalLines: 1, healthyLines: 0, warningLines: 1, criticalLines: 0, activeWorkers: 2, totalWorkers: 3, attendanceRate: 67 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete', readinessState: 'available', attendanceState: 'available', notificationsState: 'available', hasLoadError: false }));
    const component = new DashboardPageComponent(api, permissions(true));

    component.ngOnInit();

    expect(component.hasLoadError).toBeFalse();
    expect(component.lineReadinessSummary.overallReadiness).toBe(75);
  });

  it('shows an error state rather than fabricated data when the API fails', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(throwError(() => new Error('offline')));
    const component = new DashboardPageComponent(api, permissions(true));

    component.ngOnInit();

    expect(component.hasLoadError).toBeTrue();
    expect(component.cards).toEqual([]);
  });

  it('loads the permitted sources without attendance when attendance.view is absent', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 75, totalLines: 1, healthyLines: 0, warningLines: 1, criticalLines: 0, activeWorkers: 2, totalWorkers: 3, attendanceRate: 0 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Complete', readinessState: 'available', attendanceState: 'not-authorized', notificationsState: 'available', hasLoadError: false }));
    const component = new DashboardPageComponent(api, permissions(false));

    component.ngOnInit();

    expect(api.loadDashboardData).toHaveBeenCalledWith({ includeAttendance: false });
    expect(component.attendanceUnavailableByPermission).toBeTrue();
    expect(component.hasLoadError).toBeFalse();
  });

  it('does not treat unavailable attendance data as confirmed operational readiness', () => {
    const api = jasmine.createSpyObj<DashboardApiService>('DashboardApiService', ['loadDashboardData']);
    api.loadDashboardData.and.returnValue(of({ cards: [], lineReadinessSummary: { overallReadiness: 0, totalLines: 1, healthyLines: 0, warningLines: 0, criticalLines: 1, activeWorkers: 0, totalWorkers: 2, attendanceRate: 0 }, attendanceIndicators: [], previewLines: [], criticalReadinessAlerts: [], assignmentCoveragePercent: 100, attendanceDataStatus: 'Unavailable', readinessState: 'available', attendanceState: 'available', notificationsState: 'available', hasLoadError: false }));
    const component = new DashboardPageComponent(api, permissions(true));

    component.ngOnInit();

    expect(component.hasReadinessData).toBeFalse();
    expect(component.readinessUnavailableMessage).toContain('مزامنة حضور اليوم');
  });
});
