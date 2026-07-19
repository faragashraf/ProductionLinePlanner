import { Inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { WORKER_MANAGEMENT_DATA_SOURCE, WorkerManagementDataSource } from './worker-management.data-source';
import { WorkerManagementPage, WorkerManagementProfile, WorkerManagementQuery } from './worker-management.models';

@Injectable()
export class WorkerManagementFacade {
  constructor(@Inject(WORKER_MANAGEMENT_DATA_SOURCE) private readonly dataSource: WorkerManagementDataSource) {}

  loadWorkers(query: WorkerManagementQuery): Observable<WorkerManagementPage> {
    return this.dataSource.loadPage(query);
  }

  loadProfile(workerId: string): Observable<WorkerManagementProfile> {
    return this.dataSource.loadProfile(workerId);
  }
}
