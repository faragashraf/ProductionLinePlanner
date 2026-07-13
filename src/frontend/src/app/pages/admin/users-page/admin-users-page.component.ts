import { Component, OnInit } from '@angular/core';
import { catchError, finalize, of } from 'rxjs';
import { Router } from '@angular/router';
import { AdminUserListItem, IamAdminService } from '../../../core/services/iam-admin.service';
import { IamConfirmationService } from '../../../core/services/iam-confirmation.service';
import { PERMISSIONS } from '../../../core/config/permission-identifiers';

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
  searchTerm = '';
  savingUserId: string | null = null;
  readonly permissions = PERMISSIONS;

  constructor(
    private readonly adminService: IamAdminService,
    private readonly router: Router,
    private readonly confirmation: IamConfirmationService
  ) {}

  ngOnInit(): void {
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
        if (users === null) {
          return;
        }

        this.users = users;
        this.hasError = false;
        this.errorMessage = null;
      });
  }

  openAuthorization(userId: string): void {
    this.router.navigateByUrl(`/admin/users/${userId}`);
  }

  get filteredUsers(): AdminUserListItem[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) {
      return this.users;
    }

    return this.users.filter((user) =>
      [user.fullName, user.email, ...user.roles].some((value) => value.toLowerCase().includes(term))
    );
  }

  toggleStatus(user: AdminUserListItem, event: Event): void {
    event.stopPropagation();
    const nextStatus = !user.isActive;
    if (!nextStatus && this.isLastActiveSuperAdmin(user)) {
      this.hasError = true;
      this.errorMessage = 'لا يمكن تعطيل آخر مستخدم SuperAdmin نشط. سيتحقق الخادم من ذلك نهائياً.';
      return;
    }

    const action = nextStatus ? 'تفعيل' : 'تعطيل';
    if (!this.confirmation.confirm(`هل تريد ${action} المستخدم ${user.fullName}؟`)) {
      return;
    }

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

  private isLastActiveSuperAdmin(user: AdminUserListItem): boolean {
    return user.isActive && user.roles.includes('SuperAdmin') && this.users.filter(
      (candidate) => candidate.isActive && candidate.roles.includes('SuperAdmin')
    ).length === 1;
  }

  trackByUserId(_: number, user: AdminUserListItem): string {
    return user.id;
  }
}
