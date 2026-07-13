import { parsePermissionRequirement } from './permission-requirement';

describe('parsePermissionRequirement', () => {
  it('accepts requireAny only', () => {
    const parsed = parsePermissionRequirement({ requireAny: ['users.view', 'roles.view'] });

    expect(parsed.isMalformed).toBeFalse();
    expect(parsed.requirement?.requireAny).toEqual(['users.view', 'roles.view']);
  });

  it('accepts permission only', () => {
    const parsed = parsePermissionRequirement({ permission: 'users.view' });

    expect(parsed.isMalformed).toBeFalse();
    expect(parsed.requirement).toEqual({ permission: 'users.view' });
  });

  it('accepts requireAll only', () => {
    const parsed = parsePermissionRequirement({ requireAll: ['users.view', 'roles.view'] });

    expect(parsed.isMalformed).toBeFalse();
    expect(parsed.requirement?.requireAll).toEqual(['users.view', 'roles.view']);
  });

  it('rejects permission + requireAny', () => {
    const parsed = parsePermissionRequirement({
      permission: 'users.view',
      requireAny: ['users.view', 'roles.view']
    });

    expect(parsed.hasMetadata).toBeTrue();
    expect(parsed.isMalformed).toBeTrue();
    expect(parsed.requirement).toBeUndefined();
  });

  it('rejects permission + requireAll', () => {
    const parsed = parsePermissionRequirement({
      permission: 'users.view',
      requireAll: ['users.view', 'roles.view']
    });

    expect(parsed.hasMetadata).toBeTrue();
    expect(parsed.isMalformed).toBeTrue();
    expect(parsed.requirement).toBeUndefined();
  });

  it('rejects requireAny + requireAll', () => {
    const parsed = parsePermissionRequirement({
      requireAny: ['users.view'],
      requireAll: ['users.view']
    });

    expect(parsed.hasMetadata).toBeTrue();
    expect(parsed.isMalformed).toBeTrue();
    expect(parsed.requirement).toBeUndefined();
  });

  it('rejects malformed metadata and empty metadata', () => {
    const malformed = parsePermissionRequirement({ permission: ['users.view'] });
    const empty = parsePermissionRequirement({});

    expect(malformed.hasMetadata).toBeTrue();
    expect(malformed.isMalformed).toBeTrue();
    expect(malformed.requirement).toBeUndefined();

    expect(empty.hasMetadata).toBeFalse();
    expect(empty.isMalformed).toBeTrue();
    expect(empty.requirement).toBeUndefined();
  });
});
