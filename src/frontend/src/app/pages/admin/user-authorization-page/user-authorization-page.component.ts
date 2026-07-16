import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { AdminRoleItem, AdminUserAuthorization, IamAdminService, PermissionCatalogGroup } from '../../../core/services/iam-admin.service';
import { PERMISSIONS } from '../../../core/config/permission-identifiers';
import { PermissionService } from '../../../core/services/permission.service';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';
import { resolveUsersReturnUrl } from '../../../core/navigation/user-management-navigation';
import { iamCapabilityLabel, iamPermissionLabel, iamRoleLabel } from '../iam-display-labels';

interface PermissionViewItem {
  permission: string;
  capability: string;
  label: string;
  descriptionAr?: string | null;
  isCritical: boolean;
  granted: boolean;
  sources: string[];
  inheritedRoles: string[];
}

interface PermissionViewGroup {
  capability: string;
  label: string;
  permissions: PermissionViewItem[];
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
  returnUrl: string;
  readonly permissions = PERMISSIONS;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly adminService: IamAdminService,
    private readonly permissionService: PermissionService,
    private readonly confirmation: IamConfirmationService
  ) {
    const queryReturnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    const stateReturnUrl = this.router.getCurrentNavigation()?.extras.state?.['returnUrl'];
    this.returnUrl = resolveUsersReturnUrl(queryReturnUrl || stateReturnUrl);
  }

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

  backToUsers(): void {
    this.router.navigateByUrl(this.returnUrl);
  }

  get permissionGroups(): PermissionViewGroup[] {
    const effective = new Map(
      (this.authorization?.effectivePermissions ?? []).map((permission) => [permission.permission.toLowerCase(), permission])
    );
    const catalogPermissions = this.catalog.flatMap((group) => group.permissions);
    const catalogByName = new Map(catalogPermissions.map((permission) => [permission.name.toLowerCase(), permission]));
    const permissionNames = new Set<string>([
      ...catalogPermissions.map((permission) => permission.name),
      ...(this.authorization?.effectivePermissions ?? []).map((permission) => permission.permission)
    ]);
    const groups = new Map<string, PermissionViewItem[]>();

    for (const permissionName of permissionNames) {
      const effectivePermission = effective.get(permissionName.toLowerCase());
      const catalogPermission = catalogByName.get(permissionName.toLowerCase());
      const capability = catalogPermission?.capability || permissionName.split('.')[0] || 'other';
      const sources = effectivePermission?.sources ?? [];
      const permission: PermissionViewItem = {
        permission: permissionName,
        capability,
        label: iamPermissionLabel(permissionName, catalogPermission?.descriptionAr || effectivePermission?.descriptionAr),
        descriptionAr: catalogPermission?.descriptionAr || effectivePermission?.descriptionAr,
        isCritical: catalogPermission?.isCritical ?? effectivePermission?.isCritical ?? false,
        granted: effectivePermission?.granted ?? false,
        sources,
        inheritedRoles: this.previewInheritedRoleNames(permissionName, sources)
      };
      groups.set(capability, [...(groups.get(capability) ?? []), permission]);
    }

    return [...groups.entries()]
      .map(([capability, permissions]) => ({
        capability,
        label: iamCapabilityLabel(capability),
        permissions: permissions.sort((left, right) => left.label.localeCompare(right.label, 'ar'))
      }))
      .sort((left, right) => left.label.localeCompare(right.label, 'ar'));
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

  get selectedDirectOverrideCount(): number {
    return this.selectedDirectGrants.length + this.selectedDirectDenies.length;
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

  toggleDirectGrant(permission: string, checked: boolean): void {
    this.selectedDirectGrants = this.withSelection(this.selectedDirectGrants, permission, checked);
    if (checked) {
      this.selectedDirectDenies = this.withSelection(this.selectedDirectDenies, permission, false);
    }
  }

  toggleDirectDeny(permission: string, checked: boolean): void {
    this.selectedDirectDenies = this.withSelection(this.selectedDirectDenies, permission, checked);
    if (checked) {
      this.selectedDirectGrants = this.withSelection(this.selectedDirectGrants, permission, false);
    }
  }

  isDirectGrantSelected(permission: string): boolean {
    return this.contains(this.selectedDirectGrants, permission);
  }

  isDirectDenySelected(permission: string): boolean {
    return this.contains(this.selectedDirectDenies, permission);
  }

  previewGranted(permission: PermissionViewItem): boolean {
    if (this.isDirectDenySelected(permission.permission)) {
      return false;
    }
    if (this.isDirectGrantSelected(permission.permission) || permission.inheritedRoles.length > 0) {
      return true;
    }
    if (permission.sources.some((source) => source === 'Role Grant' || source.startsWith('Role Grant:'))) {
      return false;
    }
    return permission.granted && !permission.sources.some((source) => source === 'User Grant' || source === 'User Deny');
  }

  roleLabel(role: string): string {
    return iamRoleLabel(role);
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
    return sources.length > 0
      ? sources.map((source) => source.startsWith('Role Grant:')
        ? `من الدور: ${iamRoleLabel(source.slice('Role Grant:'.length))}`
        : labels[source] || source).join(' + ')
      : 'الافتراضي';
  }

  trackPermission(_: number, permission: PermissionViewItem): string {
    return permission.permission;
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

  private roleSourceNames(sources: string[]): string[] {
    const explicitRoles = sources
      .filter((source) => source.startsWith('Role Grant:'))
      .map((source) => source.slice('Role Grant:'.length))
      .filter(Boolean);
    if (explicitRoles.length > 0) {
      return [...new Set(explicitRoles)];
    }
    return sources.includes('Role Grant') ? [...(this.authorization?.roles ?? [])] : [];
  }

  private previewInheritedRoleNames(permission: string, sources: string[]): string[] {
    if (this.roles.length === 0) {
      return this.roleSourceNames(sources);
    }

    return this.roles
      .filter((role) => this.contains(this.selectedRoles, role.role) && this.contains(role.permissions, permission))
      .map((role) => role.role);
  }

  private withSelection(values: string[], permission: string, selected: boolean): string[] {
    if (!selected) {
      return values.filter((item) => item.toLowerCase() !== permission.toLowerCase());
    }
    return this.contains(values, permission) ? values : [...values, permission];
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
