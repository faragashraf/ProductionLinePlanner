import { of, throwError } from 'rxjs';
import { WorkerPageItem } from '../../shared/models/worker.model';
import { WorkersApiService } from '../../core/services/workers-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { WorkerManagementApiDataSource } from './worker-management-api-data-source';

describe('WorkerManagementApiDataSource', () => {
  const worker: WorkerPageItem = {
    id: '11111111-1111-1111-1111-111111111111', code: 'EMP-1', fullName: 'عامل محلي', state: 'على رأس العمل',
    employmentStatus: 'Active', isActive: true, attendanceUserId: '99', badgeNumber: 'B-1',
    hasPhoto: true, photoReference: '/api/workers/11111111-1111-1111-1111-111111111111/photo?v=' + 'a'.repeat(64), photoVersion: 'a'.repeat(64)
  };

  function createApi(): jasmine.SpyObj<WorkersApiService> {
    const api = jasmine.createSpyObj<WorkersApiService>('WorkersApiService', ['loadWorkers', 'getWorker', 'updateWorker', 'setEmploymentStatus', 'assignOrganizationalDepartment', 'uploadWorkerPhoto', 'deleteWorkerPhoto']);
    api.loadWorkers.and.returnValue(of({ workers: [worker], hasBackendData: true, hasUsableBackendData: true, totalCount: 1, page: 1, pageSize: 6, totalPages: 1, supportsServerPagination: true }));
    api.getWorker.and.returnValue(of(worker));
    api.updateWorker.and.returnValue(of({ ...worker, fullName: 'اسم جديد' }));
    api.setEmploymentStatus.and.returnValue(of({ ...worker, employmentStatus: 'Suspended', isActive: false }));
    api.uploadWorkerPhoto.and.returnValue(of(undefined));
    api.deleteWorkerPhoto.and.returnValue(of(undefined));
    return api;
  }

  it('maps the application database worker list with no mock fallback', () => {
    const api = createApi();
    const source = new WorkerManagementApiDataSource(api);
    source.loadPage({ page: 1, pageSize: 6, search: 'عامل', localEmploymentStatus: 'active' }).subscribe(page => {
      expect(page.items[0]).toEqual(jasmine.objectContaining({ localName: 'عامل محلي', badgeNumber: 'B-1', photoUrl: worker.photoReference, sourceLinkStatus: 'linked' }));
    });
    expect(api.loadWorkers).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'عامل', serviceStatus: 'active' }));
  });

  it('maps profile metadata without a direct ZKTime request', () => {
    const api = createApi();
    const source = new WorkerManagementApiDataSource(api);
    source.loadProfile(worker.id!).subscribe(profile => {
      expect(profile.local.photoUrl).toContain('?v=');
      expect(profile.source.sourceName).toBeNull();
      expect(profile.history).toEqual([]);
    });
    expect(api.getWorker).toHaveBeenCalledWith(worker.id!);
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
