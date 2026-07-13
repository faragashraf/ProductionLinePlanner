import { Component, OnInit } from '@angular/core';
import { catchError, finalize, of } from 'rxjs';
import { PermissionCatalogGroup, PermissionCatalogItem, IamAdminService } from '../../../core/services/iam-admin.service';

@Component({
  selector: 'app-permission-catalog-page',
  templateUrl: './permission-catalog-page.component.html',
  styleUrls: ['./permission-catalog-page.component.scss']
})
export class PermissionCatalogPageComponent implements OnInit {
  isLoading = true;
  isRefreshing = false;
  hasError = false;
  errorMessage: string | null = null;
  searchTerm = '';

  catalog: PermissionCatalogGroup[] = [];
  filtered: PermissionCatalogGroup[] = [];

  constructor(private readonly adminService: IamAdminService) {}

  ngOnInit(): void {
    this.loadCatalog(true);
  }

  loadCatalog(force = false): void {
    if (force) {
      this.hasError = false;
      this.errorMessage = null;
      this.isLoading = true;
    } else {
      this.isRefreshing = true;
    }

    this.adminService.getPermissionCatalog()
      .pipe(
        catchError((error: { message?: string }) => {
          this.hasError = true;
          this.errorMessage = error?.message || 'تعذر تحميل الكتالوج الآن.';
          return of(null);
        }),
        finalize(() => {
          this.isLoading = false;
          this.isRefreshing = false;
          this.applySearch();
        })
      )
      .subscribe((catalog) => {
        if (catalog === null) {
          return;
        }

        this.catalog = catalog
          .map((group) => ({
            ...group,
            permissions: [...group.permissions].sort((left, right) => left.name.localeCompare(right.name))
          }))
          .sort((left, right) => left.capability.localeCompare(right.capability));

        this.hasError = false;
        this.errorMessage = null;
      });
  }

  onSearchTermChanged(term: string): void {
    this.searchTerm = term;
    this.applySearch();
  }

  private applySearch(): void {
    const normalized = this.searchTerm.trim().toLowerCase();

    if (!normalized) {
      this.filtered = this.catalog;
      return;
    }

    this.filtered = this.catalog
      .map((group) => ({
        ...group,
        permissions: group.permissions.filter((permission) => this.includesPermission(permission, normalized))
      }))
      .filter((group) => group.permissions.length > 0);
  }

  private includesPermission(permission: PermissionCatalogItem, normalized: string): boolean {
    return (
      permission.name.toLowerCase().includes(normalized) ||
      permission.capability.toLowerCase().includes(normalized) ||
      (permission.descriptionAr?.toLowerCase() || '').includes(normalized) ||
      (permission.descriptionEn?.toLowerCase() || '').includes(normalized)
    );
  }
}
