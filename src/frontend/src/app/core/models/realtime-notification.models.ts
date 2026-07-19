export type RealtimeConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface NotificationSummary {
  id: string;
  title: string;
  message: string;
  status: 'Draft' | 'Unread' | 'Read' | 'Dismissed' | number;
  isRead: boolean;
  relatedEntityType: string | null;
  relatedEntityId: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface NotificationPage {
  items: NotificationSummary[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

export type NotificationSoundKey = 'default';

export interface NotificationPresentationPreferences {
  enabled: boolean;
  soundKey: NotificationSoundKey;
  volume: number;
}
