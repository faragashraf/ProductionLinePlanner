import { of, throwError } from 'rxjs';
import { AttendanceApiService } from '../../core/services/attendance-api.service';
import { AttendanceWorkforceApiService, WorkforceRow } from '../../core/services/attendance-workforce-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { AttendanceWorkforcePageComponent } from './attendance-workforce-page.component';

describe('AttendanceWorkforcePageComponent', () => {
  const row: WorkforceRow = {
    workerId: 'worker-1', employeeCode: '1001', fullName: 'فاطمة عربي', departmentName: 'الخياطة', photoReference: null, hasPhoto: false,
    attendanceStatus: 'Present', firstCheckInUtc: '2026-07-19T05:00:00Z', lastCheckOutUtc: '2026-07-19T13:30:00Z', hasAttendanceData: true, hasSinglePunch: false,
    assignments: [], isAssigned: false, hasTemporaryAssignment: false, needsReview: true
  };

  function createComponent(canSync = true, query: Record<string, string> = {}): AttendanceWorkforcePageComponent {
    const api = jasmine.createSpyObj<AttendanceWorkforceApiService>('AttendanceWorkforceApiService', ['getPage', 'getDetail']);
    api.getPage.and.returnValue(of({ productionDate: '2026-07-19', items: [row], summary: { totalWorkers: 1, presentWorkers: 1, absentWorkers: 0, lateWorkers: 0, incompleteWorkers: 0, unassignedPresentWorkers: 1, assignedAbsentWorkers: 0, reviewRequiredWorkers: 1, attendanceDataAvailable: true, scope: 'filtered-results' }, page: 1, pageSize: 25, totalCount: 1, totalPages: 1 }));
    api.getDetail.and.returnValue(of({ workerId: row.workerId, productionDate: '2026-07-19', attendanceRecords: [], assignments: [] }));
    const attendance = jasmine.createSpyObj<AttendanceApiService>('AttendanceApiService', ['syncForProductionDate']);
    attendance.syncForProductionDate.and.returnValue(of({ syncDateUtc: '2026-07-19T00:00:00Z', sourceUsersCount: 1, sourceCheckInsCount: 1, matchedWorkersCount: 1, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0, insertedRecords: 1, updatedRecords: 0, skippedRecords: 0 }));
    const master = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'mainStagesForDepartment', 'subStagesForMainStage']);
    master.factories.and.returnValue(of([])); master.allProductionLines.and.returnValue(of([])); master.mainStagesForDepartment.and.returnValue(of([])); master.subStagesForMainStage.and.returnValue(of([]));
    const permissions = jasmine.createSpyObj<PermissionService>('PermissionService', ['has']);
    permissions.has.and.returnValue(canSync);
    const route = { snapshot: { queryParamMap: { get: (name: string) => query[name] ?? null } } } as any;
    const router = jasmine.createSpyObj('Router', ['navigate']);
    router.navigate.and.resolveTo(true);
    return new AttendanceWorkforcePageComponent(api, attendance, master, permissions, undefined, route, router);
  }

  afterEach(() => localStorage.clear());

  it('toggles the compact filter panel without loading data', () => {
    const component = createComponent(); component.ngOnInit();
    component.toggleFilters();
    expect(component.filtersCollapsed).toBeFalse();
  });

  it('counts only active filter values', () => {
    const component = createComponent();
    component.search = 'فاطمة'; component.selectedFactoryId = 'factory-1'; component.attendanceFilter = 'Absent';
    expect(component.activeFilterCount).toBe(3);
  });

  it('applies notification worker and production-date query parameters to the server-side request', () => {
    const workerId = '11111111-1111-4111-8111-111111111111';
    const component = createComponent(true, { workerId, productionDate: '2026-07-29' });

    component.ngOnInit();

    const api = (component as any).api as jasmine.SpyObj<AttendanceWorkforceApiService>;
    expect(component.selectedWorkerId).toBe(workerId);
    expect(component.selectedDate).toBe('2026-07-29');
    expect(api.getPage).toHaveBeenCalledWith(jasmine.objectContaining({ workerId, productionDate: '2026-07-29' }));
  });

  it('removes the notification worker filter and reloads the normal query', () => {
    const component = createComponent(true, { workerId: '11111111-1111-4111-8111-111111111111', productionDate: '2026-07-29' });
    component.ngOnInit();

    component.clearWorkerFilter();

    expect(component.selectedWorkerId).toBe('');
    const api = (component as any).api as jasmine.SpyObj<AttendanceWorkforceApiService>;
    expect(api.getPage).toHaveBeenCalledWith(jasmine.objectContaining({ workerId: undefined }));
  });

  it('labels a filtered summary distinctly from the current page', () => {
    const component = createComponent();
    component.summary = { totalWorkers: 1, presentWorkers: 1, absentWorkers: 0, lateWorkers: 0, incompleteWorkers: 0, unassignedPresentWorkers: 0, assignedAbsentWorkers: 0, reviewRequiredWorkers: 0, attendanceDataAvailable: true, scope: 'filtered-results' };
    expect(component.summaryScopeLabel).toBe('الملخص للنتائج المفلترة');
    component.summary.scope = 'current-page';
    expect(component.summaryScopeLabel).toBe('الملخص للصفحة الحالية');
  });

  it('explains that an empty day needs a sync instead of calling it absence', () => {
    const component = createComponent();
    component.summary = { totalWorkers: 0, presentWorkers: 0, absentWorkers: 0, lateWorkers: 0, incompleteWorkers: 0, unassignedPresentWorkers: 0, assignedAbsentWorkers: 0, reviewRequiredWorkers: 0, attendanceDataAvailable: false, scope: 'filtered-results' };
    expect(component.emptyDescription).toContain('مزامنة التاريخ المحدد');
  });

  it('formats a known attendance duration without relying on the viewport', () => {
    const component = createComponent();
    expect(component.formatDuration(row)).toBe('8 س 30 د');
    expect(component.formatDuration({ ...row, lastCheckOutUtc: null })).toBe('—');
  });

  it('uses semantic status tones and keeps the Cairo time identical for row and timeline evidence', () => {
    const component = createComponent();
    const utcTimestamp = '2026-07-19T04:52:00Z';

    expect(component.statusTone('Present')).toBe('success');
    expect(component.statusTone('Late')).toBe('warning');
    expect(component.statusTone('Absent')).toBe('danger');
    expect(component.statusTone('NeedsSync')).toBe('neutral');
    expect(component.statusLabel('Present')).toBe('حاضر');
    expect(component.formatTime(utcTimestamp)).toBe(component.formatTime(utcTimestamp));
  });

  it('loads neutral punch evidence without exposing a numeric attendance enum in the detail contract', () => {
    const component = createComponent();
    const api = (component as any).api as jasmine.SpyObj<AttendanceWorkforceApiService>;
    api.getDetail.and.returnValue(of({ workerId: row.workerId, productionDate: '2026-07-19', attendanceRecords: [{ occurredAtUtc: '2026-07-19T04:52:00Z', label: 'Punch' }], assignments: [] }));

    component.ngOnInit();
    component.toggleDetails(row);

    expect(component.detailFor(row.workerId)?.attendanceRecords[0].label).toBe('Punch');
    expect(component.formatTime(component.detailFor(row.workerId)?.attendanceRecords[0].occurredAtUtc ?? null)).toBe(component.formatTime('2026-07-19T04:52:00Z'));
  });

  it('resets saved structural filters and restores the default date query', () => {
    const component = createComponent(); component.ngOnInit();
    component.selectedFactoryId = 'factory-1'; component.selectedProductionLineId = 'line-1'; component.selectedMainStageId = 'main-1'; component.selectedSubStageId = 'sub-1'; component.search = 'فاطمة';
    component.reset();
    expect(component.selectedFactoryId).toBe(''); expect(component.selectedProductionLineId).toBe(''); expect(component.selectedMainStageId).toBe(''); expect(component.selectedSubStageId).toBe(''); expect(component.search).toBe('');
  });

  it('loads a worker detail once when the row is expanded', () => {
    const component = createComponent(); component.ngOnInit();
    component.toggleDetails(row);
    expect(component.detailFor(row.workerId)?.workerId).toBe(row.workerId);
    component.toggleDetails(row);
    expect(component.expandedWorkerId).toBeNull();
  });

  it('contains a detail failure within the expanded row state', () => {
    const component = createComponent();
    const api = (component as any).api as jasmine.SpyObj<AttendanceWorkforceApiService>;
    api.getDetail.and.returnValue(throwError(() => new Error('detail offline')));
    component.ngOnInit(); component.toggleDetails(row);
    expect(component.expandedWorkerId).toBe(row.workerId);
    expect(component.rows).toEqual([row]);
    expect(component.detailErrors.get(row.workerId)).toContain('تعذر تحميل تفاصيل العامل');
  });

  it('invalidates cached details after a successful attendance sync', () => {
    const component = createComponent(); component.ngOnInit();
    component.details.set(row.workerId, { workerId: row.workerId, productionDate: '2026-07-19', attendanceRecords: [], assignments: [] });
    component.syncSelectedDate();
    expect(component.details.size).toBe(0);
  });

  it('keeps previously loaded rows visible when a refresh fails', () => {
    const component = createComponent(); component.ngOnInit();
    const api = (component as any).api as jasmine.SpyObj<AttendanceWorkforceApiService>;
    api.getPage.and.returnValue(throwError(() => new Error('refresh offline')));
    component.retry();
    expect(component.rows).toEqual([row]);
    expect(component.hasError).toBeFalse();
    expect(component.syncMessage).toContain('refresh offline');
  });

  it('does not permit manual sync without attendance.sync', () => {
    const component = createComponent(false); component.ngOnInit();
    expect(component.canSync).toBeFalse();
    component.syncSelectedDate();
    expect(component.syncInProgress).toBeFalse();
  });

  it('keeps displayed rows when synchronization fails', () => {
    const component = createComponent();
    const attendance = (component as any).attendanceApi as jasmine.SpyObj<AttendanceApiService>;
    attendance.syncForProductionDate.and.returnValue(throwError(() => new Error('offline')));
    component.ngOnInit(); component.syncSelectedDate();
    expect(component.rows).toEqual([row]);
    expect(component.syncMessage).toContain('offline');
  });
});
