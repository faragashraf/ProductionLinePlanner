import { fakeAsync, tick } from '@angular/core/testing';
import { of } from 'rxjs';
import { WorkersPageComponent } from './workers-page.component';
import { WorkersApiData } from '../../core/services/workers-api.service';

describe('WorkersPageComponent', () => {
  const samplePayload: WorkersApiData = {
    workers: [
      {
        id: 'w-1',
        code: 'E-1',
        fullName: 'عامل تجريبي',
        state: 'جاهز'
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

  function createComponent(loadWorkers = of(samplePayload)) {
    const service = {
      loadWorkers
    };

    return {
      component: new WorkersPageComponent(service as any),
      service: service as { loadWorkers: jasmine.Spy }
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
    const loadWorkers = jasmine.createSpy('loadWorkers').and.returnValue(of(samplePayload));
    const { component } = createComponent(loadWorkers);

    component.ngOnInit();

    expect(loadWorkers).toHaveBeenCalledTimes(1);
  });

  it('ignores duplicated lazy events for the same state', () => {
    const loadWorkers = jasmine.createSpy('loadWorkers').and.returnValue(of(samplePayload));
    const { component } = createComponent(loadWorkers);

    component.ngOnInit();
    component.hasLoadedOnce = true;
    component.isServerSidePagination = true;
    component.first = 0;
    component.rows = 10;

    component.onLazyLoad({ first: 0, rows: 10 } as any);
    component.onLazyLoad({ first: 0, rows: 10 } as any);

    expect(loadWorkers).toHaveBeenCalledTimes(1);
  });

  it('sends one request when page changes', () => {
    const loadWorkers = jasmine.createSpy('loadWorkers').and.returnValue(of(samplePayload));
    const { component } = createComponent(loadWorkers);

    component.ngOnInit();
    component.hasLoadedOnce = true;
    component.isServerSidePagination = true;
    component.first = 0;
    component.rows = 10;

    component.onLazyLoad({ first: 10, rows: 10 } as any);

    expect(loadWorkers).toHaveBeenCalledTimes(2);
  });

  it('debounces search into a single request', fakeAsync(() => {
    const loadWorkers = jasmine.createSpy('loadWorkers').and.returnValue(of(samplePayload));
    const { component } = createComponent(loadWorkers);

    component.ngOnInit();
    expect(loadWorkers).toHaveBeenCalledTimes(1);

    component.onSearch(buildInput('a'));
    tick(100);
    component.onSearch(buildInput('al'));
    tick(100);
    component.onSearch(buildInput('ali'));

    expect(loadWorkers).toHaveBeenCalledTimes(1);

    tick(300);
    expect(loadWorkers).toHaveBeenCalledTimes(2);
  }));

  it('forces a new request on manual refresh', () => {
    const loadWorkers = jasmine.createSpy('loadWorkers').and.returnValue(of(samplePayload));
    const { component } = createComponent(loadWorkers);

    component.ngOnInit();
    expect(loadWorkers).toHaveBeenCalledTimes(1);

    component.onRefresh();
    expect(loadWorkers).toHaveBeenCalledTimes(2);

    component.onRefresh();
    expect(loadWorkers).toHaveBeenCalledTimes(3);
  });
});
