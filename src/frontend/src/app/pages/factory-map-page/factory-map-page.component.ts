import { Component } from '@angular/core';
import { FactoryMapLine, MockDataService, FactorySubStage } from '../../core/services/mock-data.service';

@Component({
  selector: 'app-factory-map-page',
  templateUrl: './factory-map-page.component.html',
  styleUrls: ['./factory-map-page.component.scss']
})
export class FactoryMapPageComponent {
  lines: FactoryMapLine[] = [];
  readonly totalWorkers: number;

  constructor(private readonly dataService: MockDataService) {
    this.lines = this.dataService.getFactoryMapData();
    this.totalWorkers = this.lines.reduce((sum, line) => {
      return sum + line.stages.reduce((workerSum, stage) => workerSum + stage.workersRequired, 0);
    }, 0);
  }

  getReadinessClass(percent: number): string {
    if (percent >= 85) {
      return 'line-green';
    }
    if (percent >= 60) {
      return 'line-yellow';
    }
    return 'line-red';
  }

  getReadinessText(percent: number): string {
    if (percent >= 85) {
      return 'ممتاز';
    }
    if (percent >= 60) {
      return 'متوسط';
    }
    return 'يحتاج متابعة';
  }

  getStageRatio(stage: FactorySubStage): number {
    if (stage.workersRequired === 0) {
      return 0;
    }
    const ratio = stage.workersCurrent / stage.workersRequired * 100;
    return Math.min(100, Math.max(0, Math.round(ratio)));
  }

  getStageClass(stage: FactorySubStage): string {
    const ratio = this.getStageRatio(stage);
    if (ratio >= 100) {
      return 'stage-green';
    }
    if (ratio >= 80) {
      return 'stage-yellow';
    }
    return 'stage-red';
  }

  getStageStatus(stage: FactorySubStage): string {
    const ratio = this.getStageRatio(stage);
    if (ratio >= 100) {
      return 'مكتمل';
    }
    if (ratio >= 80) {
      return 'تغطية جيدة';
    }
    return 'عجز';
  }
}
