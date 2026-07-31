import { Injectable, OnDestroy } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel
} from '@microsoft/signalr';
import { BehaviorSubject, Observable, Subject, Subscription, distinctUntilChanged, map } from 'rxjs';
import { buildHubUrl } from '../config/api.config';
import { ManufacturingDataChanged, NotificationReadStateChanged, NotificationSummary, RealtimeConnectionStatus } from '../models/realtime-notification.models';
import { AuthService } from './auth.service';

const NOTIFICATION_EVENT = 'NotificationReceived';
const NOTIFICATION_READ_STATE_CHANGED_EVENT = 'NotificationReadStateChanged';
const MANUFACTURING_DATA_CHANGED_EVENT = 'ManufacturingDataChanged';
const REALTIME_KEEP_ALIVE_MS = 5_000;
const REALTIME_SERVER_TIMEOUT_MS = 30_000;
const RECONNECT_DELAYS_MS = [2_000, 10_000, 30_000, 60_000] as const;

export function realtimeReconnectDelay(previousRetryCount: number): number {
  return RECONNECT_DELAYS_MS[Math.min(Math.max(previousRetryCount, 0), RECONNECT_DELAYS_MS.length - 1)];
}

export interface RealtimeConnection {
  readonly state: HubConnectionState;
  start(): Promise<void>;
  stop(): Promise<void>;
  on<T>(methodName: string, handler: (message: T) => void): void;
  off(methodName: string): void;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  onreconnecting(handler: (error?: Error) => void): void;
  onreconnected(handler: (connectionId?: string) => void): void;
  onclose(handler: (error?: Error) => void): void;
}

@Injectable({ providedIn: 'root' })
export class SignalRConnectionFactory {
  create(accessTokenFactory: () => string): RealtimeConnection {
    const connection = new HubConnectionBuilder()
      .withUrl(buildHubUrl('/notifications'), { accessTokenFactory })
      .withAutomaticReconnect({
        // Keep retrying at a bounded pace. A short proxy interruption must not
        // permanently put operational screens to sleep or create a negotiate storm.
        nextRetryDelayInMilliseconds: context => realtimeReconnectDelay(context.previousRetryCount)
      })
      .configureLogging(LogLevel.Warning)
      .build();
    // Five-second traffic keeps LAN/IIS proxies with aggressive idle timeouts
    // from closing an otherwise healthy connection before SignalR's defaults fire.
    connection.keepAliveIntervalInMilliseconds = REALTIME_KEEP_ALIVE_MS;
    connection.serverTimeoutInMilliseconds = REALTIME_SERVER_TIMEOUT_MS;
    return connection;
  }
}

@Injectable({ providedIn: 'root' })
export class RealtimeService implements OnDestroy {
  private readonly connectionStatusSubject = new BehaviorSubject<RealtimeConnectionStatus>('disconnected');
  private readonly notificationSubject = new Subject<NotificationSummary>();
  private readonly notificationReadStateChangedSubject = new Subject<NotificationReadStateChanged>();
  private readonly manufacturingDataChangedSubject = new Subject<ManufacturingDataChanged>();
  private readonly reconnectedSubject = new Subject<void>();
  private authSubscription?: Subscription;
  private connection?: RealtimeConnection;
  private connectedUserId: string | null = null;
  private initialized = false;
  private lifecycle = Promise.resolve();

  readonly connectionStatus$ = this.connectionStatusSubject.asObservable();
  readonly notifications$: Observable<NotificationSummary> = this.notificationSubject.asObservable();
  readonly notificationReadStateChanged$: Observable<NotificationReadStateChanged> = this.notificationReadStateChangedSubject.asObservable();
  readonly manufacturingDataChanged$: Observable<ManufacturingDataChanged> = this.manufacturingDataChangedSubject.asObservable();
  readonly reconnected$ = this.reconnectedSubject.asObservable();

  constructor(
    private readonly authService: AuthService,
    private readonly connectionFactory: SignalRConnectionFactory
  ) {}

  initialize(): void {
    if (this.initialized) return;
    this.initialized = true;

    this.authSubscription = this.authService.currentUser$
      .pipe(
        map(user => user?.id ?? null),
        distinctUntilChanged()
      )
      .subscribe(userId => {
        this.queueLifecycle(async () => {
          if (!userId || !this.authService.accessToken) {
            await this.stopConnection();
            return;
          }

          await this.startConnection(userId);
        });
      });
  }

  ngOnDestroy(): void {
    this.authSubscription?.unsubscribe();
    this.authSubscription = undefined;
    this.initialized = false;
    this.queueLifecycle(() => this.stopConnection());
    this.notificationSubject.complete();
    this.notificationReadStateChangedSubject.complete();
    this.manufacturingDataChangedSubject.complete();
    this.reconnectedSubject.complete();
    this.connectionStatusSubject.complete();
  }

  private async startConnection(userId: string): Promise<void> {
    if (this.connection && this.connectedUserId === userId && this.connection.state !== HubConnectionState.Disconnected) {
      return;
    }

    if (this.connection) {
      await this.stopConnection();
    }

    const connection = this.connectionFactory.create(() => this.authService.accessToken ?? '');
    this.connection = connection;
    this.connectedUserId = userId;
    connection.on<NotificationSummary>(NOTIFICATION_EVENT, notification => this.notificationSubject.next(notification));
    connection.on<NotificationReadStateChanged>(NOTIFICATION_READ_STATE_CHANGED_EVENT, change => this.notificationReadStateChangedSubject.next(change));
    connection.on<ManufacturingDataChanged>(MANUFACTURING_DATA_CHANGED_EVENT, change => this.manufacturingDataChangedSubject.next(change));
    connection.onreconnecting(() => this.connectionStatusSubject.next('reconnecting'));
    connection.onreconnected(() => {
      this.connectionStatusSubject.next('connected');
      this.reconnectedSubject.next();
    });
    connection.onclose(() => this.connectionStatusSubject.next('disconnected'));

    this.connectionStatusSubject.next('connecting');
    try {
      await connection.start();
      if (this.connection === connection) {
        this.connectionStatusSubject.next('connected');
      }
    } catch {
      if (this.connection === connection) {
        this.connectionStatusSubject.next('disconnected');
      }
    }
  }

  private async stopConnection(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    this.connectedUserId = null;
    if (!connection) {
      this.connectionStatusSubject.next('disconnected');
      return;
    }

    connection.off(NOTIFICATION_EVENT);
    connection.off(NOTIFICATION_READ_STATE_CHANGED_EVENT);
    connection.off(MANUFACTURING_DATA_CHANGED_EVENT);
    try {
      await connection.stop();
    } finally {
      this.connectionStatusSubject.next('disconnected');
    }
  }

  private queueLifecycle(operation: () => Promise<void>): void {
    this.lifecycle = this.lifecycle.then(operation, operation);
  }

  async invoke(methodName: string, ...args: unknown[]): Promise<boolean> {
    const connection = this.connection;
    if (!connection || connection.state !== HubConnectionState.Connected) return false;
    await connection.invoke(methodName, ...args);
    return true;
  }
}
