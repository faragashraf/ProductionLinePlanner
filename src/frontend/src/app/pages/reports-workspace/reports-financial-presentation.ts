import { FinancialReportRow, FinancialReportSummary } from '../../core/services/production-financial-report-api.service';
import { QuantitiesReportRow, QuantitiesReportSummary } from '../../core/services/production-quantities-report-api.service';

export function isFinancialSummary(summary: QuantitiesReportSummary | FinancialReportSummary): summary is FinancialReportSummary {
  return 'financialDataStatus' in summary;
}

export function isFinancialRow(row: QuantitiesReportRow | FinancialReportRow): row is FinancialReportRow {
  return 'financialDataStatus' in row;
}

export function formatEgp(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return new Intl.NumberFormat('ar-EG', {
    style: 'currency',
    currency: 'EGP',
    currencyDisplay: 'code',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2
  }).format(value);
}

export function formatPercentage(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return `${new Intl.NumberFormat('ar-EG', { minimumFractionDigits: 0, maximumFractionDigits: 2 }).format(value)}٪`;
}

export function financialStatusLabel(status: FinancialReportSummary['financialDataStatus'] | null | undefined): string {
  switch (status) {
    case 'Complete': return 'مكتملة';
    case 'Incomplete': return 'بيانات مالية غير مكتملة';
    case 'ReviewRequired': return 'تحتاج مراجعة';
    default: return '—';
  }
}

export function compensationModeLabel(mode: string | null | undefined): string {
  switch (mode) {
    case 'SharedPercentage': return 'توزيع نسبي';
    case 'FullRatePerWorker': return 'سعر كامل لكل عامل';
    case 'FixedAmount': return 'قيمة ثابتة';
    case 'Mixed': return 'طرق احتساب متعددة';
    default: return '—';
  }
}
