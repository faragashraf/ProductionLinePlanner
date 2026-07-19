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
    hasIdentityConflict: false
  };
  const profile: WorkerManagementProfile = {
    id: listItem.id,
    local: { displayName: listItem.localName, photoUrl: null, salary: null, profileStatus: 'complete', employmentStatus: 'active' },
    source: { sourceName: null, badgeNumber: 'B-1', employeeCode: 'EMP-1', employmentStatus: null, department: null, shift: null, lastObservedAt: null, linkStatus: 'linked' },
    assignments: [], history: [], sourcePreview: [], assignmentStatus: 'unassigned', defaultSubStageId: null
  };
  const page: WorkerManagementPage = { items: [listItem], totalCount: 1, page: 1, pageSize: 6, totalPages: 1 };

  let facade: jasmine.SpyObj<WorkerManagementFacade>;
  let permissions: jasmine.SpyObj<PermissionService>;

  beforeEach(() => {
    localStorage.clear();
    facade = jasmine.createSpyObj<WorkerManagementFacade>('WorkerManagementFacade', ['loadWorkers', 'loadProfile']);
    facade.loadWorkers.and.returnValue(of(page));
    facade.loadProfile.and.returnValue(of(profile));
    permissions = jasmine.createSpyObj<PermissionService>('PermissionService', ['hasPermission']);
    permissions.hasPermission.and.callFake(permission => permission === 'workers.manage' || permission === 'assignments.view');
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
    expect(facade.loadProfile).toHaveBeenCalledWith(listItem.id);
    expect(component.selectedProfile).toEqual(profile);
    component.closeProfile();
    expect(component.workers).toEqual([listItem]);
  });
});
