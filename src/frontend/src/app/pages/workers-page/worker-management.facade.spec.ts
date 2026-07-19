import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WORKER_MANAGEMENT_DATA_SOURCE, WorkerManagementDataSource } from './worker-management.data-source';
import { WorkerManagementFacade } from './worker-management.facade';
import { WorkerManagementPage, WorkerManagementProfile, WorkerManagementQuery } from './worker-management.models';

describe('WorkerManagementFacade', () => {
  it('delegates to the replaceable real-data contract without owning HTTP or mock data', () => {
    const query: WorkerManagementQuery = { page: 1, pageSize: 6, search: '', localEmploymentStatus: '' };
    const page: WorkerManagementPage = { items: [], totalCount: 0, page: 1, pageSize: 6, totalPages: 1 };
    const profile = {} as WorkerManagementProfile;
    const source = jasmine.createSpyObj<WorkerManagementDataSource>('WorkerManagementDataSource', ['loadPage', 'loadProfile', 'saveLocalProfile', 'uploadPhoto', 'deletePhoto']);
    source.loadPage.and.returnValue(of(page));
    source.loadProfile.and.returnValue(of(profile));
    TestBed.configureTestingModule({ providers: [WorkerManagementFacade, { provide: WORKER_MANAGEMENT_DATA_SOURCE, useValue: source }] });
    const facade = TestBed.inject(WorkerManagementFacade);

    facade.loadWorkers(query).subscribe(result => expect(result).toBe(page));
    facade.loadProfile('worker-1').subscribe(result => expect(result).toBe(profile));

    expect(source.loadPage).toHaveBeenCalledWith(query);
    expect(source.loadProfile).toHaveBeenCalledWith('worker-1');
    expect((facade as unknown as { http?: unknown }).http).toBeUndefined();
  });
});
