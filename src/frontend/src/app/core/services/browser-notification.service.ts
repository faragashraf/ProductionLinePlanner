import { DOCUMENT } from '@angular/common';
import { Inject, Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { NotificationSummary } from '../models/realtime-notification.models';

export type BrowserNotificationState = 'unsupported' | 'insecure' | NotificationPermission;

@Injectable({ providedIn: 'root' })
export class BrowserNotificationService {
  private readonly shownIds = new Set<string>();
  private readonly stateSubject = new BehaviorSubject<BrowserNotificationState>(this.readState());
  readonly state$ = this.stateSubject.asObservable();

  constructor(@Inject(DOCUMENT) private readonly document: Document) {}

  get state(): BrowserNotificationState {
    const state = this.readState();
    if (state !== this.stateSubject.value) this.stateSubject.next(state);
    return state;
  }

  async requestPermission(): Promise<BrowserNotificationState> {
    const current = this.readState();
    if (current === 'unsupported' || current === 'insecure' || current === 'denied') {
      this.stateSubject.next(current);
      return current;
    }

    try {
      const permission = await this.document.defaultView!.Notification.requestPermission();
      this.stateSubject.next(permission);
      return permission;
    } catch {
      const state = this.readState();
      this.stateSubject.next(state);
      return state;
    }
  }

  show(notification: NotificationSummary): boolean {
    if (!notification.isBrowserEnabled || notification.isRead || this.state !== 'granted' || this.shownIds.has(notification.id)) {
      return false;
    }

    try {
      const systemNotification = new this.document.defaultView!.Notification(notification.title, {
        body: notification.message,
        tag: notification.id,
        data: { navigationUrl: this.safeNavigationUrl(notification.navigationUrl) }
      });
      this.shownIds.add(notification.id);
      systemNotification.onclick = () => {
        const windowRef = this.document.defaultView;
        windowRef?.focus();
        const navigationUrl = this.safeNavigationUrl(notification.navigationUrl);
        if (windowRef && navigationUrl) windowRef.location.assign(navigationUrl);
        systemNotification.close();
      };
      return true;
    } catch {
      return false;
    }
  }

  private readState(): BrowserNotificationState {
    const windowRef = this.document.defaultView;
    if (!windowRef || !('Notification' in windowRef)) return 'unsupported';
    if (!windowRef.isSecureContext) return 'insecure';
    return windowRef.Notification.permission;
  }

  private safeNavigationUrl(value: string | null | undefined): string | null {
    return value?.startsWith('/') && !value.startsWith('//') ? value : null;
  }
}
