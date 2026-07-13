import { PermissionRequirementDescriptor } from '../authorization/permission-requirement';
import { PERMISSIONS } from './permission-identifiers';

export interface ManufacturingWorkspaceItem extends PermissionRequirementDescriptor {
  id: string;
  label: string;
  description: string;
  route: string;
  icon: string;
}

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
      PERMISSIONS.compensation.view
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
    label: 'النماذج',
    description: 'كتالوج نماذج المنتجات سيظهر هنا.',
    route: '/manufacturing/models',
    icon: 'pi-box',
    permission: PERMISSIONS.models.view
  },
  {
    id: 'compensation',
    label: 'التعويضات',
    description: 'سياسة وأنماط التعويض ستظهر هنا.',
    route: '/manufacturing/compensation',
    icon: 'pi-wallet',
    permission: PERMISSIONS.compensation.view
  }
];

export const MANUFACTURING_WORKSPACE_VIEW_PERMISSIONS = [
  PERMISSIONS.workers.view,
  PERMISSIONS.departments.view,
  PERMISSIONS.factoryStructure.view,
  PERMISSIONS.stages.view,
  PERMISSIONS.models.view,
  PERMISSIONS.compensation.view
] as const;
