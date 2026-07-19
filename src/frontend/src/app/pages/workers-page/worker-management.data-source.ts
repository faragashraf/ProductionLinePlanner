import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';
import { WorkerManagementPage, WorkerManagementProfile, WorkerManagementQuery } from './worker-management.models';

export interface WorkerManagementDataSource {
  loadPage(query: WorkerManagementQuery): Observable<WorkerManagementPage>;
  loadProfile(workerId: string): Observable<WorkerManagementProfile>;
}

export const WORKER_MANAGEMENT_DATA_SOURCE = new InjectionToken<WorkerManagementDataSource>('WORKER_MANAGEMENT_DATA_SOURCE');
