import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, EMPTY, Observable, Subject, Subscription, catchError, distinctUntilChanged, filter, map, tap, throwError } from 'rxjs';
import { buildApiUrl } from '../config/api.config';
import { ApiResponse } from '../models/api-response.model';
import { NotificationPage, NotificationReadStateChanged, NotificationSummary } from '../models/realtime-notification.models';
import { AuthService } from './auth.service';
import { RealtimeService } from './realtime.service';

@Injectable({ providedIn: 'root' })
export class NotificationInboxService implements OnDestroy {
  private readonly unreadCountSubject = new BehaviorSubject<number>(0);
  private readonly recentSubject = new BehaviorSubject<NotificationSummary[]>([]);
  private readonly liveNotificationSubject = new Subject<NotificationSummary>();
  private readonly subscriptions = new Subscription();
  private sessionRequests = new Subscription();
  private unreadLoad?: Subscription;
  private recentLoad?: Subscription;
  private readonly seenNotificationIds = new Set<string>();
  private activeUserId: string | null = null;
  private sessionVersion = 0;
  private initialized = false;

  readonly unreadCount$ = this.unreadCountSubject.asObservable();
  readonly recent$ = this.recentSubject.asObservable();
  readonly liveNotifications$ = this.liveNotificationSubject.asObservable();

  constructor(
    private readonly http: HttpClient,
    private readonly authService: AuthService,
    private readonly realtime: RealtimeService
  ) {}

  initialize(): void {
    if (this.initialized) return;
    this.initialized = true;

    this.subscriptions.add(this.authService.currentUser$
      .pipe(
        map(user => user?.id ?? null),
        distinctUntilChanged()
      )
      .subscribe(userId => this.beginSession(userId)));

    this.subscriptions.add(this.realtime.notifications$
      .subscribe(notification => this.acceptLiveNotification(notification)));

    this.subscriptions.add(this.realtime.notificationReadStateChanged$
      .subscribe(change => this.applyRealtimeReadState(change)));

    this.subscriptions.add(this.realtime.connectionStatus$
      .pipe(
        distinctUntilChanged(),
        filter(status => status === 'connected')
      )
      .subscribe(() => this.refresh()));
  }

  refresh(): void {
    if (!this.activeUserId) return;
    this.loadUnreadCount();
    this.loadRecent();
  }

  getPage(page: number, pageSize: number): Observable<NotificationPage> {
    return this.http.get<ApiResponse<NotificationPage>>(buildApiUrl('/api/notifications'), {
      params: { page, pageSize }
    }).pipe(map(response => this.extractData(response)));
  }

  markAsRead(notificationId: string): Observable<{ id: string; isRead: boolean; readAtUtc: string }> {
    if (!this.activeUserId) return throwError(() => new Error('يلزم تسجيل الدخول لتحديث الإشعارات.'));
    const expectedSessionVersion = this.sessionVersion;
    return this.http.patch<ApiResponse<{ id: string; isRead: boolean; readAtUtc: string }>>(
      buildApiUrl(`/api/notifications/${encodeURIComponent(notificationId)}/read`),
      {}
    ).pipe(
      map(response => this.extractData(response)),
      tap(result => {
        if (expectedSessionVersion !== this.sessionVersion) return;
        const updated = this.recentSubject.value.map(notification =>
          notification.id === result.id
            ? { ...notification, isRead: true, status: 'Read' as const, readAtUtc: result.readAtUtc }
            : notification
        );
        const wasUnread = this.recentSubject.value.some(item => item.id === result.id && !item.isRead);
        this.recentSubject.next(updated);
        if (wasUnread) {
          this.unreadCountSubject.next(Math.max(0, this.unreadCountSubject.value - 1));
        }
      })
    );
  }

  markAllAsRead(): Observable<{ updatedCount: number }> {
    if (!this.activeUserId) return throwError(() => new Error('يلزم تسجيل الدخول لتحديث الإشعارات.'));
    const expectedSessionVersion = this.sessionVersion;
    return this.http.patch<ApiResponse<{ updatedCount: number }>>(
      buildApiUrl('/api/notifications/read-all'),
      {}
    ).pipe(
      map(response => this.extractData(response)),
      tap(result => {
        if (expectedSessionVersion !== this.sessionVersion) return;
        this.recentSubject.next(this.recentSubject.value.map(notification => notification.isRead
          ? notification
          : { ...notification, isRead: true, status: 'Read' as const, readAtUtc: new Date().toISOString() }));
        this.unreadCountSubject.next(Math.max(0, this.unreadCountSubject.value - result.updatedCount));
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.sessionRequests.unsubscribe();
    this.initialized = false;
    this.unreadCountSubject.complete();
    this.recentSubject.complete();
    this.liveNotificationSubject.complete();
  }

  private loadUnreadCount(): void {
    const expectedSessionVersion = this.sessionVersion;
    this.unreadLoad?.unsubscribe();
    this.unreadLoad = this.http.get<ApiResponse<{ unreadCount: number }>>(buildApiUrl('/api/notifications/unread-count')).pipe(
      map(response => this.extractData(response)),
      tap(result => {
        if (expectedSessionVersion === this.sessionVersion) {
          this.unreadCountSubject.next(result.unreadCount);
        }
      }),
      catchError(() => EMPTY)
    ).subscribe();
    this.sessionRequests.add(this.unreadLoad);
  }

  private loadRecent(): void {
    const expectedSessionVersion = this.sessionVersion;
    this.recentLoad?.unsubscribe();
    this.recentLoad = this.http.get<ApiResponse<NotificationPage>>(buildApiUrl('/api/notifications'), {
      params: { page: 1, pageSize: 20 }
    }).pipe(
      map(response => this.extractData(response)),
      tap(result => {
        if (expectedSessionVersion !== this.sessionVersion) return;
        result.items.forEach(notification => this.seenNotificationIds.add(notification.id));
        // Persisted rows are authoritative for read state, while any newer
        // live-only row absent from this response is still retained.
        this.recentSubject.next(this.mergeRecent(result.items, this.recentSubject.value));
      }),
      catchError(() => EMPTY)
    ).subscribe();
    this.sessionRequests.add(this.recentLoad);
  }

  private beginSession(userId: string | null): void {
    this.sessionVersion++;
    this.activeUserId = userId;
    this.sessionRequests.unsubscribe();
    this.sessionRequests = new Subscription();
    this.unreadLoad = undefined;
    this.recentLoad = undefined;
    this.seenNotificationIds.clear();
    this.unreadCountSubject.next(0);
    this.recentSubject.next([]);
    if (userId) {
      this.loadUnreadCount();
      this.loadRecent();
    }
  }

  private acceptLiveNotification(notification: NotificationSummary): void {
    if (!this.activeUserId || this.seenNotificationIds.has(notification.id)) return;

    this.seenNotificationIds.add(notification.id);
    this.recentSubject.next(this.mergeRecent([notification], this.recentSubject.value));
    if (!notification.isRead) {
      this.unreadCountSubject.next(this.unreadCountSubject.value + 1);
      // The server persists before dispatch. Replacing any older count request
      // with one started after this event prevents a stale connect/reconnect
      // response from overwriting the new unread state.
      this.loadUnreadCount();
    }
    this.liveNotificationSubject.next(notification);
  }

  private applyRealtimeReadState(change: NotificationReadStateChanged): void {
    if (!this.activeUserId || !change.isRead) return;
    const recent = this.recentSubject.value;
    if (change.notificationId) {
      const wasUnread = recent.some(item => item.id === change.notificationId && !item.isRead);
      this.recentSubject.next(recent.map(item => item.id === change.notificationId
        ? { ...item, isRead: true, status: 'Read' as const, readAtUtc: change.occurredAtUtc }
        : item));
      if (wasUnread) this.unreadCountSubject.next(Math.max(0, this.unreadCountSubject.value - 1));
    } else {
      this.recentSubject.next(recent.map(item => item.isRead
        ? item
        : { ...item, isRead: true, status: 'Read' as const, readAtUtc: change.occurredAtUtc }));
      this.unreadCountSubject.next(Math.max(0, this.unreadCountSubject.value - change.updatedCount));
    }
    // A read-all event can race a newly delivered notification; reload the
    // authoritative total instead of assuming the local count is permanently zero.
    this.loadUnreadCount();
  }

  private mergeRecent(
    preferred: NotificationSummary[],
    additional: NotificationSummary[]
  ): NotificationSummary[] {
    const merged = new Map<string, NotificationSummary>();
    [...preferred, ...additional].forEach(notification => {
      if (!merged.has(notification.id)) {
        merged.set(notification.id, notification);
      }
    });

    return [...merged.values()]
      .sort((left, right) => Date.parse(right.createdAtUtc) - Date.parse(left.createdAtUtc))
      .slice(0, 20);
  }

  private extractData<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data === null || response.data === undefined) {
      throw new Error(response.error?.message || 'Unexpected API response.');
    }

    return response.data;
  }
}
