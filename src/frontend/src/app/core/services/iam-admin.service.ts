import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, map, Observable, throwError } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { ApiResponse } from '../models/api-response.model';

export interface PermissionCatalogItem {
  name: string;
  capability: string;
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  isCritical: boolean;
  isActive: boolean;
}

export interface PermissionCatalogGroup {
  capability: string;
  permissions: PermissionCatalogItem[];
}

export interface AdminUserListItem {
  id: string;
  fullName: string;
  email: string;
  isActive: boolean;
  roles: string[];
}

export interface AdminUserDetails extends AdminUserListItem {
  preferredLanguage: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  roleIds: string[];
}

export interface AdminUserCreateRequest {
  fullName: string;
  /** Legacy technical name; functionally this is the login identifier / username. */
  email: string;
  password: string;
  roleIds: string[];
  isActive: boolean;
}

export interface AdminUserUpdateRequest {
  fullName: string;
  /** Legacy technical name; functionally this is the login identifier / username. */
  email: string;
  roleIds: string[];
  isActive: boolean;
}

export interface AdminUserRoleOption {
  id: string;
  name: string;
  isActive: boolean;
}

export interface AdminUserAuthorization {
  id: string;
  fullName: string;
  email: string;
  isActive: boolean;
  permissionsVersion: string;
  roles: string[];
  directGrants: string[];
  directDenies: string[];
  effectivePermissions: {
    permission: string;
    granted: boolean;
    sources: string[];
    isCritical: boolean;
    descriptionAr?: string | null;
    descriptionEn?: string | null;
  }[];
}

export interface AdminRoleItem {
  id: string;
  role: string;
  name: string;
  description?: string | null;
  isSystemRole: boolean;
  isActive: boolean;
  assignedUsers: number;
  permissions: string[];
}

export interface AdminRoleDraft {
  name: string;
  description?: string | null;
  isActive?: boolean;
}

export interface AdminRoleUpdate {
  name?: string;
  description?: string | null;
  isActive?: boolean;
}

export interface UserPermissionOverrideRequest {
  permission: string;
  effect: 'Grant' | 'Deny';
}

export interface UserAuthorizationUpdateRequest {
  roleIds: string[];
  directGrants: string[];
  directDenies: string[];
}

@Injectable({
  providedIn: 'root'
})
export class IamAdminService {
  constructor(private readonly http: HttpClient) {}

  getPermissionCatalog(): Observable<PermissionCatalogGroup[]> {
    return this.http
      .get<ApiResponse<PermissionCatalogGroup[]>>(buildApiUrl('/api/admin/permissions/catalog'))
      .pipe(map((response) => this.extractData(response)));
  }

  getUsers(): Observable<AdminUserListItem[]> {
    return this.http
      .get<ApiResponse<AdminUserListItem[]>>(buildApiUrl('/api/admin/users'))
      .pipe(map((response) => this.extractData(response)));
  }

  getUser(userId: string): Observable<AdminUserDetails> {
    return this.http
      .get<ApiResponse<AdminUserDetails>>(buildApiUrl(`/api/admin/users/${userId}`))
      .pipe(map((response) => this.extractData(response)), catchError((error) => this.toIamError(error)));
  }

  getUserRoleOptions(): Observable<AdminUserRoleOption[]> {
    return this.http
      .get<ApiResponse<AdminUserRoleOption[]>>(buildApiUrl('/api/admin/users/role-options'))
      .pipe(map((response) => this.extractData(response)), catchError((error) => this.toIamError(error)));
  }

  createUser(request: AdminUserCreateRequest): Observable<AdminUserDetails> {
    return this.http
      .post<ApiResponse<AdminUserDetails>>(buildApiUrl('/api/admin/users'), request)
      .pipe(map((response) => this.extractData(response)), catchError((error) => this.toIamError(error)));
  }

  updateUser(userId: string, request: AdminUserUpdateRequest): Observable<AdminUserDetails> {
    return this.http
      .put<ApiResponse<AdminUserDetails>>(buildApiUrl(`/api/admin/users/${userId}`), request)
      .pipe(map((response) => this.extractData(response)), catchError((error) => this.toIamError(error)));
  }

  getUserAuthorization(userId: string): Observable<AdminUserAuthorization> {
    return this.http
      .get<ApiResponse<AdminUserAuthorization>>(buildApiUrl(`/api/admin/users/${userId}/authorization`))
      .pipe(map((response) => this.extractData(response)));
  }

  getRoles(): Observable<AdminRoleItem[]> {
    return this.http
      .get<ApiResponse<AdminRoleItem[]>>(buildApiUrl('/api/admin/roles'))
      .pipe(map((response) => this.extractData(response)));
  }

  updateUserStatus(userId: string, isActive: boolean): Observable<void> {
    return this.http
      .patch<ApiResponse<unknown>>(buildApiUrl(`/api/admin/users/${userId}/status`), { isActive })
      .pipe(map((response) => this.extractData(response)), map(() => undefined));
  }

  updateUserRoles(userId: string, roles: string[]): Observable<void> {
    return this.http
      .patch<ApiResponse<unknown>>(buildApiUrl(`/api/admin/users/${userId}/roles`), { roles })
      .pipe(map((response) => this.extractData(response)), map(() => undefined));
  }

  replaceUserAuthorization(userId: string, request: UserAuthorizationUpdateRequest): Observable<void> {
    return this.http
      .put<ApiResponse<unknown>>(buildApiUrl(`/api/admin/users/${userId}/authorization`), request)
      .pipe(map((response) => this.extractData(response)), map(() => undefined), catchError((error) => this.toIamError(error)));
  }

  addUserPermissionOverride(userId: string, request: UserPermissionOverrideRequest): Observable<void> {
    return this.http
      .post<ApiResponse<unknown>>(buildApiUrl(`/api/admin/users/${userId}/permission-overrides`), request)
      .pipe(map((response) => this.extractData(response)), map(() => undefined));
  }

  removeUserPermissionOverride(userId: string, permission: string): Observable<void> {
    return this.http
      .delete<ApiResponse<unknown>>(buildApiUrl(`/api/admin/users/${userId}/permission-overrides/${encodeURIComponent(permission)}`))
      .pipe(map((response) => this.extractData(response)), map(() => undefined));
  }

  createRole(draft: AdminRoleDraft): Observable<AdminRoleItem> {
    return this.http
      .post<ApiResponse<AdminRoleItem>>(buildApiUrl('/api/admin/roles'), { name: draft.name.trim(), description: draft.description?.trim() || null })
      .pipe(map((response) => this.extractData(response)), catchError((error) => this.toIamError(error)));
  }

  updateRole(roleId: string, draft: AdminRoleUpdate): Observable<AdminRoleItem> {
    return this.http
      .patch<ApiResponse<AdminRoleItem>>(buildApiUrl(`/api/admin/roles/${roleId}`), draft)
      .pipe(map((response) => this.extractData(response)), catchError((error) => this.toIamError(error)));
  }

  setRolePermissions(roleId: string, permissionNames: string[]): Observable<AdminRoleItem> {
    return this.http
      .put<ApiResponse<AdminRoleItem>>(buildApiUrl(`/api/admin/roles/${roleId}/permissions`), { permissionNames })
      .pipe(map((response) => this.extractData(response)));
  }

  deleteRole(roleId: string): Observable<void> {
    return this.http.delete<void>(buildApiUrl(`/api/admin/roles/${roleId}`));
  }

  private extractData<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data === undefined || response.data === null) {
      throw new Error(response.error?.message || 'Unexpected API response.');
    }

    return response.data;
  }

  private toIamError(error: { error?: { error?: { message?: string }; message?: string }; message?: string }): Observable<never> {
    return throwError(() => new Error(error.error?.error?.message || error.error?.message || error.message || 'تعذر تنفيذ طلب إدارة المستخدمين.'));
  }
}
