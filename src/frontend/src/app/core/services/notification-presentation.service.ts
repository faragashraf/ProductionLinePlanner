import { DOCUMENT } from '@angular/common';
import { Inject, Injectable, OnDestroy } from '@angular/core';
import { MessageService } from 'primeng/api';
import { Subscription, distinctUntilChanged, map } from 'rxjs';
import {
  NotificationPresentationPreferences,
  NotificationSoundKey,
  NotificationSummary
} from '../models/realtime-notification.models';
import { AuthService } from './auth.service';
import { NotificationInboxService } from './notification-inbox.service';

export const DEFAULT_NOTIFICATION_PRESENTATION_PREFERENCES: Readonly<NotificationPresentationPreferences> = {
  enabled: true,
  soundKey: 'default',
  volume: 0.2
};

type AudioContextWindow = Window & typeof globalThis & {
  webkitAudioContext?: typeof AudioContext;
};

@Injectable({ providedIn: 'root' })
export class NotificationSoundPlayer implements OnDestroy {
  private context: AudioContext | null = null;
  private listeningForUnlock = false;
  private readonly unlockListener = (): void => {
    void this.unlock();
  };

  constructor(@Inject(DOCUMENT) private readonly document: Document) {}

  initialize(): void {
    if (this.listeningForUnlock || this.context?.state === 'running') return;

    this.document.addEventListener('pointerdown', this.unlockListener, { capture: true });
    this.document.addEventListener('keydown', this.unlockListener, { capture: true });
    this.listeningForUnlock = true;
  }

  play(soundKey: NotificationSoundKey, volume: number): void {
    const context = this.context;
    if (soundKey !== 'default' || !context || context.state !== 'running') {
      // Do not queue a missed notification sound. A later user interaction
      // only unlocks future live notifications, which avoids historical replay.
      this.initialize();
      return;
    }

    try {
      const startAt = context.currentTime;
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      const safeVolume = Math.min(1, Math.max(0, volume));

      oscillator.type = 'sine';
      oscillator.frequency.setValueAtTime(880, startAt);
      gain.gain.setValueAtTime(0.0001, startAt);
      gain.gain.exponentialRampToValueAtTime(Math.max(0.0001, safeVolume), startAt + 0.012);
      gain.gain.exponentialRampToValueAtTime(0.0001, startAt + 0.16);
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start(startAt);
      oscillator.stop(startAt + 0.17);
    } catch {
      // Browser audio support is optional presentation behavior.
    }
  }

  teardown(): void {
    this.detachUnlockListeners();
    const context = this.context;
    this.context = null;
    if (context && context.state !== 'closed') {
      try {
        void context.close().catch(() => undefined);
      } catch {
        // Some browser implementations can reject close synchronously.
      }
    }
  }

  ngOnDestroy(): void {
    this.teardown();
  }

  private async unlock(): Promise<void> {
    try {
      const context = this.context ?? this.createContext();
      if (!context) return;
      this.context = context;

      if (context.state === 'suspended') {
        await context.resume();
      }
      if (context.state !== 'running') return;

      this.primeSilently(context);
      this.detachUnlockListeners();
    } catch {
      // Autoplay rejection is expected before an accepted user gesture. Keep
      // the single listeners installed so a later interaction can retry.
    }
  }

  private createContext(): AudioContext | null {
    const audioWindow = this.document.defaultView as AudioContextWindow | null;
    const AudioContextConstructor = audioWindow?.AudioContext ?? audioWindow?.webkitAudioContext;
    if (!AudioContextConstructor) return null;

    try {
      return new AudioContextConstructor();
    } catch {
      return null;
    }
  }

  private primeSilently(context: AudioContext): void {
    try {
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      gain.gain.setValueAtTime(0, context.currentTime);
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start(context.currentTime);
      oscillator.stop(context.currentTime + 0.001);
    } catch {
      // Priming is best-effort; the running context can still play later.
    }
  }

  private detachUnlockListeners(): void {
    if (!this.listeningForUnlock) return;
    this.document.removeEventListener('pointerdown', this.unlockListener, true);
    this.document.removeEventListener('keydown', this.unlockListener, true);
    this.listeningForUnlock = false;
  }
}

@Injectable({ providedIn: 'root' })
export class NotificationPresentationService implements OnDestroy {
  private readonly subscriptions = new Subscription();
  private readonly presentedNotificationIds = new Set<string>();
  private preferences: NotificationPresentationPreferences = {
    ...DEFAULT_NOTIFICATION_PRESENTATION_PREFERENCES
  };
  private activeUserId: string | null = null;
  private initialized = false;

  constructor(
    private readonly authService: AuthService,
    private readonly inbox: NotificationInboxService,
    private readonly messages: MessageService,
    private readonly soundPlayer: NotificationSoundPlayer
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

    this.subscriptions.add(this.inbox.liveNotifications$
      .subscribe(notification => this.present(notification)));
  }

  configureSoundPreferences(preferences: Partial<NotificationPresentationPreferences>): void {
    this.preferences = {
      enabled: preferences.enabled ?? this.preferences.enabled,
      soundKey: preferences.soundKey ?? this.preferences.soundKey,
      volume: Math.min(1, Math.max(0, preferences.volume ?? this.preferences.volume))
    };
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    this.presentedNotificationIds.clear();
    this.activeUserId = null;
    this.initialized = false;
    this.soundPlayer.teardown();
  }

  private beginSession(userId: string | null): void {
    this.presentedNotificationIds.clear();
    this.activeUserId = userId;
    this.soundPlayer.teardown();
    if (userId) {
      this.soundPlayer.initialize();
    }
  }

  private present(notification: NotificationSummary): void {
    if (!this.activeUserId || this.presentedNotificationIds.has(notification.id)) return;
    this.presentedNotificationIds.add(notification.id);

    if (this.shouldShowToast(notification)) {
      this.messages.add({
        severity: 'info',
        summary: notification.title,
        detail: notification.message,
        life: 5_000,
        closable: true
      });
    }

    if (this.shouldPlaySound(notification)) {
      this.soundPlayer.play(this.preferences.soundKey, this.preferences.volume);
    }
  }

  private shouldShowToast(notification: NotificationSummary): boolean {
    return !notification.isRead;
  }

  private shouldPlaySound(notification: NotificationSummary): boolean {
    return this.preferences.enabled && !notification.isRead;
  }
}
