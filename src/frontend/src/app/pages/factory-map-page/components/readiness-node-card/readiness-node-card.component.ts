import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { OperationalReadinessMetrics, ReadinessNodeType } from '../../../../shared/models/operational-readiness.model';

export interface ReadinessCardNode {
  id: string;
  name: string;
  code?: string | null;
  metrics: OperationalReadinessMetrics;
  modelNames?: string[];
}
@Component({
  selector: 'app-readiness-node-card',
  templateUrl: './readiness-node-card.component.html',
  styleUrls: ['./readiness-node-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReadinessNodeCardComponent {
  @Input({ required: true }) node!: ReadinessCardNode;
  @Input({ required: true }) nodeType!: ReadinessNodeType;
  @Input() featured = false;
  @Input() interactive = true;
  @Output() activate = new EventEmitter<void>();

  get icon(): string {
    return ({ Factory: 'pi-building', Department: 'pi-sitemap', ProductionLine: 'pi-cog', Stage: 'pi-box' } as const)[this.nodeType];
  }

  get childLabel(): string {
    if (this.nodeType === 'ProductionLine' && this.node.modelNames?.length) return 'موديل';
    return ({ Factory: 'قسم', Department: 'خط', ProductionLine: 'مرحلة', Stage: 'عامل' } as const)[this.nodeType];
  }
}
