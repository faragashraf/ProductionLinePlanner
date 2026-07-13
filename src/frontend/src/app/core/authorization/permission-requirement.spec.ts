import { parsePermissionRequirement } from './permission-requirement';

describe('parsePermissionRequirement', () => {
  it('accepts one typed requirement', () => {
    const parsed = parsePermissionRequirement({ requireAny: ['users.view', 'roles.view'] });

    expect(parsed.isMalformed).toBeFalse();
    expect(parsed.requirement?.requireAny).toEqual(['users.view', 'roles.view']);
  });

  it('fails closed for malformed route metadata', () => {
    const parsed = parsePermissionRequirement({ permission: ['users.view'] });

    expect(parsed.hasMetadata).toBeTrue();
    expect(parsed.isMalformed).toBeTrue();
    expect(parsed.requirement).toBeUndefined();
  });

  it('accepts requireAll and rejects mixed requirement modes', () => {
    expect(parsePermissionRequirement({ requireAll: ['users.view', 'roles.view'] }).requirement?.requireAll)
      .toEqual(['users.view', 'roles.view']);
    expect(parsePermissionRequirement({ permission: 'users.view', requireAny: ['roles.view'] }).isMalformed).toBeTrue();
  });
});
