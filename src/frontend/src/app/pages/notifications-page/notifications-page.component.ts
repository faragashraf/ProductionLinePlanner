import { Component, OnDestroy, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { AttendanceNotificationMetadata, NotificationSummary } from '../../core/models/realtime-notification.models';
import { BrowserNotificationService, BrowserNotificationState } from '../../core/services/browser-notification.service';
import { NotificationInboxService } from '../../core/services/notification-inbox.service';
import { NotificationPresentationService } from '../../core/services/notification-presentation.service';

interface NotificationViewModel extends NotificationSummary {
  metadata: AttendanceNotificationMetadata | null;
}

@Component({
  selector: 'app-notifications-page',
  templateUrl: './notifications-page.component.html',
  styleUrls: ['./notifications-page.component.scss']
})
export class NotificationsPageComponent implements OnInit, OnDestroy {
  notifications: NotificationViewModel[] = [];
  isLoading = true;
  hasError = false;
  page = 1;
  pageSize = 20;
  totalCount = 0;
  browserState: BrowserNotificationState;
  private readonly subscriptions = new Subscription();

  constructor(
    private readonly inbox: NotificationInboxService,
    readonly presentation: NotificationPresentationService,
    private readonly browserNotifications: BrowserNotificationService,
    private readonly router: Router
  ) {
    this.browserState = browserNotifications.state;
  }

  ngOnInit(): void {
    this.subscriptions.add(this.browserNotifications.state$.subscribe(state => this.browserState = state));
    this.loadPage(1);
  }

  ngOnDestroy(): void { this.subscriptions.unsubscribe(); }

  loadPage(page: number): void {
    this.page = page;
    this.isLoading = true;
    this.hasError = false;
    const request = this.inbox.getPage(page, this.pageSize)
      .pipe(finalize(() => this.isLoading = false))
      .subscribe({
        next: result => {
          this.notifications = result.items.map(item => ({ ...item, metadata: this.parseMetadata(item.metadataJson) }));
          this.totalCount = result.totalCount;
        },
        error: () => this.hasError = true
      });
    this.subscriptions.add(request);
  }

  onPageChange(event: { page?: number }): void { this.loadPage((event.page ?? 0) + 1); }

  async enableDeviceNotifications(): Promise<void> { await this.browserNotifications.requestPermission(); }

  toggleSound(): void { this.presentation.setSoundEnabled(!this.presentation.isSoundEnabled); }

  open(notification: NotificationViewModel): void {
    if (!notification.isRead) {
      notification.isRead = true;
      notification.status = 'Read';
      this.inbox.markAsRead(notification.id);
    }
    if (notification.navigationUrl?.startsWith('/') && !notification.navigationUrl.startsWith('//')) {
      void this.router.navigateByUrl(notification.navigationUrl);
    }
  }

  iconFor(item: NotificationViewModel): string {
    if (item.eventKey === 'WorkerCheckedIn') return 'pi pi-sign-in';
    if (item.eventKey === 'WorkerCheckedOut') return 'pi pi-sign-out';
    return 'pi pi-bell';
  }

  severityFor(item: NotificationViewModel): string {
    return ({ Information: 'info', Success: 'success', Warning: 'warning', Critical: 'danger' } as Record<string, string>)[String(item.severity)] || 'info';
  }

  attendanceTime(item: NotificationViewModel): string | null {
    return item.metadata ? new Intl.DateTimeFormat('ar-EG', { hour: '2-digit', minute: '2-digit' }).format(new Date(item.metadata.attendanceTimeUtc)) : null;
  }

  assignmentLabel(item: NotificationViewModel): string | null {
    if (!item.metadata) return null;
    return item.metadata.assignmentStatus === 'Unassigned'
      ? 'غير مسكن'
      : `${item.metadata.stageName || 'مرحلة'} — ${item.metadata.productionLineName || 'خط الإنتاج'}`;
  }

  createdAtLabel(item: NotificationViewModel): string {
    return new Intl.DateTimeFormat('ar-EG', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(item.createdAtUtc));
  }

  browserStateLabel(): string {
    return ({ granted: 'مفعّلة', denied: 'مرفوضة من المتصفح', default: 'غير مفعّلة', unsupported: 'غير مدعومة', insecure: 'تحتاج HTTPS' } as Record<BrowserNotificationState, string>)[this.browserState];
  }

  trackByNotification(_: number, item: NotificationViewModel): string { return item.id; }

  private parseMetadata(value: string | null | undefined): AttendanceNotificationMetadata | null {
    if (!value) return null;
    try {
      const parsed = JSON.parse(value) as AttendanceNotificationMetadata;
      return parsed?.workerId && parsed?.attendanceType ? parsed : null;
    } catch { return null; }
  }
}
