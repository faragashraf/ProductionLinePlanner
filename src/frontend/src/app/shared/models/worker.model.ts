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
}
