import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { finalize, Subject, takeUntil } from 'rxjs';
import { productionNavigationIconFor } from '../../shared/design-system/icons/production-icon-map';
import { PlpSectionNavigationItem } from '../../shared/product/plp-section-navigation.component';
import {
  WorkerAssignmentStatus,
  WorkerHistoryKind,
  WorkerLocalEmploymentStatus,
  WorkerManagementProfile,
  WorkerSourcePreviewItem
} from './worker-management.models';
import { WorkerManagementFacade } from './worker-management.facade';
import {
  assignmentStatusPresentation,
  formatWorkerCurrency,
  formatWorkerObservedAt,
  localEmploymentStatusPresentation,
  localProfileStatusPresentation,
  sourceLinkStatusPresentation,
  sourcePreviewPresentation
} from './worker-management.presentation';

type WorkerProfileSection = 'local' | 'source' | 'operations' | 'history' | 'source-preview';

const MAX_PHOTO_BYTES = 5 * 1024 * 1024;
const ALLOWED_PHOTO_TYPES = new Set(['image/jpeg', 'image/png', 'image/bmp']);

@Component({
  selector: 'app-worker-profile-workspace',
  templateUrl: './worker-profile-workspace.component.html',
  styleUrls: ['./worker-profile-workspace.component.scss']
})
export class WorkerProfileWorkspaceComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) worker!: WorkerManagementProfile;
  @Input() canManage = false;
  @Input() canViewAssignments = false;
  @Output() closed = new EventEmitter<void>();
  @Output() changed = new EventEmitter<WorkerManagementProfile>();

  readonly backIcon = productionNavigationIconFor('back', 'rtl');
  readonly sections: readonly PlpSectionNavigationItem[] = [
    { id: 'local', label: 'البيانات المحلية', icon: 'pi pi-home' },
    { id: 'source', label: 'مرجع الهوية الخارجي', icon: 'pi pi-eye' },
    { id: 'operations', label: 'التسكين والتشغيل', icon: 'pi pi-sitemap' },
    { id: 'history', label: 'السجل', icon: 'pi pi-history' },
    { id: 'source-preview', label: 'بيانات غير متاحة', icon: 'pi pi-info-circle' }
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
  private readonly destroy$ = new Subject<void>();

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
      this.resetDraft();
      this.clearSelectedPhoto();
      this.activeSection = 'local';
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.clearSelectedPhoto();
  }

  selectSection(sectionId: string): void {
    if (this.sections.some(section => section.id === sectionId)) {
      this.activeSection = sectionId as WorkerProfileSection;
      this.saveMessage = '';
      this.saveError = '';
    }
  }

  saveDraft(): void {
    if (!this.canManage || this.draftForm.invalid || this.isSaving) {
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

  onPhotoSelected(input: HTMLInputElement): void {
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

    if (!ALLOWED_PHOTO_TYPES.has(file.type.toLowerCase())) {
      this.photoError = 'الأنواع المسموحة هي JPEG وPNG وBMP فقط.';
      return;
    }

    this.selectedPhoto = file;
    if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
      this.photoPreviewUrl = URL.createObjectURL(file);
    }
  }

  uploadSelectedPhoto(): void {
    if (!this.canManage || !this.selectedPhoto || this.isPhotoBusy) return;

    this.isPhotoBusy = true;
    this.photoError = '';
    this.photoMessage = '';
    this.facade.uploadPhoto(this.worker.id, this.selectedPhoto).pipe(
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
    if (!this.canManage || !this.worker.local.photoUrl || this.isPhotoBusy) return;
    if (typeof window !== 'undefined' && !window.confirm('هل تريد حذف الصورة المحلية الحالية؟')) return;

    this.isPhotoBusy = true;
    this.photoError = '';
    this.photoMessage = '';
    this.facade.deletePhoto(this.worker.id).pipe(
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

  localProfileStatus() { return localProfileStatusPresentation(this.worker.local.profileStatus); }
  sourceLinkStatus() { return sourceLinkStatusPresentation(this.worker.source.linkStatus); }
  employmentStatus() { return localEmploymentStatusPresentation(this.worker.local.employmentStatus); }
  assignmentStatusMeta() { return assignmentStatusPresentation(this.assignmentStatus); }
  previewStatus(item: WorkerSourcePreviewItem) { return sourcePreviewPresentation(item.kind); }

  sourceValue(value: string | null): string {
    return value?.trim() || 'غير متاح من قاعدة بيانات التطبيق';
  }

  historyIcon(kind: WorkerHistoryKind): string {
    return ({ name: 'pi pi-user-edit', photo: 'pi pi-image', status: 'pi pi-info-circle', assignment: 'pi pi-map-marker' })[kind];
  }

  formatHistoryDate(value: string): string {
    return formatWorkerObservedAt(value);
  }

  private applyWorker(worker: WorkerManagementProfile): void {
    this.worker = worker;
    this.resetDraft();
    this.clearSelectedPhoto();
    this.changed.emit(worker);
  }

  private clearSelectedPhoto(): void {
    this.selectedPhoto = null;
    if (this.photoPreviewUrl && typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') {
      URL.revokeObjectURL(this.photoPreviewUrl);
    }
    this.photoPreviewUrl = '';
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
