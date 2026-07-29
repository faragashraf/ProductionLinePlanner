export type WorkersPageState = 'على رأس العمل' | 'خارج الخدمة';

export interface WorkerPermanentAssignment {
  id: string;
  factoryId: string;
  factoryName: string;
  productionLineId: string;
  productionLineName: string;
  departmentId: string;
  departmentName: string;
  mainStageId: string;
  mainStageName: string;
  subStageId: string;
  subStageName: string;
  assignedAtUtc: string;
}

export interface WorkerPageItem {
  id?: string;
  code: string;
  fullName: string;
  state: WorkersPageState;
  email?: string;
  phone?: string;
  department?: string;
  employmentStatus?: string;
  isActive?: boolean;
  photoReference?: string;
  hasPhoto?: boolean;
  photoVersion?: string;
  attendanceUserId?: string;
  badgeNumber?: string;
  attendanceDepartmentId?: number;
  defaultSubStageId?: string;
  permanentAssignments?: WorkerPermanentAssignment[];
  employmentEndDate?: string;
  lastExternalSyncAt?: string;
  createdAtUtc?: string;
  updatedAtUtc?: string;
  organizationalDepartmentId?: string;
  organizationalDepartmentName?: string;
  organizationalFactoryId?: string;
  organizationalFactoryName?: string;
  organizationalDepartmentConcurrencyToken?: string;
}
