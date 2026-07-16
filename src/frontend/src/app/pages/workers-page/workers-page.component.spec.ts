import { fakeAsync, tick } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { Observable, of, throwError } from 'rxjs';
import { WorkersPageComponent } from './workers-page.component';
import { WorkersApiData } from '../../core/services/workers-api.service';
import { WorkersApiService } from '../../core/services/workers-api.service';

describe('WorkersPageComponent', () => {
  const samplePayload: WorkersApiData = {
    workers: [
      {
        id: 'w-1',
        code: 'E-1',
        fullName: 'عامل تجريبي',
        state: 'على رأس العمل',
        employmentStatus: 'Active',
        isActive: true
      }
    ],
    hasBackendData: true,
    hasUsableBackendData: true,
    totalCount: 1,
    page: 1,
    pageSize: 10,
    totalPages: 1,
    supportsServerPagination: false
  };

  function createComponent(
    loadWorkers$: Observable<WorkersApiData> = of(samplePayload)
  ) {
    const workersApi = jasmine.createSpyObj<WorkersApiService>('WorkersApiService', ['loadWorkers', 'updateWorker']);
    workersApi.loadWorkers.and.returnValue(loadWorkers$);
    workersApi.updateWorker.and.returnValue(of({ ...samplePayload.workers[0], fullName: 'عامل محدّث', phone: '01012345678' }));

    return {
      component: new WorkersPageComponent(workersApi, new FormBuilder()),
      workersApi
    };
  }

  function buildInput(value: string): Event {
    return {
      target: {
        value
      }
    } as unknown as Event;
  }

  it('loads workers exactly once when opening the page', () => {
    const { component, workersApi } = createComponent();

    component.ngOnInit();

    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(1);
  });

  it('ignores duplicated lazy events for the same state', () => {
    const { component, workersApi } = createComponent();

    component.ngOnInit();
    component.hasLoadedOnce = true;
    component.isServerSidePagination = true;
    component.first = 0;
    component.rows = 10;

    component.onLazyLoad({ first: 0, rows: 10 } as any);
    component.onLazyLoad({ first: 0, rows: 10 } as any);

    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(1);
  });

  it('sends one request when page changes', () => {
    const { component, workersApi } = createComponent();

    component.ngOnInit();
    component.hasLoadedOnce = true;
    component.isServerSidePagination = true;
    component.first = 0;
    component.rows = 10;

    component.onLazyLoad({ first: 10, rows: 10 } as any);

    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(2);
  });

  it('debounces search into a single request', fakeAsync(() => {
    const { component, workersApi } = createComponent();

    component.ngOnInit();
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(1);

    component.onSearch(buildInput('a'));
    tick(100);
    component.onSearch(buildInput('al'));
    tick(100);
    component.onSearch(buildInput('ali'));

    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(1);

    tick(300);
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(2);
  }));

  it('forces a new request on manual refresh', () => {
    const { component, workersApi } = createComponent();

    component.ngOnInit();
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(1);

    component.onRefresh();
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(2);

    component.onRefresh();
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(3);
  });

  it('reloads all directory workers by the selected service-status filter', () => {
    const { component, workersApi } = createComponent();

    component.ngOnInit();
    component.onServiceStatusChange('inactive');

    expect(workersApi.loadWorkers).toHaveBeenCalledWith(jasmine.objectContaining({ serviceStatus: 'inactive' }));
  });

  it('switching Active or Former back to All clears the status constraint, resets pagination, and preserves search', () => {
    const { component, workersApi } = createComponent();
    component.ngOnInit();
    component.searchTerm = 'علي';
    component.first = 30;

    component.onServiceStatusChange('active');
    component.first = 20;
    component.onServiceStatusChange('all');

    expect(component.first).toBe(0);
    expect(workersApi.loadWorkers).toHaveBeenCalledWith(jasmine.objectContaining({
      page: 1,
      search: 'علي',
      serviceStatus: 'all'
    }));

    component.onServiceStatusChange('inactive');
    component.first = 10;
    component.onServiceStatusChange('all');

    expect(component.first).toBe(0);
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(5);
  });

  it('opens edit in the shared sheet state and reconciles only the changed worker without reloading filters or paging', () => {
    const { component, workersApi } = createComponent();
    component.ngOnInit();
    component.searchTerm = 'عامل';
    component.serviceStatus = 'active';
    component.first = 20;
    component.workers = [...samplePayload.workers];

    component.openWorkerEdit(samplePayload.workers[0]);
    component.workerForm.setValue({ fullName: 'عامل محدّث', phone: '01012345678' });
    component.saveWorker();

    expect(component.workerSheetVisible).toBeFalse();
    expect(component.workerSheetMode).toBe('edit');
    expect(workersApi.updateWorker).toHaveBeenCalledWith('w-1', { fullName: 'عامل محدّث', phone: '01012345678' });
    expect(workersApi.loadWorkers).toHaveBeenCalledTimes(1);
    expect(component.workers[0]).toEqual(jasmine.objectContaining({ fullName: 'عامل محدّث', phone: '01012345678' }));
    expect(component.searchTerm).toBe('عامل');
    expect(component.serviceStatus).toBe('active');
    expect(component.first).toBe(20);
  });

  it('keeps the edit sheet and entered values open after a failed targeted save', () => {
    const { component, workersApi } = createComponent();
    workersApi.updateWorker.and.returnValue(throwError(() => new Error('network')));
    component.workers = [...samplePayload.workers];
    component.openWorkerEdit(samplePayload.workers[0]);
    component.workerForm.setValue({ fullName: 'عامل محفوظ', phone: '01000000000' });

    component.saveWorker();

    expect(component.workerSheetVisible).toBeTrue();
    expect(component.workerForm.getRawValue()).toEqual({ fullName: 'عامل محفوظ', phone: '01000000000' });
    expect(component.workerSaveError).toContain('network');
  });

  it('opens both details and edit through the shared sheet state without changing the loaded row set', () => {
    const { component } = createComponent();
    component.workers = [...samplePayload.workers];

    component.openWorkerDetails(samplePayload.workers[0]);
    expect(component.workerSheetVisible).toBeTrue();
    expect(component.workerSheetMode).toBe('details');

    component.closeWorkerSheet();
    component.openWorkerEdit(samplePayload.workers[0]);
    expect(component.workerSheetVisible).toBeTrue();
    expect(component.workerSheetMode).toBe('edit');
    expect(component.workerForm.getRawValue()).toEqual({ fullName: 'عامل تجريبي', phone: '' });
    expect(component.workers).toEqual(samplePayload.workers);
  });
});
