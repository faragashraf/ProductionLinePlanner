const CAPABILITY_LABELS: Readonly<Record<string, string>> = Object.freeze({
  workers: 'العاملون',
  departments: 'الأقسام',
  attendance: 'الحضور',
  'factory-structure': 'هيكل المصنع',
  assignments: 'التعيينات',
  compensation: 'الأجور والتعويضات',
  stages: 'مراحل الإنتاج',
  models: 'موديلات الإنتاج',
  production: 'تشغيل الإنتاج',
  users: 'المستخدمون',
  roles: 'الأدوار',
  permissions: 'الصلاحيات',
  audit: 'سجل التدقيق',
  notifications: 'الإشعارات',
  readiness: 'جاهزية التشغيل',
  other: 'صلاحيات أخرى'
});

const PERMISSION_LABELS: Readonly<Record<string, string>> = Object.freeze({
  'users.view': 'عرض المستخدمين',
  'users.manage': 'إدارة المستخدمين',
  'roles.view': 'عرض الأدوار',
  'roles.manage': 'إدارة الأدوار',
  'permissions.assign': 'إدارة الاستثناءات المباشرة',
  'audit.view': 'عرض سجل التدقيق'
});

const ROLE_LABELS: Readonly<Record<string, string>> = Object.freeze({
  SuperAdmin: 'مدير النظام الأعلى',
  Admin: 'مدير',
  Planner: 'مخطط إنتاج',
  Supervisor: 'مشرف',
  Viewer: 'مشاهد'
});

export function iamCapabilityLabel(capability: string): string {
  return CAPABILITY_LABELS[capability] || CAPABILITY_LABELS['other'];
}

export function iamPermissionLabel(permission: string, descriptionAr?: string | null): string {
  return descriptionAr?.trim() || PERMISSION_LABELS[permission] || 'صلاحية إدارية';
}

export function iamRoleLabel(role: string): string {
  return ROLE_LABELS[role] || role;
}
