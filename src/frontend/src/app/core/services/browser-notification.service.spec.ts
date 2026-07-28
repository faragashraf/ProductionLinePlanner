import { BrowserNotificationService } from './browser-notification.service';
import { NotificationSummary } from '../models/realtime-notification.models';

describe('BrowserNotificationService', () => {
  it('reports unsupported and insecure environments without requesting permission', async () => {
    const unsupported = new BrowserNotificationService({ defaultView: {} } as Document);
    expect(unsupported.state).toBe('unsupported');
    expect(await unsupported.requestPermission()).toBe('unsupported');

    const insecure = new BrowserNotificationService({ defaultView: { Notification: {}, isSecureContext: false } } as unknown as Document);
    expect(insecure.state).toBe('insecure');
  });

  it('requests permission only from the explicit call and deduplicates system notifications', async () => {
    const created: Array<{ title: string; options: NotificationOptions }> = [];
    const requestPermission = jasmine.createSpy('requestPermission').and.resolveTo('granted' as NotificationPermission);
    const NotificationConstructor = function(title: string, options: NotificationOptions): Notification {
      created.push({ title, options });
      return { close: jasmine.createSpy('close') } as unknown as Notification;
    } as unknown as typeof Notification;
    Object.defineProperties(NotificationConstructor, {
      permission: { get: () => requestPermission.calls.any() ? 'granted' : 'default' },
      requestPermission: { value: requestPermission }
    });
    const service = new BrowserNotificationService({
      defaultView: { Notification: NotificationConstructor, isSecureContext: true, focus: jasmine.createSpy('focus'), location: { assign: jasmine.createSpy('assign') } }
    } as unknown as Document);

    expect(requestPermission).not.toHaveBeenCalled();
    expect(await service.requestPermission()).toBe('granted');
    expect(service.show(notification())).toBeTrue();
    expect(service.show(notification())).toBeFalse();
    expect(created.length).toBe(1);
    expect(created[0].options.tag).toBe(notification().id);
  });

  it('does not show when denied or disabled by server policy', () => {
    const NotificationConstructor = function(): Notification { throw new Error('must not construct'); } as unknown as typeof Notification;
    Object.defineProperty(NotificationConstructor, 'permission', { value: 'denied' });
    const service = new BrowserNotificationService({
      defaultView: { Notification: NotificationConstructor, isSecureContext: true }
    } as unknown as Document);

    expect(service.show(notification())).toBeFalse();
    expect(service.show({ ...notification(), isBrowserEnabled: false })).toBeFalse();
  });

  function notification(): NotificationSummary {
    return {
      id: '22222222-2222-2222-2222-222222222222', title: 'حضور عامل', message: 'سجل العامل حضوره.',
      status: 'Unread', isRead: false, relatedEntityType: 'AttendanceRecord', relatedEntityId: 'record',
      createdAtUtc: '2026-07-28T04:44:00Z', readAtUtc: null, isBrowserEnabled: true,
      navigationUrl: '/attendance/workforce'
    };
  }
});
