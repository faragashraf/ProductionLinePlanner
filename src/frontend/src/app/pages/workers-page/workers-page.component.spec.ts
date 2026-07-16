import { fakeAsync, tick } from '@angular/core/testing';
import { Observable, of } from 'rxjs';
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
    const workersApi = jasmine.createSpyObj<WorkersApiService>('WorkersApiService', ['loadWorkers']);
    workersApi.loadWorkers.and.returnValue(loadWorkers$);

    return {
      component: new WorkersPageComponent(workersApi),
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
});
