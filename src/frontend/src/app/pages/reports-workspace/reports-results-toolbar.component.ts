import { Component, EventEmitter, Input, Output } from '@angular/core';
import { QuantitiesReportView } from '../../core/services/production-quantities-report-api.service';
import { ReportsWorkspaceViewOption } from './reports-workspace.models';

@Component({
  selector: 'app-reports-results-toolbar',
  templateUrl: './reports-results-toolbar.component.html',
  styleUrls: ['./reports-results-toolbar.component.scss']
})
export class ReportsResultsToolbarComponent {
  @Input() views: ReportsWorkspaceViewOption[] = [];
  @Input() selectedView!: QuantitiesReportView;
  @Input() loading = false;
  @Output() viewChange = new EventEmitter<QuantitiesReportView>();
  @Output() refresh = new EventEmitter<void>();
}
