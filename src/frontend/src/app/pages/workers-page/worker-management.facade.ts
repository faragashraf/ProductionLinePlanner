import { Inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { WORKER_MANAGEMENT_DATA_SOURCE, WorkerManagementDataSource } from './worker-management.data-source';
import { WorkerAttendanceHistoryPage, WorkerAttendanceHistoryQuery, WorkerDepartmentAssignmentResult, WorkerDepartmentOption, WorkerManagementPage, WorkerManagementProfile, WorkerManagementQuery, WorkerProfileAccess } from './worker-management.models';
import { WorkerManagementLocalUpdate } from './worker-management.data-source';

@Injectable()
export class WorkerManagementFacade {
  constructor(@Inject(WORKER_MANAGEMENT_DATA_SOURCE) private readonly dataSource: WorkerManagementDataSource) {}

  loadWorkers(query: WorkerManagementQuery): Observable<WorkerManagementPage> {
    return this.dataSource.loadPage(query);
  }

  loadProfile(workerId: string, access: WorkerProfileAccess): Observable<WorkerManagementProfile> {
    return this.dataSource.loadProfile(workerId, access);
  }

  saveLocalProfile(worker: WorkerManagementProfile, update: WorkerManagementLocalUpdate): Observable<WorkerManagementProfile> {
    return this.dataSource.saveLocalProfile(worker, update);
  }

  uploadPhoto(worker: WorkerManagementProfile, photo: File): Observable<WorkerManagementProfile> {
    return this.dataSource.uploadPhoto(worker, photo);
  }

  deletePhoto(worker: WorkerManagementProfile): Observable<WorkerManagementProfile> {
    return this.dataSource.deletePhoto(worker);
  }

  loadActiveDepartments(): Observable<WorkerDepartmentOption[]> {
    return this.dataSource.loadActiveDepartments();
  }

  assignDepartment(workerId: string, departmentId: string, concurrencyToken: string): Observable<WorkerDepartmentAssignmentResult> {
    return this.dataSource.assignDepartment(workerId, departmentId, concurrencyToken);
  }

  loadAttendanceHistory(workerId: string, query: WorkerAttendanceHistoryQuery): Observable<WorkerAttendanceHistoryPage> {
    return this.dataSource.loadAttendanceHistory(workerId, query);
  }
}
