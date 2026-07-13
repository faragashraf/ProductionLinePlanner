import { of, throwError } from 'rxjs';
import { AdminUsersPageComponent } from './admin-users-page.component';

describe('AdminUsersPageComponent', () => {
  function createComponent(getUsers: jasmine.Spy): AdminUsersPageComponent {
    return new AdminUsersPageComponent(
      { getUsers } as any,
      { navigateByUrl: () => undefined } as any,
      { confirm: () => true } as any
    );
  }

  it('shows an empty state only for a successful empty response', () => {
    const component = createComponent(jasmine.createSpy('getUsers').and.returnValue(of([])));

    component.loadUsers(true);

    expect(component.hasError).toBeFalse();
    expect(component.users).toEqual([]);
    expect(component.isLoading).toBeFalse();
  });

  it('preserves the error state after a failed request', () => {
    const component = createComponent(jasmine.createSpy('getUsers').and.returnValue(throwError(() => new Error('Users unavailable'))));

    component.loadUsers(true);

    expect(component.hasError).toBeTrue();
    expect(component.errorMessage).toBe('Users unavailable');
    expect(component.isLoading).toBeFalse();
  });

  it('clears a previous error after a successful retry', () => {
    const getUsers = jasmine.createSpy('getUsers').and.returnValues(
      throwError(() => new Error('Users unavailable')),
      of([])
    );
    const component = createComponent(getUsers);

    component.loadUsers(true);
    component.loadUsers(false);

    expect(component.hasError).toBeFalse();
    expect(component.errorMessage).toBeNull();
    expect(component.users).toEqual([]);
    expect(component.isRefreshing).toBeFalse();
  });
});
