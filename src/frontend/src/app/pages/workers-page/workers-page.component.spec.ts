import { fakeAsync, tick } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { WorkerManagementFacade } from './worker-management.facade';
import { WorkerManagementListItem, WorkerManagementPage, WorkerManagementProfile, WorkerManagementQuery } from './worker-management.models';
import { WorkersPageComponent } from './workers-page.component';

describe('WorkersPageComponent', () => {
  const listItem: WorkerManagementListItem = {
    id: '11111111-1111-1111-1111-111111111111', localName: 'عامل محلي', sourceName: null,
    photoUrl: null, badgeNumber: 'B-1', employeeCode: 'EMP-1', assignmentLabel: 'لا يوجد تسكين افتراضي نشط',
    factoryLineLabel: 'لا يوجد تسكين حالي', sourceLinkStatus: 'linked', localProfileStatus: 'complete',
    assignmentStatus: 'unassigned', localEmploymentStatus: 'active', factoryId: null, productionLineId: null,
    hasIdentityConflict: false, organizationalDepartmentId: null, organizationalDepartmentName: null,
    organizationalFactoryName: null, organizationalDepartmentConcurrencyToken: 'token-1'
  };
  const profile: WorkerManagementProfile = {
    id: listItem.id,
    local: { displayName: listItem.localName, photoUrl: null, phone: null, salary: null, profileStatus: 'complete', employmentStatus: 'active', employmentEndDate: null },
    source: { sourceName: null, attendanceUserId: '99', attendanceDepartmentId: null, badgeNumber: 'B-1', employeeCode: 'EMP-1', employmentStatus: 'Active', department: null, shift: null, lastObservedAt: null, linkStatus: 'linked' },
    assignments: [], assignmentStatus: 'unassigned', defaultSubStageId: null,
    attendance: null, organizationalDepartmentId: null, organizationalDepartmentName: null,
    organizationalFactoryName: null, organizationalDepartmentConcurrencyToken: 'token-1',
    system: { createdAtUtc: null, updatedAtUtc: null },
    dataStates: { assignments: 'empty', attendance: 'forbidden', salary: 'forbidden' }
  };
  const page: WorkerManagementPage = { items: [listItem], totalCount: 1, page: 1, pageSize: 6, totalPages: 1 };

  let facade: jasmine.SpyObj<WorkerManagementFacade>;
  let permissions: jasmine.SpyObj<PermissionService>;

  beforeEach(() => {
    localStorage.clear();
    facade = jasmine.createSpyObj<WorkerManagementFacade>('WorkerManagementFacade', [
      'loadWorkers', 'loadProfile', 'loadActiveDepartments', 'assignDepartment'
    ]);
    facade.loadWorkers.and.returnValue(of(page));
    facade.loadProfile.and.returnValue(of(profile));
    facade.loadActiveDepartments.and.returnValue(of([{
      id: 'department-1', name: 'قسم التشغيل', code: 'D-001', factoryId: 'factory-1',
      factoryName: 'المصنع الرئيسي', searchLabel: 'قسم التشغيل · D-001 · المصنع الرئيسي'
    }]));
    facade.assignDepartment.and.returnValue(of({
      workerId: listItem.id, departmentId: 'department-1', departmentName: 'قسم التشغيل',
      factoryId: 'factory-1', factoryName: 'المصنع الرئيسي', concurrencyToken: 'token-2'
    }));
    permissions = jasmine.createSpyObj<PermissionService>('PermissionService', ['hasPermission', 'hasAll']);
    permissions.hasPermission.and.callFake(permission => permission === 'workers.manage' || permission === 'assignments.view');
    permissions.hasAll.and.callFake(required =>
      (Array.isArray(required) ? required : [required]).every(permission => permissions.hasPermission(permission)));
  });

  function createComponent(): WorkersPageComponent { return new WorkersPageComponent(facade, permissions); }

  it('shows loading until the real-data facade completes', () => {
    const response = new Subject<WorkerManagementPage>();
    facade.loadWorkers.and.returnValue(response);
    const component = createComponent();
    component.ngOnInit();
    expect(component.isLoading).toBeTrue();
    response.next(page);
    response.complete();
    expect(component.workers).toEqual([listItem]);
  });

  it('represents explicit empty and API error states without a fixture fallback', () => {
    facade.loadWorkers.and.returnValue(of({ ...page, items: [], totalCount: 0 }));
    const empty = createComponent();
    empty.ngOnInit();
    expect(empty.isEmpty).toBeTrue();

    facade.loadWorkers.and.returnValue(throwError(() => new Error('offline')));
    const error = createComponent();
    error.ngOnInit();
    expect(error.hasError).toBeTrue();
    expect(error.errorMessage).toContain('offline');
  });

  it('sends debounced search and the supported active-status filter to the facade', fakeAsync(() => {
    const component = createComponent();
    component.ngOnInit();
    facade.loadWorkers.calls.reset();
    component.onSearch('EMP-1');
    tick(250);
    component.onEmploymentStatusChange('active');
    const query = facade.loadWorkers.calls.mostRecent().args[0] as WorkerManagementQuery;
    expect(query).toEqual(jasmine.objectContaining({ search: 'EMP-1', localEmploymentStatus: 'active', page: 1 }));
  }));

  it('opens a real profile request and preserves the list on close', () => {
    const component = createComponent();
    component.ngOnInit();
    component.openProfile(listItem);
    expect(facade.loadProfile).toHaveBeenCalledWith(listItem.id, { assignments: true, attendance: false, compensation: false });
    expect(component.selectedProfile).toEqual(profile);
    component.closeProfile();
    expect(component.workers).toEqual([listItem]);
  });

  it('searches by name or worker number inside an open profile and switches directly to the selected worker', fakeAsync(() => {
    const secondItem = { ...listItem, id: '22222222-2222-2222-2222-222222222222', localName: 'عامل ثانٍ', employeeCode: 'EMP-2' };
    const secondProfile = { ...structuredClone(profile), id: secondItem.id, local: { ...profile.local, displayName: secondItem.localName }, source: { ...profile.source, employeeCode: 'EMP-2' } };
    facade.loadWorkers.and.callFake(query => of(query.search ? { ...page, items: [listItem, secondItem], totalCount: 2 } : page));
    facade.loadProfile.and.callFake(workerId => of(workerId === secondItem.id ? secondProfile : profile));
    const component = createComponent();
    component.ngOnInit();
    component.openProfile(listItem);
    facade.loadWorkers.calls.reset();

    component.onProfileSearch('EMP-2');
    tick(250);

    expect(facade.loadWorkers).toHaveBeenCalledWith({ page: 1, pageSize: 6, search: 'EMP-2', localEmploymentStatus: '' });
    expect(component.profileSearchResults).toEqual([secondItem]);

    component.openProfile(secondItem);
    expect(facade.loadProfile).toHaveBeenCalledWith(secondItem.id, jasmine.any(Object));
    expect(component.selectedProfile).toEqual(secondProfile);
    expect(component.profileSearch).toBe('');
  }));

  it('cancels an older profile request before a newer worker can be replaced by its response', () => {
    const firstResponse = new Subject<WorkerManagementProfile>();
    const secondResponse = new Subject<WorkerManagementProfile>();
    const secondItem = { ...listItem, id: '22222222-2222-2222-2222-222222222222', localName: 'عامل ثانٍ' };
    const secondProfile = { ...structuredClone(profile), id: secondItem.id, local: { ...profile.local, displayName: secondItem.localName } };
    facade.loadProfile.and.callFake(workerId => workerId === listItem.id ? firstResponse : secondResponse);
    const component = createComponent();
    component.ngOnInit();

    component.openProfile(listItem);
    component.openProfile(secondItem);
    firstResponse.next(profile);
    secondResponse.next(secondProfile);
    secondResponse.complete();

    expect(component.selectedProfile).toEqual(secondProfile);
    expect(component.selectedProfileWorkerId).toBe(secondItem.id);
    expect(component.profileLoading).toBeFalse();
    component.ngOnDestroy();
  });

  it('refreshes the manufacturing employees page through the shared realtime service without losing its query or page', () => {
    let refresh: (() => void) | undefined;
    const stop = jasmine.createSpy('stop');
    const realtime = { watchScreen: jasmine.createSpy('watchScreen').and.callFake((watch: { refresh: () => void }) => { refresh = watch.refresh; return stop; }) };
    const component = new WorkersPageComponent(facade, permissions, realtime as never, { url: '/manufacturing/employees' } as never);
    component.ngOnInit();
    component.search = 'عامل';
    component.localEmploymentStatus = 'active';
    component.page = 2;
    facade.loadWorkers.calls.reset();
    refresh?.();

    expect(realtime.watchScreen).toHaveBeenCalledWith(jasmine.objectContaining({ screen: 'employees' }));
    expect(facade.loadWorkers).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'عامل', localEmploymentStatus: 'active', page: 2 }));
    component.ngOnDestroy();
    expect(stop).toHaveBeenCalled();
  });

  it('requires both existing management permissions and updates the row once after a valid department assignment', () => {
    const component = createComponent();
    component.ngOnInit();
    component.openDepartmentDialog(listItem);
    expect(component.departmentDialogVisible).toBeFalse();

    permissions.hasPermission.and.callFake(permission =>
      permission === 'workers.manage' || permission === 'departments.manage' || permission === 'assignments.view');
    const messageService = { add: jasmine.createSpy('add') };
    const permitted = new WorkersPageComponent(facade, permissions, undefined, undefined, messageService as never);
    permitted.ngOnInit();
    permitted.openDepartmentDialog(listItem);
    expect(facade.loadActiveDepartments).toHaveBeenCalled();
    expect(permitted.departmentSaveDisabled).toBeTrue();

    permitted.selectedDepartmentId = 'department-1';
    permitted.saveDepartmentAssignment();

    expect(facade.assignDepartment).toHaveBeenCalledOnceWith(listItem.id, 'department-1', 'token-1');
    expect(permitted.workers[0]).toEqual(jasmine.objectContaining({
      organizationalDepartmentId: 'department-1',
      organizationalDepartmentName: 'قسم التشغيل',
      organizationalDepartmentConcurrencyToken: 'token-2'
    }));
    expect(permitted.departmentDialogVisible).toBeFalse();
    expect(messageService.add).toHaveBeenCalledTimes(1);
  });

  it('opens the existing department form from the profile and updates both profile and row after save', () => {
    permissions.hasPermission.and.returnValue(true);
    const component = createComponent();
    component.ngOnInit();
    component.openProfile(listItem);

    component.openProfileDepartmentDialog();
    expect(component.departmentDialogVisible).toBeTrue();
    expect(component.selectedDepartmentWorker?.id).toBe(profile.id);
    component.selectedDepartmentId = 'department-1';
    component.saveDepartmentAssignment();

    expect(component.selectedProfile).toEqual(jasmine.objectContaining({
      organizationalDepartmentId: 'department-1',
      organizationalDepartmentName: 'قسم التشغيل',
      organizationalDepartmentConcurrencyToken: 'token-2'
    }));
    expect(component.workers[0].organizationalDepartmentName).toBe('قسم التشغيل');
    expect(component.profileViewOpen).toBeTrue();
  });

  it('keeps department assignment inside the authorized row action menu', () => {
    const overlay = { toggle: jasmine.createSpy('toggle'), hide: jasmine.createSpy('hide') };
    const component = createComponent();

    component.openWorkerActions(new Event('click'), listItem, overlay as never);
    component.openProfileFromWorkerActions(overlay as never);
    expect(component.profileViewOpen).toBeTrue();
    expect(overlay.hide).toHaveBeenCalled();

    permissions.hasPermission.and.returnValue(true);
    component.openWorkerActions(new Event('click'), listItem, overlay as never);
    component.openDepartmentFromWorkerActions(overlay as never);

    expect(component.departmentDialogVisible).toBeTrue();
    expect(overlay.toggle).toHaveBeenCalledTimes(2);
  });

  it('keeps an open dialog and blocks saving when another client changes the same worker', () => {
    permissions.hasPermission.and.returnValue(true);
    let refresh: ((change: {
      entityId: string; workerId: string; workerIds: string[]; workerChangeKinds: ['department-assignment'];
    }) => void) | undefined;
    const realtime = {
      watchScreen: jasmine.createSpy('watchScreen').and.callFake((watch: { refresh: typeof refresh }) => {
        refresh = watch.refresh;
        return () => undefined;
      })
    };
    const component = new WorkersPageComponent(facade, permissions, realtime as never, { url: '/manufacturing/employees' } as never);
    component.ngOnInit();
    component.openDepartmentDialog(listItem);
    component.selectedDepartmentId = 'department-1';

    refresh?.({
      entityId: listItem.id,
      workerId: listItem.id,
      workerIds: [listItem.id],
      workerChangeKinds: ['department-assignment']
    });

    expect(component.departmentDialogVisible).toBeTrue();
    expect(component.departmentConflict).toBeTrue();
    expect(component.departmentDialogError).toContain('مستخدم آخر');
    component.saveDepartmentAssignment();
    expect(facade.assignDepartment).not.toHaveBeenCalled();
  });

  it('turns an API concurrency response into a clear blocking conflict', () => {
    permissions.hasPermission.and.returnValue(true);
    facade.assignDepartment.and.returnValue(throwError(() => ({ status: 409 })));
    const component = createComponent();
    component.ngOnInit();
    component.openDepartmentDialog(listItem);
    component.selectedDepartmentId = 'department-1';

    component.saveDepartmentAssignment();

    expect(component.departmentConflict).toBeTrue();
    expect(component.departmentDialogVisible).toBeTrue();
    expect(component.departmentDialogError).toContain('أثناء التحرير');
  });
});
