import { BehaviorSubject } from 'rxjs';
import { HubConnectionState } from '@microsoft/signalr';
import { AuthUser } from '../models/auth.models';
import { NotificationSummary } from '../models/realtime-notification.models';
import { AuthService } from './auth.service';
import { RealtimeConnection, RealtimeService, SignalRConnectionFactory } from './realtime.service';

describe('RealtimeService', () => {
  let users: BehaviorSubject<AuthUser | null>;
  let token: string | null;
  let auth: AuthService;
  let connection: FakeConnection;
  let factory: jasmine.SpyObj<SignalRConnectionFactory>;
  let service: RealtimeService;

  beforeEach(() => {
    users = new BehaviorSubject<AuthUser | null>(null);
    token = null;
    auth = {
      currentUser$: users.asObservable(),
      get accessToken() { return token; }
    } as AuthService;
    connection = new FakeConnection();
    factory = jasmine.createSpyObj<SignalRConnectionFactory>('SignalRConnectionFactory', ['create']);
    factory.create.and.returnValue(connection);
    service = new RealtimeService(auth, factory);
  });

  afterEach(() => service.ngOnDestroy());

  it('does not connect before authentication and creates one connection for repeated user emissions', async () => {
    service.initialize();
    await settle();
    expect(factory.create).not.toHaveBeenCalled();

    token = 'token';
    const user = authenticatedUser();
    users.next(user);
    users.next({ ...user, fullName: 'Updated display name' });
    await settle();

    expect(factory.create).toHaveBeenCalledTimes(1);
    expect(connection.startCalls).toBe(1);
  });

  it('uses the latest access token and forwards typed notification events once', async () => {
    const received: NotificationSummary[] = [];
    service.notifications$.subscribe(notification => received.push(notification));
    service.initialize();
    token = 'first-token';
    users.next(authenticatedUser());
    await settle();

    expect(factory.create.calls.mostRecent().args[0]()).toBe('first-token');
    token = 'refreshed-token';
    expect(factory.create.calls.mostRecent().args[0]()).toBe('refreshed-token');
    connection.emitNotification(notification());
    connection.emitNotification(notification());

    expect(received.length).toBe(2);
    expect(connection.notificationHandlerRegistrations).toBe(2);
  });

  it('updates reconnect state without registering duplicate listeners', async () => {
    const statuses: string[] = [];
    service.connectionStatus$.subscribe(status => statuses.push(status));
    service.initialize();
    token = 'token';
    users.next(authenticatedUser());
    await settle();

    connection.emitReconnecting();
    connection.emitReconnected();

    expect(statuses).toContain('reconnecting');
    expect(statuses.at(-1)).toBe('connected');
    expect(connection.notificationHandlerRegistrations).toBe(2);
  });

  it('disconnects and removes listeners on logout', async () => {
    service.initialize();
    token = 'token';
    users.next(authenticatedUser());
    await settle();

    token = null;
    users.next(null);
    await settle();

    expect(connection.stopCalls).toBe(1);
    expect(connection.offCalls).toBe(2);
  });

  it('contains an initial SignalR start failure without crashing the app session', async () => {
    const statuses: string[] = [];
    service.connectionStatus$.subscribe(status => statuses.push(status));
    connection.startError = new Error('SignalR unavailable');
    service.initialize();
    token = 'token';

    users.next(authenticatedUser());
    await settle();

    expect(connection.startCalls).toBe(1);
    expect(statuses.at(-1)).toBe('disconnected');
  });

  it('stops the connection and removes transport listeners during service teardown', async () => {
    service.initialize();
    token = 'token';
    users.next(authenticatedUser());
    await settle();

    service.ngOnDestroy();
    await settle();

    expect(connection.stopCalls).toBe(1);
    expect(connection.offCalls).toBe(2);
  });

  function authenticatedUser(): AuthUser {
    return { id: '11111111-1111-1111-1111-111111111111', fullName: 'User', email: 'user@test', roles: [], permissions: [] };
  }

  function notification(): NotificationSummary {
    return {
      id: '22222222-2222-2222-2222-222222222222',
      title: 'Title',
      message: 'Message',
      status: 'Unread',
      isRead: false,
      relatedEntityType: null,
      relatedEntityId: null,
      createdAtUtc: new Date().toISOString(),
      readAtUtc: null
    };
  }

  async function settle(): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    await new Promise<void>(resolve => setTimeout(resolve, 0));
  }
});

class FakeConnection implements RealtimeConnection {
  state = HubConnectionState.Disconnected;
  startError?: Error;
  startCalls = 0;
  stopCalls = 0;
  offCalls = 0;
  notificationHandlerRegistrations = 0;
  private notificationHandler?: (notification: NotificationSummary) => void;
  private reconnectingHandler?: () => void;
  private reconnectedHandler?: () => void;
  private closeHandler?: () => void;

  async start(): Promise<void> {
    this.startCalls++;
    if (this.startError) throw this.startError;
    this.state = HubConnectionState.Connected;
  }
  async stop(): Promise<void> { this.stopCalls++; this.state = HubConnectionState.Disconnected; this.closeHandler?.(); }
  on<T>(methodName: string, handler: (message: T) => void): void {
    this.notificationHandlerRegistrations++;
    if (methodName === 'NotificationReceived') this.notificationHandler = handler as (notification: NotificationSummary) => void;
  }
  off(): void { this.offCalls++; this.notificationHandler = undefined; }
  async invoke(): Promise<unknown> { return undefined; }
  onreconnecting(handler: () => void): void { this.reconnectingHandler = handler; }
  onreconnected(handler: () => void): void { this.reconnectedHandler = handler; }
  onclose(handler: () => void): void { this.closeHandler = handler; }
  emitNotification(value: NotificationSummary): void { this.notificationHandler?.(value); }
  emitReconnecting(): void { this.state = HubConnectionState.Reconnecting; this.reconnectingHandler?.(); }
  emitReconnected(): void { this.state = HubConnectionState.Connected; this.reconnectedHandler?.(); }
}
