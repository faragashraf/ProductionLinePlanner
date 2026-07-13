import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { AdminRoleItem, AdminUserAuthorization, IamAdminService, PermissionCatalogGroup } from '../../../core/services/iam-admin.service';
import { PERMISSIONS } from '../../../core/config/permission-identifiers';
import { PermissionService } from '../../../core/services/permission.service';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';

interface EffectivePermissionGroup {
  capability: string;
  permissions: AdminUserAuthorization['effectivePermissions'];
}

@Component({
  selector: 'app-user-authorization-page',
  templateUrl: './user-authorization-page.component.html',
  styleUrls: ['./user-authorization-page.component.scss']
})
export class UserAuthorizationPageComponent implements OnInit {
  isLoading = true;
  isRefreshing = false;
  isSaving = false;
  hasError = false;
  errorMessage: string | null = null;
  authorization: AdminUserAuthorization | null = null;
  roles: AdminRoleItem[] = [];
  catalog: PermissionCatalogGroup[] = [];
  selectedRoles: string[] = [];
  selectedDirectGrants: string[] = [];
  selectedDirectDenies: string[] = [];
  readonly permissions = PERMISSIONS;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly adminService: IamAdminService,
    private readonly permissionService: PermissionService,
    private readonly confirmation: IamConfirmationService
  ) {}

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      const userId = params.get('id');
      if (!userId) {
        this.hasError = true;
        this.errorMessage = 'معرف المستخدم غير صالح.';
        this.isLoading = false;
        return;
      }

      this.load(userId, true);
    });
  }

  refresh(): void {
    if (this.authorization) {
      this.load(this.authorization.id, false);
    }
  }

  get effectivePermissionGroups(): EffectivePermissionGroup[] {
    const groups = new Map<string, AdminUserAuthorization['effectivePermissions']>();
    for (const permission of this.authorization?.effectivePermissions ?? []) {
      const capability = permission.permission.split('.')[0] || 'other';
      groups.set(capability, [...(groups.get(capability) ?? []), permission]);
    }

    return [...groups.entries()]
      .map(([capability, permissions]) => ({ capability, permissions }))
      .sort((left, right) => left.capability.localeCompare(right.capability));
  }

  get pendingChangeCount(): number {
    if (!this.authorization) {
      return 0;
    }

    return Number(!this.sameValues(this.selectedRoles, this.authorization.roles)) +
      this.differenceCount(this.authorization.directGrants, this.selectedDirectGrants) +
      this.differenceCount(this.authorization.directDenies, this.selectedDirectDenies);
  }

  get hasCriticalEffectivePermissions(): boolean {
    return (this.authorization?.effectivePermissions ?? []).some((permission) => permission.isCritical);
  }

  toggleRole(role: string, checked: boolean): void {
    this.selectedRoles = checked
      ? [...this.selectedRoles.filter((item) => item !== role), role]
      : this.selectedRoles.filter((item) => item !== role);
  }

  onGrantsChanged(grants: string[]): void {
    this.selectedDirectGrants = grants;
    this.selectedDirectDenies = this.selectedDirectDenies.filter((permission) => !this.contains(grants, permission));
  }

  onDeniesChanged(denies: string[]): void {
    this.selectedDirectDenies = denies;
    this.selectedDirectGrants = this.selectedDirectGrants.filter((permission) => !this.contains(denies, permission));
  }

  saveChanges(): void {
    const authorization = this.authorization;
    if (!authorization || this.isSaving || this.pendingChangeCount === 0) {
      return;
    }

    if (!this.confirmation.confirm(`سيتم تطبيق ${this.pendingChangeCount} تغييراً على صلاحيات المستخدم. هل تريد المتابعة؟`)) {
      return;
    }

    if (!this.permissionService.has(PERMISSIONS.users.manage)) {
      this.hasError = true;
      this.errorMessage = 'لا تملك سلطة تنفيذ تغيير صلاحيات المستخدم بصورة آمنة.';
      return;
    }

    this.isSaving = true;
    const roleIds = this.roles
      .filter((role) => this.selectedRoles.some((name) => name.toLowerCase() === role.role.toLowerCase()))
      .map((role) => role.id);
    this.adminService.replaceUserAuthorization(authorization.id, {
      roleIds,
      directGrants: this.selectedDirectGrants,
      directDenies: this.selectedDirectDenies
    }).pipe(finalize(() => this.isSaving = false)).subscribe({
      next: () => this.refresh(),
      error: (error: { message?: string }) => {
        this.hasError = true;
        this.errorMessage = error?.message || 'تعذر حفظ تغييرات الصلاحيات.';
      }
    });
  }

  sourceLabel(sources: string[]): string {
    const labels: Record<string, string> = { 'Role Grant': 'من الدور', 'User Grant': 'منح مباشر', 'User Deny': 'رفض مباشر' };
    return sources.length > 0 ? sources.map((source) => labels[source] || source).join(' + ') : 'غير معرف';
  }

  private load(userId: string, initial: boolean): void {
    this.isLoading = initial;
    this.isRefreshing = !initial;
    this.hasError = false;
    this.errorMessage = null;
    const roles$ = this.permissionService.has(PERMISSIONS.roles.view)
      ? this.adminService.getRoles()
      : of([] as AdminRoleItem[]);
    const catalog$ = this.permissionService.has(PERMISSIONS.permissions.assign)
      ? this.adminService.getPermissionCatalog()
      : of([] as PermissionCatalogGroup[]);

    forkJoin({ authorization: this.adminService.getUserAuthorization(userId), roles: roles$, catalog: catalog$ })
      .pipe(
        catchError((error: { message?: string }) => {
          this.hasError = true;
          this.errorMessage = error?.message || 'تعذر تحميل تفاصيل المستخدم.';
          return of(null);
        }),
        finalize(() => {
          this.isLoading = false;
          this.isRefreshing = false;
        })
      )
      .subscribe((result) => {
        if (!result) {
          return;
        }

        this.authorization = result.authorization;
        this.roles = result.roles;
        this.catalog = result.catalog;
        this.selectedRoles = [...result.authorization.roles];
        this.selectedDirectGrants = [...result.authorization.directGrants];
        this.selectedDirectDenies = [...result.authorization.directDenies];
      });
  }

  private changedOverrides(before: string[], after: string[]): string[] {
    return before.filter((permission) => !this.contains(after, permission));
  }

  private differenceCount(left: string[], right: string[]): number {
    return this.changedOverrides(left, right).length + this.changedOverrides(right, left).length;
  }

  private sameValues(left: string[], right: string[]): boolean {
    return left.length === right.length && left.every((value) => this.contains(right, value));
  }

  private contains(values: string[], value: string): boolean {
    return values.some((item) => item.toLowerCase() === value.toLowerCase());
  }
}
