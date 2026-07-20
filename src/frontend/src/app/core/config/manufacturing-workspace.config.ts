import { PermissionRequirementDescriptor } from '../authorization/permission-requirement';
import { PERMISSIONS } from './permission-identifiers';

export interface ManufacturingWorkspaceItem extends PermissionRequirementDescriptor {
  id: string;
  label: string;
  description: string;
  route: string;
  icon: string;
}

// Recording needs read access for its existing orders/records lookups and record access
// for draft/preview actions. Keep this shared so the tab, route, and contextual action
// cannot drift into separate permission rules.
export const PRODUCTION_RECORDING_ACCESS: PermissionRequirementDescriptor = {
  requireAll: [PERMISSIONS.production.view, PERMISSIONS.production.record]
};

export const DAILY_PRODUCTION_OPERATIONS_ACCESS: PermissionRequirementDescriptor = {
  requireAll: [PERMISSIONS.production.view, PERMISSIONS.production.record]
};

export const REPORTS_WORKSPACE_ACCESS: PermissionRequirementDescriptor = {
  permission: PERMISSIONS.reports.productionView
};

export const LINE_STAFFING_ACCESS: PermissionRequirementDescriptor = {
  requireAll: [
    PERMISSIONS.factoryStructure.view,
    PERMISSIONS.models.view,
    PERMISSIONS.workers.view,
    PERMISSIONS.assignments.view
  ]
};

export const MANUFACTURING_WORKSPACE_ITEMS: readonly ManufacturingWorkspaceItem[] = [
  {
    id: 'manufacturing-dashboard',
    label: 'لوحة التصنيع',
    description: 'نقطة البداية لإدارة بيانات التصنيع الأساسية.',
    route: '/manufacturing/dashboard',
    icon: 'pi-th-large',
    requireAny: [
      PERMISSIONS.workers.view,
      PERMISSIONS.departments.view,
      PERMISSIONS.factoryStructure.view,
      PERMISSIONS.stages.view,
      PERMISSIONS.models.view,
      PERMISSIONS.compensation.view,
      PERMISSIONS.production.view,
      PERMISSIONS.reports.productionView
    ]
  },
  {
    id: 'employees',
    label: 'الموظفون',
    description: 'إدارة بيانات العاملين ستتوفر في مرحلة لاحقة.',
    route: '/manufacturing/employees',
    icon: 'pi-users',
    permission: PERMISSIONS.workers.view
  },
  {
    id: 'departments',
    label: 'الأقسام',
    description: 'كتالوج الأقسام جاهز للربط عند إطلاق إدارة البيانات.',
    route: '/manufacturing/departments',
    icon: 'pi-building',
    permission: PERMISSIONS.departments.view
  },
  {
    id: 'factory-structure',
    label: 'بنية المصنع',
    description: 'هيكل المصنع وخطوط الإنتاج سيظهر هنا.',
    route: '/manufacturing/factory-structure',
    icon: 'pi-sitemap',
    permission: PERMISSIONS.factoryStructure.view
  },
  {
    id: 'stages',
    label: 'المراحل',
    description: 'كتالوج مراحل الإنتاج سيظهر هنا.',
    route: '/manufacturing/stages',
    icon: 'pi-list',
    permission: PERMISSIONS.stages.view
  },
  {
    id: 'models',
    label: 'الموديلات',
    description: 'كتالوج موديلات المنتجات سيظهر هنا.',
    route: '/manufacturing/models',
    icon: 'pi-box',
    permission: PERMISSIONS.models.view
  },
  {
    id: 'line-staffing',
    label: 'تسكين الخط',
    description: 'تخطيط التسكين الدائم والمؤقت لعمال الخط دون ربطه بحضور اليوم.',
    route: '/manufacturing/line-staffing',
    icon: 'pi-users',
    ...LINE_STAFFING_ACCESS
  },
  {
    id: 'daily-production-operations',
    label: 'تشغيل الإنتاج اليومي',
    description: 'تشغيل كل مراحل الموديل لليوم نفسه من التسكين والحضور حتى مسودة يومية واحدة.',
    route: '/manufacturing/daily-production-operations',
    icon: 'pi-calendar-plus',
    ...DAILY_PRODUCTION_OPERATIONS_ACCESS
  },
  {
    id: 'production-recording',
    label: 'تسجيل الإنتاج',
    description: 'تسجيل مرحلة مفردة متوافق مع السجلات السابقة.',
    route: '/manufacturing/production-recording',
    icon: 'pi-play-circle',
    ...PRODUCTION_RECORDING_ACCESS
  },
  {
    id: 'reports',
    label: 'التقارير',
    description: 'مركز تقارير التشغيل والكميات مع فلاتر محفوظة ومصادر قابلة للتتبع.',
    route: '/manufacturing/reports',
    icon: 'pi-chart-bar',
    ...REPORTS_WORKSPACE_ACCESS
  }
];

export const MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS = [
  PERMISSIONS.workers.view,
  PERMISSIONS.departments.view,
  PERMISSIONS.factoryStructure.view,
  PERMISSIONS.stages.view,
  PERMISSIONS.models.view,
  PERMISSIONS.compensation.view,
  PERMISSIONS.assignments.view,
  PERMISSIONS.production.view,
  PERMISSIONS.reports.productionView
] as const;
