export type WorkersPageState = 'على رأس العمل' | 'خارج الخدمة';

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
  defaultSubStageId?: string;
  organizationalDepartmentId?: string;
  organizationalDepartmentName?: string;
  organizationalFactoryId?: string;
  organizationalFactoryName?: string;
  organizationalDepartmentConcurrencyToken?: string;
}
