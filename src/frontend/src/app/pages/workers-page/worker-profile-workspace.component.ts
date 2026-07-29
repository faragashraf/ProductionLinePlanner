import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { finalize, Subject, takeUntil } from 'rxjs';
import { productionNavigationIconFor } from '../../shared/design-system/icons/production-icon-map';
import { PlpSectionNavigationItem } from '../../shared/product/plp-section-navigation.component';
import {
  WorkerAssignmentStatus,
  WorkerAttendanceHistoryPage,
  WorkerAttendanceHistoryRecord,
  WorkerLocalEmploymentStatus,
  WorkerManagementProfile
} from './worker-management.models';
import { WorkerManagementFacade } from './worker-management.facade';
import {
  assignmentStatusPresentation,
  formatWorkerCurrency,
  formatWorkerObservedAt,
  localEmploymentStatusPresentation,
  localProfileStatusPresentation,
  sourceLinkStatusPresentation
} from './worker-management.presentation';

type WorkerProfileSection = 'local' | 'operations' | 'attendance' | 'system';
type AttendanceHistoryState = 'idle' | 'loading' | 'loaded' | 'error';

interface PaginatorChange {
  page?: number;
  rows?: number;
}

const MAX_PHOTO_BYTES = 5 * 1024 * 1024;
const ALLOWED_PHOTO_TYPES = new Set(['image/jpeg', 'image/png', 'image/bmp']);

@Component({
  selector: 'app-worker-profile-workspace',
  templateUrl: './worker-profile-workspace.component.html',
  styleUrls: ['./worker-profile-workspace.component.scss', './worker-profile-attendance.component.scss']
})
export class WorkerProfileWorkspaceComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) worker!: WorkerManagementProfile;
  @Input() canManage = false;
  @Input() canViewAssignments = false;
  @Input() canViewAttendance = false;
  @Input() canViewCompensation = false;
  @Input() canAssignDepartment = false;
  @Output() closed = new EventEmitter<void>();
  @Output() changed = new EventEmitter<WorkerManagementProfile>();
  @Output() reloadRequested = new EventEmitter<void>();
  @Output() departmentAssignmentRequested = new EventEmitter<void>();

  readonly backIcon = productionNavigationIconFor('back', 'rtl');
  readonly sections: readonly PlpSectionNavigationItem[] = [
    { id: 'local', label: 'البيانات المحلية', icon: 'pi pi-home' },
    { id: 'operations', label: 'التسكين والتشغيل', icon: 'pi pi-sitemap' },
    { id: 'attendance', label: 'الحضور', icon: 'pi pi-clock' },
    { id: 'system', label: 'معلومات النظام', icon: 'pi pi-database' }
  ];
  readonly localStatusOptions: ReadonlyArray<{ value: WorkerLocalEmploymentStatus; label: string }> = [
    { value: 'active', label: 'نشط محليًا' },
    { value: 'inactive', label: 'معلّق محليًا' },
    { value: 'left-employment', label: 'منتهية خدمته محليًا' }
  ];

  activeSection: WorkerProfileSection = 'local';
  saveMessage = '';
  saveError = '';
  isSaving = false;
  selectedPhoto: File | null = null;
  photoPreviewUrl = '';
  photoMessage = '';
  photoError = '';
  isPhotoBusy = false;
  attendanceFromDate = '';
  attendanceToDate = '';
  attendanceRangeError = '';
  attendanceHistoryState: AttendanceHistoryState = 'idle';
  attendanceHistoryError = '';
  attendanceHistory: WorkerAttendanceHistoryRecord[] = [];
  attendanceHistoryPage = 1;
  attendanceHistoryPageSize = 10;
  attendanceHistoryTotalCount = 0;
  attendanceHistoryTotalPages = 1;
  private readonly destroy$ = new Subject<void>();
  private readonly attendanceHistoryCancel$ = new Subject<void>();
  private photoSelectionSequence = 0;
  private attendanceHistoryRequestSequence = 0;
  private destroyed = false;

  readonly draftForm = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    employmentStatus: ['active' as WorkerLocalEmploymentStatus]
  });

  constructor(
    private readonly formBuilder: FormBuilder,
    private readonly facade: WorkerManagementFacade
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['worker'] && this.worker) {
      const previousWorker = changes['worker'].previousValue as WorkerManagementProfile | undefined;
      if (!previousWorker || previousWorker.id !== this.worker.id) {
        this.photoSelectionSequence++;
        this.resetDraft();
        this.clearSelectedPhoto();
        this.activeSection = 'local';
        this.resetAttendanceHistory();
      }
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.photoSelectionSequence++;
    this.attendanceHistoryCancel$.next();
    this.attendanceHistoryCancel$.complete();
    this.destroy$.next();
    this.destroy$.complete();
    this.clearSelectedPhoto();
  }

  selectSection(sectionId: string): void {
    if (this.sections.some(section => section.id === sectionId)) {
      this.activeSection = sectionId as WorkerProfileSection;
      this.saveMessage = '';
      this.saveError = '';
      if (this.activeSection === 'attendance' && this.canViewAttendance && this.attendanceHistoryState === 'idle') {
        this.loadAttendanceHistory(1);
      }
    }
  }

  applyAttendanceFilter(): void {
    if (!this.validateAttendanceRange()) return;
    this.loadAttendanceHistory(1);
  }

  resetAttendanceFilter(): void {
    this.setDefaultAttendanceRange();
    this.attendanceRangeError = '';
    this.loadAttendanceHistory(1);
  }

  retryAttendanceHistory(): void {
    this.loadAttendanceHistory(this.attendanceHistoryPage);
  }

  onAttendanceHistoryPageChange(event: PaginatorChange): void {
    const page = Math.max(1, (event.page ?? 0) + 1);
    const pageSize = Math.max(1, event.rows ?? this.attendanceHistoryPageSize);
    if (page === this.attendanceHistoryPage && pageSize === this.attendanceHistoryPageSize) return;
    this.attendanceHistoryPageSize = pageSize;
    this.loadAttendanceHistory(page);
  }

  get attendanceHistoryFirstRecordIndex(): number {
    return (this.attendanceHistoryPage - 1) * this.attendanceHistoryPageSize;
  }

  movementLabel(type: 'In' | 'Out'): string {
    return type === 'In' ? 'حضور' : 'انصراف';
  }

  attendanceRecordStatusLabel(status: 'Present' | 'Late'): string {
    return status === 'Late' ? 'حضور متأخر' : 'حضور';
  }

  formatAttendanceDate(value: string): string {
    const date = new Date(`${value}T12:00:00Z`);
    return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('ar-EG', {
      dateStyle: 'medium', timeZone: 'Africa/Cairo'
    }).format(date);
  }

  formatAttendanceTime(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? 'وقت غير صالح' : new Intl.DateTimeFormat('ar-EG', {
      hour: 'numeric', minute: '2-digit', timeZone: 'Africa/Cairo'
    }).format(date);
  }

  saveDraft(): void {
    if (!this.canManage || this.draftForm.invalid || this.isSaving || this.isPhotoBusy) {
      this.draftForm.markAllAsTouched();
      return;
    }

    this.isSaving = true;
    this.saveMessage = '';
    this.saveError = '';
    const update = this.draftForm.getRawValue();
    this.facade.saveLocalProfile(this.worker, update).pipe(
      takeUntil(this.destroy$),
      finalize(() => this.isSaving = false)
    ).subscribe({
      next: worker => {
        this.applyWorker(worker);
        this.saveMessage = 'تم حفظ البيانات المحلية من خلال قاعدة بيانات التطبيق.';
      },
      error: error => this.saveError = this.describeApiError(error, 'تعذر حفظ البيانات المحلية.')
    });
  }

  async onPhotoSelected(input: HTMLInputElement): Promise<void> {
    const selectionSequence = ++this.photoSelectionSequence;
    this.photoError = '';
    this.photoMessage = '';
    const file = input.files?.[0] ?? null;
    input.value = '';
    this.clearSelectedPhoto();
    if (!file) return;

    if (file.size <= 0 || file.size > MAX_PHOTO_BYTES) {
      this.photoError = 'حجم الصورة يجب ألا يتجاوز 5 MiB.';
      return;
    }

    const detectedType = await this.detectPhotoType(file);
    if (this.destroyed || selectionSequence !== this.photoSelectionSequence) return;
    const declaredType = file.type.toLowerCase() === 'image/jpg' ? 'image/jpeg' : file.type.toLowerCase();
    const declaredTypeMismatch = !!declaredType
      && declaredType !== 'application/octet-stream'
      && (!ALLOWED_PHOTO_TYPES.has(declaredType) || declaredType !== detectedType);
    if (!detectedType || declaredTypeMismatch) {
      this.photoError = 'الأنواع المسموحة هي JPEG وPNG وBMP فقط.';
      return;
    }

    this.selectedPhoto = file;
    if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
      this.photoPreviewUrl = URL.createObjectURL(file);
    }
  }

  uploadSelectedPhoto(): void {
    if (!this.canManage || !this.selectedPhoto || this.isPhotoBusy || this.isSaving) return;

    this.isPhotoBusy = true;
    this.photoError = '';
    this.photoMessage = '';
    this.facade.uploadPhoto(this.worker, this.selectedPhoto).pipe(
      takeUntil(this.destroy$),
      finalize(() => this.isPhotoBusy = false)
    ).subscribe({
      next: worker => {
        this.applyWorker(worker);
        this.photoMessage = 'تم حفظ الصورة المحلية وتحديثها فورًا.';
      },
      error: error => this.photoError = this.describeApiError(error, 'تعذر رفع الصورة.')
    });
  }

  deletePhoto(): void {
    if (!this.canManage || !this.worker.local.photoUrl || this.isPhotoBusy || this.isSaving) return;
    if (typeof window !== 'undefined' && !window.confirm('هل تريد حذف الصورة المحلية الحالية؟')) return;

    this.isPhotoBusy = true;
    this.photoError = '';
    this.photoMessage = '';
    this.facade.deletePhoto(this.worker).pipe(
      takeUntil(this.destroy$),
      finalize(() => this.isPhotoBusy = false)
    ).subscribe({
      next: worker => {
        this.applyWorker(worker);
        this.photoMessage = 'تم حذف الصورة المحلية.';
      },
      error: error => this.photoError = this.describeApiError(error, 'تعذر حذف الصورة.')
    });
  }

  resetDraft(): void {
    this.draftForm.reset({
      displayName: this.worker.local.displayName,
      employmentStatus: this.worker.local.employmentStatus
    });
    this.saveMessage = '';
    this.saveError = '';
  }

  get hasDraftChanges(): boolean {
    const draft = this.draftForm.getRawValue();
    return draft.displayName !== this.worker.local.displayName
      || draft.employmentStatus !== this.worker.local.employmentStatus;
  }

  get assignmentStatus(): WorkerAssignmentStatus {
    return this.worker.assignmentStatus;
  }

  get formattedSalary(): string {
    return formatWorkerCurrency(this.worker.local.salary?.amount, this.worker.local.salary?.currencyCode);
  }

  get formattedObservedAt(): string {
    return formatWorkerObservedAt(this.worker.source.lastObservedAt);
  }

  get salaryLabel(): string {
    if (this.worker.dataStates.salary === 'forbidden') return 'غير مخول بعرض الراتب';
    if (this.worker.dataStates.salary === 'error') return 'تعذر تحميل القيمة الحالية';
    if (this.worker.dataStates.salary === 'empty') return 'لم تُسجل قيمة حالية';
    return this.formattedSalary;
  }

  get attendanceStatusLabel(): string {
    const status = this.worker.attendance?.todayStatus;
    return ({
      Present: 'حاضر اليوم',
      Late: 'حاضر متأخر اليوم',
      Absent: 'غائب اليوم',
      Incomplete: 'حركة حضور غير مكتملة',
      Unassigned: 'لا توجد حركة حضور اليوم',
      NoMovement: 'لا توجد حركة حضور اليوم',
      NeedsSync: 'لا توجد بيانات حضور متزامنة لهذا اليوم'
    } as Record<string, string>)[status ?? ''] ?? 'لا توجد بيانات حضور';
  }

  formatDateTime(value: string | null | undefined, emptyLabel = 'لم يُسجل'): string {
    if (!value) return emptyLabel;
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return emptyLabel;
    return new Intl.DateTimeFormat('ar-EG', {
      dateStyle: 'medium', timeStyle: 'short', timeZone: 'Africa/Cairo'
    }).format(date);
  }

  cancelPhotoSelection(): void {
    this.photoSelectionSequence++;
    this.clearSelectedPhoto();
    this.photoError = '';
    this.photoMessage = 'تم إلغاء اختيار الصورة؛ الصورة الحالية لم تتغير.';
  }

  localProfileStatus() { return localProfileStatusPresentation(this.worker.local.profileStatus); }
  sourceLinkStatus() { return sourceLinkStatusPresentation(this.worker.source.linkStatus); }
  employmentStatus() { return localEmploymentStatusPresentation(this.worker.local.employmentStatus); }
  assignmentStatusMeta() { return assignmentStatusPresentation(this.assignmentStatus); }

  private applyWorker(worker: WorkerManagementProfile): void {
    this.photoSelectionSequence++;
    this.worker = worker;
    this.resetDraft();
    this.clearSelectedPhoto();
    this.changed.emit(worker);
  }

  private loadAttendanceHistory(page: number): void {
    if (!this.canViewAttendance || !this.validateAttendanceRange()) return;
    const workerId = this.worker.id;
    const requestSequence = ++this.attendanceHistoryRequestSequence;
    this.attendanceHistoryCancel$.next();
    this.attendanceHistoryState = 'loading';
    this.attendanceHistoryError = '';
    this.facade.loadAttendanceHistory(workerId, {
      fromDate: this.attendanceFromDate,
      toDate: this.attendanceToDate,
      page,
      pageSize: this.attendanceHistoryPageSize
    }).pipe(
      takeUntil(this.attendanceHistoryCancel$),
      takeUntil(this.destroy$)
    ).subscribe({
      next: (result: WorkerAttendanceHistoryPage) => {
        if (this.worker.id !== workerId || requestSequence !== this.attendanceHistoryRequestSequence) return;
        this.attendanceHistory = result.items;
        this.attendanceHistoryPage = result.page;
        this.attendanceHistoryPageSize = result.pageSize;
        this.attendanceHistoryTotalCount = result.totalCount;
        this.attendanceHistoryTotalPages = result.totalPages;
        this.attendanceHistoryState = 'loaded';
      },
      error: error => {
        if (this.worker.id !== workerId || requestSequence !== this.attendanceHistoryRequestSequence) return;
        this.attendanceHistory = [];
        this.attendanceHistoryState = 'error';
        this.attendanceHistoryError = this.describeApiError(error, 'تعذر تحميل سجل الحضور والانصراف.');
      }
    });
  }

  private resetAttendanceHistory(): void {
    this.attendanceHistoryRequestSequence++;
    this.attendanceHistoryCancel$.next();
    this.attendanceHistoryState = 'idle';
    this.attendanceHistoryError = '';
    this.attendanceHistory = [];
    this.attendanceHistoryPage = 1;
    this.attendanceHistoryTotalCount = 0;
    this.attendanceHistoryTotalPages = 1;
    this.setDefaultAttendanceRange();
  }

  private setDefaultAttendanceRange(): void {
    const today = new Intl.DateTimeFormat('en-CA', {
      year: 'numeric', month: '2-digit', day: '2-digit', timeZone: 'Africa/Cairo'
    }).format(new Date());
    const [year, month, day] = today.split('-').map(Number);
    const from = new Date(Date.UTC(year, month - 1, day));
    from.setUTCDate(from.getUTCDate() - 29);
    this.attendanceToDate = today;
    this.attendanceFromDate = from.toISOString().slice(0, 10);
  }

  private validateAttendanceRange(): boolean {
    if (!this.attendanceFromDate || !this.attendanceToDate) {
      this.attendanceRangeError = 'أدخل تاريخ البداية وتاريخ النهاية.';
      return false;
    }
    if (this.attendanceFromDate > this.attendanceToDate) {
      this.attendanceRangeError = 'تاريخ البداية يجب ألا يكون بعد تاريخ النهاية.';
      return false;
    }
    this.attendanceRangeError = '';
    return true;
  }

  private clearSelectedPhoto(): void {
    this.selectedPhoto = null;
    if (this.photoPreviewUrl && typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') {
      URL.revokeObjectURL(this.photoPreviewUrl);
    }
    this.photoPreviewUrl = '';
  }

  private async detectPhotoType(file: File): Promise<string | null> {
    try {
      const bytes = new Uint8Array(await file.slice(0, 12).arrayBuffer());
      if (bytes.length >= 8 && [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a].every((value, index) => bytes[index] === value)) return 'image/png';
      if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return 'image/jpeg';
      if (bytes.length >= 2 && bytes[0] === 0x42 && bytes[1] === 0x4d) return 'image/bmp';
      return null;
    } catch {
      return null;
    }
  }

  private describeApiError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      return ({
        400: 'بيانات العامل أو الصورة غير صحيحة.',
        401: 'انتهت صلاحية الجلسة. سجّل الدخول ثم أعد المحاولة.',
        403: 'لا تملك صلاحية تنفيذ هذا الإجراء.',
        404: 'لم يعد ملف العامل أو الصورة متاحًا.',
        413: 'حجم الصورة أكبر من الحد المسموح (5 MiB).',
        409: 'تغير ملف العامل؛ حدّث الصفحة ثم أعد المحاولة.',
        429: 'تم تجاوز حد الطلبات مؤقتًا. أعد المحاولة بعد قليل.'
      } as Record<number, string>)[error.status] ?? fallback;
    }
    return error instanceof Error && error.message ? error.message : fallback;
  }
}
