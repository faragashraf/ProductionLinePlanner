import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { OperationalReadinessModelOption } from '../../../../shared/models/operational-readiness.model';

@Component({
  selector: 'app-readiness-model-selector',
  templateUrl: './readiness-model-selector.component.html',
  styleUrls: ['./readiness-model-selector.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReadinessModelSelectorComponent {
  @Input({ required: true }) models: OperationalReadinessModelOption[] = [];
  @Input() selectedModelId: string | null = null;
  @Output() modelSelected = new EventEmitter<string>();

  trackModel(_: number, model: OperationalReadinessModelOption): string { return model.id; }
}
