const PRODUCTION_DISPLAY_LABELS: Readonly<Record<string, string>> = Object.freeze({
  SharedPercentage: 'توزيع نسبي مشترك',
  EqualShare: 'توزيع متساوٍ',
  FullRatePerWorker: 'القيمة كاملة لكل عامل',
  FixedAmount: 'قيمة ثابتة لكل عامل',
  Ready: 'جاهزة',
  Staffed: 'مكتملة التسكين',
  NoStaffing: 'دون تسكين',
  AbsentWorker: 'يوجد غياب',
  NoSourceCheckIn: 'دون بصمة مصدر',
  AttendanceUnavailable: 'بيانات الحضور غير متاحة',
  Present: 'حاضر',
  Absent: 'غائب',
  Default: 'تسكين أساسي',
  Permanent: 'تسكين أساسي',
  Temporary: 'نقل مؤقت',
  Replacement: 'عامل بديل',
  FinancialReviewPending: 'تحتاج مراجعة تكلفة'
});

/**
 * Converts backend operational values into stable Arabic product language.
 * Unknown values deliberately fail closed instead of leaking technical enums.
 */
export function productionDisplayLabel(value: string | null | undefined, fallback = 'غير محدد'): string {
  if (!value) return fallback;
  return PRODUCTION_DISPLAY_LABELS[value] ?? fallback;
}
