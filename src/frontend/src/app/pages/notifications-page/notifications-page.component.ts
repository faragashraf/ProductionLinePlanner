import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subscription, finalize } from 'rxjs';
import { AttendanceNotificationMetadata, NotificationSummary } from '../../core/models/realtime-notification.models';
import { BrowserNotificationService, BrowserNotificationState } from '../../core/services/browser-notification.service';
import { NotificationInboxService } from '../../core/services/notification-inbox.service';
import { NotificationNavigationService } from '../../core/services/notification-navigation.service';
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
  unreadCount = 0;
  isMarkingAllRead = false;
  actionMessage = '';
  private readonly subscriptions = new Subscription();

  constructor(
    private readonly inbox: NotificationInboxService,
    readonly presentation: NotificationPresentationService,
    private readonly browserNotifications: BrowserNotificationService,
    private readonly navigation: NotificationNavigationService
  ) {
    this.browserState = browserNotifications.state;
  }

  ngOnInit(): void {
    this.subscriptions.add(this.browserNotifications.state$.subscribe(state => this.browserState = state));
    this.subscriptions.add(this.inbox.unreadCount$.subscribe(count => this.unreadCount = count));
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
    this.actionMessage = '';
    if (!notification.isRead) {
      const previous = { isRead: notification.isRead, status: notification.status, readAtUtc: notification.readAtUtc };
      notification.isRead = true;
      notification.status = 'Read';
      const request = this.inbox.markAsRead(notification.id).subscribe({
        next: result => notification.readAtUtc = result.readAtUtc,
        error: () => {
          Object.assign(notification, previous);
          this.actionMessage = 'تعذر تسجيل الإشعار كمقروء، لكن تم فتح التفاصيل.';
        }
      });
      this.subscriptions.add(request);
    }
    if (!this.navigation.navigate(notification)) {
      this.actionMessage = 'لا توجد تفاصيل متاحة لهذا الإشعار.';
    }
  }

  markAsRead(notification: NotificationViewModel): void {
    if (notification.isRead) return;
    this.actionMessage = '';
    const previous = { isRead: notification.isRead, status: notification.status, readAtUtc: notification.readAtUtc };
    notification.isRead = true;
    notification.status = 'Read';
    const request = this.inbox.markAsRead(notification.id).subscribe({
      next: result => {
        notification.readAtUtc = result.readAtUtc;
        this.actionMessage = 'تم تحديد الإشعار كمقروء.';
      },
      error: () => {
        Object.assign(notification, previous);
        this.actionMessage = 'تعذر تحديث حالة الإشعار. أعد المحاولة.';
      }
    });
    this.subscriptions.add(request);
  }

  markAllAsRead(): void {
    if (this.isMarkingAllRead || this.unreadCount === 0) return;
    this.actionMessage = '';
    const previous = this.notifications.map(item => ({ id: item.id, isRead: item.isRead, status: item.status, readAtUtc: item.readAtUtc }));
    this.notifications.forEach(item => { if (!item.isRead) { item.isRead = true; item.status = 'Read'; } });
    this.isMarkingAllRead = true;
    const request = this.inbox.markAllAsRead().pipe(finalize(() => this.isMarkingAllRead = false)).subscribe({
      next: result => this.actionMessage = result.updatedCount ? `تم تحديد ${result.updatedCount} إشعار كمقروء.` : 'كل الإشعارات مقروءة بالفعل.',
      error: () => {
        const byId = new Map(previous.map(item => [item.id, item]));
        this.notifications.forEach(item => Object.assign(item, byId.get(item.id)));
        this.actionMessage = 'تعذر تحديث الإشعارات. أعد المحاولة.';
      }
    });
    this.subscriptions.add(request);
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

  canNavigate(item: NotificationViewModel): boolean { return this.navigation.canNavigate(item); }

  private parseMetadata(value: string | null | undefined): AttendanceNotificationMetadata | null {
    if (!value) return null;
    try {
      const parsed = JSON.parse(value) as AttendanceNotificationMetadata;
      return parsed?.workerId && parsed?.attendanceType ? parsed : null;
    } catch { return null; }
  }
}
