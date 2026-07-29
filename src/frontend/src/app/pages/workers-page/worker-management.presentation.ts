import {
  WorkerAssignmentStatus,
  WorkerLocalEmploymentStatus,
  WorkerLocalProfileStatus,
  WorkerSourceLinkStatus
} from './worker-management.models';

export type WorkerManagementTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

export interface WorkerManagementStatusPresentation {
  label: string;
  tone: WorkerManagementTone;
  icon: string;
}

const LOCAL_PROFILE_STATUS: Readonly<Record<WorkerLocalProfileStatus, WorkerManagementStatusPresentation>> = {
  complete: { label: 'ملف مكتمل', tone: 'success', icon: 'pi pi-check-circle' },
  'needs-review': { label: 'يحتاج مراجعة', tone: 'warning', icon: 'pi pi-exclamation-triangle' },
  'source-pending': { label: 'جديد بانتظار المراجعة', tone: 'info', icon: 'pi pi-clock' }
};

const SOURCE_LINK_STATUS: Readonly<Record<WorkerSourceLinkStatus, WorkerManagementStatusPresentation>> = {
  linked: { label: 'مرجع خارجي محفوظ', tone: 'success', icon: 'pi pi-link' },
  unlinked: { label: 'لا يوجد مرجع خارجي', tone: 'neutral', icon: 'pi pi-minus-circle' },
  conflict: { label: 'تعارض هوية', tone: 'danger', icon: 'pi pi-exclamation-circle' },
  'new-source': { label: 'جديد من المصدر', tone: 'info', icon: 'pi pi-plus-circle' },
  'missing-source': { label: 'غير ظاهر في آخر قراءة', tone: 'warning', icon: 'pi pi-eye-slash' }
};

const ASSIGNMENT_STATUS: Readonly<Record<WorkerAssignmentStatus, WorkerManagementStatusPresentation>> = {
  assigned: { label: 'مسكن دائمًا', tone: 'success', icon: 'pi pi-map-marker' },
  unassigned: { label: 'غير مسكن حاليًا', tone: 'warning', icon: 'pi pi-map' },
  multiple: { label: 'تسكينات دائمة متعددة', tone: 'info', icon: 'pi pi-sitemap' }
};

const LOCAL_EMPLOYMENT_STATUS: Readonly<Record<WorkerLocalEmploymentStatus, WorkerManagementStatusPresentation>> = {
  active: { label: 'نشط محليًا', tone: 'success', icon: 'pi pi-check-circle' },
  inactive: { label: 'معلّق محليًا', tone: 'neutral', icon: 'pi pi-ban' },
  'left-employment': { label: 'منتهية خدمته محليًا', tone: 'warning', icon: 'pi pi-user-minus' }
};

export function localProfileStatusPresentation(status: WorkerLocalProfileStatus): WorkerManagementStatusPresentation {
  return LOCAL_PROFILE_STATUS[status];
}

export function sourceLinkStatusPresentation(status: WorkerSourceLinkStatus): WorkerManagementStatusPresentation {
  return SOURCE_LINK_STATUS[status];
}

export function assignmentStatusPresentation(status: WorkerAssignmentStatus): WorkerManagementStatusPresentation {
  return ASSIGNMENT_STATUS[status];
}

export function localEmploymentStatusPresentation(status: WorkerLocalEmploymentStatus): WorkerManagementStatusPresentation {
  return LOCAL_EMPLOYMENT_STATUS[status];
}

export function formatWorkerCurrency(amount: number | null | undefined, currencyCode = 'EGP'): string {
  if (amount === null || amount === undefined || !Number.isFinite(amount)) return 'غير مسجل';
  return new Intl.NumberFormat('ar-EG', {
    style: 'currency',
    currency: currencyCode,
    maximumFractionDigits: 2
  }).format(amount);
}

export function formatWorkerObservedAt(value: string | null): string {
  if (!value) return 'لم تُسجل مزامنة خارجية';
  return new Intl.DateTimeFormat('ar-EG', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Africa/Cairo'
  }).format(new Date(value));
}
