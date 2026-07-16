import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IamAdminService } from './iam-admin.service';

describe('IamAdminService', () => {
  let service: IamAdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(IamAdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('maps the users list response', () => {
    let users: any[] = [];
    service.getUsers().subscribe((result) => users = result);
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/users'));
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, data: [{ id: '1', fullName: 'A', email: 'a@test', isActive: true, roles: ['Admin'] }] });
    expect(users[0].email).toBe('a@test');
  });

  it('uses the backend request shape for a user status update', () => {
    service.updateUserStatus('u-1', false).subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/users/u-1/status'));
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ isActive: false });
    request.flush({ success: true, data: { userId: 'u-1', isActive: false } });
  });

  it('loads details and sends create and edit contracts without a password on edit', () => {
    const details = { id: 'u-1', fullName: 'Admin', email: 'admin', isActive: true, roleIds: ['r-1'], roles: ['Admin'], preferredLanguage: 'ar', createdAtUtc: '2026-07-17T00:00:00Z', updatedAtUtc: '2026-07-17T00:00:00Z' };

    service.getUser('u-1').subscribe();
    const detailsRequest = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/users/u-1'));
    expect(detailsRequest.request.method).toBe('GET');
    detailsRequest.flush({ success: true, data: details });

    service.createUser({ fullName: 'Admin', email: 'admin', password: 'secret', roleIds: ['r-1'], isActive: true }).subscribe();
    const create = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/users'));
    expect(create.request.method).toBe('POST');
    expect(create.request.body.email).toBe('admin');
    create.flush({ success: true, data: details });

    service.updateUser('u-1', { fullName: 'Admin Updated', email: 'admin', roleIds: ['r-1'], isActive: true }).subscribe();
    const update = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/users/u-1'));
    expect(update.request.method).toBe('PUT');
    expect(update.request.body.password).toBeUndefined();
    update.flush({ success: true, data: { ...details, fullName: 'Admin Updated' } });
  });

  it('loads role options through the users.manage lookup endpoint', () => {
    service.getUserRoleOptions().subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/users/role-options'));
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, data: [{ id: 'r-1', name: 'Admin', isActive: true }] });
  });

  it('sends the custom role name and description on create', () => {
    service.createRole({ name: 'Shift Lead', description: 'Leads a shift' }).subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/roles'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Shift Lead', description: 'Leads a shift' });
    request.flush({ success: true, data: { id: 'r-1', role: 'Shift Lead', name: 'Shift Lead', isSystemRole: false, isActive: true, assignedUsers: 0, permissions: [] } });
  });

  it('sends null to explicitly clear a role description', () => {
    service.updateRole('r-1', { name: 'Shift Lead', description: null, isActive: true }).subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/roles/r-1'));
    expect(request.request.body).toEqual({ name: 'Shift Lead', description: null, isActive: true });
    request.flush({ success: true, data: { id: 'r-1', role: 'Shift Lead', name: 'Shift Lead', description: null, isSystemRole: false, isActive: true, assignedUsers: 0, permissions: [] } });
  });

  it('omits description from a partial role update when the caller does not provide it', () => {
    service.updateRole('r-1', { isActive: false }).subscribe();
    const request = http.expectOne((candidate) => candidate.url.endsWith('/api/admin/roles/r-1'));
    expect(request.request.body).toEqual({ isActive: false });
    request.flush({ success: true, data: { id: 'r-1', role: 'Shift Lead', name: 'Shift Lead', description: 'Kept', isSystemRole: false, isActive: false, assignedUsers: 0, permissions: [] } });
  });
});
