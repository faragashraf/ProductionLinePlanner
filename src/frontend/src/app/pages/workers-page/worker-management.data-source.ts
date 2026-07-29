import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import {
  WorkerManagementPage,
  WorkerManagementProfile,
  WorkerManagementQuery,
  WorkerLocalEmploymentStatus,
  WorkerDepartmentOption,
  WorkerDepartmentAssignmentResult
} from './worker-management.models';

export interface WorkerManagementLocalUpdate {
  displayName: string;
  employmentStatus: WorkerLocalEmploymentStatus;
}

export interface WorkerManagementDataSource {
  loadPage(query: WorkerManagementQuery): Observable<WorkerManagementPage>;
  loadProfile(workerId: string): Observable<WorkerManagementProfile>;
  saveLocalProfile(worker: WorkerManagementProfile, update: WorkerManagementLocalUpdate): Observable<WorkerManagementProfile>;
  uploadPhoto(workerId: string, photo: File): Observable<WorkerManagementProfile>;
  deletePhoto(workerId: string): Observable<WorkerManagementProfile>;
  loadActiveDepartments(): Observable<WorkerDepartmentOption[]>;
  assignDepartment(workerId: string, departmentId: string, concurrencyToken: string): Observable<WorkerDepartmentAssignmentResult>;
}

export const WORKER_MANAGEMENT_DATA_SOURCE = new InjectionToken<WorkerManagementDataSource>('WORKER_MANAGEMENT_DATA_SOURCE');
