import { fakeAsync, tick } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { WorkerManagementFacade } from './worker-management.facade';
import { WORKER_MANAGEMENT_FIXTURES } from './worker-management.fixtures';
import { WorkerManagementListItem, WorkerManagementPage, WorkerManagementQuery } from './worker-management.models';
import { WorkersPageComponent } from './workers-page.component';

describe('WorkersPageComponent', () => {
  const listItem: WorkerManagementListItem = {
    id: 'worker-1',
    localName: 'عامل محلي',
    sourceName: 'Source Worker',
    photoUrl: null,
    badgeNumber: 'B-1',
    employeeCode: 'EMP-1',
    assignmentLabel: 'تسكين أساسي حالي',
    factoryLineLabel: 'مصنع / خط',
    sourceLinkStatus: 'linked',
    localProfileStatus: 'complete',
    assignmentStatus: 'assigned',
    localEmploymentStatus: 'active',
    factoryId: 'factory-1',
    productionLineId: 'line-1',
    hasIdentityConflict: false
  };
  const page: WorkerManagementPage = {
    items: [listItem], totalCount: 1, page: 1, pageSize: 6, totalPages: 1,
    filterOptions: { factories: [{ value: 'factory-1', label: 'مصنع' }], productionLines: [{ value: 'line-1', label: 'خط' }] }
  };

  let facade: jasmine.SpyObj<WorkerManagementFacade>;
  let permissions: jasmine.SpyObj<PermissionService>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    facade = jasmine.createSpyObj<WorkerManagementFacade>('WorkerManagementFacade', ['loadWorkers', 'loadProfile']);
    facade.loadWorkers.and.returnValue(of(page));
    facade.loadProfile.and.returnValue(of(WORKER_MANAGEMENT_FIXTURES[0]));
    permissions = jasmine.createSpyObj<PermissionService>('PermissionService', ['hasPermission']);
    permissions.hasPermission.and.callFake(permission => permission === 'workers.manage' || permission === 'assignments.view');
  });

  function createComponent(): WorkersPageComponent {
    return new WorkersPageComponent(facade, permissions);
  }

  it('shows the initial loading state until the facade completes', () => {
    const response = new Subject<WorkerManagementPage>();
    facade.loadWorkers.and.returnValue(response);
    const component = createComponent();

    component.ngOnInit();
    expect(component.isLoading).toBeTrue();
    expect(component.hasLoaded).toBeFalse();

    response.next(page);
    response.complete();
    expect(component.isLoading).toBeFalse();
    expect(component.workers).toEqual([listItem]);
  });

  it('represents empty and API-like error results explicitly', () => {
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

  it('debounces search across local/source names and source identifiers through the facade query', fakeAsync(() => {
    const component = createComponent();
    component.ngOnInit();
    facade.loadWorkers.calls.reset();

    component.onSearch('B-4108');
    tick(249);
    expect(facade.loadWorkers).not.toHaveBeenCalled();
    tick(1);

    const query = facade.loadWorkers.calls.mostRecent().args[0] as WorkerManagementQuery;
    expect(query.search).toBe('B-4108');
    expect(query.page).toBe(1);
  }));

  it('applies filters and resets all persisted filter values', () => {
    const component = createComponent();
    component.ngOnInit();
    component.onSourceLinkStatusChange('conflict');
    component.onAssignmentStatusChange('unassigned');
    component.onFactoryChange('factory-1');
    expect(component.activeFilterCount).toBe(3);

    component.resetFilters();
    expect(component.activeFilterCount).toBe(0);
    expect(component.sourceLinkStatus).toBe('');
    expect(component.assignmentStatus).toBe('');
    expect(component.factoryId).toBe('');
  });

  it('opens a profile through the facade and closes back to the preserved list', () => {
    const component = createComponent();
    component.ngOnInit();

    component.openProfile(listItem);
    expect(facade.loadProfile).toHaveBeenCalledWith(listItem.id);
    expect(component.profileViewOpen).toBeTrue();
    expect(component.selectedProfile?.id).toBe(WORKER_MANAGEMENT_FIXTURES[0].id);

    component.closeProfile();
    expect(component.profileViewOpen).toBeFalse();
    expect(component.workers).toEqual([listItem]);
  });

  it('uses workers.manage for drafts and assignments.view for the operational link', () => {
    const component = createComponent();
    expect(component.canManage).toBeTrue();
    expect(component.canViewAssignments).toBeTrue();
    permissions.hasPermission.and.returnValue(false);
    expect(component.canManage).toBeFalse();
    expect(component.canViewAssignments).toBeFalse();
  });

  it('tears down list subscriptions and ignores emissions after destroy', () => {
    const response = new Subject<WorkerManagementPage>();
    facade.loadWorkers.and.returnValue(response);
    const component = createComponent();
    component.ngOnInit();
    component.ngOnDestroy();

    response.next(page);
    response.complete();
    expect(component.workers).toEqual([]);
  });
});
