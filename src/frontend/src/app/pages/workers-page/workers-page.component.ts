import { Component, OnDestroy, OnInit, Optional } from '@angular/core';
import { MenuItem, MessageService } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { Router } from '@angular/router';
import { PermissionService } from '../../core/services/permission.service';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { Subject, catchError, debounceTime, distinctUntilChanged, finalize, map, of, switchMap, takeUntil } from 'rxjs';
import { WorkerManagementFacade } from './worker-management.facade';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import {
  WorkerAssignmentStatus,
  WorkerLocalEmploymentStatus,
  WorkerLocalProfileStatus,
  WorkerManagementListItem,
  WorkerManagementPage,
  WorkerManagementProfile,
  WorkerManagementQuery,
  WorkerSourceLinkStatus,
  WorkerDepartmentOption
} from './worker-management.models';
import { ManufacturingDataChanged } from '../../core/models/realtime-notification.models';
import {
  assignmentStatusPresentation,
  localProfileStatusPresentation,
  sourceLinkStatusPresentation
} from './worker-management.presentation';

type WorkerPageResult = { payload: WorkerManagementPage; requestId: number; error: null } | { payload: null; requestId: number; error: unknown };

interface PaginatorChange {
  page?: number;
  rows?: number;
}

@Component({
  selector: 'app-workers-page',
  templateUrl: './workers-page.component.html',
  styleUrls: ['./workers-page.component.scss']
})
export class WorkersPageComponent implements OnInit, OnDestroy {
  private readonly destroy$ = new Subject<void>();
  private readonly load$ = new Subject<WorkerManagementQuery>();
  private readonly search$ = new Subject<string>();
  private readonly storageKey = 'plp.worker-management.filters.v1';
  private loadSequence = 0;
  private stopRealtime?: () => void;

  readonly permissions = PERMISSIONS;
  readonly employmentStatuses = [
    { value: '', label: 'كل حالات العمل المحلية' },
    { value: 'active', label: 'نشط محليًا' },
    { value: 'inactive', label: 'غير نشط محليًا' }
  ];

  workers: WorkerManagementListItem[] = [];
  search = '';
  localEmploymentStatus: WorkerLocalEmploymentStatus | '' = '';
  page = 1;
  pageSize = 6;
  totalRecords = 0;
  totalPages = 1;
  isLoading = true;
  hasLoaded = false;
  hasError = false;
  errorMessage = 'تعذر تحميل مساحة إدارة العاملين.';

  profileViewOpen = false;
  profileLoading = false;
  profileError = '';
  selectedProfile: WorkerManagementProfile | null = null;
  departmentDialogVisible = false;
  departmentOptionsLoading = false;
  departmentSaving = false;
  departmentDialogError = '';
  departmentConflict = false;
  selectedDepartmentId = '';
  selectedDepartmentWorker: WorkerManagementListItem | null = null;
  departmentOptions: WorkerDepartmentOption[] = [];
  workerActionItems: MenuItem[] = [];

  constructor(
    private readonly facade: WorkerManagementFacade,
    private readonly permissionService: PermissionService,
    @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService,
    @Optional() private readonly router?: Router,
    @Optional() private readonly messageService?: MessageService
  ) {}

  ngOnInit(): void {
    this.restoreFilters();
    if (this.router?.url.includes('/manufacturing/employees')) {
      this.stopRealtime = this.manufacturingRealtime?.watchScreen({ screen: 'employees', refresh: change => this.handleRealtimeChange(change) });
    }
    this.load$.pipe(
      switchMap(query => {
        const requestId = ++this.loadSequence;
        this.isLoading = true;
        this.hasError = false;
        this.persistFilters();
        return this.facade.loadWorkers(query).pipe(
          map(payload => ({ payload, requestId, error: null }) as WorkerPageResult),
          catchError(error => of({ payload: null, requestId, error } as WorkerPageResult)),
          finalize(() => {
            if (requestId === this.loadSequence) {
              this.isLoading = false;
              this.hasLoaded = true;
            }
          })
        );
      }),
      takeUntil(this.destroy$)
    ).subscribe(result => {
      if (result.requestId !== this.loadSequence) return;
      if (result.error || !result.payload) {
        this.hasError = true;
        this.workers = [];
        this.totalRecords = 0;
        this.errorMessage = result.error instanceof Error ? result.error.message : 'تعذر تحميل مساحة إدارة العاملين.';
        return;
      }
      this.applyPage(result.payload);
    });

    this.search$.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(() => this.reload(1));

    this.reload(this.page);
  }

  ngOnDestroy(): void {
    this.stopRealtime?.();
    this.destroy$.next();
    this.destroy$.complete();
  }

  get canManage(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.workers.manage);
  }

  get canViewAssignments(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.assignments.view);
  }

  get canAssignDepartment(): boolean {
    return this.permissionService.hasAll([PERMISSIONS.workers.manage, PERMISSIONS.departments.manage]);
  }

  get departmentSaveDisabled(): boolean {
    return this.departmentSaving || this.departmentOptionsLoading || this.departmentConflict || !this.selectedDepartmentId ||
      this.selectedDepartmentId === this.selectedDepartmentWorker?.organizationalDepartmentId;
  }

  get isEmpty(): boolean {
    return this.hasLoaded && !this.isLoading && !this.hasError && this.workers.length === 0;
  }

  get activeFilterCount(): number {
    return [
      this.search.trim(),
      this.localEmploymentStatus
    ].filter(Boolean).length;
  }

  get firstRecordIndex(): number {
    return (this.page - 1) * this.pageSize;
  }

  get emptyDescription(): string {
    return this.activeFilterCount
      ? 'لا توجد ملفات مطابقة للبحث والفلاتر الحالية. أعد ضبط الفلاتر لعرض القائمة الكاملة.'
      : 'لا توجد ملفات عاملين في مصدر البيانات الحالي.';
  }

  onSearch(value: string): void {
    this.search = value;
    this.search$.next(value.trim());
  }

  onEmploymentStatusChange(value: string): void {
    this.localEmploymentStatus = value as WorkerLocalEmploymentStatus | '';
    this.reload(1);
  }

  resetFilters(): void {
    this.search = '';
    this.localEmploymentStatus = '';
    localStorage.removeItem(this.storageKey);
    this.reload(1);
  }

  retry(): void {
    this.reload(this.page);
  }

  onPageChange(event: PaginatorChange): void {
    const pageSize = Math.max(1, event.rows ?? this.pageSize);
    const page = Math.max(1, (event.page ?? 0) + 1);
    if (page === this.page && pageSize === this.pageSize) return;
    this.pageSize = pageSize;
    this.reload(page);
  }

  openProfile(worker: WorkerManagementListItem): void {
    this.profileViewOpen = true;
    this.profileLoading = true;
    this.profileError = '';
    this.selectedProfile = null;
    this.facade.loadProfile(worker.id).pipe(
      finalize(() => this.profileLoading = false),
      takeUntil(this.destroy$)
    ).subscribe({
      next: profile => {
        this.selectedProfile = profile;
        if (typeof window !== 'undefined') window.scrollTo({ top: 0, behavior: 'smooth' });
      },
      error: error => {
        this.profileError = error instanceof Error ? error.message : 'تعذر تحميل ملف العامل.';
      }
    });
  }

  openWorkerActions(event: Event, worker: WorkerManagementListItem, menu: Menu): void {
    this.workerActionItems = [
      {
        label: 'فتح الملف',
        icon: 'pi pi-folder-open',
        command: () => this.openProfile(worker)
      },
      ...(this.canAssignDepartment ? [{
        label: worker.organizationalDepartmentId ? 'تغيير القسم' : 'تعيين إلى قسم',
        icon: 'pi pi-sitemap',
        command: () => this.openDepartmentDialog(worker)
      }] : [])
    ];
    menu.toggle(event);
  }

  openDepartmentDialog(worker: WorkerManagementListItem): void {
    if (!this.canAssignDepartment) return;
    this.selectedDepartmentWorker = worker;
    this.selectedDepartmentId = worker.organizationalDepartmentId ?? '';
    this.departmentDialogError = worker.organizationalDepartmentConcurrencyToken
      ? ''
      : 'تعذر بدء التعديل لأن نسخة بيانات العامل غير متاحة. أعد تحميل الصفحة.';
    this.departmentConflict = !worker.organizationalDepartmentConcurrencyToken;
    this.departmentDialogVisible = true;
    this.departmentOptionsLoading = true;
    this.facade.loadActiveDepartments().pipe(
      finalize(() => this.departmentOptionsLoading = false),
      takeUntil(this.destroy$)
    ).subscribe({
      next: departments => this.departmentOptions = departments,
      error: error => this.departmentDialogError = error instanceof Error ? error.message : 'تعذر تحميل الأقسام النشطة.'
    });
  }

  closeDepartmentDialog(): void {
    if (this.departmentSaving) return;
    this.departmentDialogVisible = false;
    this.selectedDepartmentWorker = null;
    this.selectedDepartmentId = '';
    this.departmentDialogError = '';
    this.departmentConflict = false;
  }

  saveDepartmentAssignment(): void {
    const worker = this.selectedDepartmentWorker;
    if (!worker || this.departmentSaveDisabled) return;
    this.departmentSaving = true;
    this.departmentDialogError = '';
    this.facade.assignDepartment(worker.id, this.selectedDepartmentId, worker.organizationalDepartmentConcurrencyToken ?? '').pipe(
      finalize(() => this.departmentSaving = false),
      takeUntil(this.destroy$)
    ).subscribe({
      next: result => {
        this.workers = this.workers.map(item => item.id !== result.workerId ? item : {
          ...item,
          organizationalDepartmentId: result.departmentId,
          organizationalDepartmentName: result.departmentName,
          organizationalFactoryName: result.factoryName,
          organizationalDepartmentConcurrencyToken: result.concurrencyToken
        });
        this.departmentSaving = false;
        this.closeDepartmentDialog();
        this.messageService?.add({ severity: 'success', summary: 'تم تحديث القسم', detail: 'حُفظ التعيين التنظيمي داخل Dayoub فقط.' });
      },
      error: error => {
        const status = (error as { status?: number })?.status;
        if (status === 409) this.departmentConflict = true;
        this.departmentDialogError = status === 409
          ? 'تغيرت بيانات العامل أثناء التحرير. أغلق النافذة وافتحها مجددًا قبل الحفظ.'
          : error instanceof Error ? error.message : 'تعذر حفظ القسم التنظيمي.';
      }
    });
  }

  closeProfile(): void {
    this.profileViewOpen = false;
    this.profileLoading = false;
    this.profileError = '';
    this.selectedProfile = null;
  }

  onProfileChanged(profile: WorkerManagementProfile): void {
    this.selectedProfile = profile;
    this.workers = this.workers.map(worker => worker.id !== profile.id
      ? worker
      : {
        ...worker,
        localName: profile.local.displayName,
        photoUrl: profile.local.photoUrl,
        localEmploymentStatus: profile.local.employmentStatus,
        assignmentStatus: profile.assignmentStatus,
        assignmentLabel: profile.assignmentStatus === 'assigned' ? 'مرتبط بمرحلة محلية' : 'لا يوجد تسكين افتراضي نشط'
      });
  }

  localProfileStatusMeta(status: WorkerLocalProfileStatus) { return localProfileStatusPresentation(status); }
  sourceLinkStatusMeta(status: WorkerSourceLinkStatus) { return sourceLinkStatusPresentation(status); }
  assignmentStatusMeta(status: WorkerAssignmentStatus) { return assignmentStatusPresentation(status); }

  trackWorker(_: number, worker: WorkerManagementListItem): string {
    return worker.id;
  }

  private reload(page: number): void {
    this.page = Math.max(1, page);
    this.load$.next(this.currentQuery());
  }

  private handleRealtimeChange(change?: ManufacturingDataChanged): void {
    if (change?.workerChangeKinds?.includes('department-assignment') && this.selectedDepartmentWorker) {
      const affectedIds = new Set([change.workerId, change.entityId, ...(change.workerIds ?? [])].filter((id): id is string => !!id));
      if (affectedIds.size === 0 || affectedIds.has(this.selectedDepartmentWorker.id)) {
        this.departmentConflict = true;
        this.departmentDialogError = 'غيّر مستخدم آخر قسم هذا العامل. احتفظنا بالنافذة مفتوحة ومنعنا الحفظ فوق التغيير.';
      }
    }
    this.reload(this.page);
  }

  private currentQuery(): WorkerManagementQuery {
    return {
      page: this.page,
      pageSize: this.pageSize,
      search: this.search.trim(),
      localEmploymentStatus: this.localEmploymentStatus
    };
  }

  private applyPage(payload: WorkerManagementPage): void {
    this.workers = payload.items;
    this.totalRecords = payload.totalCount;
    this.page = payload.page;
    this.pageSize = payload.pageSize;
    this.totalPages = payload.totalPages;
    this.hasError = false;
  }

  private persistFilters(): void {
    localStorage.setItem(this.storageKey, JSON.stringify({
      search: this.search,
      localEmploymentStatus: this.localEmploymentStatus
    }));
  }

  private restoreFilters(): void {
    try {
      const value = JSON.parse(localStorage.getItem(this.storageKey) ?? '{}') as Record<string, unknown>;
      this.search = this.stringValue(value['search']);
      this.localEmploymentStatus = this.allowedValue(value['localEmploymentStatus'], ['active', 'inactive']);
    } catch {
      localStorage.removeItem(this.storageKey);
    }
  }

  private stringValue(value: unknown): string {
    return typeof value === 'string' ? value : '';
  }

  private allowedValue<T extends string>(value: unknown, allowed: readonly T[]): T | '' {
    return typeof value === 'string' && allowed.includes(value as T) ? value as T : '';
  }
}
