import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'plp-statistic-card',
  templateUrl: './statistic-card.component.html',
  styleUrls: ['./statistic-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StatisticCardComponent {
  @Input() title = '';
  @Input() value = '—';
  @Input() helper = '';
  @Input() icon = '';
}
