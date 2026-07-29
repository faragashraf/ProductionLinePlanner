import { Component, HostListener, OnInit } from '@angular/core';
import { catchError, finalize, forkJoin, of, switchMap } from 'rxjs';
import {
  NotificationPolicyAdminService,
  NotificationPolicyDetails,
  NotificationPolicyListItem,
  NotificationRecipientKind,
  NotificationPolicyRecipientOptions,
  NotificationPolicyRecipientRule,
  NotificationPolicySeverity
} from '../../../core/services/notification-policy-admin.service';

type EnabledFilter = 'all' | 'enabled' | 'disabled';

@Component({
  selector: 'app-notification-policy-studio-page',
  templateUrl: './notification-policy-studio-page.component.html',
  styleUrls: ['./notification-policy-studio-page.component.scss']
})
export class NotificationPolicyStudioPageComponent implements OnInit {
  isLoading = true;
  isSaving = false;
  hasError = false;
  isLoadError = false;
  errorMessage: string | null = null;
  searchTerm = '';
  enabledFilter: EnabledFilter = 'all';
  severityFilter: 'all' | NotificationPolicySeverity = 'all';
  policies: NotificationPolicyListItem[] = [];
  recipientOptions: NotificationPolicyRecipientOptions = { users: [], roles: [], permissions: [], capabilityGroups: [] };
  selectedEventKey: string | null = null;
  draft: NotificationPolicyDetails | null = null;
  isDirty = false;
  readonly severities: NotificationPolicySeverity[] = ['Information', 'Success', 'Warning', 'Critical'];
  readonly recipientKinds: NotificationRecipientKind[] = ['AllActiveUsers', 'User', 'Role', 'Permission', 'CapabilityGroup', 'Creator', 'ExcludeActor'];
  readonly tokenSamples: Record<string, string> = {
    WorkerName: 'أحمد محمد',
    ActorName: 'مشرف الخط',
    LineName: 'خط التجميع 1',
    FactoryName: 'مصنع القاهرة',
    EmployeeCode: '1024',
    AttendanceTime: '07:44 ص',
    AssignmentText: 'التسكين الحالي: مرحلة التجميع، خط الإنتاج 2.'
  };

  constructor(private readonly policyService: NotificationPolicyAdminService) {}

  ngOnInit(): void {
    this.loadStudio();
  }

  get filteredPolicies(): NotificationPolicyListItem[] {
    const term = this.searchTerm.trim().toLowerCase();
    return this.policies.filter(policy => {
      const matchesSearch = !term || policy.displayName.toLowerCase().includes(term) || policy.eventKey.toLowerCase().includes(term);
      const matchesEnabled = this.enabledFilter === 'all' || (this.enabledFilter === 'enabled' ? policy.isEnabled : !policy.isEnabled);
      const matchesSeverity = this.severityFilter === 'all' || policy.severity === this.severityFilter;
      return matchesSearch && matchesEnabled && matchesSeverity;
    });
  }

  get canSave(): boolean {
    return this.validateDraft() === null;
  }

  loadStudio(): void {
    this.isLoading = true;
    this.hasError = false;
    this.isLoadError = false;
    this.errorMessage = null;
    forkJoin({ policies: this.policyService.listPolicies(), options: this.policyService.getRecipientOptions() })
      .pipe(
        catchError((error: { message?: string }) => {
          this.hasError = true;
          this.isLoadError = true;
          this.errorMessage = error.message || 'تعذر تحميل إعدادات السياسات.';
          return of({ policies: [] as NotificationPolicyListItem[], options: this.recipientOptions });
        }),
        finalize(() => this.isLoading = false)
      )
      .subscribe(({ policies, options }) => {
        this.policies = policies;
        this.recipientOptions = options;
        if (!this.hasError && policies.length > 0 && !this.selectedEventKey) {
          this.selectPolicy(policies[0], false);
        }
      });
  }

  selectPolicy(policy: NotificationPolicyListItem, confirmDiscard = true): void {
    if (confirmDiscard && this.isDirty && !window.confirm('لديك تعديلات غير محفوظة. هل تريد تجاهلها؟')) return;
    this.selectedEventKey = policy.eventKey;
    this.draft = null;
    this.hasError = false;
    this.isLoadError = false;
    this.errorMessage = null;
    this.policyService.getPolicy(policy.eventKey)
      .pipe(catchError((error: { message?: string }) => {
        this.hasError = true;
        this.errorMessage = error.message || 'تعذر تحميل تفاصيل السياسة.';
        return of(null);
      }))
      .subscribe(details => {
        if (!details) return;
        this.draft = this.cloneDetails(details);
        this.isDirty = false;
      });
  }

  markDirty(): void {
    this.isDirty = true;
  }

  addRule(): void {
    if (!this.draft) return;
    this.draft.recipientRules.push({
      recipientKind: 'Creator',
      isExcludeActor: false,
      sortOrder: this.draft.recipientRules.length,
      isActive: true
    });
    this.markDirty();
  }

  removeRule(index: number): void {
    if (!this.draft) return;
    this.draft.recipientRules.splice(index, 1);
    this.normalizeSortOrder();
    this.markDirty();
  }

  onRecipientKindChanged(rule: NotificationPolicyRecipientRule): void {
    rule.userId = null;
    rule.roleId = null;
    rule.permissionKey = null;
    rule.capabilityKey = null;
    rule.isExcludeActor = rule.recipientKind === 'ExcludeActor';
    this.markDirty();
  }

  insertToken(token: string, target: 'title' | 'message'): void {
    if (!this.draft) return;
    const placeholder = `{${token}}`;
    if (target === 'title') this.draft.titleTemplateAr += placeholder;
    else this.draft.messageTemplateAr += placeholder;
    this.markDirty();
  }

  preview(template: string): string {
    return template.replace(/\{([A-Za-z][A-Za-z0-9]*)\}/g, (_full, token: string) => this.tokenSamples[token] || `{${token}}`);
  }

  save(): void {
    if (!this.draft || this.isSaving) return;
    const validation = this.validateDraft();
    if (validation) {
      this.hasError = true;
      this.errorMessage = validation;
      return;
    }

    this.isSaving = true;
    this.hasError = false;
    this.isLoadError = false;
    this.errorMessage = null;
    const eventKey = this.draft.eventKey;
    this.policyService.updatePolicy(eventKey, {
      isEnabled: this.draft.isEnabled,
      severity: this.draft.severity,
      isToastEnabled: this.draft.isToastEnabled,
      isInboxEnabled: this.draft.isInboxEnabled,
      isSoundEnabled: this.draft.isSoundEnabled,
      isBrowserEnabled: this.draft.isBrowserEnabled,
      soundKey: this.draft.isSoundEnabled ? 'default' : null,
      titleTemplateAr: this.draft.titleTemplateAr.trim(),
      messageTemplateAr: this.draft.messageTemplateAr.trim(),
      rowVersion: this.draft.rowVersion,
      recipientRules: this.draft.recipientRules.map((rule, index) => this.toUpdateRule(rule, index))
    })
      .pipe(
        switchMap(() => this.policyService.getPolicy(eventKey)),
        finalize(() => this.isSaving = false)
      )
      .subscribe({
        next: details => {
          this.draft = this.cloneDetails(details);
          this.isDirty = false;
          this.policies = this.policies.map(policy => policy.eventKey === details.eventKey ? this.toListItem(details) : policy);
        },
        error: (error: { message?: string }) => {
          this.hasError = true;
          this.errorMessage = error.message || 'تعذر حفظ السياسة.';
        }
      });
  }

  severityLabel(severity: NotificationPolicySeverity): string {
    return ({ Information: 'معلومة', Success: 'نجاح', Warning: 'تحذير', Critical: 'حرج' })[severity];
  }

  recipientKindLabel(kind: NotificationRecipientKind): string {
    return ({ User: 'مستخدم', Role: 'دور', Permission: 'صلاحية', CapabilityGroup: 'مجموعة قدرات', Creator: 'منشئ الحدث', ExcludeActor: 'استبعاد المنفذ', AllActiveUsers: 'كل مستخدمي التطبيق النشطين' })[kind];
  }

  userLabel(id: string | null | undefined): string {
    return this.recipientOptions.users.find(user => user.id === id)?.fullName || 'اختر مستخدمًا';
  }

  trackPolicy(_: number, policy: NotificationPolicyListItem): string { return policy.eventKey; }
  trackRule(_: number, rule: NotificationPolicyRecipientRule): string { return rule.id || `new-rule-${rule.sortOrder}`; }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (!this.isDirty) return;
    event.preventDefault();
    event.returnValue = '';
  }

  private validateDraft(): string | null {
    if (!this.draft) return 'اختر سياسة أولاً.';
    if (!this.draft.titleTemplateAr.trim() || !this.draft.messageTemplateAr.trim()) return 'العنوان والرسالة العربية مطلوبان.';
    if (this.draft.titleTemplateAr.trim().length > 200 || this.draft.messageTemplateAr.trim().length > 2000) return 'تجاوزت القوالب الحدود المسموح بها.';
    if (this.draft.recipientRules.some(rule => rule.recipientKind === 'Role' && !this.isKnownRole(rule.roleId))) return 'اختر الدور المستلم';
    return null;
  }

  private isKnownRole(roleId: string | null | undefined): boolean {
    return !!roleId && this.recipientOptions.roles.some(role => role.id === roleId);
  }

  private toUpdateRule(rule: NotificationPolicyRecipientRule, sortOrder: number): NotificationPolicyRecipientRule {
    return {
      recipientKind: rule.recipientKind,
      userId: rule.recipientKind === 'User' ? rule.userId ?? null : null,
      roleId: rule.recipientKind === 'Role' ? rule.roleId ?? null : null,
      permissionKey: rule.recipientKind === 'Permission' ? rule.permissionKey ?? null : null,
      capabilityKey: rule.recipientKind === 'CapabilityGroup' ? rule.capabilityKey ?? null : null,
      isExcludeActor: rule.recipientKind === 'ExcludeActor',
      sortOrder,
      isActive: rule.isActive
    };
  }

  private normalizeSortOrder(): void {
    this.draft?.recipientRules.forEach((rule, index) => rule.sortOrder = index);
  }

  private cloneDetails(details: NotificationPolicyDetails): NotificationPolicyDetails {
    return { ...details, allowedTokens: [...details.allowedTokens], recipientRules: details.recipientRules.map(rule => ({ ...rule })) };
  }

  private toListItem(details: NotificationPolicyDetails): NotificationPolicyListItem {
    const { allowedTokens, soundKey, titleTemplateAr, messageTemplateAr, rowVersion, recipientRules, ...item } = details;
    return item;
  }
}
