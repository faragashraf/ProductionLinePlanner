import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { TagModule } from 'primeng/tag';
import { productionVisualToneFor, resolveProductionStatus } from '../design-system/status/production-status-map';

/** Displays a status using the centralized Product Status Language. */
@Component({
  selector: 'plp-status-badge',
  standalone: true,
  imports: [TagModule],
  template: `
    <p-tag
      [value]="label || statusMeta.labelAr"
      [icon]="showIcon ? statusMeta.icon : undefined"
      [severity]="tone.primeSeverity"
      styleClass="plp-status-badge"
    ></p-tag>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpStatusBadgeComponent {
  @Input() status: string | null | undefined;
  @Input() label = '';
  @Input() showIcon = true;

  get statusMeta() {
    return resolveProductionStatus(this.status);
  }

  get tone() {
    return productionVisualToneFor(this.statusMeta.tone);
  }
}
