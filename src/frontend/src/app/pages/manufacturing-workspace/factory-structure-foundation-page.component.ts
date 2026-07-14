import { Component, OnDestroy, OnInit } from '@angular/core';
import { EMPTY, Observable, Subject, catchError, distinctUntilChanged, forkJoin, switchMap, takeUntil, tap, finalize } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { AssignmentsApiService, AssignmentWorker } from '../../core/services/assignments-api.service';
import {
  FactoryItem,
  MainStageOption,
  ManufacturingMasterDataApiService,
  ProductionLineOption,
  SubStageOption
} from '../../core/services/manufacturing-master-data-api.service';
import { WorkersApiService } from '../../core/services/workers-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { WorkerPageItem } from '../../shared/models/worker.model';

interface FactoryDraft {
  id: string;
  name: string;
  code: string;
  location: string;
}

interface LineDraft {
  id: string;
  factoryId: string;
  name: string;
  lineCode: string;
  sequenceOrder: number;
}

interface MainStageDraft {
  id: string;
  productionLineId: string;
  name: string;
  sequenceOrder: number;
  isCritical: boolean;
}

interface SubStageDraft {
  id: string;
  mainStageId: string;
  code: string;
  name: string;
  defaultOrder: number;
  capacity: number;
}

type FactoryStructureFormId = 'factory' | 'line' | 'main-stage' | 'sub-stage' | 'assignment';

@Component({
  selector: 'app-factory-structure-foundation-page',
  templateUrl: './factory-structure-foundation-page.component.html',
  styleUrls: ['./factory-structure-foundation-page.component.scss']
})
export class FactoryStructureFoundationPageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;

  factories: FactoryItem[] = [];
  lines: ProductionLineOption[] = [];
  mainStages: MainStageOption[] = [];
  subStages: SubStageOption[] = [];
  workers: WorkerPageItem[] = [];
  assignedWorkers: AssignmentWorker[] = [];

  selectedFactoryId = '';
  selectedLineId = '';
  selectedMainStageId = '';
  selectedSubStageId = '';
  selectedWorkerId = '';
  searchTerm = '';
  isLoading = false;
  isSaving = false;
  hasLoadedOnce = false;
  hasError = false;
  errorMessage = 'تعذر تحميل بنية المصنع، يرجى المحاولة مرة أخرى.';
  successMessage = '';
  activeForm: FactoryStructureFormId | null = null;

  factoryDraft: FactoryDraft = this.emptyFactoryDraft();
  lineDraft: LineDraft = this.emptyLineDraft();
  mainStageDraft: MainStageDraft = this.emptyMainStageDraft();
  subStageDraft: SubStageDraft = this.emptySubStageDraft();
  private mainStagesRequestId = 0;
  private subStagesRequestId = 0;
  private assignedWorkersRequestId = 0;
  private readonly selectedLineChanges$ = new Subject<string>();
  private readonly selectedMainStageChanges$ = new Subject<string>();
  private readonly selectedSubStageChanges$ = new Subject<string>();
  private readonly reloadAssignedWorkers$ = new Subject<string>();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly masterDataApi: ManufacturingMasterDataApiService,
    private readonly assignmentsApi: AssignmentsApiService,
    private readonly workersApi: WorkersApiService,
    private readonly permissionService: PermissionService
  ) {}

  ngOnInit(): void {
    this.bindSelectionStreams();
    this.reload();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get filteredFactories(): FactoryItem[] {
    const search = this.normalizedSearch;
    return this.factories.filter(item =>
      search.length === 0 ||
      item.name.toLowerCase().includes(search) ||
      item.code.toLowerCase().includes(search)
    );
  }

  get visibleLines(): ProductionLineOption[] {
    return this.selectedFactoryId
      ? this.lines.filter(item => item.factoryId === this.selectedFactoryId)
      : [];
  }

  get visibleMainStages(): MainStageOption[] {
    return this.selectedLineId
      ? this.mainStages.filter(item => item.productionLineId === this.selectedLineId)
      : [];
  }

  get visibleSubStages(): SubStageOption[] {
    return this.selectedMainStageId
      ? this.subStages.filter(item => item.mainStageId === this.selectedMainStageId)
      : [];
  }

  get isEmpty(): boolean {
    return !this.isLoading && !this.hasError && this.factories.length === 0 && this.lines.length === 0;
  }

  get selectedWorkerExistsInOptions(): boolean {
    return this.workers.some(worker => worker.id === this.selectedWorkerId);
  }

  get canManage(): boolean {
    return this.permissionService.hasPermission(this.permissions.factoryStructure.manage);
  }

  onSearch(event: Event): void {
    this.searchTerm = ((event.target as HTMLInputElement).value ?? '').trim();
  }

  onClearSearch(): void {
    this.searchTerm = '';
  }

  onFormExpandedChange(formId: FactoryStructureFormId, expanded: boolean): void {
    this.activeForm = expanded ? formId : null;
  }

  reload(): void {
    this.isLoading = true;
    this.hasError = false;
    this.successMessage = '';

    forkJoin({
      factories: this.masterDataApi.factories(),
      lines: this.masterDataApi.allProductionLines()
    })
      .pipe(finalize(() => {
        this.isLoading = false;
        this.hasLoadedOnce = true;
      }))
      .subscribe({
        next: data => {
          this.factories = data.factories;
          this.lines = data.lines;
          this.resetSelectionsForReload();
        },
        error: error => {
          this.hasError = true;
          this.errorMessage = this.extractErrorMessage(error);
        }
      });
  }

  selectFactory(id: string): void {
    this.selectedFactoryId = id;
    this.clearLineAndDownstream();
    this.lineDraft.factoryId = id;
  }

  selectLine(id: string): void {
    if (id === this.selectedLineId) {
      return;
    }

    this.selectedLineId = id;
    this.clearMainStageAndDownstream();
    this.mainStages = [];
    this.mainStageDraft.productionLineId = id;
    this.selectedLineChanges$.next(id);
  }

  selectMainStage(id: string): void {
    if (id === this.selectedMainStageId) {
      return;
    }

    this.selectedMainStageId = id;
    this.clearSubStageAndDownstream();
    this.subStages = [];
    this.subStageDraft.mainStageId = id;
    this.selectedMainStageChanges$.next(id);
  }

  selectSubStage(id: string): void {
    if (id === this.selectedSubStageId) {
      return;
    }

    this.selectedSubStageId = id;
    this.assignedWorkers = [];
    this.selectedSubStageChanges$.next(id);
  }

  editFactory(item: FactoryItem): void {
    this.factoryDraft = {
      id: item.id,
      name: item.name,
      code: item.code,
      location: item.location ?? ''
    };
    this.activeForm = 'factory';
  }

  saveFactory(): void {
    if (!this.factoryDraft.name.trim() || !this.factoryDraft.code.trim()) {
      this.errorMessage = 'اسم المصنع وكوده مطلوبان.';
      this.hasError = true;
      return;
    }

    const payload = {
      name: this.factoryDraft.name.trim(),
      code: this.factoryDraft.code.trim(),
      location: this.factoryDraft.location.trim() || null,
      isActive: true
    };
    const request = this.factoryDraft.id
      ? this.masterDataApi.updateFactory(this.factoryDraft.id, { name: payload.name, location: payload.location, isActive: true })
      : this.masterDataApi.createFactory(payload);

    this.save(request, () => {
      this.factoryDraft = this.emptyFactoryDraft();
      this.activeForm = null;
    });
  }

  editLine(item: ProductionLineOption): void {
    this.lineDraft = {
      id: item.id,
      factoryId: item.factoryId,
      name: item.name,
      lineCode: item.lineCode ?? '',
      sequenceOrder: item.sequenceOrder
    };
    this.activeForm = 'line';
  }

  saveLine(): void {
    if (!this.lineDraft.factoryId || !this.lineDraft.name.trim()) {
      this.errorMessage = 'المصنع واسم الخط مطلوبان.';
      this.hasError = true;
      return;
    }

    const payload = {
      factoryId: this.lineDraft.factoryId,
      name: this.lineDraft.name.trim(),
      lineCode: this.lineDraft.lineCode.trim() || null,
      sequenceOrder: Number(this.lineDraft.sequenceOrder) || 0,
      isActive: true
    };
    const request = this.lineDraft.id
      ? this.masterDataApi.updateProductionLine(this.lineDraft.id, {
        name: payload.name,
        lineCode: payload.lineCode,
        sequenceOrder: payload.sequenceOrder,
        isActive: true
      })
      : this.masterDataApi.createProductionLine(payload);

    this.save(request, () => {
      this.lineDraft = this.emptyLineDraft();
      this.lineDraft.factoryId = this.selectedFactoryId;
      this.activeForm = null;
    });
  }

  setLineActive(item: ProductionLineOption, isActive: boolean): void {
    this.save(this.masterDataApi.updateProductionLine(item.id, { isActive }));
  }

  editMainStage(item: MainStageOption): void {
    this.mainStageDraft = {
      id: item.id,
      productionLineId: item.productionLineId,
      name: item.name,
      sequenceOrder: item.sequenceOrder,
      isCritical: item.isCritical
    };
    this.activeForm = 'main-stage';
  }

  saveMainStage(): void {
    if (!this.mainStageDraft.productionLineId || !this.mainStageDraft.name.trim()) {
      this.errorMessage = 'خط الإنتاج واسم المرحلة مطلوبان.';
      this.hasError = true;
      return;
    }

    const payload = {
      productionLineId: this.mainStageDraft.productionLineId,
      name: this.mainStageDraft.name.trim(),
      sequenceOrder: Number(this.mainStageDraft.sequenceOrder) || 0,
      isCritical: this.mainStageDraft.isCritical,
      isActive: true
    };
    const request = this.mainStageDraft.id
      ? this.masterDataApi.updateMain(this.mainStageDraft.id, {
        name: payload.name,
        sequenceOrder: payload.sequenceOrder,
        isCritical: payload.isCritical,
        isActive: true
      })
      : this.masterDataApi.createMain(payload);

    this.save(request, () => {
      this.mainStageDraft = this.emptyMainStageDraft();
      this.mainStageDraft.productionLineId = this.selectedLineId;
      this.activeForm = null;
    });
  }

  setMainStageActive(item: MainStageOption, isActive: boolean): void {
    this.save(this.masterDataApi.updateMain(item.id, { isActive }));
  }

  editSubStage(item: SubStageOption): void {
    this.subStageDraft = {
      id: item.id,
      mainStageId: item.mainStageId,
      code: item.code,
      name: item.name,
      defaultOrder: item.sequenceOrder,
      capacity: item.capacity
    };
    this.activeForm = 'sub-stage';
  }

  saveSubStage(): void {
    if (!this.subStageDraft.mainStageId || !this.subStageDraft.code.trim() || !this.subStageDraft.name.trim()) {
      this.errorMessage = 'المرحلة الرئيسية وكود واسم المرحلة الفرعية مطلوبة.';
      this.hasError = true;
      return;
    }

    const payload = {
      mainStageId: this.subStageDraft.mainStageId,
      code: this.subStageDraft.code.trim(),
      name: this.subStageDraft.name.trim(),
      defaultOrder: Number(this.subStageDraft.defaultOrder) || 1,
      capacity: Number(this.subStageDraft.capacity) || 0,
      isActive: true
    };
    const request = this.subStageDraft.id
      ? this.masterDataApi.updateSub(this.subStageDraft.id, {
        code: payload.code,
        name: payload.name,
        defaultOrder: payload.defaultOrder,
        capacity: payload.capacity,
        isActive: true
      })
      : this.masterDataApi.createSub(payload);

    this.save(request, () => {
      this.subStageDraft = this.emptySubStageDraft();
      this.subStageDraft.mainStageId = this.selectedMainStageId;
      this.activeForm = null;
    });
  }

  setSubStageActive(item: SubStageOption, isActive: boolean): void {
    this.save(this.masterDataApi.updateSub(item.id, { isActive }));
  }

  assignWorker(): void {
    if (!this.selectedSubStageId) {
      this.errorMessage = 'اختر العامل والمرحلة الفرعية أولاً.';
      this.hasError = true;
      return;
    }

    if (!this.selectedWorkerId) {
      this.errorMessage = 'اختر العامل والمرحلة الفرعية أولاً.';
      this.hasError = true;
      return;
    }

    if (!this.selectedWorkerExistsInOptions) {
      this.errorMessage = 'العامل المحدد غير متاح لهذه المرحلة الفرعية.';
      this.hasError = true;
      return;
    }

    this.save(
      this.assignmentsApi.createFactoryStructureDefaultAssignment({
        workerId: this.selectedWorkerId,
        subStageId: this.selectedSubStageId,
        reason: 'Factory structure assignment'
      }),
      () => {
        this.selectedWorkerId = '';
        this.reloadAssignedWorkers$.next(this.selectedSubStageId);
        this.activeForm = null;
      },
      false
    );
  }

  factoryName(id: string): string {
    return this.factories.find(item => item.id === id)?.name ?? '-';
  }

  lineName(id: string): string {
    return this.lines.find(item => item.id === id)?.name ?? '-';
  }

  mainStageName(id: string): string {
    return this.mainStages.find(item => item.id === id)?.name ?? '-';
  }

  private get normalizedSearch(): string {
    return this.searchTerm.trim().toLowerCase();
  }

  private resetSelectionsForReload(): void {
    if (!this.selectedFactoryId || !this.factories.some(item => item.id === this.selectedFactoryId)) {
      this.selectedFactoryId = this.factories[0]?.id ?? '';
    }

    this.lineDraft.factoryId = this.selectedFactoryId;
    this.clearLineAndDownstream();
  }

  private bindSelectionStreams(): void {
    this.selectedLineChanges$
      .pipe(
        distinctUntilChanged(),
        switchMap(lineId => {
          if (!lineId) {
            this.mainStages = [];
            return EMPTY;
          }

          const requestId = ++this.mainStagesRequestId;
          return this.masterDataApi.mainStagesForLine(lineId).pipe(
            tap(stages => {
              if (requestId === this.mainStagesRequestId && this.selectedLineId === lineId) {
                this.mainStages = stages;
              }
            }),
            catchError(error => {
              if (requestId === this.mainStagesRequestId && this.selectedLineId === lineId) {
                this.mainStages = [];
                this.hasError = true;
                this.errorMessage = this.extractErrorMessage(error);
              }
              return EMPTY;
            })
          );
        }),
        takeUntil(this.destroy$)
      )
      .subscribe();

    this.selectedMainStageChanges$
      .pipe(
        distinctUntilChanged(),
        switchMap(mainStageId => {
          if (!mainStageId) {
            this.subStages = [];
            return EMPTY;
          }

          const requestId = ++this.subStagesRequestId;
          return this.masterDataApi.subStagesForMainStage(mainStageId).pipe(
            tap(stages => {
              if (requestId === this.subStagesRequestId && this.selectedMainStageId === mainStageId) {
                this.subStages = stages;
              }
            }),
            catchError(error => {
              if (requestId === this.subStagesRequestId && this.selectedMainStageId === mainStageId) {
                this.subStages = [];
                this.hasError = true;
                this.errorMessage = this.extractErrorMessage(error);
              }
              return EMPTY;
            })
          );
        }),
        takeUntil(this.destroy$)
      )
      .subscribe();

    this.selectedSubStageChanges$
      .pipe(
        distinctUntilChanged(),
        switchMap(subStageId => this.loadWorkersForSubStage(subStageId)),
        takeUntil(this.destroy$)
      )
      .subscribe();

    this.selectedSubStageChanges$
      .pipe(
        distinctUntilChanged(),
        switchMap(subStageId => this.loadAssignedWorkersForSubStage(subStageId)),
        takeUntil(this.destroy$)
      )
      .subscribe();

    this.reloadAssignedWorkers$
      .pipe(
        switchMap(subStageId => this.loadAssignedWorkersForSubStage(subStageId)),
        takeUntil(this.destroy$)
      )
      .subscribe();
  }

  private loadAssignedWorkersForSubStage(subStageId: string): Observable<unknown> {
    if (!subStageId) {
      this.assignedWorkers = [];
      return EMPTY;
    }

    const requestId = ++this.assignedWorkersRequestId;
    return this.assignmentsApi.getFactoryStructureSubStageWorkers(subStageId).pipe(
      tap(data => {
        if (requestId === this.assignedWorkersRequestId && this.selectedSubStageId === subStageId) {
          this.assignedWorkers = data.workers;
        }
      }),
      catchError(error => {
        if (requestId === this.assignedWorkersRequestId && this.selectedSubStageId === subStageId) {
          this.assignedWorkers = [];
          this.hasError = true;
          this.errorMessage = this.extractErrorMessage(error);
        }
        return EMPTY;
      })
    );
  }

  private loadWorkersForSubStage(subStageId: string): Observable<unknown> {
    if (!subStageId) {
      this.workers = [];
      return EMPTY;
    }

    return this.workersApi.loadFactoryStructureEligibleWorkers(subStageId).pipe(
      tap(workers => {
        if (this.selectedSubStageId === subStageId) {
          this.workers = workers;
        }
      }),
      catchError(error => {
        if (this.selectedSubStageId === subStageId) {
          this.workers = [];
          this.hasError = true;
          this.errorMessage = this.extractErrorMessage(error);
        }
        return EMPTY;
      })
    );
  }

  private clearLineAndDownstream(): void {
    this.selectedLineId = '';
    this.mainStages = [];
    this.mainStageDraft = this.emptyMainStageDraft();
    this.clearMainStageAndDownstream();
    this.mainStagesRequestId++;
  }

  private clearMainStageAndDownstream(): void {
    this.selectedMainStageId = '';
    this.subStages = [];
    this.subStageDraft = this.emptySubStageDraft();
    this.clearSubStageAndDownstream();
    this.subStagesRequestId++;
    this.selectedMainStageChanges$.next('');
  }

  private clearSubStageAndDownstream(): void {
    this.selectedSubStageId = '';
    this.selectedWorkerId = '';
    this.assignedWorkers = [];
    this.workers = [];
    this.assignedWorkersRequestId++;
  }

  private save(request: Observable<unknown>, success?: () => void, reload = true): void {
    this.isSaving = true;
    this.hasError = false;
    this.successMessage = '';
    request.subscribe({
      next: () => {
        success?.();
        this.successMessage = 'تم حفظ التغيير.';
        this.isSaving = false;
        if (reload) {
          this.reload();
        }
      },
      error: error => {
        this.isSaving = false;
        this.hasError = true;
        this.errorMessage = this.extractErrorMessage(error);
      }
    });
  }

  private emptyFactoryDraft(): FactoryDraft {
    return { id: '', name: '', code: '', location: '' };
  }

  private emptyLineDraft(): LineDraft {
    return { id: '', factoryId: '', name: '', lineCode: '', sequenceOrder: 0 };
  }

  private emptyMainStageDraft(): MainStageDraft {
    return { id: '', productionLineId: '', name: '', sequenceOrder: 0, isCritical: false };
  }

  private emptySubStageDraft(): SubStageDraft {
    return { id: '', mainStageId: '', code: '', name: '', defaultOrder: 1, capacity: 0 };
  }

  private extractErrorMessage(error: unknown): string {
    return error instanceof Error && error.message.length > 0
      ? error.message
      : 'حدث خطأ غير متوقع أثناء حفظ أو تحميل بنية المصنع.';
  }
}
