import { PERMISSIONS } from './permission-identifiers';
import { PermissionRequirementDescriptor } from '../authorization/permission-requirement';
import { MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS } from './manufacturing-workspace.config';

const HUMAN_RESOURCES_ROLE_NAMES = ['HumanResources', 'Human Resources', 'HR', 'Hr'] as const;
const ACCOUNTING_ROLE_NAMES = ['Accounting', 'Accountant'] as const;
const RESTRICTED_NAVIGATION_ROLE_NAMES = [...HUMAN_RESOURCES_ROLE_NAMES, ...ACCOUNTING_ROLE_NAMES] as const;

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
  { id: 'dashboard', label: 'لوحة التحكم', route: '/dashboard', icon: 'pi-home', order: 10, group: 'workspace', hiddenForRoles: RESTRICTED_NAVIGATION_ROLE_NAMES },
  {
    id: 'manufacturing-workspace',
    label: 'مساحة التصنيع',
    route: '/manufacturing/dashboard',
    icon: 'pi-briefcase',
    order: 15,
    group: 'workspace',
    requireAny: [...MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS],
    hiddenForRoles: RESTRICTED_NAVIGATION_ROLE_NAMES
  },
  { id: 'daily-production-operations', label: 'تشغيل الإنتاج اليومي', route: '/manufacturing/daily-production-operations', icon: 'pi-calendar-plus', order: 16, group: 'workspace', permission: PERMISSIONS.production.dailyDraftsApprove, visibleForRoles: ACCOUNTING_ROLE_NAMES },
  {
    id: 'factory-map',
    label: 'خريطة المصنع',
    route: '/factory-map',
    icon: 'pi-map',
    order: 20,
    group: 'workspace',
    requireAll: [PERMISSIONS.factoryStructure.view, PERMISSIONS.stages.view, PERMISSIONS.assignments.view, PERMISSIONS.attendance.view]
  },
  { id: 'workers', label: 'إدارة العاملين', route: '/workers', icon: 'pi-users', order: 50, group: 'workspace', permission: PERMISSIONS.workers.view },
  { id: 'attendance-workforce', label: 'الحضور والتسكين اليومي', route: '/attendance/workforce', icon: 'pi-clock', order: 55, group: 'workspace', requireAll: [PERMISSIONS.attendance.view, PERMISSIONS.assignments.view] },
  { id: 'notifications', label: 'الإشعارات', route: '/notifications', icon: 'pi-bell', order: 90, group: 'workspace', hiddenForRoles: RESTRICTED_NAVIGATION_ROLE_NAMES },
  { id: 'admin-users', label: 'إدارة المستخدمين', route: '/admin/users', icon: 'pi-id-card', order: 100, group: 'administration', permission: PERMISSIONS.users.view },
  { id: 'admin-roles', label: 'إدارة الأدوار', route: '/admin/roles', icon: 'pi-lock', order: 110, group: 'administration', permission: PERMISSIONS.roles.view },
  { id: 'admin-permissions', label: 'كتالوج الصلاحيات', route: '/admin/permissions', icon: 'pi-key', order: 120, group: 'administration', permission: PERMISSIONS.permissions.assign },
  { id: 'admin-notification-policies', label: 'سياسات الإشعارات', route: '/admin/notification-policies', icon: 'pi-bell', order: 130, group: 'administration', permission: PERMISSIONS.notifications.policiesManage }
];
