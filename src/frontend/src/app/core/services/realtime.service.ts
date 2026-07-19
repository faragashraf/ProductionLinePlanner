import { Injectable, OnDestroy } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel
} from '@microsoft/signalr';
import { BehaviorSubject, Observable, Subject, Subscription, distinctUntilChanged, map } from 'rxjs';
import { buildHubUrl } from '../config/api.config';
import { NotificationSummary, RealtimeConnectionStatus } from '../models/realtime-notification.models';
import { AuthService } from './auth.service';

const NOTIFICATION_EVENT = 'NotificationReceived';

export interface RealtimeConnection {
  readonly state: HubConnectionState;
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, handler: (notification: NotificationSummary) => void): void;
  off(methodName: string): void;
  onreconnecting(handler: (error?: Error) => void): void;
  onreconnected(handler: (connectionId?: string) => void): void;
  onclose(handler: (error?: Error) => void): void;
}

@Injectable({ providedIn: 'root' })
export class SignalRConnectionFactory {
  create(accessTokenFactory: () => string): RealtimeConnection {
    return new HubConnectionBuilder()
      .withUrl(buildHubUrl('/notifications'), { accessTokenFactory })
      .withAutomaticReconnect([0, 2_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();
  }
}

@Injectable({ providedIn: 'root' })
export class RealtimeService implements OnDestroy {
  private readonly connectionStatusSubject = new BehaviorSubject<RealtimeConnectionStatus>('disconnected');
  private readonly notificationSubject = new Subject<NotificationSummary>();
  private readonly reconnectedSubject = new Subject<void>();
  private authSubscription?: Subscription;
  private connection?: RealtimeConnection;
  private connectedUserId: string | null = null;
  private initialized = false;
  private lifecycle = Promise.resolve();

  readonly connectionStatus$ = this.connectionStatusSubject.asObservable();
  readonly notifications$: Observable<NotificationSummary> = this.notificationSubject.asObservable();
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
    connection.on(NOTIFICATION_EVENT, notification => this.notificationSubject.next(notification));
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
    try {
      await connection.stop();
    } finally {
      this.connectionStatusSubject.next('disconnected');
    }
  }

  private queueLifecycle(operation: () => Promise<void>): void {
    this.lifecycle = this.lifecycle.then(operation, operation);
  }
}
