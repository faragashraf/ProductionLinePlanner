import { PERMISSIONS } from './permission-identifiers';
import { PermissionRequirementDescriptor } from '../authorization/permission-requirement';
import { MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS } from './manufacturing-workspace.config';

export interface AppNavigationItem extends PermissionRequirementDescriptor {
  id: string;
  label: string;
  route: string;
  icon: string;
  order: number;
  group: 'workspace' | 'administration';
  children?: AppNavigationItem[];
}

export const APP_NAVIGATION_ITEMS: AppNavigationItem[] = [
  { id: 'dashboard', label: 'لوحة التحكم', route: '/dashboard', icon: 'pi-home', order: 10, group: 'workspace' },
  {
    id: 'manufacturing-workspace',
    label: 'مساحة التصنيع',
    route: '/manufacturing/dashboard',
    icon: 'pi-briefcase',
    order: 15,
    group: 'workspace',
    requireAny: [...MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS]
  },
  { id: 'factory-map', label: 'خريطة المصنع', route: '/factory-map', icon: 'pi-map', order: 20, group: 'workspace', permission: PERMISSIONS.factoryStructure.view },
  { id: 'production-lines', label: 'خطوط الإنتاج', route: '/production-lines', icon: 'pi-sitemap', order: 30, group: 'workspace', permission: PERMISSIONS.factoryStructure.view },
  { id: 'stages', label: 'المراحل', route: '/stages', icon: 'pi-list', order: 40, group: 'workspace', permission: PERMISSIONS.stages.view },
  { id: 'workers', label: 'العاملون', route: '/workers', icon: 'pi-users', order: 50, group: 'workspace', permission: PERMISSIONS.workers.view },
  { id: 'assignments', label: 'التعيينات', route: '/assignments', icon: 'pi-file-check', order: 60, group: 'workspace', permission: PERMISSIONS.assignments.view },
  { id: 'notifications', label: 'الإشعارات', route: '/notifications', icon: 'pi-bell', order: 90, group: 'workspace' },
  { id: 'admin-users', label: 'إدارة المستخدمين', route: '/admin/users', icon: 'pi-id-card', order: 100, group: 'administration', permission: PERMISSIONS.users.view },
  { id: 'admin-roles', label: 'إدارة الأدوار', route: '/admin/roles', icon: 'pi-lock', order: 110, group: 'administration', permission: PERMISSIONS.roles.view },
  { id: 'admin-permissions', label: 'كتالوج الصلاحيات', route: '/admin/permissions', icon: 'pi-key', order: 120, group: 'administration', permission: PERMISSIONS.permissions.assign }
];
