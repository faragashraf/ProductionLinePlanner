import { Component, Input } from '@angular/core';
import { ReportsWorkspaceSummary } from './reports-workspace.models';

@Component({
  selector: 'app-reports-summary-cards',
  templateUrl: './reports-summary-cards.component.html',
  styleUrls: ['./reports-summary-cards.component.scss']
})
export class ReportsSummaryCardsComponent {
  @Input() summary: ReportsWorkspaceSummary | null = null;
  @Input() loading = false;

  quantity(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 3 }).format(value);
  }
}
