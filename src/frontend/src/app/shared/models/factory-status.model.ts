import { clampPercent } from '../utils/number.utils';

export type FactoryStatus =
  | 'present'
  | 'late'
  | 'absent'
  | 'unassigned'
  | 'ready'
  | 'warning'
  | 'critical'
  | 'info';

export interface FactoryStatusMeta {
  status: FactoryStatus;
  labelAr: string;
  labelEn: string;
  icon: string;
  toneClass: FactoryStatus;
  ariaLabel: string;
}

const fallbackMeta: FactoryStatusMeta = {
  status: 'info',
  labelAr: 'معلومة',
  labelEn: 'info',
  icon: 'pi-info-circle',
  toneClass: 'info',
  ariaLabel: 'حالة معلومات'
};

export const FACTORY_STATUS_MAP: Record<FactoryStatus, FactoryStatusMeta> = {
  present: {
    status: 'present',
    labelAr: 'حاضر',
    labelEn: 'Present',
    icon: 'pi-check',
    toneClass: 'present',
    ariaLabel: 'عامل حاضر'
  },
  late: {
    status: 'late',
    labelAr: 'متأخر',
    labelEn: 'Late',
    icon: 'pi-clock',
    toneClass: 'late',
    ariaLabel: 'عامل متأخر'
  },
  absent: {
    status: 'absent',
    labelAr: 'غائب',
    labelEn: 'Absent',
    icon: 'pi-times',
    toneClass: 'absent',
    ariaLabel: 'عامل غائب'
  },
  unassigned: {
    status: 'unassigned',
    labelAr: 'غير مُعين',
    labelEn: 'Unassigned',
    icon: 'pi-user',
    toneClass: 'unassigned',
    ariaLabel: 'موظف غير مُعين'
  },
  ready: {
    status: 'ready',
    labelAr: 'جاهز',
    labelEn: 'Ready',
    icon: 'pi-check-circle',
    toneClass: 'ready',
    ariaLabel: 'جاهز'
  },
  warning: {
    status: 'warning',
    labelAr: 'تحذير',
    labelEn: 'Warning',
    icon: 'pi-exclamation-triangle',
    toneClass: 'warning',
    ariaLabel: 'تحذير'
  },
  critical: {
    status: 'critical',
    labelAr: 'حرج',
    labelEn: 'Critical',
    icon: 'pi-times-circle',
    toneClass: 'critical',
    ariaLabel: 'حالة حرجة'
  },
  info: {
    status: 'info',
    labelAr: 'معلومات',
    labelEn: 'Info',
    icon: 'pi-info-circle',
    toneClass: 'info',
    ariaLabel: 'معلومة'
  }
};

export function resolveFactoryStatus(input?: FactoryStatus | string | null): FactoryStatusMeta {
  if (!input) {
    return fallbackMeta;
  }
  if (input in FACTORY_STATUS_MAP) {
    return FACTORY_STATUS_MAP[input as FactoryStatus];
  }

  const normalized = String(input).trim();
  const legacyMap: Record<string, FactoryStatus> = {
    حاضر: 'present',
    متأخر: 'late',
    غائب: 'absent',
    جاهز: 'ready',
    حاضرون: 'present',
    warning: 'warning',
    critical: 'critical',
    info: 'info'
  };

  return FACTORY_STATUS_MAP[legacyMap[normalized] ?? 'info'];
}

export function deriveStatusFromReadiness(percent: number): FactoryStatus {
  const value = clampPercent(percent);
  if (value >= 85) {
    return 'ready';
  }
  if (value >= 70) {
    return 'warning';
  }
  if (value >= 40) {
    return 'late';
  }
  return 'critical';
}

export const FACTORY_STATUS_KEYS: FactoryStatus[] = [
  'present',
  'late',
  'absent',
  'unassigned',
  'ready',
  'warning',
  'critical',
  'info'
];
