import { of, throwError } from 'rxjs';
import { WorkerPageItem } from '../../shared/models/worker.model';
import { WorkersApiService } from '../../core/services/workers-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { AttendanceWorkforceApiService } from '../../core/services/attendance-workforce-api.service';
import { WorkerManagementApiDataSource } from './worker-management-api-data-source';

describe('WorkerManagementApiDataSource', () => {
  const worker: WorkerPageItem = {
    id: '11111111-1111-1111-1111-111111111111', code: 'EMP-1', fullName: 'عامل محلي', state: 'على رأس العمل',
    employmentStatus: 'Active', isActive: true, attendanceUserId: '99', badgeNumber: 'B-1',
    hasPhoto: true, photoReference: '/api/workers/11111111-1111-1111-1111-111111111111/photo?v=' + 'a'.repeat(64), photoVersion: 'a'.repeat(64),
    lastExternalSyncAt: '2026-07-29T07:00:00Z', createdAtUtc: '2026-01-01T08:00:00Z', updatedAtUtc: '2026-07-29T07:00:00Z',
    permanentAssignments: [{ id: 'assignment-1', factoryId: 'factory-1', factoryName: 'المصنع', productionLineId: 'line-1', productionLineName: 'الخط', departmentId: 'department-1', departmentName: 'التشغيل', mainStageId: 'main-1', mainStageName: 'التجهيز', subStageId: 'sub-1', subStageName: 'القص', assignedAtUtc: '2026-07-01T08:00:00Z' }]
  };

  function createApi(): jasmine.SpyObj<WorkersApiService> {
    const api = jasmine.createSpyObj<WorkersApiService>('WorkersApiService', ['loadWorkers', 'getWorker', 'updateWorker', 'setEmploymentStatus', 'assignOrganizationalDepartment', 'getCurrentSalary', 'uploadWorkerPhoto', 'deleteWorkerPhoto']);
    api.loadWorkers.and.returnValue(of({ workers: [worker], hasBackendData: true, hasUsableBackendData: true, totalCount: 1, page: 1, pageSize: 6, totalPages: 1, supportsServerPagination: true }));
    api.getWorker.and.returnValue(of(worker));
    api.updateWorker.and.returnValue(of({ ...worker, fullName: 'اسم جديد' }));
    api.setEmploymentStatus.and.returnValue(of({ ...worker, employmentStatus: 'Suspended', isActive: false }));
    api.getCurrentSalary.and.returnValue(of({ id: 'salary-1', workerId: worker.id!, amount: 7000, currencyCode: 'EGP', effectiveFrom: '2026-07-01T00:00:00Z', effectiveTo: null }));
    api.uploadWorkerPhoto.and.returnValue(of({ photo: { workerId: worker.id!, photoReference: `/api/workers/${worker.id}/photo?v=${'b'.repeat(64)}`, version: 'b'.repeat(64), contentType: 'image/png', length: 100 }, created: false, replaced: true, unchanged: false }));
    api.deleteWorkerPhoto.and.returnValue(of(undefined));
    return api;
  }

  it('maps the application database worker list with no mock fallback', () => {
    const api = createApi();
    const source = new WorkerManagementApiDataSource(api);
    source.loadPage({ page: 1, pageSize: 6, search: 'عامل', localEmploymentStatus: 'active' }).subscribe(page => {
      expect(page.items[0]).toEqual(jasmine.objectContaining({ localName: 'عامل محلي', badgeNumber: 'B-1', photoUrl: worker.photoReference, sourceLinkStatus: 'linked', assignmentLabel: 'التجهيز / القص', factoryLineLabel: 'المصنع / الخط' }));
    });
    expect(api.loadWorkers).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'عامل', serviceStatus: 'active' }));
  });

  it('maps profile metadata without a direct ZKTime request', () => {
    const api = createApi();
    const attendance = jasmine.createSpyObj<AttendanceWorkforceApiService>('AttendanceWorkforceApiService', ['getProfileSummary']);
    attendance.getProfileSummary.and.returnValue(of({ workerId: worker.id!, productionDate: '2026-07-29', todayStatus: 'Present', attendanceDataAvailableForDate: true, firstCheckInUtc: '2026-07-29T05:00:00Z', lastCheckOutUtc: '2026-07-29T14:00:00Z', lastKnownMovementUtc: '2026-07-29T14:00:00Z' }));
    const source = new WorkerManagementApiDataSource(api, undefined, undefined, attendance);
    source.loadProfile(worker.id!, { assignments: true, attendance: true, compensation: true }).subscribe(profile => {
      expect(profile.local.photoUrl).toContain('?v=');
      expect(profile.local.salary?.amount).toBe(7000);
      expect(profile.assignments[0]).toEqual(jasmine.objectContaining({ factoryName: 'المصنع', productionLineName: 'الخط', stageNames: ['التجهيز', 'القص'] }));
      expect(profile.attendance?.todayStatus).toBe('Present');
      expect(profile.source.lastObservedAt).toBe('2026-07-29T07:00:00Z');
      expect(profile.dataStates).toEqual({ assignments: 'loaded', attendance: 'loaded', salary: 'loaded' });
    });
    expect(api.getWorker).toHaveBeenCalledWith(worker.id!);
  });

  it('does not request protected profile data without its permissions', () => {
    const api = createApi();
    const attendance = jasmine.createSpyObj<AttendanceWorkforceApiService>('AttendanceWorkforceApiService', ['getProfileSummary']);
    const source = new WorkerManagementApiDataSource(api, undefined, undefined, attendance);

    source.loadProfile(worker.id!, { assignments: false, attendance: false, compensation: false }).subscribe(profile => {
      expect(profile.assignments).toEqual([]);
      expect(profile.local.salary).toBeNull();
      expect(profile.attendance).toBeNull();
      expect(profile.dataStates).toEqual({ assignments: 'forbidden', attendance: 'forbidden', salary: 'forbidden' });
    });

    expect(api.getCurrentSalary).not.toHaveBeenCalled();
    expect(attendance.getProfileSummary).not.toHaveBeenCalled();
  });

  it('loads attendance history with server-side range and pagination and preserves explicit movement types', () => {
    const api = createApi();
    const attendance = jasmine.createSpyObj<AttendanceWorkforceApiService>('AttendanceWorkforceApiService', ['getWorkerHistory']);
    attendance.getWorkerHistory.and.returnValue(of({
      workerId: worker.id!, fromDate: '2026-07-01', toDate: '2026-07-29', page: 2, pageSize: 10, totalCount: 11, totalPages: 2,
      items: [{ recordId: 'record-1', productionDate: '2026-07-20', attendanceStatus: 'Present', source: 'AttendanceSync', movements: [{ occurredAtUtc: '2026-07-20T05:00:00Z', movementType: 'In' }] }]
    }));
    const source = new WorkerManagementApiDataSource(api, undefined, undefined, attendance);

    source.loadAttendanceHistory(worker.id!, { fromDate: '2026-07-01', toDate: '2026-07-29', page: 2, pageSize: 10 }).subscribe(page => {
      expect(page.page).toBe(2);
      expect(page.items[0].movements[0].movementType).toBe('In');
    });

    expect(attendance.getWorkerHistory).toHaveBeenCalledWith(worker.id!, jasmine.objectContaining({ page: 2, sortDirection: 'desc' }));
  });

  it('keeps the current photo until a photo mutation succeeds and preserves supplemental profile data', () => {
    const api = createApi();
    const source = new WorkerManagementApiDataSource(api);
    let profile: import('./worker-management.models').WorkerManagementProfile | undefined;
    source.loadProfile(worker.id!, { assignments: true, attendance: false, compensation: true }).subscribe(value => profile = value);

    source.uploadPhoto(profile!, new File([new Uint8Array([0x89, 0x50])], 'worker.png', { type: 'image/png' })).subscribe(updated => {
      expect(updated.local.photoUrl).toContain('b'.repeat(64));
      expect(updated.local.salary?.amount).toBe(7000);
      expect(updated.assignments.length).toBe(1);
    });
    expect(api.uploadWorkerPhoto).toHaveBeenCalledWith(worker.id!, jasmine.any(File), undefined);
  });

  it('keeps the current photo when saving profile fields without selecting a replacement', () => {
    const api = createApi();
    api.updateWorker.and.returnValue(of({
      ...worker,
      fullName: 'اسم محلي محدث',
      hasPhoto: false,
      photoReference: undefined,
      photoVersion: undefined
    }));
    const source = new WorkerManagementApiDataSource(api);
    let profile: import('./worker-management.models').WorkerManagementProfile | undefined;
    source.loadProfile(worker.id!, { assignments: true, attendance: false, compensation: true }).subscribe(value => profile = value);

    source.saveLocalProfile(profile!, {
      displayName: 'اسم محلي محدث',
      employmentStatus: profile!.local.employmentStatus
    }).subscribe(updated => expect(updated.local.photoUrl).toBe(worker.photoReference!));

    expect(api.uploadWorkerPhoto).not.toHaveBeenCalled();
    expect(api.deleteWorkerPhoto).not.toHaveBeenCalled();
  });

  it('preserves an API failure as an error instead of replacing it with mock data', () => {
    const api = createApi();
    api.loadWorkers.and.returnValue(throwError(() => new Error('network offline')));
    const source = new WorkerManagementApiDataSource(api);
    let failure = '';
    source.loadPage({ page: 1, pageSize: 6, search: '', localEmploymentStatus: '' }).subscribe({ error: error => failure = error.message });
    expect(failure).toBe('network offline');
  });

  it('offers only active departments that belong to active factories', () => {
    const api = createApi();
    const masterData = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', ['departments', 'factories']);
    masterData.factories.and.returnValue(of([
      { id: 'factory-active', name: 'مصنع نشط', code: 'F-1', isActive: true },
      { id: 'factory-inactive', name: 'مصنع متوقف', code: 'F-2', isActive: false }
    ]));
    masterData.departments.and.returnValue(of([
      { id: 'department-active', factoryId: 'factory-active', code: 'D-1', nameAr: 'قسم نشط', isActive: true },
      { id: 'department-inactive', factoryId: 'factory-active', code: 'D-2', nameAr: 'قسم متوقف', isActive: false },
      { id: 'department-inactive-factory', factoryId: 'factory-inactive', code: 'D-3', nameAr: 'قسم بمصنع متوقف', isActive: true }
    ]));
    const source = new WorkerManagementApiDataSource(api, undefined, masterData);

    source.loadActiveDepartments().subscribe(departments => {
      expect(departments.map(department => department.id)).toEqual(['department-active']);
      expect(departments[0].searchLabel).toContain('مصنع نشط');
    });
    expect(masterData.departments).toHaveBeenCalledWith(undefined, false);
  });
});
