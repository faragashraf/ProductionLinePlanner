import { PermissionService } from '../services/permission.service';
import { AppNavigationItem } from './navigation.config';

describe('navigation filtering', () => {
  function service(permissions: string[]): PermissionService {
    const auth = { currentUser$: { subscribe: (callback: (user: any) => void) => callback({ permissions }) } };
    return new PermissionService(auth as any);
  }

  const navigation: AppNavigationItem[] = [
    { id: 'a', label: 'A', route: '/a', icon: 'pi-a', order: 20, group: 'workspace', permission: 'a.view' },
    {
      id: 'parent', label: 'Parent', route: '/parent', icon: 'pi-folder', order: 10, group: 'administration', children: [
        { id: 'child', label: 'Child', route: '/parent/child', icon: 'pi-file', order: 1, group: 'administration', permission: 'child.view' }
      ]
    }
  ];

  it('keeps allowed items, hides denied items, preserves order and hides empty parents', () => {
    expect(service(['child.view', 'a.view']).filterNavigation(navigation).map((item) => item.id)).toEqual(['parent', 'a']);
    expect(service(['a.view']).filterNavigation(navigation).map((item) => item.id)).toEqual(['a']);
  });
});
