import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

type KpiTrend = 'up' | 'down' | 'stable';

@Component({
  selector: 'plp-kpi-card',
  templateUrl: './kpi-card.component.html',
  styleUrls: ['./kpi-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class KpiCardComponent {
  @Input() title = '';
  @Input() value = '0';
  @Input() icon = '';
  @Input() trend: KpiTrend = 'stable';
  @Input() trendLabel = '';

  get trendClass(): string {
    return `plp-kpi-card__trend--${this.trend}`;
  }

  get trendIcon(): string {
    if (this.trend === 'up') {
      return 'pi-arrow-up';
    }
    if (this.trend === 'down') {
      return 'pi-arrow-down';
    }
    return 'pi-minus';
  }
}
