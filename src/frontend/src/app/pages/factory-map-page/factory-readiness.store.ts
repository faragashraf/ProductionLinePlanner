import { Injectable, computed, signal } from '@angular/core';
import { EMPTY, catchError, finalize } from 'rxjs';
import { OperationalReadinessApiService } from '../../core/services/operational-readiness-api.service';
import {
  OperationalAttendanceState,
  OperationalReadinessDelta,
  OperationalReadinessDepartment,
  OperationalReadinessFactory,
  OperationalReadinessLine,
  OperationalReadinessNodePatch,
  OperationalReadinessSnapshot,
  OperationalReadinessStage,
  OperationalReadinessStages,
  OperationalReadinessWorker,
  OperationalReadinessWorkers,
  ReadinessLevel,
  ReadinessWorkerFilter
} from '../../shared/models/operational-readiness.model';
import {
  READINESS_STAGE_FILTER_DEFINITIONS,
  ReadinessStageFilter,
  ReadinessStageFilterOption,
  compareReadinessStagesByDomainOrder,
  matchesReadinessStageFilter
} from './stage-readiness-filter';

@Injectable()
export class FactoryReadinessStore {
  readonly snapshot = signal<OperationalReadinessSnapshot | null>(null);
  readonly stages = signal<OperationalReadinessStages | null>(null);
  readonly workerResult = signal<OperationalReadinessWorkers | null>(null);
  readonly selectedFactoryId = signal<string | null>(null);
  readonly selectedDepartmentId = signal<string | null>(null);
  readonly selectedLineId = signal<string | null>(null);
  readonly selectedModelId = signal<string | null>(null);
  readonly selectedStageId = signal<string | null>(null);
  readonly workerFilter = signal<ReadinessWorkerFilter>('all');
  readonly selectedStageFilters = signal<ReadinessStageFilter[]>([]);
  readonly loading = signal(false);
  readonly loadingChildren = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly childrenError = signal<string | null>(null);
  readonly realtimeDegraded = signal(false);

  readonly selectedFactory = computed(() => this.snapshot()?.factories.find(item => item.id === this.selectedFactoryId()) ?? null);
  readonly selectedDepartment = computed(() => this.selectedFactory()?.departments.find(item => item.id === this.selectedDepartmentId()) ?? null);
  readonly selectedLine = computed(() => this.selectedDepartment()?.productionLines.find(item => item.id === this.selectedLineId()) ?? null);
  readonly selectedModel = computed(() => this.selectedLine()?.models.find(item => item.id === this.selectedModelId()) ?? null);
  readonly selectedStage = computed(() => this.stages()?.stages.find(item => item.id === this.selectedStageId()) ?? null);
  readonly level = computed<ReadinessLevel>(() => this.selectedStageId() ? 'stage' : this.selectedLineId() ? 'line' : this.selectedDepartmentId() ? 'department' : 'factory');
  readonly visibleWorkers = computed(() => this.filterWorkers(this.workerResult()?.workers ?? [], this.workerFilter()));
  readonly orderedStages = computed(() =>
    [...(this.stages()?.stages ?? [])].sort(compareReadinessStagesByDomainOrder));
  readonly visibleStages = computed(() => {
    const stages = this.orderedStages();
    const selected = this.selectedStageFilters();
    if (selected.length === 0) return stages;
    const selectedSet = new Set(selected);
    return stages.filter(stage => READINESS_STAGE_FILTER_DEFINITIONS.some(definition =>
      selectedSet.has(definition.value) && matchesReadinessStageFilter(stage, definition.value)));
  });
  readonly stageFilterOptions = computed<ReadinessStageFilterOption[]>(() => {
    const stages = this.stages()?.stages ?? [];
    return READINESS_STAGE_FILTER_DEFINITIONS.map(definition => ({
      ...definition,
      count: stages.filter(stage => matchesReadinessStageFilter(stage, definition.value)).length
    }));
  });
  readonly totalStageCount = computed(() => this.stages()?.stages.length ?? 0);
  readonly visibleStageCount = computed(() => this.visibleStages().length);

  private readonly seenDeltaIds = new Set<string>();
  private pendingChildLoads = 0;
  private backgroundSnapshotLoading = false;

  constructor(private readonly api: OperationalReadinessApiService) {}

  loadSnapshot(background = false, forceRefresh = false): void {
    if (background && this.backgroundSnapshotLoading) return;
    if (background) this.backgroundSnapshotLoading = true;
    if (!background || !this.snapshot()) this.loading.set(true);
    this.loadError.set(null);
    this.api.loadSnapshot(undefined, forceRefresh).pipe(
      catchError(() => {
        this.loadError.set('تعذر تحميل خريطة الجاهزية الحالية.');
        return EMPTY;
      }),
      finalize(() => {
        this.loading.set(false);
        if (background) this.backgroundSnapshotLoading = false;
      })
    ).subscribe(snapshot => {
      this.snapshot.set(snapshot);
      this.realtimeDegraded.set(false);
      this.reconcileSelection();
      if (background) {
        const line = this.selectedLine();
        const lineId = this.selectedLineId();
        const stageId = this.selectedStageId();
        const canLoadStages = lineId && ((line?.models.length ?? 0) <= 1 || !!this.selectedModelId());
        if (canLoadStages) {
          this.loadStages(lineId!, stageId, this.selectedModelId(), true);
        }
      }
    });
  }

  openFactory(factory: OperationalReadinessFactory): void {
    this.selectedFactoryId.set(factory.id);
    this.selectedDepartmentId.set(null);
    this.selectedLineId.set(null);
    this.selectedModelId.set(null);
    this.selectedStageId.set(null);
    this.stages.set(null);
    this.workerResult.set(null);
  }

  reset(): void {
    this.selectedFactoryId.set(null);
    this.selectedDepartmentId.set(null);
    this.selectedLineId.set(null);
    this.selectedModelId.set(null);
    this.selectedStageId.set(null);
    this.stages.set(null);
    this.workerResult.set(null);
    this.childrenError.set(null);
  }

  showFactory(): void {
    this.selectedDepartmentId.set(null);
    this.selectedLineId.set(null);
    this.selectedModelId.set(null);
    this.selectedStageId.set(null);
    this.stages.set(null);
    this.workerResult.set(null);
    this.childrenError.set(null);
  }

  openDepartment(department: OperationalReadinessDepartment): void {
    this.selectedDepartmentId.set(department.id);
    this.selectedLineId.set(null);
    this.selectedModelId.set(null);
    this.selectedStageId.set(null);
    this.stages.set(null);
    this.workerResult.set(null);
  }

  openLine(line: OperationalReadinessLine): void {
    this.selectedLineId.set(line.id);
    this.selectedModelId.set(line.models.length === 1 ? line.models[0].id : null);
    this.selectedStageId.set(null);
    this.stages.set(null);
    this.workerResult.set(null);
    if (line.models.length <= 1) this.loadStages(line.id, null, this.selectedModelId());
  }

  selectModel(modelId: string): void {
    const line = this.selectedLine();
    if (!line || !line.models.some(model => model.id === modelId)) return;
    this.selectedModelId.set(modelId);
    this.selectedStageId.set(null);
    this.stages.set(null);
    this.workerResult.set(null);
    this.loadStages(line.id, null, modelId);
  }

  openStage(stage: OperationalReadinessStage): void {
    const lineId = this.selectedLineId();
    if (!lineId) return;
    this.selectedStageId.set(stage.id);
    this.workerFilter.set('all');
    this.loadWorkers(lineId, stage.id);
  }

  goTo(level: ReadinessLevel): void {
    if (level === 'factory') {
      this.showFactory();
    } else if (level === 'department') {
      this.selectedLineId.set(null);
      this.selectedModelId.set(null);
      this.selectedStageId.set(null);
    } else if (level === 'line') {
      this.selectedStageId.set(null);
    }
    if (level !== 'stage') this.workerResult.set(null);
    if (level === 'factory' || level === 'department') this.stages.set(null);
  }

  setWorkerFilter(filter: ReadinessWorkerFilter): void { this.workerFilter.set(filter); }

  setStageFilters(filters: ReadinessStageFilter[] | null | undefined): void {
    const allowed = new Set(READINESS_STAGE_FILTER_DEFINITIONS.map(definition => definition.value));
    this.selectedStageFilters.set([...new Set(filters ?? [])].filter(filter => allowed.has(filter)));
  }

  clearStageFilters(): void { this.selectedStageFilters.set([]); }

  retryChildren(): void {
    const lineId = this.selectedLineId();
    const stageId = this.selectedStageId();
    if (lineId && stageId) this.loadWorkers(lineId, stageId);
    else if (lineId && (this.selectedLine()?.models.length ?? 0) <= 1) this.loadStages(lineId, null, this.selectedModelId());
    else if (lineId && this.selectedModelId()) this.loadStages(lineId, null, this.selectedModelId());
  }

  applyDelta(delta: OperationalReadinessDelta): void {
    if (!delta?.eventId || this.seenDeltaIds.has(delta.eventId)) return;
    this.seenDeltaIds.add(delta.eventId);
    if (this.seenDeltaIds.size > 256) this.seenDeltaIds.delete(this.seenDeltaIds.values().next().value!);

    const current = this.snapshot();
    const freshnessChanged =
      current?.attendanceSync.isTrusted !== delta.attendanceSync.isTrusted ||
      current?.attendanceSync.status !== delta.attendanceSync.status ||
      current?.attendanceSync.lastSuccessfulAtUtc !== delta.attendanceSync.lastSuccessfulAtUtc;

    if (!current || delta.requiresSnapshotReload || current.operationalDate !== delta.operationalDate || freshnessChanged) {
      this.loadSnapshot(true, true);
      return;
    }

    const patches = new Map(delta.nodes.map(patch => [`${patch.nodeType}:${patch.id}`, patch]));
    this.snapshot.set({
      ...current,
      calculatedAtUtc: delta.calculatedAtUtc,
      attendanceSync: delta.attendanceSync,
      factories: current.factories.map(factory => this.patchFactory(factory, patches))
    });
    const stageResult = this.stages();
    if (stageResult) {
      this.stages.set({
        ...stageResult,
        calculatedAtUtc: delta.calculatedAtUtc,
        attendanceSync: delta.attendanceSync,
        stages: stageResult.stages.map(stage => this.patchStage(stage, patches))
      });
    }
    const workerResult = this.workerResult();
    if (workerResult) {
      const relevant = delta.workers.filter(patch => patch.productionLineId === workerResult.productionLineId && patch.stageId === workerResult.stageId);
      let workers = [...workerResult.workers];
      for (const patch of relevant) {
        workers = workers.filter(worker => worker.workerId !== patch.workerId);
        if (!patch.isRemoved && patch.worker) workers.push(patch.worker);
      }
      this.workerResult.set({ ...workerResult, workers: this.sortWorkers(workers), calculatedAtUtc: delta.calculatedAtUtc, attendanceSync: delta.attendanceSync });
    }
  }

  private loadStages(
    lineId: string,
    stageToReload: string | null = null,
    productModelId: string | null = null,
    background = false
  ): void {
    const showLoading = !background || !this.stages();
    if (showLoading) this.beginChildLoad();
    this.childrenError.set(null);
    this.api.loadStages(lineId, productModelId).pipe(
      catchError(() => {
        if (!this.stages()) this.childrenError.set('تعذر تحميل مراحل الخط.');
        return EMPTY;
      }),
      finalize(() => {
        if (showLoading) this.endChildLoad();
      })
    ).subscribe(result => {
      if (this.selectedLineId() !== lineId) return;
      if (productModelId && this.selectedModelId() !== productModelId) return;
      this.stages.set(result);
      if (!stageToReload) return;
      if (result.stages.some(stage => stage.id === stageToReload)) this.loadWorkers(lineId, stageToReload, background);
      else {
        this.selectedStageId.set(null);
        this.workerResult.set(null);
      }
    });
  }

  private loadWorkers(lineId: string, stageId: string, background = false): void {
    const showLoading = !background || !this.workerResult();
    if (showLoading) this.beginChildLoad();
    this.childrenError.set(null);
    this.api.loadWorkers(lineId, stageId).pipe(
      catchError(() => {
        if (!this.workerResult()) this.childrenError.set('تعذر تحميل العمال المسكنين في المرحلة.');
        return EMPTY;
      }),
      finalize(() => {
        if (showLoading) this.endChildLoad();
      })
    ).subscribe(result => {
      if (this.selectedLineId() === lineId && this.selectedStageId() === stageId) {
        this.workerResult.set({ ...result, workers: this.sortWorkers(result.workers) });
      }
    });
  }

  private patchFactory(factory: OperationalReadinessFactory, patches: Map<string, OperationalReadinessNodePatch>): OperationalReadinessFactory {
    const patch = patches.get(`Factory:${factory.id}`);
    return {
      ...factory,
      metrics: patch?.metrics ?? factory.metrics,
      departments: factory.departments.map(department => {
        const departmentPatch = patches.get(`Department:${department.id}`);
        return {
          ...department,
          metrics: departmentPatch?.metrics ?? department.metrics,
          productionLines: department.productionLines.map(line => {
            const linePatch = patches.get(`ProductionLine:${line.id}`);
            return linePatch ? { ...line, metrics: linePatch.metrics, modelNames: linePatch.modelNames } : line;
          })
        };
      })
    };
  }

  private patchStage(stage: OperationalReadinessStage, patches: Map<string, OperationalReadinessNodePatch>): OperationalReadinessStage {
    const patch = patches.get(`Stage:${stage.id}`);
    return patch ? { ...stage, metrics: patch.metrics, modelNames: patch.modelNames } : stage;
  }

  private reconcileSelection(): void {
    if (this.selectedFactoryId() && !this.selectedFactory()) this.reset();
    else if (this.selectedDepartmentId() && !this.selectedDepartment()) this.goTo('department');
    else if (this.selectedLineId() && !this.selectedLine()) this.goTo('department');
    else if (this.selectedModelId() && !this.selectedModel()) {
      this.selectedModelId.set(null);
      this.selectedStageId.set(null);
      this.stages.set(null);
      this.workerResult.set(null);
    }
  }

  private filterWorkers(workers: OperationalReadinessWorker[], filter: ReadinessWorkerFilter): OperationalReadinessWorker[] {
    if (filter === 'all') return workers;
    if (filter === 'present') return workers.filter(worker => worker.attendanceState === 'Present');
    if (filter === 'late') return workers.filter(worker => worker.attendanceState === 'Late');
    if (filter === 'checkedOut') return workers.filter(worker => !!worker.checkOutAtUtc);
    return workers.filter(worker => worker.attendanceState === 'Absent' || worker.attendanceState === 'NotCheckedIn');
  }

  private sortWorkers(workers: OperationalReadinessWorker[]): OperationalReadinessWorker[] {
    const priority: Record<OperationalAttendanceState, number> = { Absent: 0, NotCheckedIn: 1, CheckedOut: 2, Unknown: 3, Late: 4, Present: 5 };
    return [...workers].sort((first, second) => priority[first.attendanceState] - priority[second.attendanceState]
      || first.fullName.localeCompare(second.fullName, 'ar'));
  }

  private beginChildLoad(): void {
    this.pendingChildLoads++;
    this.loadingChildren.set(true);
  }

  private endChildLoad(): void {
    this.pendingChildLoads = Math.max(0, this.pendingChildLoads - 1);
    this.loadingChildren.set(this.pendingChildLoads > 0);
  }
}
