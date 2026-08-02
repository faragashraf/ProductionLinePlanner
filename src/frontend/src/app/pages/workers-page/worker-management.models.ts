export type WorkerLocalProfileStatus = 'complete' | 'needs-review' | 'source-pending';
export type WorkerSourceLinkStatus = 'linked' | 'unlinked' | 'conflict' | 'new-source' | 'missing-source';
export type WorkerAssignmentStatus = 'assigned' | 'unassigned' | 'multiple';
export type WorkerLocalEmploymentStatus = 'active' | 'inactive' | 'left-employment';
export type WorkerAssignmentKind = 'permanent';
export type WorkerProfileDataState = 'loaded' | 'empty' | 'forbidden' | 'error';
export type WorkerPhotoFilter = 'with-photo' | 'without-photo';

export interface WorkerProfileAccess {
  assignments: boolean;
  attendance: boolean;
  compensation: boolean;
}

export interface WorkerLocalSalary {
  amount: number;
  currencyCode: string;
  effectiveFrom: string;
}

export interface WorkerLocalProfile {
  displayName: string;
  photoUrl: string | null;
  phone: string | null;
  salary: WorkerLocalSalary | null;
  profileStatus: WorkerLocalProfileStatus;
  employmentStatus: WorkerLocalEmploymentStatus;
  employmentEndDate: string | null;
}

export interface WorkerSourceObservedProfile {
  sourceName: string | null;
  attendanceUserId: string | null;
  attendanceDepartmentId: number | null;
  badgeNumber: string | null;
  employeeCode: string | null;
  employmentStatus: string | null;
  department: string | null;
  shift: string | null;
  lastObservedAt: string | null;
  linkStatus: WorkerSourceLinkStatus;
}

export interface WorkerAttendanceSummary {
  productionDate: string;
  todayStatus: 'Present' | 'Late' | 'Absent' | 'Incomplete' | 'Unassigned' | 'NoMovement' | 'NeedsSync';
  attendanceDataAvailableForDate: boolean;
  firstCheckInUtc: string | null;
  lastCheckOutUtc: string | null;
  lastKnownMovementUtc: string | null;
}

export interface WorkerAttendanceHistoryMovement {
  occurredAtUtc: string;
  movementType: 'In' | 'Out';
}

export interface WorkerAttendanceHistoryRecord {
  recordId: string;
  productionDate: string;
  attendanceStatus: 'Present' | 'Late';
  source: string | null;
  movements: WorkerAttendanceHistoryMovement[];
}

export interface WorkerAttendanceHistoryPage {
  items: WorkerAttendanceHistoryRecord[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface WorkerAttendanceHistoryQuery {
  fromDate: string;
  toDate: string;
  page: number;
  pageSize: number;
}

export interface WorkerSystemSummary {
  createdAtUtc: string | null;
  updatedAtUtc: string | null;
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

export interface WorkerManagementProfile {
  id: string;
  local: WorkerLocalProfile;
  source: WorkerSourceObservedProfile;
  assignments: WorkerAssignmentSummary[];
  assignmentStatus: WorkerAssignmentStatus;
  defaultSubStageId: string | null;
  attendance: WorkerAttendanceSummary | null;
  organizationalDepartmentId?: string | null;
  organizationalDepartmentName?: string | null;
  organizationalFactoryName?: string | null;
  organizationalDepartmentConcurrencyToken?: string;
  system: WorkerSystemSummary;
  dataStates: {
    assignments: WorkerProfileDataState;
    attendance: WorkerProfileDataState;
    salary: WorkerProfileDataState;
  };
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
  organizationalDepartmentId?: string | null;
  organizationalDepartmentName?: string | null;
  organizationalFactoryName?: string | null;
  organizationalDepartmentConcurrencyToken?: string;
}

export interface WorkerDepartmentOption {
  id: string;
  name: string;
  code: string;
  factoryId: string;
  factoryName: string;
  searchLabel: string;
}

export interface WorkerDepartmentAssignmentResult {
  workerId: string;
  departmentId: string;
  departmentName: string;
  factoryId: string;
  factoryName: string;
  concurrencyToken: string;
}

export interface WorkerManagementQuery {
  page: number;
  pageSize: number;
  search: string;
  localEmploymentStatus: WorkerLocalEmploymentStatus | '';
  photoFilter?: WorkerPhotoFilter;
}

export interface WorkerManagementPage {
  items: WorkerManagementListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
