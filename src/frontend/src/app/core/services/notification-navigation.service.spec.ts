import { NotificationSummary } from '../models/realtime-notification.models';
import { NotificationNavigationService } from './notification-navigation.service';

describe('NotificationNavigationService', () => {
  let router: jasmine.SpyObj<any>;
  let service: NotificationNavigationService;

  beforeEach(() => {
    router = jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']);
    router.navigate.and.resolveTo(true);
    router.navigateByUrl.and.resolveTo(true);
    service = new NotificationNavigationService(router);
  });

  it('routes OpenDailyAttendance through the registered action with worker and date query params', () => {
    const notification = createNotification({
      metadataJson: JSON.stringify({ navigationAction: 'OpenDailyAttendance', navigationPayload: { workerId: '11111111-1111-4111-8111-111111111111', productionDate: '2026-07-29' } })
    });

    expect(service.navigate(notification)).toBeTrue();
    expect(router.navigate).toHaveBeenCalledWith(['/attendance/workforce'], { queryParams: { workerId: '11111111-1111-4111-8111-111111111111', productionDate: '2026-07-29' } });
  });

  it('does not crash or navigate for an incomplete action payload without a legacy fallback', () => {
    const notification = createNotification({ metadataJson: JSON.stringify({ navigationAction: 'OpenDailyAttendance', navigationPayload: { workerId: 'invalid' } }) });

    expect(service.navigate(notification)).toBeFalse();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('uses a trusted legacy route when an older notification has no action metadata', () => {
    const notification = createNotification({ navigationUrl: '/attendance/workforce' });

    expect(service.navigate(notification)).toBeTrue();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/attendance/workforce');
  });

  function createNotification(overrides: Partial<NotificationSummary> = {}): NotificationSummary {
    return {
      id: '22222222-2222-4222-8222-222222222222', title: 'تنبيه', message: 'رسالة', status: 'Unread', isRead: false,
      relatedEntityType: null, relatedEntityId: null, createdAtUtc: '2026-07-29T08:00:00Z', readAtUtc: null,
      ...overrides
    };
  }
});
