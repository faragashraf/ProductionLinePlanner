import { ProductionIconClass } from '../icons/production-icon-map';

export type ProductionVisualTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';
export type PrimeNgSeverity = 'success' | 'warning' | 'danger' | 'info' | 'secondary';

export interface ProductionVisualToneMeta {
  readonly tone: ProductionVisualTone;
  readonly token: `--plp-color-${string}`;
  readonly softToken: `--plp-color-${string}`;
  readonly primeSeverity: PrimeNgSeverity;
  readonly icon: ProductionIconClass;
}

export const PRODUCTION_VISUAL_TONE_MAP = {
  success: {
    tone: 'success',
    token: '--plp-color-success',
    softToken: '--plp-color-success-soft',
    primeSeverity: 'success',
    icon: 'pi pi-check-circle'
  },
  warning: {
    tone: 'warning',
    token: '--plp-color-warning',
    softToken: '--plp-color-warning-soft',
    primeSeverity: 'warning',
    icon: 'pi pi-exclamation-triangle'
  },
  danger: {
    tone: 'danger',
    token: '--plp-color-danger',
    softToken: '--plp-color-danger-soft',
    primeSeverity: 'danger',
    icon: 'pi pi-times-circle'
  },
  info: {
    tone: 'info',
    token: '--plp-color-info',
    softToken: '--plp-color-info-soft',
    primeSeverity: 'info',
    icon: 'pi pi-info-circle'
  },
  neutral: {
    tone: 'neutral',
    token: '--plp-color-neutral',
    softToken: '--plp-color-neutral-soft',
    primeSeverity: 'secondary',
    icon: 'pi pi-minus-circle'
  }
} as const satisfies Readonly<Record<ProductionVisualTone, ProductionVisualToneMeta>>;

export function productionVisualToneFor(tone?: ProductionVisualTone | null): ProductionVisualToneMeta {
  return PRODUCTION_VISUAL_TONE_MAP[tone ?? 'neutral'];
}

export type ProductionStatusKey =
  | 'draft'
  | 'approved'
  | 'cancelled'
  | 'active'
  | 'inactive'
  | 'pending'
  | 'success'
  | 'warning'
  | 'danger'
  | 'info'
  | 'neutral';

export interface ProductionStatusMeta {
  readonly key: ProductionStatusKey;
  readonly labelAr: string;
  readonly labelEn: string;
  readonly tone: ProductionVisualTone;
  readonly icon: ProductionIconClass;
}

export const PRODUCTION_STATUS_MAP = {
  draft: { key: 'draft', labelAr: 'مسودة', labelEn: 'Draft', tone: 'neutral', icon: 'pi pi-file-edit' },
  approved: { key: 'approved', labelAr: 'معتمد', labelEn: 'Approved', tone: 'success', icon: 'pi pi-check-circle' },
  cancelled: { key: 'cancelled', labelAr: 'ملغى', labelEn: 'Cancelled', tone: 'danger', icon: 'pi pi-times-circle' },
  active: { key: 'active', labelAr: 'نشط', labelEn: 'Active', tone: 'success', icon: 'pi pi-check-circle' },
  inactive: { key: 'inactive', labelAr: 'غير نشط', labelEn: 'Inactive', tone: 'neutral', icon: 'pi pi-ban' },
  pending: { key: 'pending', labelAr: 'قيد الانتظار', labelEn: 'Pending', tone: 'warning', icon: 'pi pi-clock' },
  success: { key: 'success', labelAr: 'تم بنجاح', labelEn: 'Success', tone: 'success', icon: 'pi pi-check' },
  warning: { key: 'warning', labelAr: 'تحذير', labelEn: 'Warning', tone: 'warning', icon: 'pi pi-exclamation-triangle' },
  danger: { key: 'danger', labelAr: 'خطر', labelEn: 'Danger', tone: 'danger', icon: 'pi pi-exclamation-circle' },
  info: { key: 'info', labelAr: 'معلومة', labelEn: 'Info', tone: 'info', icon: 'pi pi-info-circle' },
  neutral: { key: 'neutral', labelAr: 'محايد', labelEn: 'Neutral', tone: 'neutral', icon: 'pi pi-minus-circle' }
} as const satisfies Readonly<Record<ProductionStatusKey, ProductionStatusMeta>>;

const statusAliases: Readonly<Record<string, ProductionStatusKey>> = {
  canceled: 'cancelled',
  cancelled: 'cancelled',
  inactive: 'inactive',
  active: 'active',
  approved: 'approved',
  pending: 'pending',
  draft: 'draft',
  success: 'success',
  warning: 'warning',
  danger: 'danger',
  info: 'info',
  neutral: 'neutral'
};

export function resolveProductionStatus(input?: string | null): ProductionStatusMeta {
  const normalized = input?.trim().toLowerCase();
  const key = normalized ? statusAliases[normalized] : undefined;
  return PRODUCTION_STATUS_MAP[key ?? 'neutral'];
}
