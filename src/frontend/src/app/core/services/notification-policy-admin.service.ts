import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, map, Observable, throwError } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { ApiResponse } from '../models/api-response.model';

export type NotificationPolicySeverity = 'Information' | 'Success' | 'Warning' | 'Critical';
export type NotificationRecipientKind = 'User' | 'Role' | 'Permission' | 'CapabilityGroup' | 'Creator' | 'ExcludeActor' | 'AllActiveUsers';

export interface NotificationPolicyListItem {
  eventKey: string;
  displayName: string;
  isEnabled: boolean;
  severity: NotificationPolicySeverity;
  isToastEnabled: boolean;
  isInboxEnabled: boolean;
  isSoundEnabled: boolean;
  isBrowserEnabled: boolean;
  updatedAtUtc: string;
}

export interface NotificationPolicyRecipientRule {
  id?: string;
  recipientKind: NotificationRecipientKind;
  userId?: string | null;
  roleId?: string | null;
  permissionKey?: string | null;
  capabilityKey?: string | null;
  isExcludeActor: boolean;
  sortOrder: number;
  isActive: boolean;
}

export interface NotificationPolicyDetails extends NotificationPolicyListItem {
  allowedTokens: string[];
  soundKey: 'default' | null;
  titleTemplateAr: string;
  messageTemplateAr: string;
  rowVersion: string;
  recipientRules: NotificationPolicyRecipientRule[];
}

export interface NotificationPolicyRecipientOptions {
  users: { id: string; fullName: string; email: string }[];
  roles: { id: string; name: string }[];
  permissions: { name: string; capability: string; descriptionAr?: string | null }[];
  capabilityGroups: string[];
}

export interface NotificationPolicyUpdateRequest {
  isEnabled: boolean;
  severity: NotificationPolicySeverity;
  isToastEnabled: boolean;
  isInboxEnabled: boolean;
  isSoundEnabled: boolean;
  isBrowserEnabled: boolean;
  soundKey: 'default' | null;
  titleTemplateAr: string;
  messageTemplateAr: string;
  rowVersion: string;
  recipientRules: NotificationPolicyRecipientRule[];
}

@Injectable({ providedIn: 'root' })
export class NotificationPolicyAdminService {
  constructor(private readonly http: HttpClient) {}

  listPolicies(): Observable<NotificationPolicyListItem[]> {
    return this.http.get<ApiResponse<NotificationPolicyListItem[]>>(buildApiUrl('/api/admin/notification-policies'))
      .pipe(map(response => this.data(response)), catchError(error => this.toError(error)));
  }

  getPolicy(eventKey: string): Observable<NotificationPolicyDetails> {
    return this.http.get<ApiResponse<NotificationPolicyDetails>>(
      buildApiUrl(`/api/admin/notification-policies/${encodeURIComponent(eventKey)}`)
    ).pipe(map(response => this.data(response)), catchError(error => this.toError(error)));
  }

  getRecipientOptions(): Observable<NotificationPolicyRecipientOptions> {
    return this.http.get<ApiResponse<NotificationPolicyRecipientOptions>>(
      buildApiUrl('/api/admin/notification-policies/recipient-options')
    ).pipe(map(response => this.data(response)), catchError(error => this.toError(error)));
  }

  updatePolicy(eventKey: string, request: NotificationPolicyUpdateRequest): Observable<NotificationPolicyDetails> {
    return this.http.put<ApiResponse<NotificationPolicyDetails>>(
      buildApiUrl(`/api/admin/notification-policies/${encodeURIComponent(eventKey)}`),
      request
    ).pipe(map(response => this.data(response)), catchError(error => this.toError(error)));
  }

  private data<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data === null || response.data === undefined) {
      throw new Error(response.error?.message || 'تعذر تنفيذ طلب سياسات الإشعارات.');
    }

    return response.data;
  }

  private toError(error: { error?: { error?: { code?: string; message?: string }; message?: string }; message?: string }): Observable<never> {
    const message = error.error?.error?.message || error.error?.message || error.message || 'تعذر الاتصال بخدمة سياسات الإشعارات.';
    return throwError(() => new Error(message));
  }
}
