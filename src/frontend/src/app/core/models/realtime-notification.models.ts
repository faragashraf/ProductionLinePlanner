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
export type WorkerChangeKind = 'created' | 'deleted' | 'employment-status' | 'department-assignment' | 'attendance-identity' | 'profile';
export type AttendanceChangeKind = 'created' | 'updated';

/** A small invalidation hint; screens refetch API data rather than accepting a pushed entity. */
export interface ManufacturingDataChanged {
  eventId: string;
  /** Logical event name; optional while older API instances remain in rotation. */
  eventType?: 'manufacturing.attendance.changed' | 'manufacturing.workers.changed' | 'manufacturing.worker-department.changed' | 'manufacturing.data.changed' | string;
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
  /** Optional during rolling deployment with an older API instance. */
  source?: 'Application' | 'ZkTimeSync' | string;
  affectedAttendanceDates?: string[];
  workerIds?: string[];
  departmentIds?: string[];
  addedAttendanceCount?: number;
  updatedAttendanceCount?: number;
  workerChangeKinds?: WorkerChangeKind[];
  attendanceChangeKinds?: AttendanceChangeKind[];
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
  isToastEnabled?: boolean;
  isSoundEnabled?: boolean;
  isBrowserEnabled?: boolean;
  navigationUrl?: string | null;
  metadataJson?: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export type NotificationNavigationAction = 'OpenDailyAttendance';

export interface NotificationNavigationPayload {
  workerId?: string;
  productionDate?: string;
}

export interface NotificationMetadataEnvelope {
  navigationAction?: NotificationNavigationAction | string | null;
  navigationPayload?: NotificationNavigationPayload | null;
}

export interface NotificationReadStateChanged {
  notificationId: string | null;
  isRead: boolean;
  updatedCount: number;
  occurredAtUtc: string;
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

export interface AttendanceNotificationMetadata {
  workerId: string;
  workerName: string;
  employeeCode: string;
  attendanceType: 'CheckIn' | 'CheckOut';
  attendanceTimeUtc: string;
  assignmentStatus: 'Assigned' | 'Unassigned';
  stageId?: string | null;
  stageName?: string | null;
  productionLineId?: string | null;
  productionLineName?: string | null;
  navigationAction?: NotificationNavigationAction | string | null;
  navigationPayload?: NotificationNavigationPayload | null;
}
