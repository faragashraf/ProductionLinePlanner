import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { FactoryStatus } from '../../models/factory-status.model';

export interface TimelineItem {
  time?: string;
  title: string;
  details?: string;
  status?: FactoryStatus | string;
}

@Component({
  selector: 'plp-timeline-card',
  templateUrl: './timeline-card.component.html',
  styleUrls: ['./timeline-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TimelineCardComponent {
  @Input() title = 'الخط الزمني';
  @Input() items: TimelineItem[] = [];
}
