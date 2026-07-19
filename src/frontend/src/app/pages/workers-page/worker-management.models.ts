export type WorkerLocalProfileStatus = 'complete' | 'needs-review' | 'source-pending';
export type WorkerSourceLinkStatus = 'linked' | 'unlinked' | 'conflict' | 'new-source' | 'missing-source';
export type WorkerAssignmentStatus = 'assigned' | 'unassigned' | 'mixed';
export type WorkerLocalEmploymentStatus = 'active' | 'inactive' | 'left-employment';
export type WorkerAssignmentKind = 'permanent' | 'temporary';
export type WorkerHistoryKind = 'name' | 'photo' | 'status' | 'assignment';
export type WorkerSourcePreviewKind = 'new' | 'unchanged' | 'protected-local' | 'identity-conflict' | 'observed';

export interface WorkerLocalSalary {
  amount: number;
  currencyCode: 'EGP';
  effectiveFrom: string;
}

export interface WorkerLocalProfile {
  displayName: string;
  photoUrl: string | null;
  salary: WorkerLocalSalary | null;
  profileStatus: WorkerLocalProfileStatus;
  employmentStatus: WorkerLocalEmploymentStatus;
}

export interface WorkerSourceObservedProfile {
  sourceName: string | null;
  badgeNumber: string | null;
  employeeCode: string | null;
  employmentStatus: string | null;
  department: string | null;
  shift: string | null;
  lastObservedAt: string | null;
  linkStatus: WorkerSourceLinkStatus;
}

export interface WorkerAssignmentSummary {
  id: string;
  kind: WorkerAssignmentKind;
  factoryId: string;
  factoryName: string;
  productionLineId: string;
  productionLineName: string;
  stageNames: string[];
  periodLabel: string;
}

export interface WorkerHistoryEntry {
  id: string;
  kind: WorkerHistoryKind;
  title: string;
  detail: string;
  occurredAt: string;
  actorLabel: string;
}

export interface WorkerSourcePreviewItem {
  id: string;
  kind: WorkerSourcePreviewKind;
  title: string;
  detail: string;
}

export interface WorkerManagementProfile {
  id: string;
  local: WorkerLocalProfile;
  source: WorkerSourceObservedProfile;
  assignments: WorkerAssignmentSummary[];
  history: WorkerHistoryEntry[];
  sourcePreview: WorkerSourcePreviewItem[];
  assignmentStatus: WorkerAssignmentStatus;
  defaultSubStageId: string | null;
}

export interface WorkerManagementListItem {
  id: string;
  localName: string;
  sourceName: string | null;
  photoUrl: string | null;
  badgeNumber: string | null;
  employeeCode: string | null;
  assignmentLabel: string;
  factoryLineLabel: string;
  sourceLinkStatus: WorkerSourceLinkStatus;
  localProfileStatus: WorkerLocalProfileStatus;
  assignmentStatus: WorkerAssignmentStatus;
  localEmploymentStatus: WorkerLocalEmploymentStatus;
  factoryId: string | null;
  productionLineId: string | null;
  hasIdentityConflict: boolean;
}

export interface WorkerManagementQuery {
  page: number;
  pageSize: number;
  search: string;
  localEmploymentStatus: WorkerLocalEmploymentStatus | '';
}

export interface WorkerManagementPage {
  items: WorkerManagementListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
