import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Subject } from 'rxjs';
import { AuthUser } from '../models/auth.models';
import { NotificationSummary, RealtimeConnectionStatus } from '../models/realtime-notification.models';
import { AuthService } from './auth.service';
import { NotificationInboxService } from './notification-inbox.service';
import { RealtimeService } from './realtime.service';

describe('NotificationInboxService', () => {
  let service: NotificationInboxService;
  let http: HttpTestingController;
  let realtime: RealtimeStub;
  let auth: AuthStub;

  beforeEach(() => {
    realtime = new RealtimeStub();
    auth = new AuthStub();
    auth.users.next(authenticatedUser());
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        NotificationInboxService,
        { provide: AuthService, useValue: auth },
        { provide: RealtimeService, useValue: realtime }
      ]
    });
    service = TestBed.inject(NotificationInboxService);
    http = TestBed.inject(HttpTestingController);
    service.initialize();
  });

  afterEach(() => {
    http.verify();
    service.ngOnDestroy();
  });

  it('loads the persisted inbox after connect and reconnect without breaking on API failure', () => {
    flushSessionBootstrap();
    const counts: number[] = [];
    service.unreadCount$.subscribe(count => counts.push(count));

    realtime.connectionStatus.next('connected');
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 3 } });
    http.expectOne(request => request.url === '/api/notifications').flush({
      success: true,
      data: { items: [notification()], totalCount: 1, pageNumber: 1, pageSize: 20 }
    });

    realtime.connectionStatus.next('reconnecting');
    realtime.connectionStatus.next('connected');
    http.expectOne('/api/notifications/unread-count').flush('unavailable', { status: 503, statusText: 'Unavailable' });
    http.expectOne(request => request.url === '/api/notifications').flush('unavailable', { status: 503, statusText: 'Unavailable' });

    expect(counts).toContain(3);
    expect(counts.at(-1)).toBe(3);
  });

  it('updates unread state from a live event without duplicate inbox entries', () => {
    flushSessionBootstrap();
    const recent: NotificationSummary[][] = [];
    const counts: number[] = [];
    const live: NotificationSummary[] = [];
    service.recent$.subscribe(items => recent.push(items));
    service.unreadCount$.subscribe(count => counts.push(count));
    service.liveNotifications$.subscribe(value => live.push(value));

    const value = notification();
    realtime.notifications.next(value);
    realtime.notifications.next(value);
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 1 } });

    expect(recent.at(-1)?.length).toBe(1);
    expect(counts.at(-1)).toBe(1);
    expect(live).toEqual([value]);
  });

  it('marks only the selected notification as read and decrements the count', () => {
    flushSessionBootstrap();
    const value = notification();
    realtime.notifications.next(value);
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 1 } });
    service.markAsRead(value.id).subscribe();

    http.expectOne(`/api/notifications/${value.id}/read`).flush({
      success: true,
      data: { id: value.id, isRead: true, readAtUtc: '2026-07-19T10:00:00Z' }
    });

    let latest: NotificationSummary[] = [];
    let unread = -1;
    service.recent$.subscribe(items => latest = items);
    service.unreadCount$.subscribe(count => unread = count);
    expect(latest[0].isRead).toBeTrue();
    expect(unread).toBe(0);
  });

  it('marks all notifications read through the server-wide read-all endpoint and updates the counter', () => {
    flushSessionBootstrap();
    realtime.notifications.next(notification());
    realtime.notifications.next(notification('77777777-7777-7777-7777-777777777777'));
    const unreadRequests = http.match('/api/notifications/unread-count');
    expect(unreadRequests.length).toBe(2);
    unreadRequests.at(-1)!.flush({ success: true, data: { unreadCount: 2 } });

    service.markAllAsRead().subscribe();
    http.expectOne('/api/notifications/read-all').flush({ success: true, data: { updatedCount: 2 } });

    let latest: NotificationSummary[] = [];
    let unread = -1;
    service.recent$.subscribe(items => latest = items);
    service.unreadCount$.subscribe(count => unread = count);
    expect(latest.every(item => item.isRead)).toBeTrue();
    expect(unread).toBe(0);
  });

  it('applies a cross-tab read update and reloads the authoritative unread total', () => {
    flushSessionBootstrap();
    const value = notification();
    realtime.notifications.next(value);
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 1 } });

    realtime.notificationReadStateChanged.next({ notificationId: value.id, isRead: true, updatedCount: 1, occurredAtUtc: '2026-07-29T09:00:00Z' });
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 0 } });

    let latest: NotificationSummary[] = [];
    service.recent$.subscribe(items => latest = items);
    expect(latest[0].isRead).toBeTrue();
  });

  it('does not emit historical inbox rows as live notifications or replay them after reconnect', () => {
    flushSessionBootstrap();
    const live: NotificationSummary[] = [];
    service.liveNotifications$.subscribe(value => live.push(value));
    const persisted = notification();

    realtime.connectionStatus.next('connected');
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 1 } });
    http.expectOne(request => request.url === '/api/notifications').flush({
      success: true,
      data: { items: [persisted], totalCount: 1, pageNumber: 1, pageSize: 20 }
    });
    realtime.notifications.next(persisted);

    expect(live).toEqual([]);
  });

  it('cancels stale session requests and clears notification state on logout', () => {
    flushSessionBootstrap();
    const value = notification();
    realtime.notifications.next(value);
    const pendingCount = http.expectOne('/api/notifications/unread-count');

    auth.users.next(null);
    realtime.notifications.next(notification('33333333-3333-3333-3333-333333333333'));

    let recent: NotificationSummary[] = [value];
    let unread = -1;
    service.recent$.subscribe(items => recent = items);
    service.unreadCount$.subscribe(count => unread = count);
    expect(pendingCount.cancelled).toBeTrue();
    expect(recent).toEqual([]);
    expect(unread).toBe(0);
  });

  it('preserves a live row while replacing an older connect count request', () => {
    flushSessionBootstrap();
    realtime.connectionStatus.next('connected');
    const liveValue = notification();
    const olderValue = notification('44444444-4444-4444-4444-444444444444', '2026-07-19T08:00:00Z');

    realtime.notifications.next(liveValue);

    const countRequests = http.match('/api/notifications/unread-count');
    expect(countRequests.length).toBe(2);
    expect(countRequests[0].cancelled).toBeTrue();
    countRequests[1].flush({ success: true, data: { unreadCount: 2 } });
    http.expectOne(request => request.url === '/api/notifications').flush({
      success: true,
      data: { items: [olderValue], totalCount: 2, pageNumber: 1, pageSize: 20 }
    });

    let latest: NotificationSummary[] = [];
    let unread = -1;
    service.recent$.subscribe(items => latest = items);
    service.unreadCount$.subscribe(count => unread = count);
    expect(latest.map(item => item.id)).toEqual([liveValue.id, olderValue.id]);
    expect(unread).toBe(2);
  });

  it('continues accepting live events after an unread-count API failure', () => {
    flushSessionBootstrap();
    realtime.notifications.next(notification());
    http.expectOne('/api/notifications/unread-count').flush('unavailable', { status: 503, statusText: 'Unavailable' });
    realtime.notifications.next(notification('55555555-5555-5555-5555-555555555555'));
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 2 } });

    let latest: NotificationSummary[] = [];
    service.recent$.subscribe(items => latest = items);
    expect(latest.length).toBe(2);
  });

  it('loads persisted state immediately for an authenticated session without waiting for SignalR', () => {
    const persisted = notification();
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 1 } });
    http.expectOne(request => request.url === '/api/notifications').flush({
      success: true,
      data: { items: [persisted], totalCount: 1, pageNumber: 1, pageSize: 20 }
    });

    let latest: NotificationSummary[] = [];
    let unread = -1;
    service.recent$.subscribe(items => latest = items);
    service.unreadCount$.subscribe(count => unread = count);
    expect(latest).toEqual([persisted]);
    expect(unread).toBe(1);
  });

  function flushSessionBootstrap(): void {
    http.expectOne('/api/notifications/unread-count').flush({ success: true, data: { unreadCount: 0 } });
    http.expectOne(request => request.url === '/api/notifications').flush({
      success: true,
      data: { items: [], totalCount: 0, pageNumber: 1, pageSize: 20 }
    });
  }

  function notification(
    id = '22222222-2222-2222-2222-222222222222',
    createdAtUtc = '2026-07-19T09:00:00Z'
  ): NotificationSummary {
    return {
      id,
      title: 'Title',
      message: 'Message',
      status: 'Unread',
      isRead: false,
      relatedEntityType: null,
      relatedEntityId: null,
      createdAtUtc,
      readAtUtc: null
    };
  }

  function authenticatedUser(id = '11111111-1111-1111-1111-111111111111'): AuthUser {
    return { id, fullName: 'User', email: 'user@test', roles: [], permissions: [] };
  }
});

class RealtimeStub {
  readonly connectionStatus = new BehaviorSubject<RealtimeConnectionStatus>('disconnected');
  readonly notifications = new Subject<NotificationSummary>();
  readonly notificationReadStateChanged = new Subject<any>();
  readonly connectionStatus$ = this.connectionStatus.asObservable();
  readonly notifications$ = this.notifications.asObservable();
  readonly notificationReadStateChanged$ = this.notificationReadStateChanged.asObservable();
}

class AuthStub {
  readonly users = new BehaviorSubject<AuthUser | null>(null);
  readonly currentUser$ = this.users.asObservable();
}
