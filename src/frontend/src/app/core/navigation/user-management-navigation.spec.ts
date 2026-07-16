import { resolveUsersReturnUrl, USERS_MANAGEMENT_URL } from './user-management-navigation';

describe('user management navigation', () => {
  it('preserves a users-list URL including its search context', () => {
    expect(resolveUsersReturnUrl('/admin/users?q=factory.manager')).toBe('/admin/users?q=factory.manager');
  });

  it('falls back safely for external and unrelated routes', () => {
    expect(resolveUsersReturnUrl('https://example.com/admin/users')).toBe(USERS_MANAGEMENT_URL);
    expect(resolveUsersReturnUrl('/dashboard')).toBe(USERS_MANAGEMENT_URL);
    expect(resolveUsersReturnUrl(null)).toBe(USERS_MANAGEMENT_URL);
  });
});
