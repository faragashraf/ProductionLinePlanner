import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FactoryReadinessStore } from '../../factory-readiness.store';
import {
  OperationalReadinessDepartment,
  OperationalReadinessFactory,
  OperationalReadinessLine,
  OperationalReadinessStage
} from '../../../../shared/models/operational-readiness.model';

@Component({
  selector: 'app-factory-readiness-map',
  templateUrl: './factory-readiness-map.component.html',
  styleUrls: ['./factory-readiness-map.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FactoryReadinessMapComponent {
  constructor(readonly store: FactoryReadinessStore) {}

  trackFactory(_: number, item: OperationalReadinessFactory): string { return item.id; }
  trackDepartment(_: number, item: OperationalReadinessDepartment): string { return item.id; }
  trackLine(_: number, item: OperationalReadinessLine): string { return item.id; }
  trackStage(_: number, item: OperationalReadinessStage): string { return item.id; }
}
