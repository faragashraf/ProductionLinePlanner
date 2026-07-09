import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { clampPercent } from '../../utils/number.utils';
import { FactoryStatus, deriveStatusFromReadiness, resolveFactoryStatus } from '../../models/factory-status.model';

@Component({
  selector: 'plp-completion-bar',
  templateUrl: './completion-bar.component.html',
  styleUrls: ['./completion-bar.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CompletionBarComponent {
  @Input() value = 0;
  @Input() label = '';
  @Input() status: FactoryStatus | string = 'info';

  get percent(): number {
    return clampPercent(this.value);
  }

  get toneClass(): string {
    return `plp-completion-bar--${resolveFactoryStatus(this.status || deriveStatusFromReadiness(this.percent)).toneClass}`;
  }
}
