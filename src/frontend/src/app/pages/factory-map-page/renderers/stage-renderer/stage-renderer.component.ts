import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { ProductionLineLayout, SubStageLayout, MainStageLayout, WorkerLayout } from '../../../../shared/models/factory-visualization.model';

type StageRenderMode = 'stage' | 'worker';
type StageRendererBack = 'line' | 'stage';

@Component({
  selector: 'plp-stage-renderer',
  templateUrl: './stage-renderer.component.html',
  styleUrls: ['./stage-renderer.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StageRendererComponent {
  @Input() line!: ProductionLineLayout;
  @Input() mainStage!: MainStageLayout;
  @Input() mode: StageRenderMode = 'stage';
  @Input() subStage?: SubStageLayout;

  @Output() back = new EventEmitter<StageRendererBack>();
  @Output() subStageSelected = new EventEmitter<string>();

  onBackToLine(): void {
    this.back.emit('line');
  }

  onBackToStage(): void {
    this.back.emit('stage');
  }

  onSubStageSelected(subStageId: string): void {
    this.subStageSelected.emit(subStageId);
  }

  trackBySubStage(_index: number, subStage: SubStageLayout): string {
    return subStage.id;
  }

  trackByWorker(_index: number, worker: WorkerLayout): string {
    return worker.id;
  }
}
