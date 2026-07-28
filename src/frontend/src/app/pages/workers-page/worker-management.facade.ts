import { Inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { WORKER_MANAGEMENT_DATA_SOURCE, WorkerManagementDataSource } from './worker-management.data-source';
import { WorkerDepartmentAssignmentResult, WorkerDepartmentOption, WorkerManagementPage, WorkerManagementProfile, WorkerManagementQuery } from './worker-management.models';
import { WorkerManagementLocalUpdate } from './worker-management.data-source';

@Injectable()
export class WorkerManagementFacade {
  constructor(@Inject(WORKER_MANAGEMENT_DATA_SOURCE) private readonly dataSource: WorkerManagementDataSource) {}

  loadWorkers(query: WorkerManagementQuery): Observable<WorkerManagementPage> {
    return this.dataSource.loadPage(query);
  }

  loadProfile(workerId: string): Observable<WorkerManagementProfile> {
    return this.dataSource.loadProfile(workerId);
  }

  saveLocalProfile(worker: WorkerManagementProfile, update: WorkerManagementLocalUpdate): Observable<WorkerManagementProfile> {
    return this.dataSource.saveLocalProfile(worker, update);
  }

  uploadPhoto(workerId: string, photo: File): Observable<WorkerManagementProfile> {
    return this.dataSource.uploadPhoto(workerId, photo);
  }

  deletePhoto(workerId: string): Observable<WorkerManagementProfile> {
    return this.dataSource.deletePhoto(workerId);
  }

  loadActiveDepartments(): Observable<WorkerDepartmentOption[]> {
    return this.dataSource.loadActiveDepartments();
  }

  assignDepartment(workerId: string, departmentId: string, concurrencyToken: string): Observable<WorkerDepartmentAssignmentResult> {
    return this.dataSource.assignDepartment(workerId, departmentId, concurrencyToken);
  }
}
