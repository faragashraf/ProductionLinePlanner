import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { MainStageLayout, ProductionLineLayout } from '../../../../shared/models/factory-visualization.model';

@Component({
  selector: 'plp-line-renderer',
  templateUrl: './line-renderer.component.html',
  styleUrls: ['./line-renderer.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LineRendererComponent {
  @Input() line!: ProductionLineLayout;
  @Output() backToFactory = new EventEmitter<void>();
  @Output() mainStageSelected = new EventEmitter<string>();

  onBackToFactory(): void {
    this.backToFactory.emit();
  }

  onMainStageSelected(stageId: string): void {
    this.mainStageSelected.emit(stageId);
  }

  trackByStage(_index: number, stage: MainStageLayout): string {
    return stage.id;
  }
}
