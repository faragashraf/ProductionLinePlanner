export const USERS_MANAGEMENT_URL = '/admin/users';

/** Accepts only the users list route and its local query/hash state. */
export function resolveUsersReturnUrl(candidate: unknown): string {
  if (typeof candidate !== 'string') return USERS_MANAGEMENT_URL;
  const value = candidate.trim();
  return /^\/admin\/users(?:\?[^#]*)?(?:#.*)?$/.test(value)
    ? value
    : USERS_MANAGEMENT_URL;
}
