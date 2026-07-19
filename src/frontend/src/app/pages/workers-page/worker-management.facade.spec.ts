import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WORKER_MANAGEMENT_DATA_SOURCE, WorkerManagementDataSource } from './worker-management.data-source';
import { WorkerManagementFacade } from './worker-management.facade';
import { WORKER_MANAGEMENT_FIXTURES } from './worker-management.fixtures';
import { WorkerManagementPage, WorkerManagementQuery } from './worker-management.models';

describe('WorkerManagementFacade', () => {
  it('depends on the replaceable data-source contract and makes no HTTP call', () => {
    const query: WorkerManagementQuery = { page: 1, pageSize: 6, search: '', localProfileStatus: '', sourceLinkStatus: '', factoryId: '', productionLineId: '', assignmentStatus: '', localEmploymentStatus: '' };
    const page: WorkerManagementPage = { items: [], totalCount: 0, page: 1, pageSize: 6, totalPages: 1, filterOptions: { factories: [], productionLines: [] } };
    const dataSource = jasmine.createSpyObj<WorkerManagementDataSource>('WorkerManagementDataSource', ['loadPage', 'loadProfile']);
    dataSource.loadPage.and.returnValue(of(page));
    dataSource.loadProfile.and.returnValue(of(WORKER_MANAGEMENT_FIXTURES[0]));
    TestBed.configureTestingModule({ providers: [WorkerManagementFacade, { provide: WORKER_MANAGEMENT_DATA_SOURCE, useValue: dataSource }] });
    const facade = TestBed.inject(WorkerManagementFacade);

    facade.loadWorkers(query).subscribe(result => expect(result).toBe(page));
    facade.loadProfile(WORKER_MANAGEMENT_FIXTURES[0].id).subscribe(result => expect(result.id).toBe(WORKER_MANAGEMENT_FIXTURES[0].id));

    expect(dataSource.loadPage).toHaveBeenCalledWith(query);
    expect(dataSource.loadProfile).toHaveBeenCalledTimes(1);
    expect((facade as unknown as { http?: unknown }).http).toBeUndefined();
  });
});
