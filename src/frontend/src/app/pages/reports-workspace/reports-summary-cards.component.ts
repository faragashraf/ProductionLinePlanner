import { Component, Input } from '@angular/core';
import { QuantitiesReportSummary } from '../../core/services/production-quantities-report-api.service';

@Component({
  selector: 'app-reports-summary-cards',
  templateUrl: './reports-summary-cards.component.html',
  styleUrls: ['./reports-summary-cards.component.scss']
})
export class ReportsSummaryCardsComponent {
  @Input() summary: QuantitiesReportSummary | null = null;
  @Input() loading = false;

  quantity(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 3 }).format(value);
  }
}
