import { Component, OnDestroy, OnInit } from '@angular/core';
import { TableLazyLoadEvent } from 'primeng/table';
import { Subject, catchError, debounceTime, distinctUntilChanged, finalize, map, of, switchMap, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { AttendanceApiService } from '../../core/services/attendance-api.service';
import { AttendanceWorkforceApiService, WorkforceDetail, WorkforcePage, WorkforceQuery, WorkforceRow } from '../../core/services/attendance-workforce-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { FactoryItem, MainStageOption, ManufacturingMasterDataApiService, ProductionLineOption, SubStageOption } from '../../core/services/manufacturing-master-data-api.service';

type LoadRequest = WorkforceQuery & { force?: boolean };

@Component({ selector: 'app-attendance-workforce-page', templateUrl: './attendance-workforce-page.component.html', styleUrls: ['./attendance-workforce-page.component.scss'] })
export class AttendanceWorkforcePageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;
  private readonly destroy$ = new Subject<void>();
  private readonly load$ = new Subject<LoadRequest>();
  private readonly search$ = new Subject<string>();
  private readonly storageKey = 'plp.attendance-workforce.filters.v1';
  private loadSequence = 0;
  rows: WorkforceRow[] = [];
  summary: WorkforcePage['summary'] | null = null;
  selectedDate = this.cairoToday();
  search = '';
  attendanceFilter = 'all'; assignmentFilter = 'all'; operationalFilter = '';
  selectedFactoryId = ''; selectedProductionLineId = ''; selectedMainStageId = ''; selectedSubStageId = '';
  factories: FactoryItem[] = []; productionLines: ProductionLineOption[] = []; mainStages: MainStageOption[] = []; subStages: SubStageOption[] = [];
  page = 1; pageSize = 25; totalRecords = 0; first = 0;
  isLoading = true; hasLoaded = false; hasError = false; errorMessage = 'تعذر تحميل بيانات الحضور والتسكين.';
  syncInProgress = false; syncMessage = ''; expandedWorkerId: string | null = null; details = new Map<string, WorkforceDetail>(); detailsLoading = new Set<string>();
  detailErrors = new Map<string, string>();
  filtersCollapsed = true;

  constructor(private readonly api: AttendanceWorkforceApiService, private readonly attendanceApi: AttendanceApiService, private readonly masterData: ManufacturingMasterDataApiService, readonly permissionService: PermissionService) {}

  ngOnInit(): void {
    this.restoreFilters();
    this.loadFactories();
    this.load$.pipe(
      map(request => ({ ...this.currentQuery(), ...request, page: Math.max(1, request.page ?? this.page), pageSize: Math.max(10, request.pageSize ?? this.pageSize) })),
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b) && !a.force && !b.force),
      switchMap(query => {
        const requestId = ++this.loadSequence;
        this.isLoading = true;
        this.hasError = false;
        this.persistFilters(query);
        return this.api.getPage(query).pipe(
          map(payload => ({ payload, query, requestId, error: null as unknown | null })),
          catchError(error => of({ payload: null, query, requestId, error })),
          finalize(() => { if (requestId === this.loadSequence) { this.isLoading = false; this.hasLoaded = true; } })
        );
      }),
      takeUntil(this.destroy$)
    ).subscribe(result => {
      if (result.requestId !== this.loadSequence) return;
      if (result.error) {
        this.errorMessage = result.error instanceof Error ? result.error.message : 'تعذر تحميل بيانات الحضور والتسكين.';
        if (this.rows.length > 0) this.syncMessage = this.errorMessage;
        else this.hasError = true;
        return;
      }
      const payload = result.payload!;
      this.rows = payload.items; this.summary = payload.summary; this.totalRecords = payload.totalCount; this.page = payload.page; this.pageSize = payload.pageSize; this.first = (payload.page - 1) * payload.pageSize;
    });
    this.search$.pipe(debounceTime(300), distinctUntilChanged(), takeUntil(this.destroy$)).subscribe(search => this.reload({ search, page: 1 }));
    this.reload({ force: true });
  }
  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }
  get canSync(): boolean { return this.permissionService.has(PERMISSIONS.attendance.sync); }
  get isEmpty(): boolean { return this.hasLoaded && !this.isLoading && !this.hasError && this.rows.length === 0; }
  get activeFilterCount(): number { return [this.search, this.selectedFactoryId, this.selectedProductionLineId, this.selectedMainStageId, this.selectedSubStageId, this.attendanceFilter !== 'all' ? this.attendanceFilter : '', this.assignmentFilter !== 'all' ? this.assignmentFilter : '', this.operationalFilter].filter(Boolean).length; }
  get summaryScopeLabel(): string { return this.summary?.scope === 'current-page' ? 'الملخص للصفحة الحالية' : 'الملخص للنتائج المفلترة'; }
  get emptyDescription(): string { return this.summary?.attendanceDataAvailable === false ? 'لا توجد بيانات حضور مؤكدة لهذا التاريخ. نفّذ مزامنة التاريخ المحدد إن كانت لديك الصلاحية.' : 'لا توجد حالات مطابقة للفلاتر المحددة.'; }
  onSearch(value: string): void { this.search = value; this.search$.next(value.trim()); }
  onFilterChange(): void { this.reload({ page: 1 }); }
  onDateChange(): void { this.expandedWorkerId = null; this.details.clear(); this.detailErrors.clear(); this.reload({ page: 1, force: true }); }
  toggleFilters(): void { this.filtersCollapsed = !this.filtersCollapsed; }
  onFactoryChange(): void { this.selectedProductionLineId = ''; this.selectedMainStageId = ''; this.selectedSubStageId = ''; this.productionLines = []; this.mainStages = []; this.subStages = []; if (this.selectedFactoryId) this.masterData.allProductionLines().pipe(takeUntil(this.destroy$)).subscribe(lines => { this.productionLines = lines.filter(line => line.factoryId === this.selectedFactoryId); }); this.onFilterChange(); }
  onProductionLineChange(): void { this.selectedMainStageId = ''; this.selectedSubStageId = ''; this.mainStages = []; this.subStages = []; if (this.selectedProductionLineId) this.masterData.mainStagesForLine(this.selectedProductionLineId).pipe(takeUntil(this.destroy$)).subscribe(stages => this.mainStages = stages); this.onFilterChange(); }
  onMainStageChange(): void { this.selectedSubStageId = ''; this.subStages = []; if (this.selectedMainStageId) this.masterData.subStagesForMainStage(this.selectedMainStageId).pipe(takeUntil(this.destroy$)).subscribe(stages => this.subStages = stages); this.onFilterChange(); }
  onSubStageChange(): void { this.onFilterChange(); }
  onLazyLoad(event: TableLazyLoadEvent): void { if (!this.hasLoaded) return; const size = event.rows ?? this.pageSize; const page = Math.floor((event.first ?? 0) / Math.max(size, 1)) + 1; if (page !== this.page || size !== this.pageSize) this.reload({ page, pageSize: size }); }
  retry(): void { this.reload({ force: true }); }
  reset(): void { this.selectedDate = this.cairoToday(); this.search = ''; this.attendanceFilter = 'all'; this.assignmentFilter = 'all'; this.operationalFilter = ''; this.selectedFactoryId = ''; this.selectedProductionLineId = ''; this.selectedMainStageId = ''; this.selectedSubStageId = ''; this.productionLines = []; this.mainStages = []; this.subStages = []; localStorage.removeItem(this.storageKey); this.reload({ page: 1, force: true }); }
  syncSelectedDate(): void { if (!this.canSync || this.syncInProgress) return; this.syncInProgress = true; this.syncMessage = ''; this.attendanceApi.syncForProductionDate(this.selectedDate).pipe(finalize(() => this.syncInProgress = false), takeUntil(this.destroy$)).subscribe({ next: result => { this.syncMessage = `تمت مزامنة ${result.matchedWorkersCount} عاملًا للتاريخ المحدد.`; this.details.clear(); this.detailErrors.clear(); this.reload({ force: true }); }, error: error => this.syncMessage = error instanceof Error ? error.message : 'تعذرت مزامنة الحضور. لم تتغير البيانات المعروضة.' }); }
  toggleDetails(row: WorkforceRow): void { if (this.expandedWorkerId === row.workerId) { this.expandedWorkerId = null; return; } this.expandedWorkerId = row.workerId; if (this.details.has(row.workerId) || this.detailsLoading.has(row.workerId)) return; this.detailErrors.delete(row.workerId); this.detailsLoading.add(row.workerId); this.api.getDetail(row.workerId, this.selectedDate).pipe(finalize(() => this.detailsLoading.delete(row.workerId)), takeUntil(this.destroy$)).subscribe({ next: detail => this.details.set(row.workerId, detail), error: () => this.detailErrors.set(row.workerId, 'تعذر تحميل تفاصيل العامل. أعد المحاولة بإغلاق التفاصيل وفتحها مرة أخرى.') }); }
  statusLabel(status: string): string { return ({ Present: 'حاضر', Late: 'متأخر', Absent: 'غائب', Incomplete: 'بصمة واحدة / بدون خروج', Unassigned: 'غير معروف', NeedsSync: 'تحتاج مزامنة حضور اليوم' } as Record<string, string>)[status] ?? status; }
  statusTone(status: string): 'success' | 'warning' | 'danger' | 'neutral' { return status === 'Present' ? 'success' : status === 'Late' || status === 'Incomplete' ? 'warning' : status === 'Absent' ? 'danger' : 'neutral'; }
  statusIcon(status: string): string { return status === 'Present' ? 'pi pi-check-circle' : status === 'Late' || status === 'Incomplete' ? 'pi pi-clock' : status === 'Absent' ? 'pi pi-times-circle' : 'pi pi-info-circle'; }
  assignmentLabel(row: WorkforceRow): string { if (!row.isAssigned) return 'غير مسكن'; if (row.hasTemporaryAssignment) return 'تسكين مؤقت'; return row.assignments.length > 1 ? `تسكين متعدد (${row.assignments.length})` : 'مسكن'; }
  formatTime(value: string | null): string { return value ? new Intl.DateTimeFormat('ar-EG', { hour: '2-digit', minute: '2-digit', timeZone: 'Africa/Cairo' }).format(new Date(value)) : '—'; }
  formatDuration(row: WorkforceRow): string { if (!row.firstCheckInUtc || !row.lastCheckOutUtc) return '—'; const minutes = Math.max(0, Math.round((new Date(row.lastCheckOutUtc).getTime() - new Date(row.firstCheckInUtc).getTime()) / 60000)); return `${Math.floor(minutes / 60)} س ${minutes % 60} د`; }
  detailFor(id: string): WorkforceDetail | undefined { return this.details.get(id); }
  private reload(overrides: Partial<LoadRequest> = {}): void { this.load$.next({ ...this.currentQuery(), ...overrides }); }
  private currentQuery(): LoadRequest { return { productionDate: this.selectedDate, page: this.page, pageSize: this.pageSize, search: this.search.trim(), factoryId: this.selectedFactoryId || undefined, productionLineId: this.selectedProductionLineId || undefined, mainStageId: this.selectedMainStageId || undefined, subStageId: this.selectedSubStageId || undefined, attendanceFilter: this.attendanceFilter, assignmentFilter: this.assignmentFilter, operationalFilter: this.operationalFilter, sortBy: 'name', sortDirection: 'asc' }; }
  private persistFilters(query: WorkforceQuery): void { localStorage.setItem(this.storageKey, JSON.stringify({ selectedDate: query.productionDate, search: query.search ?? '', factoryId: this.selectedFactoryId, productionLineId: this.selectedProductionLineId, mainStageId: this.selectedMainStageId, subStageId: this.selectedSubStageId, attendanceFilter: query.attendanceFilter ?? 'all', assignmentFilter: query.assignmentFilter ?? 'all', operationalFilter: query.operationalFilter ?? '' })); }
  private restoreFilters(): void { try { const value = JSON.parse(localStorage.getItem(this.storageKey) ?? '{}'); if (/^\d{4}-\d{2}-\d{2}$/.test(value.selectedDate)) this.selectedDate = value.selectedDate; this.search = typeof value.search === 'string' ? value.search : ''; this.selectedFactoryId = typeof value.factoryId === 'string' ? value.factoryId : ''; this.selectedProductionLineId = typeof value.productionLineId === 'string' ? value.productionLineId : ''; this.selectedMainStageId = typeof value.mainStageId === 'string' ? value.mainStageId : ''; this.selectedSubStageId = typeof value.subStageId === 'string' ? value.subStageId : ''; this.attendanceFilter = typeof value.attendanceFilter === 'string' ? value.attendanceFilter : 'all'; this.assignmentFilter = typeof value.assignmentFilter === 'string' ? value.assignmentFilter : 'all'; this.operationalFilter = typeof value.operationalFilter === 'string' ? value.operationalFilter : ''; } catch { localStorage.removeItem(this.storageKey); } }
  private loadFactories(): void { this.masterData.factories().pipe(takeUntil(this.destroy$)).subscribe({ next: factories => { this.factories = factories; if (!this.factories.some(factory => factory.id === this.selectedFactoryId)) { this.selectedFactoryId = ''; this.selectedProductionLineId = ''; this.selectedMainStageId = ''; this.selectedSubStageId = ''; return; } this.restoreStructuralOptions(); }, error: () => { this.factories = []; } }); }
  private restoreStructuralOptions(): void { this.masterData.allProductionLines().pipe(takeUntil(this.destroy$)).subscribe(lines => { this.productionLines = lines.filter(line => line.factoryId === this.selectedFactoryId); if (!this.productionLines.some(line => line.id === this.selectedProductionLineId)) { this.selectedProductionLineId = ''; this.selectedMainStageId = ''; this.selectedSubStageId = ''; return; } this.masterData.mainStagesForLine(this.selectedProductionLineId).pipe(takeUntil(this.destroy$)).subscribe(stages => { this.mainStages = stages; if (!this.mainStages.some(stage => stage.id === this.selectedMainStageId)) { this.selectedMainStageId = ''; this.selectedSubStageId = ''; return; } this.masterData.subStagesForMainStage(this.selectedMainStageId).pipe(takeUntil(this.destroy$)).subscribe(subStages => { this.subStages = subStages; if (!this.subStages.some(stage => stage.id === this.selectedSubStageId)) this.selectedSubStageId = ''; }); }); }); }
  private cairoToday(): string { const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Cairo', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date()); const value = (type: string) => parts.find(part => part.type === type)?.value ?? ''; return `${value('year')}-${value('month')}-${value('day')}`; }
}
