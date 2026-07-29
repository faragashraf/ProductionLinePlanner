import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { NotificationMetadataEnvelope, NotificationNavigationPayload, NotificationSummary } from '../models/realtime-notification.models';

@Injectable({ providedIn: 'root' })
export class NotificationNavigationService {
  constructor(private readonly router: Router) {}

  canNavigate(notification: NotificationSummary): boolean {
    const metadata = this.parseMetadata(notification.metadataJson);
    return this.isSupportedAction(metadata?.navigationAction, metadata?.navigationPayload) || this.isTrustedLegacyUrl(notification.navigationUrl);
  }

  navigate(notification: NotificationSummary): boolean {
    const metadata = this.parseMetadata(notification.metadataJson);
    if (metadata?.navigationAction === 'OpenDailyAttendance' && this.isDailyAttendancePayload(metadata.navigationPayload)) {
      void this.router.navigate(['/attendance/workforce'], {
        queryParams: {
          workerId: metadata.navigationPayload.workerId,
          productionDate: metadata.navigationPayload.productionDate
        }
      });
      return true;
    }

    if (this.isTrustedLegacyUrl(notification.navigationUrl)) {
      void this.router.navigateByUrl(notification.navigationUrl!);
      return true;
    }

    return false;
  }

  private isSupportedAction(action: string | null | undefined, payload: NotificationNavigationPayload | null | undefined): boolean {
    return action === 'OpenDailyAttendance' && this.isDailyAttendancePayload(payload);
  }

  private isDailyAttendancePayload(payload: NotificationNavigationPayload | null | undefined): payload is Required<NotificationNavigationPayload> {
    return !!payload && this.isGuid(payload.workerId) && /^\d{4}-\d{2}-\d{2}$/.test(payload.productionDate ?? '');
  }

  private parseMetadata(value: string | null | undefined): NotificationMetadataEnvelope | null {
    if (!value) return null;
    try {
      const parsed = JSON.parse(value) as NotificationMetadataEnvelope;
      return parsed && typeof parsed === 'object' ? parsed : null;
    } catch {
      return null;
    }
  }

  private isTrustedLegacyUrl(url: string | null | undefined): boolean {
    return !!url && url.startsWith('/') && !url.startsWith('//');
  }

  private isGuid(value: string | undefined): boolean {
    return !!value && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  }
}
