import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject, catchError, finalize, forkJoin, of, takeUntil } from 'rxjs';
import { FactoryItem, ManufacturingMasterDataApiService, ModelStageItem, ProductModelItem, ProductionLineOption } from '../../core/services/manufacturing-master-data-api.service';
import { ProductionOrder, ProductionCostRecordingApiService, WorkerOption } from '../../core/services/production-cost-recording-api.service';
import { ProductionQuantitiesReportApiService, QuantitiesReportResult, QuantitiesReportRow, QuantitiesReportSortBy, QuantitiesReportView } from '../../core/services/production-quantities-report-api.service';
import { ReportsWorkspaceFilters, ReportsWorkspaceViewOption } from './reports-workspace.models';
import { ReportsWorkspaceStateService } from './reports-workspace-state.service';

interface ReportsColumn {
  key: 'stage' | 'worker' | 'date' | 'status' | 'produced' | 'accepted' | 'rejected' | 'allocated' | 'records' | 'stages' | 'workers';
  label: string;
  sortBy?: QuantitiesReportSortBy;
  numeric?: boolean;
}

type ReportLoadState = 'idle' | 'loading' | 'loaded' | 'empty' | 'error' | 'unauthorized';

@Component({
  selector: 'app-reports-workspace-page',
  templateUrl: './reports-workspace-page.component.html',
  styleUrls: ['./reports-workspace-page.component.scss']
})
export class ReportsWorkspacePageComponent implements OnInit, OnDestroy {
  readonly views: ReportsWorkspaceViewOption[] = [
    { value: 'Details', label: 'التفاصيل التشغيلية', description: 'سجل المرحلة وكمية لقطته.', icon: 'pi-list' },
    { value: 'ByStage', label: 'حسب المرحلة', description: 'ملخص كمية لقطة كل مرحلة.', icon: 'pi-sitemap' },
    { value: 'ByWorker', label: 'حسب العامل', description: 'مشاركة العامل وحصته المخصصة.', icon: 'pi-user' },
    { value: 'WorkerStages', label: 'العامل ← المراحل', description: 'سجل مشاركة العامل عبر المراحل.', icon: 'pi-arrow-left' },
    { value: 'StageWorkers', label: 'المرحلة ← العمال', description: 'سجل عمال كل مرحلة.', icon: 'pi-arrow-right' }
  ];

  filters = this.defaultFilters();
  factories: FactoryItem[] = [];
  productionLines: ProductionLineOption[] = [];
  models: ProductModelItem[] = [];
  stages: ModelStageItem[] = [];
  workers: WorkerOption[] = [];
  orders: ProductionOrder[] = [];
  result: QuantitiesReportResult | null = null;
  loading = false;
  lookupsLoading = false;
  stageLoading = false;
  error = '';
  errorTitle = '';
  loadState: ReportLoadState = 'idle';
  hasAppliedFilters = false;
  lastUpdatedAt: Date | null = null;

  private requestVersion = 0;
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly reports: ProductionQuantitiesReportApiService,
    private readonly masterData: ManufacturingMasterDataApiService,
    private readonly production: ProductionCostRecordingApiService,
    private readonly state: ReportsWorkspaceStateService
  ) {}

  ngOnInit(): void {
    this.filters = this.state.restore(this.defaultFilters());
    this.loadLookups();
    if (this.filters.productModelId) this.loadStages(this.filters.productModelId);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get rows(): QuantitiesReportRow[] {
    return this.result?.rows ?? [];
  }

  get columns(): readonly ReportsColumn[] {
    switch (this.filters.view) {
      case 'ByStage':
        return [
          { key: 'stage', label: 'المرحلة', sortBy: 'StageCode' },
          { key: 'records', label: 'السجلات', sortBy: 'RecordCount', numeric: true },
          { key: 'workers', label: 'العمال', sortBy: 'WorkerCount', numeric: true },
          { key: 'produced', label: 'كمية المرحلة', sortBy: 'ProducedQuantity', numeric: true },
          { key: 'accepted', label: 'المقبول', sortBy: 'AcceptedQuantity', numeric: true },
          { key: 'rejected', label: 'المرفوض', sortBy: 'RejectedQuantity', numeric: true }
        ];
      case 'ByWorker':
        return [
          { key: 'worker', label: 'العامل', sortBy: 'WorkerCode' },
          { key: 'stages', label: 'المراحل', sortBy: 'StageCount', numeric: true },
          { key: 'records', label: 'السجلات', sortBy: 'RecordCount', numeric: true },
          { key: 'allocated', label: 'حصة العامل', sortBy: 'WorkerAllocatedQuantity', numeric: true }
        ];
      case 'WorkerStages':
      case 'StageWorkers':
        return [
          { key: 'stage', label: 'المرحلة', sortBy: 'StageCode' },
          { key: 'worker', label: 'العامل', sortBy: 'WorkerCode' },
          { key: 'date', label: 'التاريخ', sortBy: 'ProductionDate' },
          { key: 'allocated', label: 'حصة العامل', sortBy: 'WorkerAllocatedQuantity', numeric: true },
          { key: 'produced', label: 'كمية المرحلة', numeric: true }
        ];
      default:
        return [
          { key: 'stage', label: 'المرحلة', sortBy: 'StageCode' },
          { key: 'date', label: 'التاريخ', sortBy: 'ProductionDate' },
          { key: 'status', label: 'الحالة' },
          { key: 'produced', label: 'كمية المرحلة', sortBy: 'ProducedQuantity', numeric: true },
          { key: 'accepted', label: 'المقبول', sortBy: 'AcceptedQuantity', numeric: true },
          { key: 'rejected', label: 'المرفوض', sortBy: 'RejectedQuantity', numeric: true },
          { key: 'workers', label: 'العمال', numeric: true }
        ];
    }
  }

  get currentViewLabel(): string {
    return this.views.find(view => view.value === this.filters.view)?.label ?? 'التفاصيل التشغيلية';
  }

  get filterSignature(): string {
    const { page, pageSize, sortBy, sortDirection, ...filter } = this.filters;
    return JSON.stringify(filter);
  }

  onFiltersChange(next: ReportsWorkspaceFilters): void {
    const factoryChanged = next.factoryId !== this.filters.factoryId;
    const lineChanged = next.productionLineId !== this.filters.productionLineId;
    const modelChanged = next.productModelId !== this.filters.productModelId;
    this.filters = {
      ...next,
      productionLineId: factoryChanged ? '' : next.productionLineId,
      productModelId: factoryChanged || lineChanged ? '' : next.productModelId,
      productModelStageId: factoryChanged || lineChanged || modelChanged ? '' : next.productModelStageId
    };
    if (modelChanged) this.loadStages(this.filters.productModelId);
  }

  applyFilters(): void {
    if (!this.filters.from || !this.filters.to) {
      this.loadState = 'error';
      this.errorTitle = 'حدّد فترة التقرير';
      this.error = 'حدد تاريخ البداية وتاريخ النهاية قبل تطبيق التقرير.';
      return;
    }
    this.filters = { ...this.filters, page: 1, sortBy: undefined, sortDirection: 'Ascending' };
    this.state.save(this.filters);
    this.hasAppliedFilters = true;
    this.loadReport();
  }

  resetFilters(): void {
    this.state.clear();
    this.requestVersion++;
    this.filters = this.defaultFilters();
    this.stages = [];
    this.result = null;
    this.lastUpdatedAt = null;
    this.error = '';
    this.errorTitle = '';
    this.loadState = 'idle';
    this.hasAppliedFilters = false;
  }

  changeView(view: QuantitiesReportView): void {
    if (view === this.filters.view) return;
    this.filters = { ...this.filters, view, page: 1, sortBy: undefined, sortDirection: 'Ascending' };
    this.state.save(this.filters);
    if (this.hasAppliedFilters) this.loadReport();
  }

  refresh(): void {
    if (this.hasAppliedFilters) this.loadReport();
    else this.applyFilters();
  }

  onLazyLoad(event: { rows?: number | null; first?: number | null; sortField?: string | string[] | null; sortOrder?: number | null }): void {
    if (!this.hasAppliedFilters) return;
    const pageSize = event.rows ?? this.filters.pageSize;
    const page = Math.floor((event.first ?? 0) / pageSize) + 1;
    const sortBy = this.isSortBy(event.sortField) ? event.sortField : undefined;
    const sortDirection = event.sortOrder === -1 ? 'Descending' : 'Ascending';
    if (page === this.filters.page && pageSize === this.filters.pageSize && sortBy === this.filters.sortBy && sortDirection === this.filters.sortDirection) return;
    this.filters = { ...this.filters, page, pageSize, sortBy, sortDirection };
    this.loadReport();
  }

  rowValue(row: QuantitiesReportRow, column: ReportsColumn['key']): string {
    switch (column) {
      case 'stage': return [row.stageCode, row.stageName].filter(Boolean).join(' · ') || '—';
      case 'worker': return [row.workerCode, row.workerName].filter(Boolean).join(' · ') || '—';
      case 'date': return row.productionDate ?? '—';
      case 'status': return this.statusLabel(row.status);
      case 'produced': return this.quantity(row.producedQuantity);
      case 'accepted': return this.quantity(row.acceptedQuantity);
      case 'rejected': return this.quantity(row.rejectedQuantity);
      case 'allocated': return this.quantity(row.workerAllocatedQuantity);
      case 'records': return this.quantity(row.recordCount);
      case 'stages': return this.quantity(row.stageCount);
      case 'workers': return this.quantity(row.workerCount);
    }
  }

  rowKey(row: QuantitiesReportRow): string {
    return row.source.stageProductionWorkerAllocationId || row.source.stageProductionRecordId || row.source.productModelStageId || row.source.workerId || `${row.stageCode}-${row.workerCode}`;
  }

  quantity(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : new Intl.NumberFormat('ar-EG', { maximumFractionDigits: 3 }).format(value);
  }

  statusLabel(status: string): string {
    return status === 'Approved' ? 'معتمد' : status === 'Draft' ? 'مسودة' : status === 'Cancelled' ? 'ملغى' : status;
  }

  private loadLookups(): void {
    this.lookupsLoading = true;
    forkJoin({
      factories: this.masterData.factories().pipe(catchError(() => of([] as FactoryItem[]))),
      productionLines: this.masterData.allProductionLines().pipe(catchError(() => of([] as ProductionLineOption[]))),
      models: this.masterData.models().pipe(catchError(() => of([] as ProductModelItem[]))),
      workers: this.production.listWorkers().pipe(catchError(() => of([] as WorkerOption[]))),
      orders: this.production.listOrders().pipe(catchError(() => of([] as ProductionOrder[])))
    }).pipe(finalize(() => this.lookupsLoading = false), takeUntil(this.destroy$)).subscribe(lookups => {
      this.factories = lookups.factories.filter(factory => factory.isActive);
      this.productionLines = lookups.productionLines.filter(line => line.isActive);
      this.models = lookups.models.filter(model => model.isActive);
      this.workers = lookups.workers.filter(worker => worker.isActive !== false);
      this.orders = lookups.orders;
    });
  }

  private loadStages(productModelId: string): void {
    if (!productModelId) {
      this.stages = [];
      return;
    }
    this.stageLoading = true;
    this.masterData.modelStages(productModelId)
      .pipe(catchError(() => of([] as ModelStageItem[])), finalize(() => this.stageLoading = false), takeUntil(this.destroy$))
      .subscribe(stages => this.stages = stages.filter(stage => stage.isActive));
  }

  private loadReport(): void {
    if (!this.filters.from || !this.filters.to) return;
    const request = ++this.requestVersion;
    this.loading = true;
    this.error = '';
    this.errorTitle = '';
    this.result = null;
    this.loadState = 'loading';
    this.reports.query(this.filters)
      .pipe(finalize(() => { if (request === this.requestVersion) this.loading = false; }), takeUntil(this.destroy$))
      .subscribe({
        next: result => {
          if (request !== this.requestVersion) return;
          this.result = result;
          this.lastUpdatedAt = new Date();
          this.loadState = result.rows.length ? 'loaded' : 'empty';
        },
        error: (error: unknown) => {
          if (request !== this.requestVersion) return;
          this.result = null;
          const status = error instanceof HttpErrorResponse ? error.status : (error as { status?: number })?.status;
          if (status === 401) {
            this.loadState = 'unauthorized';
            this.errorTitle = 'انتهت جلسة الدخول';
            this.error = 'سجّل الدخول من جديد، ثم أعد محاولة تحميل تقرير الكميات.';
            return;
          }
          if (status === 403) {
            this.loadState = 'unauthorized';
            this.errorTitle = 'لا تملك صلاحية التقرير';
            this.error = 'تحتاج إلى صلاحية عرض تقارير كميات الإنتاج للوصول إلى هذه النتائج.';
            return;
          }
          this.loadState = 'error';
          this.errorTitle = 'تعذر تحميل التقرير';
          this.error = 'تعذر تحميل تقرير الكميات. تحقق من الفلاتر والاتصال ثم أعد المحاولة.';
        }
      });
  }

  private defaultFilters(): ReportsWorkspaceFilters {
    const today = new Date();
    const year = today.getFullYear();
    const month = String(today.getMonth() + 1).padStart(2, '0');
    const date = `${year}-${month}-${String(today.getDate()).padStart(2, '0')}`;
    return {
      from: `${year}-${month}-01`, to: date, factoryId: '', productionLineId: '', productModelId: '', productionOrderId: '',
      productModelStageId: '', workerId: '', status: 'Approved', view: 'Details', page: 1, pageSize: 20,
      sortDirection: 'Ascending'
    };
  }

  private isSortBy(value: unknown): value is QuantitiesReportSortBy {
    return typeof value === 'string' && [
      'ProductionDate', 'StageCode', 'WorkerCode', 'ProducedQuantity', 'AcceptedQuantity', 'RejectedQuantity',
      'WorkerAllocatedQuantity', 'RecordCount', 'WorkerCount', 'StageCount'
    ].includes(value);
  }
}
