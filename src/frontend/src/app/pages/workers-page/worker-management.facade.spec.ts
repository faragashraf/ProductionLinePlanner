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
    const access = { assignments: true, attendance: false, compensation: false };
    const source = jasmine.createSpyObj<WorkerManagementDataSource>('WorkerManagementDataSource', ['loadPage', 'loadProfile', 'saveLocalProfile', 'uploadPhoto', 'deletePhoto', 'loadAttendanceHistory']);
    source.loadPage.and.returnValue(of(page));
    source.loadProfile.and.returnValue(of(profile));
    source.loadAttendanceHistory.and.returnValue(of({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 1 }));
    TestBed.configureTestingModule({ providers: [WorkerManagementFacade, { provide: WORKER_MANAGEMENT_DATA_SOURCE, useValue: source }] });
    const facade = TestBed.inject(WorkerManagementFacade);

    facade.loadWorkers(query).subscribe(result => expect(result).toBe(page));
    facade.loadProfile('worker-1', access).subscribe(result => expect(result).toBe(profile));
    facade.loadAttendanceHistory('worker-1', { fromDate: '2026-07-01', toDate: '2026-07-29', page: 1, pageSize: 10 }).subscribe(result => expect(result.items).toEqual([]));

    expect(source.loadPage).toHaveBeenCalledWith(query);
    expect(source.loadProfile).toHaveBeenCalledWith('worker-1', access);
    expect(source.loadAttendanceHistory).toHaveBeenCalled();
    expect((facade as unknown as { http?: unknown }).http).toBeUndefined();
  });
});
