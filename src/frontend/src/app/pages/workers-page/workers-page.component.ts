import { Component, OnDestroy, OnInit } from '@angular/core';
import { catchError, finalize, Subject, debounceTime, distinctUntilChanged, map, merge, of, switchMap, takeUntil, tap } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { TableLazyLoadEvent } from 'primeng/table';
import { WorkerPageItem } from '../../shared/models/worker.model';
import { WorkersApiData, WorkersApiQuery, WorkersApiService } from '../../core/services/workers-api.service';

type WorkersLoadResult = { payload: WorkersApiData; query: WorkersApiRequest; error?: undefined } | { payload: WorkersApiData; query: WorkersApiRequest; error: unknown };

interface WorkersApiRequest extends WorkersApiQuery {
  force?: boolean;
}

@Component({
  selector: 'app-workers-page',
  templateUrl: './workers-page.component.html',
  styleUrls: ['./workers-page.component.scss']
})
export class WorkersPageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;

  private readonly destroy$ = new Subject<void>();
  private readonly loadQueue$ = new Subject<WorkersApiRequest>();
  private readonly searchTerm$ = new Subject<string>();

  workers: WorkerPageItem[] = [];
  selectedWorker: WorkerPageItem | null = null;
  searchTerm = '';
  serviceStatus: 'all' | 'active' | 'inactive' = 'all';
  isLoading = false;
  hasLoadedOnce = false;
  hasError = false;
  errorMessage = 'تعذر تحميل بيانات العمال، يرجى المحاولة مرة أخرى.';
  isServerSidePagination = false;
  first = 0;
  rows = 10;
  totalRecords = 0;
  errorRetryText = 'إعادة المحاولة';

  constructor(private readonly workersApiService: WorkersApiService) {}

  ngOnInit(): void {
    merge(
      this.searchTerm$.pipe(
        debounceTime(300),
        distinctUntilChanged(),
        map((search) => ({
          page: 1,
          pageSize: this.rows,
          search,
          serviceStatus: this.serviceStatus,
          force: false
        }))
      ),
      this.loadQueue$
    )
      .pipe(
        map((request) => ({
          ...request,
          page: Math.max(Math.trunc(request.page ?? 1), 1),
          pageSize: Math.max(Math.trunc(request.pageSize ?? 20), 1),
          search: (request.search ?? '').trim(),
          serviceStatus: request.serviceStatus ?? this.serviceStatus,
          force: request.force ?? false
        })),
        distinctUntilChanged((previous, current) => {
          if (current.force || previous.force) {
            return false;
          }

          return (
            previous.page === current.page &&
            previous.pageSize === current.pageSize &&
            previous.search === current.search &&
            previous.serviceStatus === current.serviceStatus &&
            previous.force === current.force
          );
        }),
        tap(() => {
          this.isLoading = true;
          this.hasError = false;
        }),
        switchMap((query) =>
          this.workersApiService.loadWorkers({
            page: query.page,
            pageSize: query.pageSize,
            search: query.search,
            serviceStatus: query.serviceStatus
          })
            .pipe(
              map((payload) => ({ payload, query })),
              catchError((error) => of({ payload: this.createEmptyWorkersPayload(query), query, error })),
              finalize(() => {
                this.isLoading = false;
                this.hasLoadedOnce = true;
              })
            )
        ),
        takeUntil(this.destroy$)
      )
      .subscribe((result: WorkersLoadResult) => {
        const payload = result.payload;
        if ('error' in result) {
          this.hasError = true;
          this.errorMessage = this.extractErrorMessage(result.error);
          this.workers = [];
        } else {
          this.hasError = false;
          this.processWorkersPayload(payload, result.query.search);
        }
      });

    this.loadQueue$.next({ page: 1, pageSize: this.rows, serviceStatus: this.serviceStatus });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get isTableLoading(): boolean {
    return this.isLoading && this.hasLoadedOnce;
  }

  get isSearchableEmpty(): boolean {
    return !this.isLoading && !this.hasError && this.workers.length === 0 && this.searchTerm.trim().length > 0;
  }

  get hasAnyError(): boolean {
    return this.hasError;
  }

  get isEmpty(): boolean {
    return !this.isLoading && !this.hasError && this.workers.length === 0 && this.searchTerm.trim().length === 0;
  }

  onSearch(event: Event): void {
    const searchTerm = ((event.target as HTMLInputElement).value ?? '').trim();
    this.searchTerm = searchTerm;
    this.first = 0;
    this.selectedWorker = null;
    this.searchTerm$.next(this.searchTerm);
  }

  onClearSearch(): void {
    this.searchTerm = '';
    this.first = 0;
    this.selectedWorker = null;
    this.searchTerm$.next('');
  }

  onServiceStatusChange(status: 'all' | 'active' | 'inactive'): void {
    this.serviceStatus = status;
    this.first = 0;
    this.selectedWorker = null;
    this.loadQueue$.next({
      page: 1,
      pageSize: this.rows,
      search: this.searchTerm,
      serviceStatus: status,
      force: false
    });
  }

  onLazyLoad(event: TableLazyLoadEvent): void {
    if (!this.isServerSidePagination || !this.hasLoadedOnce) {
      return;
    }

    const first = event.first ?? 0;
    const rows = event.rows ?? this.rows;
    const page = Math.floor(first / Math.max(rows, 1)) + 1;
    const normalizedRows = Math.max(rows, 1);

    if (event.first === this.first && normalizedRows === this.rows) {
      return;
    }

    this.loadQueue$.next({
      page,
      pageSize: rows,
      search: this.searchTerm,
      serviceStatus: this.serviceStatus,
      force: false
    });
  }

  openWorkerDetails(worker: WorkerPageItem): void {
    this.selectedWorker = worker;
  }

  closeWorkerDetails(): void {
    this.selectedWorker = null;
  }

  private get currentPage(): number {
    return Math.floor(this.first / Math.max(this.rows, 1)) + 1;
  }

  onRefresh(): void {
    const currentPage = this.currentPage;
    this.loadQueue$.next({
      page: currentPage,
      pageSize: this.rows,
      search: this.searchTerm,
      serviceStatus: this.serviceStatus,
      force: true
    });
  }

  private processWorkersPayload(payload: WorkersApiData, search?: string): void {
    if (!payload.hasUsableBackendData) {
      this.workers = [];
    } else {
      this.workers = payload.workers;
    }

    if (search && !this.isServerSidePagination && payload.hasUsableBackendData) {
      this.workers = this.filterWorkers(payload.workers, search);
    }

    this.totalRecords = Math.max(payload.totalCount, this.workers.length);
    this.rows = payload.pageSize;
    this.isServerSidePagination = payload.supportsServerPagination && this.totalRecords > this.rows;
    this.first = (Math.max(1, payload.page) - 1) * this.rows;
  }

  private filterWorkers(list: WorkerPageItem[], searchTerm: string): WorkerPageItem[] {
    if (!searchTerm.trim()) {
      return list;
    }

    const normalized = searchTerm.trim().toLowerCase();
    return list.filter((worker) => {
      return (
        worker.code.toLowerCase().includes(normalized) ||
        worker.fullName.toLowerCase().includes(normalized) ||
        this.withFallback(worker.department, '').toLowerCase().includes(normalized) ||
        this.withFallback(worker.email, '').toLowerCase().includes(normalized) ||
        this.withFallback(worker.phone, '').toLowerCase().includes(normalized)
      );
    });
  }

  private withFallback(value: string | undefined, fallback: string): string {
    return value?.trim() || fallback;
  }

  private createEmptyWorkersPayload(query: WorkersApiQuery): WorkersApiData {
    return {
      workers: [],
      hasBackendData: false,
      hasUsableBackendData: false,
      totalCount: 0,
      page: query.page ?? this.currentPage,
      pageSize: query.pageSize ?? this.rows,
      totalPages: 1,
      supportsServerPagination: false
    };
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof Error && error.message.length > 0) {
      return error.message;
    }
    return 'حدث خطأ غير متوقع أثناء تحميل البيانات.';
  }
}
