import { Component, OnDestroy, OnInit, Optional } from '@angular/core';
import { MessageService } from 'primeng/api';
import { OverlayPanel } from 'primeng/overlaypanel';
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
  WorkerPhotoFilter,
  WorkerSourceLinkStatus,
  WorkerDepartmentOption
} from './worker-management.models';
import { ManufacturingDataChanged } from '../../core/models/realtime-notification.models';
import {
  assignmentStatusPresentation,
  localEmploymentStatusPresentation,
  localProfileStatusPresentation,
  sourceLinkStatusPresentation
} from './worker-management.presentation';

type WorkerPageResult = { payload: WorkerManagementPage; requestId: number; error: null } | { payload: null; requestId: number; error: unknown };
type WorkerProfileSearchResult = { payload: WorkerManagementPage; requestId: number; error: null } | { payload: null; requestId: number; error: unknown };

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
  private readonly profileSearch$ = new Subject<string>();
  private readonly profileRequestCancel$ = new Subject<void>();
  private readonly storageKey = 'plp.worker-management.filters.v1';
  private loadSequence = 0;
  private profileSearchSequence = 0;
  private stopRealtime?: () => void;
  workerActionTarget: WorkerManagementListItem | null = null;

  readonly permissions = PERMISSIONS;
  readonly employmentStatuses = [
    { value: '', label: 'كل حالات العمل المحلية' },
    { value: 'active', label: 'نشط محليًا' },
    { value: 'inactive', label: 'غير نشط محليًا' }
  ];
  readonly photoFilters = [
    { value: '', label: 'الكل' },
    { value: 'with-photo', label: 'بصورة' },
    { value: 'without-photo', label: 'بدون صورة' }
  ];

  workers: WorkerManagementListItem[] = [];
  search = '';
  localEmploymentStatus: WorkerLocalEmploymentStatus | '' = '';
  photoFilter: WorkerPhotoFilter | '' = '';
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
  selectedProfileWorkerId = '';
  profileSearch = '';
  profileSearchResults: WorkerManagementListItem[] = [];
  profileSearchLoading = false;
  profileSearchError = '';
  departmentDialogVisible = false;
  departmentOptionsLoading = false;
  departmentSaving = false;
  departmentDialogError = '';
  departmentConflict = false;
  selectedDepartmentId = '';
  selectedDepartmentWorker: WorkerManagementListItem | null = null;
  departmentOptions: WorkerDepartmentOption[] = [];

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

    this.profileSearch$.pipe(
      debounceTime(250),
      switchMap(search => {
        const normalizedSearch = search.trim();
        const requestId = ++this.profileSearchSequence;
        if (!normalizedSearch) {
          this.profileSearchLoading = false;
          this.profileSearchError = '';
          return of({ payload: { items: [], totalCount: 0, page: 1, pageSize: 6, totalPages: 1 }, requestId, error: null } as WorkerProfileSearchResult);
        }
        this.profileSearchLoading = true;
        this.profileSearchError = '';
        return this.facade.loadWorkers({ page: 1, pageSize: 6, search: normalizedSearch, localEmploymentStatus: '' }).pipe(
          map(payload => ({ payload, requestId, error: null }) as WorkerProfileSearchResult),
          catchError(error => of({ payload: null, requestId, error } as WorkerProfileSearchResult)),
          finalize(() => {
            if (requestId === this.profileSearchSequence) this.profileSearchLoading = false;
          })
        );
      }),
      takeUntil(this.destroy$)
    ).subscribe(result => {
      if (result.requestId !== this.profileSearchSequence) return;
      if (result.error || !result.payload) {
        this.profileSearchResults = [];
        this.profileSearchError = 'تعذر البحث عن عامل الآن. أعد المحاولة.';
        return;
      }
      this.profileSearchResults = result.payload.items.filter(worker => worker.id !== this.selectedProfileWorkerId);
      this.profileSearchError = '';
    });

    this.reload(this.page);
  }

  ngOnDestroy(): void {
    this.stopRealtime?.();
    this.profileRequestCancel$.next();
    this.profileRequestCancel$.complete();
    this.destroy$.next();
    this.destroy$.complete();
  }

  get canManage(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.workers.manage);
  }

  get canViewAssignments(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.assignments.view);
  }

  get canViewAttendance(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.attendance.view);
  }

  get canViewCompensation(): boolean {
    return this.permissionService.hasPermission(PERMISSIONS.compensation.view);
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
      this.localEmploymentStatus,
      this.photoFilter
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

  onProfileSearch(value: string): void {
    this.profileSearch = value;
    this.profileSearch$.next(value);
  }

  onEmploymentStatusChange(value: string): void {
    this.localEmploymentStatus = value as WorkerLocalEmploymentStatus | '';
    this.reload(1);
  }

  onPhotoFilterChange(value: string): void {
    this.photoFilter = value as WorkerPhotoFilter | '';
    this.reload(1);
  }

  resetFilters(): void {
    this.search = '';
    this.localEmploymentStatus = '';
    this.photoFilter = '';
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
    this.selectedProfileWorkerId = worker.id;
    this.clearProfileSearch();
    this.loadSelectedProfile();
  }

  retryProfile(): void {
    if (!this.selectedProfileWorkerId) return;
    this.loadSelectedProfile();
  }

  private loadSelectedProfile(): void {
    const workerId = this.selectedProfileWorkerId;
    this.profileRequestCancel$.next();
    this.profileLoading = true;
    this.profileError = '';
    this.selectedProfile = null;
    this.facade.loadProfile(workerId, {
      assignments: this.canViewAssignments,
      attendance: this.canViewAttendance,
      compensation: this.canViewCompensation
    }).pipe(
      finalize(() => {
        if (this.selectedProfileWorkerId === workerId) this.profileLoading = false;
      }),
      takeUntil(this.profileRequestCancel$),
      takeUntil(this.destroy$)
    ).subscribe({
      next: profile => {
        if (this.selectedProfileWorkerId !== workerId) return;
        this.selectedProfile = profile;
        if (typeof window !== 'undefined') window.scrollTo({ top: 0, behavior: 'smooth' });
      },
      error: error => {
        if (this.selectedProfileWorkerId !== workerId) return;
        this.profileError = error instanceof Error ? error.message : 'تعذر تحميل ملف العامل.';
      }
    });
  }

  openWorkerActions(event: Event, worker: WorkerManagementListItem, overlay: OverlayPanel): void {
    this.workerActionTarget = worker;
    overlay.toggle(event);
  }

  openProfileFromWorkerActions(overlay: OverlayPanel): void {
    const worker = this.workerActionTarget;
    if (!worker) return;
    this.workerActionTarget = null;
    overlay.hide();
    this.openProfile(worker);
  }

  openDepartmentFromWorkerActions(overlay: OverlayPanel): void {
    const worker = this.workerActionTarget;
    if (!worker) return;
    this.workerActionTarget = null;
    overlay.hide();
    this.openDepartmentDialog(worker);
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
      error: () => this.departmentDialogError = 'تعذر تحميل الأقسام النشطة، أعد المحاولة.'
    });
  }

  openProfileDepartmentDialog(): void {
    const profile = this.selectedProfile;
    if (!profile || !this.canAssignDepartment) return;
    const currentRow = this.workers.find(worker => worker.id === profile.id);
    const worker: WorkerManagementListItem = {
      ...(currentRow ?? {
        id: profile.id,
        localName: profile.local.displayName,
        sourceName: null,
        photoUrl: profile.local.photoUrl,
        badgeNumber: profile.source.badgeNumber,
        employeeCode: profile.source.employeeCode,
        assignmentLabel: profile.assignments[0]?.stageNames.join(' / ') || 'غير مسكن حاليًا',
        factoryLineLabel: profile.assignments[0] ? `${profile.assignments[0].factoryName} / ${profile.assignments[0].productionLineName}` : 'لا يوجد تسكين دائم نشط',
        sourceLinkStatus: profile.source.linkStatus,
        localProfileStatus: profile.local.profileStatus,
        assignmentStatus: profile.assignmentStatus,
        localEmploymentStatus: profile.local.employmentStatus,
        factoryId: profile.assignments[0]?.factoryId ?? null,
        productionLineId: profile.assignments[0]?.productionLineId ?? null,
        hasIdentityConflict: false
      }),
      organizationalDepartmentId: profile.organizationalDepartmentId ?? null,
      organizationalDepartmentName: profile.organizationalDepartmentName ?? null,
      organizationalFactoryName: profile.organizationalFactoryName ?? null,
      organizationalDepartmentConcurrencyToken: profile.organizationalDepartmentConcurrencyToken ?? ''
    };
    this.openDepartmentDialog(worker);
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
    const wasAssigned = !!worker.organizationalDepartmentId;
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
        if (this.selectedProfile?.id === result.workerId) {
          this.selectedProfile = {
            ...this.selectedProfile,
            organizationalDepartmentId: result.departmentId,
            organizationalDepartmentName: result.departmentName,
            organizationalFactoryName: result.factoryName,
            organizationalDepartmentConcurrencyToken: result.concurrencyToken
          };
        }
        this.departmentSaving = false;
        this.closeDepartmentDialog();
        this.messageService?.add({
          severity: 'success',
          summary: wasAssigned ? 'تم تغيير القسم التنظيمي بنجاح' : 'تم تعيين العامل إلى القسم بنجاح',
          detail: 'حُفظ التعيين التنظيمي داخل Dayoub فقط.'
        });
      },
      error: error => {
        const status = (error as { status?: number })?.status;
        if (status === 409) this.departmentConflict = true;
        this.departmentDialogError = status === 409
          ? 'تغيرت بيانات العامل أثناء التحرير. أغلق النافذة وافتحها مجددًا قبل الحفظ.'
          : status === 403
            ? 'لا تملك صلاحية تعيين العامل إلى قسم.'
            : status === 400
              ? 'لا يمكن التعيين إلى قسم غير نشط أو لعامل غير نشط.'
              : 'تعذر حفظ القسم، أعد المحاولة.';
      }
    });
  }

  closeProfile(): void {
    this.profileRequestCancel$.next();
    this.profileViewOpen = false;
    this.profileLoading = false;
    this.profileError = '';
    this.selectedProfile = null;
    this.selectedProfileWorkerId = '';
    this.clearProfileSearch();
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
        assignmentLabel: profile.assignments[0]
          ? `${profile.assignments[0].stageNames.join(' / ')}${profile.assignments.length > 1 ? ` +${profile.assignments.length - 1}` : ''}`
          : 'غير مسكن حاليًا',
        factoryLineLabel: profile.assignments[0]
          ? `${profile.assignments[0].factoryName} / ${profile.assignments[0].productionLineName}`
          : 'لا يوجد تسكين دائم نشط'
      });
  }

  localProfileStatusMeta(status: WorkerLocalProfileStatus) { return localProfileStatusPresentation(status); }
  localEmploymentStatusMeta(status: WorkerLocalEmploymentStatus) { return localEmploymentStatusPresentation(status); }
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
      localEmploymentStatus: this.localEmploymentStatus,
      photoFilter: this.photoFilter || undefined
    };
  }

  private clearProfileSearch(): void {
    this.profileSearchSequence++;
    this.profileSearch = '';
    this.profileSearchResults = [];
    this.profileSearchLoading = false;
    this.profileSearchError = '';
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
      localEmploymentStatus: this.localEmploymentStatus,
      photoFilter: this.photoFilter
    }));
  }

  private restoreFilters(): void {
    try {
      const value = JSON.parse(localStorage.getItem(this.storageKey) ?? '{}') as Record<string, unknown>;
      this.search = this.stringValue(value['search']);
      this.localEmploymentStatus = this.allowedValue(value['localEmploymentStatus'], ['active', 'inactive']);
      this.photoFilter = this.allowedValue(value['photoFilter'], ['with-photo', 'without-photo']);
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
