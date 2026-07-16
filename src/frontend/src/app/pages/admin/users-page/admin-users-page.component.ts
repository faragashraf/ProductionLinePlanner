import { Component, OnInit } from '@angular/core';
import { FormBuilder } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import {
  AdminUserDetails,
  AdminUserListItem,
  AdminUserRoleOption,
  IamAdminService
} from '../../../core/services/iam-admin.service';
import { PERMISSIONS } from '../../../core/config/permission-identifiers';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';
import {
  buildUserForm,
  toCreateUserRequest,
  toUpdateUserRequest,
  UserFormGroup,
  UserFormMode
} from './user-form.model';
import { resolveUsersReturnUrl } from '../../../core/navigation/user-management-navigation';
import { iamRoleLabel } from '../iam-display-labels';

@Component({
  selector: 'app-admin-users-page',
  templateUrl: './admin-users-page.component.html',
  styleUrls: ['./admin-users-page.component.scss']
})
export class AdminUsersPageComponent implements OnInit {
  isLoading = true;
  isRefreshing = false;
  hasError = false;
  errorMessage: string | null = null;
  users: AdminUserListItem[] = [];
  roles: AdminUserRoleOption[] = [];
  searchTerm = '';
  savingUserId: string | null = null;

  dialogVisible = false;
  dialogMode: UserFormMode = 'create';
  dialogLoading = false;
  dialogSaving = false;
  dialogError = '';
  selectedUser: AdminUserDetails | null = null;
  userForm: UserFormGroup;

  readonly permissions = PERMISSIONS;

  constructor(
    private readonly adminService: IamAdminService,
    private readonly router: Router,
    private readonly confirmation: IamConfirmationService,
    private readonly formBuilder: FormBuilder,
    private readonly route: ActivatedRoute
  ) {
    this.userForm = buildUserForm(this.formBuilder, 'create');
  }

  ngOnInit(): void {
    this.searchTerm = this.route.snapshot.queryParamMap.get('q') || '';
    this.loadUsers(true);
  }

  loadUsers(initial = false): void {
    if (initial) {
      this.isLoading = true;
      this.hasError = false;
      this.errorMessage = null;
    } else {
      this.isRefreshing = true;
    }

    this.adminService.getUsers()
      .pipe(
        catchError((error: { message?: string }) => {
          this.hasError = true;
          this.errorMessage = error?.message || 'تعذر تحميل المستخدمين الآن.';
          return of(null);
        }),
        finalize(() => {
          this.isLoading = false;
          this.isRefreshing = false;
        })
      )
      .subscribe((users) => {
        if (users === null) return;
        this.users = users;
        this.hasError = false;
        this.errorMessage = null;
      });
  }

  openCreateDialog(): void {
    if (this.dialogSaving) return;
    this.dialogMode = 'create';
    this.selectedUser = null;
    this.dialogError = '';
    this.userForm = buildUserForm(this.formBuilder, 'create');
    this.dialogVisible = true;
    this.loadRolesForDialog();
  }

  openEditDialog(user: AdminUserListItem, event?: Event): void {
    event?.stopPropagation();
    if (this.dialogSaving) return;
    this.dialogMode = 'edit';
    this.selectedUser = null;
    this.dialogError = '';
    this.dialogLoading = true;
    this.dialogVisible = true;

    forkJoin({
      user: this.adminService.getUser(user.id),
      roles: this.roles.length ? of(this.roles) : this.adminService.getUserRoleOptions()
    }).pipe(finalize(() => this.dialogLoading = false)).subscribe({
      next: ({ user: details, roles }) => {
        this.selectedUser = details;
        this.roles = roles;
        this.userForm = buildUserForm(this.formBuilder, 'edit', details);
      },
      error: (error: { message?: string }) => {
        this.dialogError = this.resolveDialogError(error, 'تعذر تحميل بيانات المستخدم.');
      }
    });
  }

  saveUser(): void {
    if (this.dialogSaving || this.dialogLoading) return;
    this.userForm.markAllAsTouched();
    if (this.userForm.invalid) return;

    if (this.dialogMode === 'edit' && !this.selectedUser) return;
    if (this.wouldInvalidateLastSuperAdmin()) {
      this.dialogError = 'لا يمكن تعطيل آخر مستخدم SuperAdmin نشط أو إزالة دوره.';
      return;
    }

    this.dialogSaving = true;
    this.dialogError = '';
    const request = this.dialogMode === 'create'
      ? this.adminService.createUser(toCreateUserRequest(this.userForm))
      : this.adminService.updateUser(this.selectedUser!.id, toUpdateUserRequest(this.userForm));

    request.pipe(finalize(() => this.dialogSaving = false)).subscribe({
      next: (saved) => {
        this.upsertUser(saved);
        this.dialogVisible = false;
        this.selectedUser = null;
      },
      error: (error: { message?: string }) => {
        this.dialogError = this.resolveDialogError(error, 'تعذر حفظ بيانات المستخدم. راجع المدخلات وحاول مرة أخرى.');
      }
    });
  }

  closeDialog(): void {
    if (this.dialogSaving) return;
    this.dialogVisible = false;
    this.dialogError = '';
    this.selectedUser = null;
  }

  onDialogVisibleChange(visible: boolean): void {
    if (!visible) this.closeDialog();
  }

  openAuthorization(userId: string, event?: Event): void {
    event?.stopPropagation();
    const returnUrl = resolveUsersReturnUrl(this.router.url);
    this.router.navigate(['/admin/users', userId], {
      queryParams: { returnUrl },
      state: { returnUrl, source: 'user-management' }
    });
  }

  onSearchTermChange(value: string): void {
    this.searchTerm = value;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: value.trim() || null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  get filteredUsers(): AdminUserListItem[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) return this.users;
    return this.users.filter((user) =>
      [user.fullName, user.email, ...user.roles].some((value) => value.toLowerCase().includes(term))
    );
  }

  get roleOptions(): { label: string; value: string; disabled: boolean }[] {
    return this.roles.map((role) => ({
      label: this.roleLabel(role.name),
      value: role.id,
      disabled: !role.isActive && !this.selectedUser?.roleIds.includes(role.id)
    }));
  }

  toggleStatus(user: AdminUserListItem, event: Event): void {
    event.stopPropagation();
    const nextStatus = !user.isActive;
    if (!nextStatus && this.isLastActiveSuperAdmin(user)) {
      this.hasError = true;
      this.errorMessage = 'لا يمكن تعطيل آخر مستخدم SuperAdmin نشط.';
      return;
    }

    const action = nextStatus ? 'تفعيل' : 'تعطيل';
    if (!this.confirmation.confirm(`هل تريد ${action} المستخدم ${user.fullName}؟`)) return;

    this.savingUserId = user.id;
    this.adminService.updateUserStatus(user.id, nextStatus)
      .pipe(finalize(() => this.savingUserId = null))
      .subscribe({
        next: () => {
          user.isActive = nextStatus;
          this.hasError = false;
          this.errorMessage = null;
        },
        error: (error: { message?: string }) => {
          this.hasError = true;
          this.errorMessage = error?.message || 'تعذر تحديث حالة المستخدم.';
        }
      });
  }

  fieldError(field: 'fullName' | 'email' | 'password' | 'roleIds'): string {
    const control = this.userForm.controls[field];
    if (!control.touched || !control.errors) return '';
    if (control.errors['required'] || control.errors['minlength']) {
      const labels = { fullName: 'الاسم الكامل', email: 'اسم المستخدم', password: 'كلمة المرور', roleIds: 'الدور' };
      return `${labels[field]} مطلوب.`;
    }
    if (control.errors['maxlength']) return 'القيمة أطول من الحد المسموح.';
    return 'القيمة غير صحيحة.';
  }

  roleLabel(roleName: string): string {
    return iamRoleLabel(roleName);
  }

  trackByUserId(_: number, user: AdminUserListItem): string {
    return user.id;
  }

  private loadRolesForDialog(): void {
    if (this.roles.length) return;
    this.dialogLoading = true;
    this.adminService.getUserRoleOptions().pipe(finalize(() => this.dialogLoading = false)).subscribe({
      next: (roles) => this.roles = roles,
      error: (error: { message?: string }) => this.dialogError = this.resolveDialogError(error, 'تعذر تحميل الأدوار المتاحة.')
    });
  }

  private upsertUser(saved: AdminUserDetails): void {
    const row: AdminUserListItem = {
      id: saved.id,
      fullName: saved.fullName,
      email: saved.email,
      isActive: saved.isActive,
      roles: [...saved.roles]
    };
    const index = this.users.findIndex((user) => user.id === saved.id);
    this.users = index < 0
      ? [...this.users, row].sort((a, b) => a.fullName.localeCompare(b.fullName, 'ar'))
      : this.users.map((user) => user.id === saved.id ? row : user);
  }

  private isLastActiveSuperAdmin(user: AdminUserListItem): boolean {
    return user.isActive && user.roles.includes('SuperAdmin') && this.users.filter(
      (candidate) => candidate.isActive && candidate.roles.includes('SuperAdmin')
    ).length === 1;
  }

  private wouldInvalidateLastSuperAdmin(): boolean {
    if (!this.selectedUser || !this.isLastActiveSuperAdmin(this.selectedUser)) return false;
    const value = this.userForm.getRawValue();
    const superAdminRole = this.roles.find((role) => role.name === 'SuperAdmin');
    return !value.isActive || !superAdminRole || !value.roleIds.includes(superAdminRole.id);
  }

  private resolveDialogError(error: { message?: string }, fallback: string): string {
    const message = error?.message || '';
    if (message.includes('already in use')) return 'اسم المستخدم مستخدم بالفعل. اختر اسمًا آخر.';
    if (message.includes('last active SuperAdmin')) return 'لا يمكن تعطيل آخر مدير نظام أعلى أو إزالة دوره.';
    if (message.includes('required')) return 'يرجى استكمال الحقول المطلوبة.';
    if (message.includes('not found')) return 'تعذر العثور على المستخدم أو أحد الأدوار المحددة.';
    if (message.includes('Forbidden') || message.includes('delegate')) return 'ليست لديك صلاحية كافية لتنفيذ هذا التغيير.';
    return /[\u0600-\u06ff]/.test(message) ? message : fallback;
  }
}
