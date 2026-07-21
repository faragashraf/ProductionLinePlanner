import { MenuItem } from 'primeng/api';
import { FactoryStructureEntityType } from './factory-structure-tree.adapter';

export interface FactoryStructureTreePermissions { canManageStructure: boolean; canManageDepartments: boolean; }
export type FactoryStructureTreeAction = 'add-department' | 'add-line' | 'edit' | 'toggle-active' | 'delete';

export function buildFactoryStructureContextMenu(entityType: FactoryStructureEntityType, isActive: boolean, canDelete: boolean, permissions: FactoryStructureTreePermissions, run: (action: FactoryStructureTreeAction) => void): MenuItem[] {
  const items: MenuItem[] = [];
  const add = (label: string, icon: string, action: FactoryStructureTreeAction): void => { items.push({ label, icon, command: () => run(action) }); };
  const edit = (): void => add('تعديل', 'pi pi-pencil', 'edit');
  const activation = (): void => add(isActive ? 'تعطيل' : 'تفعيل', isActive ? 'pi pi-ban' : 'pi pi-check', 'toggle-active');
  const deletion = (): void => { if (canDelete) add('حذف', 'pi pi-trash', 'delete'); };
  if (entityType === 'factory') { if (permissions.canManageDepartments) add('إضافة قسم', 'pi pi-plus', 'add-department'); if (permissions.canManageStructure) { edit(); activation(); deletion(); } }
  if (entityType === 'department') { if (permissions.canManageStructure) add('إضافة خط إنتاج', 'pi pi-plus', 'add-line'); if (permissions.canManageDepartments) { edit(); activation(); deletion(); } }
  if (entityType === 'line' && permissions.canManageStructure) { edit(); activation(); deletion(); }
  return items;
}
