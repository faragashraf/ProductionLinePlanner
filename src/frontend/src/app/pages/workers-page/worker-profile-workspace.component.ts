import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { FormBuilder, Validators } from '@angular/forms';
import { productionNavigationIconFor } from '../../shared/design-system/icons/production-icon-map';
import { PlpSectionNavigationItem } from '../../shared/product/plp-section-navigation.component';
import {
  WorkerAssignmentStatus,
  WorkerHistoryKind,
  WorkerManagementProfile,
  WorkerSourcePreviewItem
} from './worker-management.models';
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

@Component({
  selector: 'app-worker-profile-workspace',
  templateUrl: './worker-profile-workspace.component.html',
  styleUrls: ['./worker-profile-workspace.component.scss']
})
export class WorkerProfileWorkspaceComponent implements OnChanges {
  @Input({ required: true }) worker!: WorkerManagementProfile;
  @Input() canManage = false;
  @Input() canViewAssignments = false;
  @Output() closed = new EventEmitter<void>();

  readonly backIcon = productionNavigationIconFor('back', 'rtl');
  readonly sections: readonly PlpSectionNavigationItem[] = [
    { id: 'local', label: 'البيانات المحلية', icon: 'pi pi-home' },
    { id: 'source', label: 'بيانات المصدر', icon: 'pi pi-eye' },
    { id: 'operations', label: 'التسكين والتشغيل', icon: 'pi pi-sitemap' },
    { id: 'history', label: 'السجل', icon: 'pi pi-history' },
    { id: 'source-preview', label: 'معاينة بيانات المصدر', icon: 'pi pi-search' }
  ];
  readonly localStatusOptions = [
    { value: 'active', label: 'نشط محليًا' },
    { value: 'inactive', label: 'غير نشط محليًا' },
    { value: 'not-set', label: 'غير محددة محليًا' }
  ] as const;

  activeSection: WorkerProfileSection = 'local';
  draftMessage = '';

  readonly draftForm = this.formBuilder.nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    salaryAmount: [null as number | null, [Validators.min(0)]],
    employmentStatus: ['not-set' as 'active' | 'inactive' | 'not-set']
  });

  constructor(private readonly formBuilder: FormBuilder) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['worker'] && this.worker) {
      this.resetDraft();
      this.activeSection = 'local';
    }
  }

  selectSection(sectionId: string): void {
    if (this.sections.some(section => section.id === sectionId)) {
      this.activeSection = sectionId as WorkerProfileSection;
      this.draftMessage = '';
    }
  }

  saveDraft(): void {
    if (!this.canManage || this.draftForm.invalid) {
      this.draftForm.markAllAsTouched();
      return;
    }
    this.draftMessage = 'حُفظت المسودة داخل العرض التجريبي فقط. لم تُرسل بيانات ولم يتغير السجل الأصلي.';
  }

  resetDraft(): void {
    this.draftForm.reset({
      displayName: this.worker.local.displayName,
      salaryAmount: this.worker.local.salary?.amount ?? null,
      employmentStatus: this.worker.local.employmentStatus
    });
    this.draftMessage = '';
  }

  get hasDraftChanges(): boolean {
    const draft = this.draftForm.getRawValue();
    return draft.displayName !== this.worker.local.displayName
      || draft.salaryAmount !== (this.worker.local.salary?.amount ?? null)
      || draft.employmentStatus !== this.worker.local.employmentStatus;
  }

  get assignmentStatus(): WorkerAssignmentStatus {
    const hasPermanent = this.worker.assignments.some(item => item.kind === 'permanent');
    const hasTemporary = this.worker.assignments.some(item => item.kind === 'temporary');
    if (!hasPermanent && !hasTemporary) return 'unassigned';
    return hasPermanent && hasTemporary ? 'mixed' : 'assigned';
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
    return value?.trim() || 'غير متاح في آخر قراءة';
  }

  historyIcon(kind: WorkerHistoryKind): string {
    return ({ name: 'pi pi-user-edit', photo: 'pi pi-image', status: 'pi pi-info-circle', assignment: 'pi pi-map-marker' })[kind];
  }

  formatHistoryDate(value: string): string {
    return formatWorkerObservedAt(value);
  }
}
