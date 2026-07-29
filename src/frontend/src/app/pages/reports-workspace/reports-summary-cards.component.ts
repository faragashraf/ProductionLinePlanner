import { Component, Input } from '@angular/core';
import { FinancialReportSummary } from '../../core/services/production-financial-report-api.service';
import { financialStatusLabel, formatEgp, isFinancialSummary } from './reports-financial-presentation';
import { ReportsWorkspaceSummary } from './reports-workspace.models';

@Component({
  selector: 'app-reports-summary-cards',
  templateUrl: './reports-summary-cards.component.html',
  styleUrls: ['./reports-summary-cards.component.scss']
})
export class ReportsSummaryCardsComponent {
  @Input() summary: ReportsWorkspaceSummary | null = null;
  @Input() financialMode = false;
  @Input() loading = false;

  get financialSummary(): FinancialReportSummary | null {
    return this.summary && isFinancialSummary(this.summary) ? this.summary : null;
  }

  quantity(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 3 }).format(value);
  }

  money(value: number | null | undefined): string {
    return formatEgp(value);
  }

  financialStatus(value: FinancialReportSummary['financialDataStatus'] | null | undefined): string {
    return financialStatusLabel(value);
  }
}
