import { Component, OnInit } from '@angular/core';
import { finalize } from 'rxjs';
import { DepartmentItem, ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';

@Component({
  selector: 'app-manufacturing-departments-page',
  templateUrl: './manufacturing-departments-page.component.html',
  styleUrls: ['./manufacturing-departments-page.component.scss']
})
export class ManufacturingDepartmentsPageComponent implements OnInit {
  departments: DepartmentItem[] = [];
  filteredDepartments: DepartmentItem[] = [];
  searchTerm = '';
  isLoading = false;
  hasLoadedOnce = false;
  hasError = false;
  errorMessage = 'تعذر تحميل بيانات الأقسام، يرجى المحاولة مرة أخرى.';
  errorRetryText = 'إعادة المحاولة';

  constructor(private readonly api: ManufacturingMasterDataApiService) {}

  ngOnInit(): void {
    this.loadDepartments();
  }

  get isEmpty(): boolean {
    return !this.isLoading && !this.hasError && this.filteredDepartments.length === 0 && this.searchTerm.trim().length === 0;
  }

  get isSearchableEmpty(): boolean {
    return !this.isLoading && !this.hasError && this.filteredDepartments.length === 0 && this.searchTerm.trim().length > 0;
  }

  onSearchValue(value: string): void {
    this.searchTerm = value.trim();
    this.applyFilter();
  }

  onClearSearch(): void {
    this.searchTerm = '';
    this.applyFilter();
  }

  onRefresh(): void {
    this.loadDepartments();
  }

  trackByDepartmentId(_: number, item: DepartmentItem): string | number {
    return item.id ?? item.departmentId ?? item.code ?? item.nameAr ?? item.name ?? '';
  }

  departmentCode(item: DepartmentItem): string { return item.code ?? String(item.departmentId ?? '-'); }
  departmentName(item: DepartmentItem): string { return item.nameAr ?? item.name ?? '-'; }

  displayStatus(item: DepartmentItem): string {
    if (typeof item.status === 'string' && item.status.trim().length > 0) {
      return item.status.trim();
    }

    if (typeof item.isActive === 'boolean') {
      return item.isActive ? 'نشط' : 'معطل';
    }

    return '-';
  }

  private loadDepartments(): void {
    this.isLoading = true;
    this.hasError = false;

    this.api.departments()
      .pipe(finalize(() => {
        this.isLoading = false;
        this.hasLoadedOnce = true;
      }))
      .subscribe({
        next: (departments) => {
          this.departments = departments;
          this.applyFilter();
        },
        error: (error) => {
          this.hasError = true;
          if (this.departments.length === 0) {
            this.filteredDepartments = [];
          }
          this.errorMessage = this.extractErrorMessage(error);
        }
      });
  }

  private applyFilter(): void {
    const normalizedSearch = this.searchTerm.trim().toLowerCase();
    if (normalizedSearch.length === 0) {
      this.filteredDepartments = this.departments;
      return;
    }

    this.filteredDepartments = this.departments.filter((department) => {
      return (
        this.departmentCode(department).toLowerCase().includes(normalizedSearch) ||
        this.departmentName(department).toLowerCase().includes(normalizedSearch)
      );
    });
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof Error && error.message.length > 0) {
      return error.message;
    }

    return 'حدث خطأ غير متوقع أثناء تحميل البيانات.';
  }
}
