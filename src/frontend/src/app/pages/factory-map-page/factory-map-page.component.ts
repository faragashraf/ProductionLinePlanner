import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import {
  FactoryLayout,
  MainStageLayout,
  ProductionLineLayout,
  SubStageLayout
} from '../../shared/models/factory-visualization.model';
import { createEmptyFactoryLayout, FactoryMapApiService } from '../../core/services/factory-map-api.service';
import { AssignmentsApiService } from '../../core/services/assignments-api.service';
import { catchError, finalize, of, Subject, takeUntil } from 'rxjs';

type FactoryZoomLevel = 'factory' | 'line' | 'stage' | 'worker';

@Component({
  selector: 'app-factory-map-page',
  templateUrl: './factory-map-page.component.html',
  styleUrls: ['./factory-map-page.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FactoryMapPageComponent implements OnInit, OnDestroy {
  isLoading = true;
  showFallbackWarning = false;
  isBackendDataIncomplete = false;
  fallbackWarningMessage: string | null = null;
  layout: FactoryLayout = createEmptyFactoryLayout();
  currentZoom: FactoryZoomLevel = 'factory';
  isWorkerLoading = false;
  workerLoadError = false;
  private selectedLineId: string | null = null;
  private selectedMainStageId: string | null = null;
  private selectedSubStageId: string | null = null;

  private readonly destroy$ = new Subject<void>();
  private readonly backendFailureWarning = 'لا يمكن الاتصال بالخادم حالياً. حاول إعادة تحميل خريطة المصنع.';
  private readonly backendIncompleteWarning = 'لا توجد بنية مصنع مكتملة متاحة من الخادم حالياً.';

  constructor(
    private readonly factoryMapApiService: FactoryMapApiService,
    private readonly assignmentsApiService: AssignmentsApiService,
    private readonly changeDetectorRef: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadFactoryMapData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  retry(): void {
    this.showFactory();
    this.loadFactoryMapData();
  }

  private loadFactoryMapData(): void {
    this.isLoading = true;
    this.factoryMapApiService
      .loadFactoryMapData()
      .pipe(
        catchError(() => of({
          layout: createEmptyFactoryLayout(),
          hasBackendData: false,
          hasUsableBackendData: false,
          fallbackReason: 'connection' as const
        })),
        takeUntil(this.destroy$),
        finalize(() => {
          this.isLoading = false;
          this.changeDetectorRef.markForCheck();
        })
      )
      .subscribe((data) => {
        if (!data.hasBackendData || !data.hasUsableBackendData) {
          this.layout = data.layout;
          this.showFallbackWarning = true;
          this.isBackendDataIncomplete = data.fallbackReason !== 'connection';
          this.fallbackWarningMessage = data.fallbackReason === 'connection'
            ? this.backendFailureWarning
            : this.backendIncompleteWarning;
          return;
        }

        this.layout = data.layout;
        this.showFallbackWarning = false;
        this.isBackendDataIncomplete = false;
        this.fallbackWarningMessage = null;
      });
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
      this.loadSubStageWorkers(this.selectedSubStage);
    } else {
      this.currentZoom = 'stage';
    }
  }

  private loadSubStageWorkers(subStage: SubStageLayout): void {
    this.isWorkerLoading = true;
    this.workerLoadError = false;
    this.assignmentsApiService
      .getFactoryStructureSubStageWorkers(subStage.id)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => {
          this.isWorkerLoading = false;
          this.changeDetectorRef.markForCheck();
        })
      )
      .subscribe({
        next: (data) => {
          this.replaceSubStageWorkers(
            subStage.id,
            data.workers.map((worker) => ({
              id: worker.id,
              fullName: worker.fullName,
              code: worker.code,
              status: 'info',
              assignmentType: worker.assignmentType || 'غير محدد',
              lastActivity: 'التسكين الحالي'
            }))
          );
        },
        error: () => {
          this.workerLoadError = true;
        }
      });
  }

  private replaceSubStageWorkers(subStageId: string, workers: SubStageLayout['workers']): void {
    this.layout = {
      ...this.layout,
      lines: this.layout.lines.map((line) => ({
        ...line,
        stages: line.stages.map((stage) => ({
          ...stage,
          subStages: stage.subStages.map((subStage) => subStage.id === subStageId
            ? { ...subStage, workers }
            : subStage)
        }))
      }))
    };
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
