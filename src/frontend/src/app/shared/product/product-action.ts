import { ProductionIconAction, productionIconFor } from '../design-system/icons/production-icon-map';

export type PlpActionKind =
  | 'save'
  | 'cancel'
  | 'edit'
  | 'delete'
  | 'activate'
  | 'deactivate'
  | 'refresh'
  | 'approve'
  | 'reject'
  | 'import'
  | 'export';

export type PlpActionTone = 'primary' | 'secondary' | 'success' | 'warning' | 'danger';

export interface PlpActionDefinition {
  readonly labelAr: string;
  readonly iconAction: ProductionIconAction;
  readonly tone: PlpActionTone;
  readonly outlined: boolean;
}

export const PLP_ACTION_DEFINITIONS = {
  save: { labelAr: 'حفظ', iconAction: 'save', tone: 'primary', outlined: false },
  cancel: { labelAr: 'إلغاء', iconAction: 'cancel', tone: 'secondary', outlined: true },
  edit: { labelAr: 'تعديل', iconAction: 'edit', tone: 'primary', outlined: true },
  delete: { labelAr: 'حذف', iconAction: 'delete', tone: 'danger', outlined: true },
  activate: { labelAr: 'تفعيل', iconAction: 'activate', tone: 'success', outlined: true },
  deactivate: { labelAr: 'تعطيل', iconAction: 'deactivate', tone: 'warning', outlined: true },
  refresh: { labelAr: 'تحديث', iconAction: 'refresh', tone: 'secondary', outlined: true },
  approve: { labelAr: 'اعتماد', iconAction: 'approve', tone: 'success', outlined: false },
  reject: { labelAr: 'رفض', iconAction: 'reject', tone: 'danger', outlined: true },
  import: { labelAr: 'استيراد', iconAction: 'import', tone: 'secondary', outlined: true },
  export: { labelAr: 'تصدير', iconAction: 'export', tone: 'secondary', outlined: true }
} as const satisfies Readonly<Record<PlpActionKind, PlpActionDefinition>>;

export function plpActionDefinitionFor(action: PlpActionKind): PlpActionDefinition {
  return PLP_ACTION_DEFINITIONS[action];
}

export function plpActionIconFor(action: PlpActionKind): string {
  return productionIconFor(plpActionDefinitionFor(action).iconAction);
}
