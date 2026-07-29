import { APP_NAVIGATION_ITEMS } from './navigation.config';
import { PermissionService } from '../services/permission.service';
import { AppNavigationItem } from './navigation.config';
import { PERMISSIONS } from './permission-identifiers';

describe('navigation filtering', () => {
  function service(permissions: string[]): PermissionService {
    const auth = {
      currentUser$: { subscribe: (callback: (user: any) => void) => callback({ permissions }) },
      isAuthenticated: () => true,
      getCurrentUser: () => ({ permissions })
    };
    return new PermissionService(auth as any);
  }

  const navigation: AppNavigationItem[] = [
    { id: 'a', label: 'A', route: '/a', icon: 'pi-a', order: 20, group: 'workspace', permission: 'a.view' },
    {
      id: 'parent', label: 'Parent', route: '/parent', icon: 'pi-folder', order: 10, group: 'administration', permission: 'parent.view', children: [
        { id: 'child', label: 'Child', route: '/parent/child', icon: 'pi-file', order: 1, group: 'administration', permission: 'child.view' }
      ]
    }
  ];

  it('keeps allowed leaf and hides denied leaf while preserving order', () => {
    expect(service(['a.view', 'child.view', 'parent.view']).filterNavigation(navigation).map((item) => item.id)).toEqual(['parent', 'a']);
    expect(service(['a.view']).filterNavigation(navigation).map((item) => item.id)).toEqual(['a']);
  });

  it('hides parent when parent requirement is denied even if child is allowed', () => {
    expect(service(['child.view']).filterNavigation(navigation).map((item) => item.id)).toEqual([]);
  });

  it('hides parent when no children are allowed and parent is not marked as standalone', () => {
    const parentWithoutChildren: AppNavigationItem[] = [
      {
        id: 'parent', label: 'Parent', route: '/parent', icon: 'pi-folder', order: 10, group: 'administration',
        permission: 'parent.view',
        children: [
          { id: 'child', label: 'Child', route: '/parent/child', icon: 'pi-file', order: 1, group: 'administration', permission: 'child.view' }
        ]
      }
    ];

    expect(service(['parent.view']).filterNavigation(parentWithoutChildren).map((item) => item.id)).toEqual([]);
  });

  it('keeps admin/workspace entries for SuperAdmin effective permissions', () => {
    const superAdminPermissions = [
      PERMISSIONS.users.view,
      PERMISSIONS.roles.view,
      PERMISSIONS.permissions.assign,
      PERMISSIONS.workers.view,
      PERMISSIONS.departments.view,
      PERMISSIONS.stages.view,
      PERMISSIONS.models.view,
      PERMISSIONS.compensation.view,
      PERMISSIONS.factoryStructure.view,
      PERMISSIONS.assignments.view,
      PERMISSIONS.workers.manage
    ];
    const items = service(superAdminPermissions).filterNavigation(APP_NAVIGATION_ITEMS).map((item) => item.id);

    expect(items).toContain('admin-users');
    expect(items).toContain('admin-roles');
    expect(items).toContain('admin-permissions');
    expect(items).toContain('manufacturing-workspace');
    expect(items).toContain('workers');
    expect(items).toContain('factory-map');
    expect(items).not.toContain('stages');
    expect(items).not.toContain('production-lines');
    expect(items).not.toContain('assignments');
    expect(items).not.toContain('models');
  });

  it('hides Factory Map unless both its hierarchy permissions are granted', () => {
    const onlyFactoryStructure = service([PERMISSIONS.factoryStructure.view])
      .filterNavigation(APP_NAVIGATION_ITEMS)
      .map((item) => item.id);
    const completeAccess = service([PERMISSIONS.factoryStructure.view, PERMISSIONS.stages.view])
      .filterNavigation(APP_NAVIGATION_ITEMS)
      .map((item) => item.id);

    expect(onlyFactoryStructure).not.toContain('factory-map');
    expect(completeAccess).toContain('factory-map');
  });

  it('places the worker management workspace behind workers.view', () => {
    const workers = APP_NAVIGATION_ITEMS.find(item => item.id === 'workers');
    expect(workers?.label).toBe('إدارة العاملين');
    expect(workers?.route).toBe('/workers');
    expect(workers?.permission).toBe(PERMISSIONS.workers.view);
  });
});
