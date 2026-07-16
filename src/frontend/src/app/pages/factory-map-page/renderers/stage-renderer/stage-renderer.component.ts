import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { ProductionLineLayout, SubStageLayout, MainStageLayout, WorkerLayout } from '../../../../shared/models/factory-visualization.model';
import {
  AssignmentContextQueryParams,
  assignmentContextToQueryParams,
  createFactoryMapAssignmentContext
} from '../../../../shared/models/assignment-context.model';
import { productionNavigationIconFor } from '../../../../shared/design-system/icons/production-icon-map';

type StageRenderMode = 'stage' | 'worker';
type StageRendererBack = 'line' | 'stage';

@Component({
  selector: 'plp-stage-renderer',
  templateUrl: './stage-renderer.component.html',
  styleUrls: ['./stage-renderer.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StageRendererComponent {
  readonly backIcon = productionNavigationIconFor('back', 'rtl');
  readonly forwardIcon = productionNavigationIconFor('forward', 'rtl');

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

  get mainStageAssignmentQueryParams(): AssignmentContextQueryParams {
    return this.getAssignmentQueryParams();
  }

  get subStageAssignmentQueryParams(): AssignmentContextQueryParams {
    return this.subStage ? this.getAssignmentQueryParams(this.subStage) : this.getAssignmentQueryParams();
  }

  private getAssignmentQueryParams(subStage?: SubStageLayout): AssignmentContextQueryParams {
    return assignmentContextToQueryParams(createFactoryMapAssignmentContext(this.line, this.mainStage, subStage));
  }

  trackBySubStage(_index: number, subStage: SubStageLayout): string {
    return subStage.id;
  }

  trackByWorker(_index: number, worker: WorkerLayout): string {
    return worker.id;
  }
}
