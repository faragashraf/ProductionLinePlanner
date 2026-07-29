import { MenuItem } from 'primeng/api';

export type ModelContextAction = 'add' | 'edit' | 'delete';

export function buildModelContextMenu(
  canManageModels: boolean,
  busy: boolean,
  canDelete: boolean,
  deleteBlockReason: string | null,
  run: (action: ModelContextAction) => void
): MenuItem[] {
  if (!canManageModels) return [];

  const item = (label: string, icon: string, action: ModelContextAction, styleClass?: string, disabled = busy, tooltip?: string): MenuItem => ({
    label,
    icon,
    disabled,
    styleClass,
    tooltip,
    command: () => run(action)
  });

  return [
    item('إضافة موديل', 'pi pi-plus', 'add'),
    item('تعديل الموديل', 'pi pi-pencil', 'edit'),
    { separator: true },
    item('حذف الموديل', 'pi pi-trash', 'delete', 'p-menuitem-danger', busy || !canDelete, deleteBlockReason ?? undefined)
  ];
}
