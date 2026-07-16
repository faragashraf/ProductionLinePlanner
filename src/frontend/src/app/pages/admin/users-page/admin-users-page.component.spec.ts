import { FormBuilder } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { RouterTestingModule } from '@angular/router/testing';
import { of, Subject, throwError } from 'rxjs';
import { IamAdminService } from '../../../core/services/iam-admin.service';
import { PermissionService } from '../../../core/services/permission.service';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';
import { IamAdminModule } from '../iam-admin.module';
import { AdminUserDetails } from '../../../core/services/iam-admin.service';
import { AdminUsersPageComponent } from './admin-users-page.component';

describe('AdminUsersPageComponent', () => {
  const role = { id: 'role-1', name: 'Admin', isActive: true };
  const user: AdminUserDetails = {
    id: 'user-1',
    fullName: 'Ashraf Farag',
    email: 'ashraf',
    isActive: true,
    roles: ['Admin'],
    roleIds: ['role-1'],
    preferredLanguage: 'ar',
    createdAtUtc: '2026-07-17T08:00:00Z',
    updatedAtUtc: '2026-07-17T09:00:00Z'
  };

  function createComponent(overrides: Record<string, unknown> = {}, query = ''): { component: AdminUsersPageComponent; admin: any; router: any } {
    const admin = {
      getUsers: jasmine.createSpy('getUsers').and.returnValue(of([])),
      getUserRoleOptions: jasmine.createSpy('getUserRoleOptions').and.returnValue(of([role])),
      getUser: jasmine.createSpy('getUser').and.returnValue(of(user)),
      createUser: jasmine.createSpy('createUser').and.returnValue(of(user)),
      updateUser: jasmine.createSpy('updateUser').and.returnValue(of(user)),
      updateUserStatus: jasmine.createSpy('updateUserStatus').and.returnValue(of(undefined)),
      ...overrides
    };
    const router = {
      url: query ? `/admin/users?q=${encodeURIComponent(query)}` : '/admin/users',
      navigateByUrl: jasmine.createSpy('navigateByUrl'),
      navigate: jasmine.createSpy('navigate')
    };
    const route = { snapshot: { queryParamMap: { get: (key: string) => key === 'q' ? query || null : null } } };
    return {
      component: new AdminUsersPageComponent(
        admin as any,
        router as any,
        { confirm: () => true } as any,
        new FormBuilder(),
        route as any
      ),
      admin,
      router
    };
  }

  it('restores search context from the URL and passes it as a safe returnUrl to permissions', () => {
    const { component, router } = createComponent({}, 'ashraf');
    component.ngOnInit();
    component.openAuthorization('user-1');

    expect(component.searchTerm).toBe('ashraf');
    expect(router.navigate).toHaveBeenCalledWith(['/admin/users', 'user-1'], {
      queryParams: { returnUrl: '/admin/users?q=ashraf' },
      state: { returnUrl: '/admin/users?q=ashraf', source: 'user-management' }
    });
  });

  it('shows an empty state only for a successful empty response', () => {
    const { component } = createComponent();
    component.loadUsers(true);
    expect(component.hasError).toBeFalse();
    expect(component.users).toEqual([]);
    expect(component.isLoading).toBeFalse();
  });

  it('opens the create dialog with required full name, username, password, and role controls', () => {
    const { component } = createComponent();
    component.openCreateDialog();
    expect(component.dialogVisible).toBeTrue();
    expect(component.dialogMode).toBe('create');
    expect(component.userForm.controls.fullName.hasError('required')).toBeTrue();
    expect(component.userForm.controls.email.hasError('required')).toBeTrue();
    expect(component.userForm.controls.password.hasError('required')).toBeTrue();
    expect(component.userForm.controls.roleIds.hasError('required')).toBeTrue();
  });

  it('opens edit and loads all manageable and read-only user details without requiring password', () => {
    const { component, admin } = createComponent();
    component.openEditDialog(user);
    expect(admin.getUser).toHaveBeenCalledOnceWith('user-1');
    expect(component.selectedUser).toEqual(user);
    expect(component.userForm.controls.fullName.value).toBe('Ashraf Farag');
    expect(component.userForm.controls.email.value).toBe('ashraf');
    expect(component.userForm.controls.roleIds.value).toEqual(['role-1']);
    expect(component.userForm.controls.password.hasError('required')).toBeFalse();
  });

  it('submits a normalized create payload and updates the list without reloading it', () => {
    const saved = { ...user, email: 'factory.manager' };
    const { component, admin } = createComponent({ createUser: jasmine.createSpy('createUser').and.returnValue(of(saved)) });
    component.openCreateDialog();
    component.userForm.setValue({
      fullName: '  Factory Manager  ',
      email: '  FACTORY.MANAGER  ',
      password: 'secret',
      roleIds: ['role-1'],
      isActive: true
    });
    component.saveUser();

    expect(admin.createUser).toHaveBeenCalledWith({
      fullName: 'Factory Manager',
      email: 'factory.manager',
      password: 'secret',
      roleIds: ['role-1'],
      isActive: true
    });
    expect(component.users[0].email).toBe('factory.manager');
    expect(admin.getUsers).not.toHaveBeenCalled();
    expect(component.dialogVisible).toBeFalse();
  });

  it('submits an edit payload without a password property', () => {
    const { component, admin } = createComponent();
    component.openEditDialog(user);
    component.userForm.controls.fullName.setValue('Updated Name');
    component.saveUser();

    const payload = admin.updateUser.calls.mostRecent().args[1];
    expect(payload.fullName).toBe('Updated Name');
    expect(payload.password).toBeUndefined();
  });

  it('keeps the dialog and entered values open when saving fails', () => {
    const { component } = createComponent({ createUser: jasmine.createSpy('createUser').and.returnValue(throwError(() => new Error('Login identifier is already in use.'))) });
    component.openCreateDialog();
    component.userForm.setValue({ fullName: 'Entered Name', email: 'duplicate', password: 'secret', roleIds: ['role-1'], isActive: true });
    component.saveUser();

    expect(component.dialogVisible).toBeTrue();
    expect(component.userForm.controls.fullName.value).toBe('Entered Name');
    expect(component.dialogError).toContain('اسم المستخدم مستخدم بالفعل');
  });

  it('blocks duplicate save requests while a request is pending', () => {
    const pending = new Subject<AdminUserDetails>();
    const { component, admin } = createComponent({ createUser: jasmine.createSpy('createUser').and.returnValue(pending) });
    component.openCreateDialog();
    component.userForm.setValue({ fullName: 'Name', email: 'login', password: 'secret', roleIds: ['role-1'], isActive: true });
    component.saveUser();
    component.saveUser();
    expect(admin.createUser).toHaveBeenCalledTimes(1);
    pending.complete();
  });

  it('preserves the error state after a failed list request and clears it on retry', () => {
    const getUsers = jasmine.createSpy('getUsers').and.returnValues(
      throwError(() => new Error('Users unavailable')),
      of([])
    );
    const { component } = createComponent({ getUsers });
    component.loadUsers(true);
    expect(component.hasError).toBeTrue();
    component.loadUsers(false);
    expect(component.hasError).toBeFalse();
    expect(component.errorMessage).toBeNull();
  });
});

describe('AdminUsersPageComponent DOM', () => {
  let fixture: ComponentFixture<AdminUsersPageComponent>;
  const role = { id: 'role-1', name: 'Admin', isActive: true };
  const details = {
    id: 'user-1', fullName: 'Ashraf Farag', email: 'ashraf', isActive: true,
    roles: ['Admin'], roleIds: ['role-1'], preferredLanguage: 'ar',
    createdAtUtc: '2026-07-17T08:00:00Z', updatedAtUtc: '2026-07-17T09:00:00Z'
  };

  beforeEach(() => {
    document.body.querySelectorAll('.p-dialog-mask').forEach((element) => element.remove());
    const admin = {
      getUsers: () => of([]),
      getUserRoleOptions: () => of([role]),
      getUser: () => of(details)
    };
    const permissions = {
      permissions$: of(['users.manage']),
      hydrationState$: of('ready'),
      hydrationState: 'ready',
      hasAccess: () => true
    };
    TestBed.configureTestingModule({
      imports: [IamAdminModule, RouterTestingModule, NoopAnimationsModule],
      providers: [
        { provide: IamAdminService, useValue: admin },
        { provide: PermissionService, useValue: permissions },
        { provide: IamConfirmationService, useValue: { confirm: () => true } }
      ]
    });
    fixture = TestBed.createComponent(AdminUsersPageComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    fixture.destroy();
    document.body.querySelectorAll('.p-dialog-mask').forEach((element) => element.remove());
  });

  it('renders the Create dialog as an RTL text-username form with a password', () => {
    fixture.componentInstance.openCreateDialog();
    fixture.detectChanges();
    const dialogs = document.body.querySelectorAll('.p-dialog');
    const dialog = dialogs.item(dialogs.length - 1) as HTMLElement;
    const username = dialog.querySelector('#user-login-identifier') as HTMLInputElement;

    expect(dialog.textContent).toContain('اسم المستخدم');
    expect(username.type).toBe('text');
    expect(dialog.querySelector('#user-password')).not.toBeNull();
    expect(dialog.querySelector('.p-dialog-content')).not.toBeNull();
  });

  it('renders the Edit dialog with the username and read-only system data but no password field', () => {
    fixture.componentInstance.openEditDialog(details);
    fixture.detectChanges();
    const dialogs = document.body.querySelectorAll('.p-dialog');
    const dialog = dialogs.item(dialogs.length - 1) as HTMLElement;

    expect((dialog.querySelector('#user-login-identifier') as HTMLInputElement).value).toBe('ashraf');
    expect(dialog.textContent).toContain('معلومات النظام');
    expect(dialog.querySelector('#user-password')).toBeNull();
  });
});
