import { fakeAsync, tick } from '@angular/core/testing';
import { WorkerManagementMockDataSource, WORKER_MANAGEMENT_MOCK_SCENARIO_STORAGE_KEY } from './worker-management.mock-data-source';
import { WorkerManagementPage, WorkerManagementQuery } from './worker-management.models';

describe('WorkerManagementMockDataSource', () => {
  const defaultQuery: WorkerManagementQuery = {
    page: 1, pageSize: 6, search: '', localProfileStatus: '', sourceLinkStatus: '', factoryId: '', productionLineId: '', assignmentStatus: '', localEmploymentStatus: ''
  };

  afterEach(() => sessionStorage.clear());

  it('paginates typed fixtures instead of placing every worker in the DOM', fakeAsync(() => {
    const source = new WorkerManagementMockDataSource();
    let result: WorkerManagementPage | undefined;
    source.loadPage(defaultQuery).subscribe(value => result = value);
    tick(120);
    expect(result?.items.length).toBe(6);
    expect(result?.totalCount).toBeGreaterThan(result?.items.length ?? 0);
    expect(result?.filterOptions.factories.length).toBeGreaterThan(0);
  }));

  it('searches local name, source name, BadgeNumber, and EmployeeCode', fakeAsync(() => {
    const source = new WorkerManagementMockDataSource();
    const terms = ['هدى', 'Hoda E. Saleh', 'B-4108', 'EMP-9991'];
    terms.forEach(term => {
      let result: WorkerManagementPage | undefined;
      source.loadPage({ ...defaultQuery, search: term }).subscribe(value => result = value);
      tick(120);
      expect(result?.items.map(item => item.id)).toEqual(['worker-identity-conflict']);
    });
  }));

  it('filters mixed assignments, conflicts, factories, and local state centrally', fakeAsync(() => {
    const source = new WorkerManagementMockDataSource();
    let result: WorkerManagementPage | undefined;
    source.loadPage({ ...defaultQuery, assignmentStatus: 'mixed' }).subscribe(value => result = value);
    tick(120);
    expect(result?.items.map(item => item.id)).toEqual(['worker-mixed-assignment']);

    source.loadPage({ ...defaultQuery, sourceLinkStatus: 'conflict' }).subscribe(value => result = value);
    tick(120);
    expect(result?.items[0].hasIdentityConflict).toBeTrue();

    source.loadPage({ ...defaultQuery, localEmploymentStatus: 'not-set' }).subscribe(value => result = value);
    tick(120);
    expect(result?.items.map(item => item.id)).toEqual(['worker-new-from-source']);
  }));

  it('returns cloned profiles so draft edits cannot mutate fixture originals', fakeAsync(() => {
    const source = new WorkerManagementMockDataSource();
    let firstName = '';
    source.loadProfile('worker-local-ar-source-en').subscribe(profile => {
      firstName = profile.local.displayName;
      profile.local.displayName = 'مسودة متغيرة';
    });
    tick(80);
    source.loadProfile('worker-local-ar-source-en').subscribe(profile => expect(profile.local.displayName).toBe(firstName));
    tick(80);
  }));

  it('provides explicit empty and API-like error mock states', fakeAsync(() => {
    const source = new WorkerManagementMockDataSource();
    sessionStorage.setItem(WORKER_MANAGEMENT_MOCK_SCENARIO_STORAGE_KEY, 'empty');
    let emptyCount = -1;
    source.loadPage(defaultQuery).subscribe(result => emptyCount = result.totalCount);
    tick(120);
    expect(emptyCount).toBe(0);

    sessionStorage.setItem(WORKER_MANAGEMENT_MOCK_SCENARIO_STORAGE_KEY, 'error');
    let message = '';
    source.loadPage(defaultQuery).subscribe({ error: error => message = error.message });
    tick(120);
    expect(message).toContain('تعذر تحميل');
  }));
});
