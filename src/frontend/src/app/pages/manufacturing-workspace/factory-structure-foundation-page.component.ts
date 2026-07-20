import { Component, OnDestroy, OnInit } from '@angular/core';
import { Observable, Subject, finalize, forkJoin, takeUntil } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import {
  DepartmentItem,
  FactoryItem,
  ManufacturingMasterDataApiService,
  ProductionLineOption
} from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';

interface FactoryDraft {
  id: string;
  name: string;
  code: string;
  location: string;
}

interface DepartmentDraft {
  id: string;
  factoryId: string;
  code: string;
  nameAr: string;
  nameEn: string;
  sequenceOrder: number;
}

interface LineDraft {
  id: string;
  factoryId: string;
  departmentId: string;
  name: string;
  lineCode: string;
  sequenceOrder: number;
}

type FactoryStructureFormId = 'factory' | 'department' | 'line';

/**
 * Administrative view of the physical factory hierarchy only.
 * Stage administration and worker staffing intentionally live in their
 * dedicated workspace screens so this page remains Factory → Department → Line.
 */
@Component({
  selector: 'app-factory-structure-foundation-page',
  templateUrl: './factory-structure-foundation-page.component.html',
  styleUrls: ['./factory-structure-foundation-page.component.scss']
})
export class FactoryStructureFoundationPageComponent implements OnInit, OnDestroy {
  readonly permissions = PERMISSIONS;

  factories: FactoryItem[] = [];
  departments: DepartmentItem[] = [];
  lines: ProductionLineOption[] = [];
  selectedFactoryId = '';
  selectedDepartmentId = '';
  selectedLineId = '';
  searchTerm = '';
  isLoading = false;
  isSaving = false;
  hasLoadedOnce = false;
  hasError = false;
  errorMessage = 'تعذر تحميل بنية المصنع، يرجى المحاولة مرة أخرى.';
  successMessage = '';
  activeForm: FactoryStructureFormId | null = null;

  factoryDraft: FactoryDraft = this.emptyFactoryDraft();
  departmentDraft: DepartmentDraft = this.emptyDepartmentDraft();
  lineDraft: LineDraft = this.emptyLineDraft();
  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly masterDataApi: ManufacturingMasterDataApiService,
    private readonly permissionService: PermissionService
  ) {}

  ngOnInit(): void {
    this.reload();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get filteredFactories(): FactoryItem[] {
    const search = this.normalizedSearch;
    return this.factories.filter(item =>
      !search || item.name.toLowerCase().includes(search) || item.code.toLowerCase().includes(search));
  }

  get visibleDepartments(): DepartmentItem[] {
    return this.selectedFactoryId
      ? this.departments.filter(item => item.factoryId === this.selectedFactoryId)
      : [];
  }

  get visibleLines(): ProductionLineOption[] {
    const factoryLines = this.selectedFactoryId
      ? this.lines.filter(item => item.factoryId === this.selectedFactoryId)
      : [];
    if (!this.selectedDepartmentId) return factoryLines;
    if (this.selectedDepartmentId === 'unassigned') return factoryLines.filter(item => !item.departmentId);
    return factoryLines.filter(item => item.departmentId === this.selectedDepartmentId);
  }

  get unassignedLines(): ProductionLineOption[] {
    return this.selectedFactoryId
      ? this.lines.filter(item => item.factoryId === this.selectedFactoryId && !item.departmentId)
      : [];
  }

  get isEmpty(): boolean {
    return !this.isLoading && !this.hasError && !this.hasStructureData;
  }

  get hasStructureData(): boolean {
    return this.factories.length > 0 || this.departments.length > 0 || this.lines.length > 0;
  }

  get canManage(): boolean {
    return this.permissionService.hasPermission(this.permissions.factoryStructure.manage);
  }

  get canManageDepartments(): boolean {
    return this.permissionService.hasPermission(this.permissions.departments.manage);
  }

  onSearchValue(value: string): void {
    this.searchTerm = value.trim();
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
      lines: this.masterDataApi.allProductionLines(),
      departments: this.masterDataApi.departments(undefined, true)
    })
      .pipe(finalize(() => {
        this.isLoading = false;
        this.hasLoadedOnce = true;
      }), takeUntil(this.destroy$))
      .subscribe({
        next: data => {
          this.factories = data.factories;
          this.lines = data.lines;
          this.departments = data.departments;
          this.resetSelectionsForReload();
        },
        error: error => this.setLoadError(error)
      });
  }

  selectFactory(id: string): void {
    this.selectedFactoryId = id;
    this.selectedDepartmentId = '';
    this.selectedLineId = '';
    this.lineDraft.factoryId = id;
    this.lineDraft.departmentId = '';
    this.departmentDraft.factoryId = id;
  }

  selectDepartment(id: string): void {
    this.selectedDepartmentId = id;
    this.selectedLineId = '';
    this.lineDraft.factoryId = this.selectedFactoryId;
    this.lineDraft.departmentId = id === 'unassigned' ? '' : id;
  }

  selectLine(id: string): void {
    this.selectedLineId = id;
  }

  editFactory(item: FactoryItem): void {
    this.factoryDraft = { id: item.id, name: item.name, code: item.code, location: item.location ?? '' };
    this.activeForm = 'factory';
  }

  saveFactory(): void {
    if (!this.factoryDraft.name.trim() || !this.factoryDraft.code.trim()) {
      this.setValidationError('اسم المصنع وكوده مطلوبان.');
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

  editDepartment(item: DepartmentItem): void {
    if (!item.id || !item.factoryId) return;
    this.departmentDraft = {
      id: item.id,
      factoryId: item.factoryId,
      code: item.code ?? '',
      nameAr: item.nameAr ?? item.name ?? '',
      nameEn: item.nameEn ?? '',
      sequenceOrder: item.sequenceOrder ?? 0
    };
    this.activeForm = 'department';
  }

  saveDepartment(): void {
    if (!this.departmentDraft.factoryId || !this.departmentDraft.code.trim() || !this.departmentDraft.nameAr.trim()) {
      this.setValidationError('المصنع وكود القسم واسمه بالعربية مطلوبة.');
      return;
    }
    const payload = {
      factoryId: this.departmentDraft.factoryId,
      code: this.departmentDraft.code.trim(),
      nameAr: this.departmentDraft.nameAr.trim(),
      nameEn: this.departmentDraft.nameEn.trim() || null,
      sequenceOrder: Number(this.departmentDraft.sequenceOrder) || 0,
      isActive: true
    };
    const request = this.departmentDraft.id
      ? this.masterDataApi.updateDepartment(this.departmentDraft.id, {
        code: payload.code,
        nameAr: payload.nameAr,
        nameEn: this.departmentDraft.nameEn.trim(),
        sequenceOrder: payload.sequenceOrder,
        isActive: true
      })
      : this.masterDataApi.createDepartment(payload);
    this.save(request, () => {
      this.departmentDraft = this.emptyDepartmentDraft();
      this.departmentDraft.factoryId = this.selectedFactoryId;
      this.activeForm = null;
    });
  }

  setDepartmentActive(item: DepartmentItem, isActive: boolean): void {
    if (item.id) this.save(this.masterDataApi.updateDepartment(item.id, { isActive }));
  }

  deleteDepartment(item: DepartmentItem): void {
    if (!item.id || !window.confirm(`حذف القسم ${item.nameAr ?? item.name ?? item.code ?? ''} نهائيًا؟`)) return;
    this.save(this.masterDataApi.deleteDepartment(item.id), () => {
      if (this.selectedDepartmentId === item.id) this.selectDepartment('');
    });
  }

  editLine(item: ProductionLineOption): void {
    this.lineDraft = {
      id: item.id,
      factoryId: item.factoryId,
      departmentId: item.departmentId ?? '',
      name: item.name,
      lineCode: item.lineCode ?? '',
      sequenceOrder: item.sequenceOrder
    };
    this.activeForm = 'line';
  }

  saveLine(): void {
    if (!this.lineDraft.factoryId || !this.lineDraft.name.trim() || (!this.lineDraft.id && !this.lineDraft.departmentId)) {
      this.setValidationError('المصنع والقسم واسم الخط مطلوبة عند إنشاء خط جديد.');
      return;
    }
    const payload = {
      factoryId: this.lineDraft.factoryId,
      departmentId: this.lineDraft.departmentId || null,
      name: this.lineDraft.name.trim(),
      lineCode: this.lineDraft.lineCode.trim() || null,
      sequenceOrder: Number(this.lineDraft.sequenceOrder) || 0,
      isActive: true
    };
    const request = this.lineDraft.id
      ? this.masterDataApi.updateProductionLine(this.lineDraft.id, {
        name: payload.name,
        ...(payload.departmentId ? { departmentId: payload.departmentId } : {}),
        lineCode: payload.lineCode,
        sequenceOrder: payload.sequenceOrder,
        isActive: true
      })
      : this.masterDataApi.createProductionLine(payload);
    this.save(request, () => {
      this.lineDraft = this.emptyLineDraft();
      this.lineDraft.factoryId = this.selectedFactoryId;
      this.lineDraft.departmentId = this.selectedDepartmentId === 'unassigned' ? '' : this.selectedDepartmentId;
      this.activeForm = null;
    });
  }

  setLineActive(item: ProductionLineOption, isActive: boolean): void {
    this.save(this.masterDataApi.updateProductionLine(item.id, { isActive }));
  }

  private get normalizedSearch(): string {
    return this.searchTerm.trim().toLowerCase();
  }

  private resetSelectionsForReload(): void {
    if (!this.selectedFactoryId || !this.factories.some(item => item.id === this.selectedFactoryId)) {
      this.selectedFactoryId = this.factories[0]?.id ?? '';
    }
    this.selectedDepartmentId = '';
    this.selectedLineId = '';
    this.departmentDraft.factoryId = this.selectedFactoryId;
    this.lineDraft.factoryId = this.selectedFactoryId;
    this.lineDraft.departmentId = '';
  }

  private save(request: Observable<unknown>, success?: () => void): void {
    this.isSaving = true;
    this.hasError = false;
    this.successMessage = '';
    request.pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        success?.();
        this.successMessage = 'تم حفظ التغيير.';
        this.isSaving = false;
        this.reload();
      },
      error: error => {
        this.isSaving = false;
        this.setLoadError(error);
      }
    });
  }

  private setValidationError(message: string): void {
    this.hasError = true;
    this.errorMessage = message;
  }

  private setLoadError(error: unknown): void {
    this.hasError = true;
    this.errorMessage = this.extractErrorMessage(error);
  }

  private emptyFactoryDraft(): FactoryDraft { return { id: '', name: '', code: '', location: '' }; }
  private emptyDepartmentDraft(): DepartmentDraft { return { id: '', factoryId: '', code: '', nameAr: '', nameEn: '', sequenceOrder: 0 }; }
  private emptyLineDraft(): LineDraft { return { id: '', factoryId: '', departmentId: '', name: '', lineCode: '', sequenceOrder: 0 }; }

  private extractErrorMessage(error: unknown): string {
    return error instanceof Error && error.message.length > 0
      ? error.message
      : 'حدث خطأ غير متوقع أثناء حفظ أو تحميل بنية المصنع.';
  }
}
