export type RealtimeConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export function realtimeConnectionStatusLabel(status: RealtimeConnectionStatus): string {
  return ({
    connected: 'متصل لحظيًا',
    connecting: 'جارٍ الاتصال',
    reconnecting: 'جارٍ إعادة الاتصال',
    disconnected: 'غير متصل'
  } as const)[status];
}

export type ManufacturingEntityType = 'Factory' | 'Department' | 'ProductionLine' | 'MainStage' | 'SubStage' | 'ProductModel' | 'ProductModelStage' | 'ProductionOrder' | 'StageProductionRecord' | 'AttendanceRecord' | 'Worker' | 'WorkerDefaultAssignment';
export type ManufacturingChangeType = 'Created' | 'Updated' | 'Deleted' | 'Activated' | 'Deactivated' | 'Reordered' | 'RelationshipChanged' | 'permanent-assignment-created' | 'permanent-assignment-updated' | 'permanent-assignment-cancelled';

/** A small invalidation hint; screens refetch API data rather than accepting a pushed entity. */
export interface ManufacturingDataChanged {
  eventId: string;
  entityType: ManufacturingEntityType;
  changeType: ManufacturingChangeType;
  entityId: string;
  occurredAtUtc: string;
  actorUserId: string | null;
  correlationId: string | null;
  factoryId: string | null;
  departmentId: string | null;
  productionLineId: string | null;
  mainStageId: string | null;
  productModelId: string | null;
  subStageId: string | null;
  productionDate: string | null;
  workerId: string | null;
}

export interface NotificationSummary {
  id: string;
  title: string;
  message: string;
  status: 'Draft' | 'Unread' | 'Read' | 'Dismissed' | number;
  isRead: boolean;
  relatedEntityType: string | null;
  relatedEntityId: string | null;
  eventKey?: string | null;
  severity?: 'Information' | 'Success' | 'Warning' | 'Critical' | number;
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
