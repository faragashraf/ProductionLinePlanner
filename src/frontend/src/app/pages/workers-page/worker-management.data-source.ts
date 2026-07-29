import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import {
  WorkerManagementPage,
  WorkerManagementProfile,
  WorkerManagementQuery,
  WorkerLocalEmploymentStatus,
  WorkerProfileAccess,
  WorkerDepartmentOption,
  WorkerDepartmentAssignmentResult,
  WorkerAttendanceHistoryPage,
  WorkerAttendanceHistoryQuery
} from './worker-management.models';

export interface WorkerManagementLocalUpdate {
  displayName: string;
  employmentStatus: WorkerLocalEmploymentStatus;
}

export interface WorkerManagementDataSource {
  loadPage(query: WorkerManagementQuery): Observable<WorkerManagementPage>;
  loadProfile(workerId: string, access: WorkerProfileAccess): Observable<WorkerManagementProfile>;
  saveLocalProfile(worker: WorkerManagementProfile, update: WorkerManagementLocalUpdate): Observable<WorkerManagementProfile>;
  uploadPhoto(worker: WorkerManagementProfile, photo: File): Observable<WorkerManagementProfile>;
  deletePhoto(worker: WorkerManagementProfile): Observable<WorkerManagementProfile>;
  loadActiveDepartments(): Observable<WorkerDepartmentOption[]>;
  assignDepartment(workerId: string, departmentId: string, concurrencyToken: string): Observable<WorkerDepartmentAssignmentResult>;
  loadAttendanceHistory(workerId: string, query: WorkerAttendanceHistoryQuery): Observable<WorkerAttendanceHistoryPage>;
}

export const WORKER_MANAGEMENT_DATA_SOURCE = new InjectionToken<WorkerManagementDataSource>('WORKER_MANAGEMENT_DATA_SOURCE');
