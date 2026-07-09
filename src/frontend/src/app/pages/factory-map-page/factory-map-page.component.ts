import { ChangeDetectionStrategy, Component } from '@angular/core';
import {
  FactoryLayout,
  MainStageLayout,
  ProductionLineLayout,
  SubStageLayout
} from '../../shared/models/factory-visualization.model';
import { MockDataService } from '../../core/services/mock-data.service';

type FactoryZoomLevel = 'factory' | 'line' | 'stage' | 'worker';

@Component({
  selector: 'app-factory-map-page',
  templateUrl: './factory-map-page.component.html',
  styleUrls: ['./factory-map-page.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FactoryMapPageComponent {
  readonly layout: FactoryLayout;
  currentZoom: FactoryZoomLevel = 'factory';
  private selectedLineId: string | null = null;
  private selectedMainStageId: string | null = null;
  private selectedSubStageId: string | null = null;

  constructor(private readonly dataService: MockDataService) {
    this.layout = this.dataService.getFactoryLayout();
  }

  get selectedLine(): ProductionLineLayout | undefined {
    if (!this.selectedLineId) {
      return undefined;
    }
    return this.layout.lines.find((line) => line.id === this.selectedLineId);
  }

  get selectedMainStage(): MainStageLayout | undefined {
    if (!this.selectedMainStageId || !this.selectedLine) {
      return undefined;
    }
    return this.selectedLine.stages.find((stage) => stage.id === this.selectedMainStageId);
  }

  get selectedSubStage(): SubStageLayout | undefined {
    if (!this.selectedSubStageId || !this.selectedMainStage) {
      return undefined;
    }
    return this.selectedMainStage.subStages.find((stage) => stage.id === this.selectedSubStageId);
  }

  onLineSelected(lineId: string): void {
    this.selectedLineId = lineId;
    this.selectedMainStageId = null;
    this.selectedSubStageId = null;
    this.currentZoom = 'line';
  }

  onMainStageSelected(stageId: string): void {
    if (!this.selectedLine) {
      return;
    }

    this.selectedMainStageId = stageId;
    this.selectedSubStageId = null;
    this.currentZoom = 'stage';
  }

  onSubStageSelected(subStageId: string): void {
    if (!this.selectedLine || !this.selectedMainStage) {
      return;
    }

    this.selectedSubStageId = subStageId;

    if (this.selectedSubStage) {
      this.currentZoom = 'worker';
    } else {
      this.currentZoom = 'stage';
    }
  }

  showFactory(): void {
    this.currentZoom = 'factory';
    this.selectedLineId = null;
    this.selectedMainStageId = null;
    this.selectedSubStageId = null;
  }

  showLine(): void {
    this.currentZoom = 'line';
    this.selectedMainStageId = null;
    this.selectedSubStageId = null;
  }

  showStage(): void {
    if (!this.selectedMainStage) {
      return;
    }

    this.currentZoom = 'stage';
    this.selectedSubStageId = null;
  }

  onStageBack(target: 'line' | 'stage'): void {
    if (target === 'line') {
      this.showLine();
      return;
    }

    this.showStage();
  }

  trackByLine(_: number, line: ProductionLineLayout): string {
    return line.id;
  }

  trackByStage(_: number, stage: MainStageLayout): string {
    return stage.id;
  }
}
