import { Component, OnInit } from '@angular/core';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { AdminRoleDraft, AdminRoleItem, IamAdminService, PermissionCatalogGroup } from '../../../core/services/iam-admin.service';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';
import { PERMISSIONS } from '../../../core/config/permission-identifiers';
import { PermissionService } from '../../../core/services/permission.service';

@Component({
  selector: 'app-admin-roles-page',
  templateUrl: './admin-roles-page.component.html',
  styleUrls: ['./admin-roles-page.component.scss']
})
export class AdminRolesPageComponent implements OnInit {
  isLoading = true;
  hasError = false;
  errorMessage: string | null = null;
  roles: AdminRoleItem[] = [];
  catalog: PermissionCatalogGroup[] = [];
  selectedRole: AdminRoleItem | null = null;
  draft: AdminRoleDraft = { name: '', description: '', isActive: true };
  selectedPermissions: string[] = [];
  isSaving = false;
  readonly maxDescriptionLength = 500;
  readonly permissions = PERMISSIONS;

  constructor(
    private readonly adminService: IamAdminService,
    private readonly permissionService: PermissionService,
    private readonly confirmation: IamConfirmationService
  ) {}

  ngOnInit(): void {
    this.loadRoles();
  }

  loadRoles(): void {
    this.isLoading = true;
    this.hasError = false;
    this.errorMessage = null;

    const catalog$ = this.permissionService.has(PERMISSIONS.permissions.assign)
      ? this.adminService.getPermissionCatalog()
      : of([] as PermissionCatalogGroup[]);

    forkJoin({ roles: this.adminService.getRoles(), catalog: catalog$ })
      .pipe(
        catchError((error: { message?: string }) => {
          this.hasError = true;
          this.errorMessage = error?.message || 'تعذر تحميل الأدوار الآن.';
          return of({ roles: [] as AdminRoleItem[], catalog: [] as PermissionCatalogGroup[] });
        }),
        finalize(() => {
          this.isLoading = false;
        })
      )
      .subscribe(({ roles, catalog }) => {
        this.roles = roles;
        this.catalog = catalog;
        if (!this.hasError) {
          this.errorMessage = null;
        }
      });
  }

  roleTypeLabel(role: AdminRoleItem): string {
    if (role.isSystemRole) {
      return 'System role';
    }

    return role.isActive ? 'Active' : 'Inactive';
  }

  trackByRoleId(_: number, role: AdminRoleItem): string {
    return role.id;
  }

  startCreate(): void {
    this.selectedRole = null;
    this.draft = { name: '', description: '', isActive: true };
    this.selectedPermissions = [];
  }

  editRole(role: AdminRoleItem): void {
    this.selectedRole = role;
    this.draft = { name: role.name, description: role.description ?? '', isActive: role.isActive };
    this.selectedPermissions = [...role.permissions];
  }

  saveRole(): void {
    if (this.selectedRole?.isSystemRole) {
      return;
    }

    if (!this.draft.name.trim()) {
      this.hasError = true;
      this.errorMessage = 'اسم الدور مطلوب.';
      return;
    }

    this.isSaving = true;
    const request = this.selectedRole
      ? this.adminService.updateRole(this.selectedRole.id, {
          name: this.draft.name?.trim(), description: this.draft.description?.trim() || null, isActive: this.draft.isActive
        })
      : this.adminService.createRole(this.draft);

    request.pipe(finalize(() => this.isSaving = false)).subscribe({
      next: (role) => {
        this.selectedRole = role;
        this.draft = { name: role.name, description: role.description ?? '', isActive: role.isActive };
        const index = this.roles.findIndex((item) => item.id === role.id);
        this.roles = index >= 0 ? this.roles.map((item) => item.id === role.id ? role : item) : [...this.roles, role];
        this.hasError = false;
        this.errorMessage = null;
      },
      error: (error: { message?: string }) => this.setSaveError(error, 'تعذر حفظ الدور.')
    });
  }

  savePermissions(): void {
    if (!this.selectedRole || this.selectedRole.isSystemRole || this.isSaving) {
      return;
    }

    this.isSaving = true;
    this.adminService.setRolePermissions(this.selectedRole.id, this.selectedPermissions)
      .pipe(finalize(() => this.isSaving = false))
      .subscribe({
        next: (role) => {
          this.selectedRole = role;
          this.roles = this.roles.map((item) => item.id === role.id ? role : item);
          this.hasError = false;
          this.errorMessage = null;
        },
        error: (error: { message?: string }) => this.setSaveError(error, 'تعذر حفظ صلاحيات الدور.')
      });
  }

  deleteRole(role: AdminRoleItem): void {
    if (role.isSystemRole || role.assignedUsers > 0 || this.isSaving) {
      return;
    }

    if (!this.confirmation.confirm(`هل تريد حذف الدور ${role.name}؟`)) {
      return;
    }

    this.isSaving = true;
    this.adminService.deleteRole(role.id).pipe(finalize(() => this.isSaving = false)).subscribe({
      next: () => {
        this.roles = this.roles.filter((item) => item.id !== role.id);
        if (this.selectedRole?.id === role.id) {
          this.startCreate();
        }
      },
      error: (error: { message?: string }) => this.setSaveError(error, 'تعذر حذف الدور.')
    });
  }

  private setSaveError(error: { message?: string }, fallback: string): void {
    this.hasError = true;
    this.errorMessage = error?.message || fallback;
  }
}
