import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { OperationalReadinessMetrics } from '../../../../shared/models/operational-readiness.model';

@Component({
  selector: 'app-readiness-metrics',
  templateUrl: './readiness-metrics.component.html',
  styleUrls: ['./readiness-metrics.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReadinessMetricsComponent {
  @Input({ required: true }) metrics!: OperationalReadinessMetrics;

  get percentageLabel(): string {
    if (this.metrics.operationalReadinessPercentage !== null) return `${this.metrics.operationalReadinessPercentage}%`;
    return this.metrics.status === 'NoAssignments' ? 'لا توجد تسكينات' : 'غير مؤكدة';
  }
}
