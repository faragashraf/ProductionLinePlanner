import { Injectable, Optional } from '@angular/core';
import { Observable, forkJoin, map, of, switchMap, throwError } from 'rxjs';
import { WorkerPageItem } from '../../shared/models/worker.model';
import {
  WorkerEmploymentStatusUpdate,
  WorkersApiService
} from '../../core/services/workers-api.service';
import {
  WorkerAssignmentStatus,
  WorkerLocalEmploymentStatus,
  WorkerManagementListItem,
  WorkerManagementPage,
  WorkerManagementProfile,
  WorkerManagementQuery,
  WorkerSourceLinkStatus,
  WorkerDepartmentOption,
  WorkerDepartmentAssignmentResult
} from './worker-management.models';
import {
  WorkerManagementDataSource,
  WorkerManagementLocalUpdate
} from './worker-management.data-source';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';

/**
 * Runtime worker workspace source. It uses only Planner APIs backed by the
 * application database; it never reads the attendance source or supplies
 * fallback fixture data when a request fails.
 */
@Injectable()
export class WorkerManagementApiDataSource implements WorkerManagementDataSource {
  constructor(
    private readonly workersApi: WorkersApiService,
    @Optional() private readonly manufacturingRealtime?: ManufacturingRealtimeService,
    @Optional() private readonly masterDataApi?: ManufacturingMasterDataApiService
  ) {}

  loadPage(query: WorkerManagementQuery): Observable<WorkerManagementPage> {
    return this.workersApi.loadWorkers({
      page: query.page,
      pageSize: query.pageSize,
      search: query.search,
      serviceStatus: query.localEmploymentStatus === 'active'
        ? 'active'
        : query.localEmploymentStatus ? 'inactive' : 'all'
    }).pipe(
      switchMap(result => {
        if (result.hasBackendData && !result.hasUsableBackendData) {
          return throwError(() => new Error('تعذر قراءة بيانات العاملين من الاستجابة الموثوقة.'));
        }

        return of({
          items: result.workers.map(worker => this.toListItem(worker)),
          totalCount: result.totalCount,
          page: result.page,
          pageSize: result.pageSize,
          totalPages: result.totalPages
        });
      })
    );
  }

  loadProfile(workerId: string): Observable<WorkerManagementProfile> {
    return this.workersApi.getWorker(workerId).pipe(map(worker => this.toProfile(worker)));
  }

  saveLocalProfile(worker: WorkerManagementProfile, update: WorkerManagementLocalUpdate): Observable<WorkerManagementProfile> {
    const nameChanged = worker.local.displayName !== update.displayName.trim();
    const statusChanged = worker.local.employmentStatus !== update.employmentStatus;
    if (!nameChanged && !statusChanged) return of(worker);

    const workerUpdate = nameChanged
      ? this.workersApi.updateWorker(worker.id, { fullName: update.displayName.trim() }, this.localCorrelation())
      : of<WorkerPageItem>(this.toWorkerPageItem(worker));

    return workerUpdate.pipe(
      switchMap(updated => statusChanged
        ? this.workersApi.setEmploymentStatus(worker.id, {
          employmentStatus: this.toApiEmploymentStatus(update.employmentStatus)
        }, this.localCorrelation())
        : of(updated)),
      map(updated => this.toProfile(updated))
    );
  }

  uploadPhoto(workerId: string, photo: File): Observable<WorkerManagementProfile> {
    return this.workersApi.uploadWorkerPhoto(workerId, photo, this.localCorrelation()).pipe(
      switchMap(() => this.loadProfile(workerId))
    );
  }

  deletePhoto(workerId: string): Observable<WorkerManagementProfile> {
    return this.workersApi.deleteWorkerPhoto(workerId, this.localCorrelation()).pipe(
      switchMap(() => this.loadProfile(workerId))
    );
  }

  private toListItem(worker: WorkerPageItem): WorkerManagementListItem {
    const assignmentStatus: WorkerAssignmentStatus = worker.defaultSubStageId ? 'assigned' : 'unassigned';
    const sourceLinkStatus = this.sourceLinkStatus(worker);
    return {
      id: worker.id ?? '',
      localName: worker.fullName,
      sourceName: null,
      photoUrl: worker.hasPhoto ? worker.photoReference ?? null : null,
      badgeNumber: worker.badgeNumber ?? null,
      employeeCode: worker.code,
      assignmentLabel: assignmentStatus === 'assigned' ? 'مرتبط بمرحلة محلية' : 'لا يوجد تسكين افتراضي نشط',
      factoryLineLabel: assignmentStatus === 'assigned'
        ? 'تفاصيل المصنع والخط غير متاحة في واجهة العاملين الحالية'
        : 'لا يوجد تسكين حالي',
      sourceLinkStatus,
      localProfileStatus: 'complete',
      assignmentStatus,
      localEmploymentStatus: this.toEmploymentStatus(worker.employmentStatus, worker.isActive),
      factoryId: null,
      productionLineId: null,
      hasIdentityConflict: false,
      organizationalDepartmentId: worker.organizationalDepartmentId ?? null,
      organizationalDepartmentName: worker.organizationalDepartmentName ?? null,
      organizationalFactoryName: worker.organizationalFactoryName ?? null,
      organizationalDepartmentConcurrencyToken: worker.organizationalDepartmentConcurrencyToken ?? ''
    };
  }

  loadActiveDepartments(): Observable<WorkerDepartmentOption[]> {
    if (!this.masterDataApi) return throwError(() => new Error('تعذر تحميل كتالوج الأقسام المحلية.'));
    return forkJoin({
      departments: this.masterDataApi.departments(undefined, false),
      factories: this.masterDataApi.factories()
    }).pipe(map(({ departments, factories }) => {
      const activeFactories = new Map(factories
        .filter(factory => factory.isActive)
        .map(factory => [factory.id, factory.name]));
      return departments
        .filter(department => !!department.id && department.isActive !== false && !!department.factoryId && activeFactories.has(department.factoryId))
        .map(department => {
          const factoryName = activeFactories.get(department.factoryId!)!;
          const name = department.nameAr || department.name || 'قسم غير محدد';
          const code = department.code ?? '';
          return {
            id: department.id!, name, code, factoryId: department.factoryId!, factoryName,
            searchLabel: [name, code, factoryName].filter(Boolean).join(' · ')
          };
        })
        .sort((first, second) => first.factoryName.localeCompare(second.factoryName, 'ar') || first.name.localeCompare(second.name, 'ar'));
    }));
  }

  assignDepartment(workerId: string, departmentId: string, concurrencyToken: string): Observable<WorkerDepartmentAssignmentResult> {
    return this.workersApi.assignOrganizationalDepartment(workerId, departmentId, concurrencyToken, this.localCorrelation())
      .pipe(map(result => ({
        workerId: result.workerId,
        departmentId: result.departmentId,
        departmentName: result.departmentName,
        factoryId: result.factoryId,
        factoryName: result.factoryName,
        concurrencyToken: result.concurrencyToken
      })));
  }

  private toProfile(worker: WorkerPageItem): WorkerManagementProfile {
    const assignmentStatus: WorkerAssignmentStatus = worker.defaultSubStageId ? 'assigned' : 'unassigned';
    return {
      id: worker.id ?? '',
      local: {
        displayName: worker.fullName,
        photoUrl: worker.hasPhoto ? worker.photoReference ?? null : null,
        salary: null,
        profileStatus: 'complete',
        employmentStatus: this.toEmploymentStatus(worker.employmentStatus, worker.isActive)
      },
      source: {
        sourceName: null,
        badgeNumber: worker.badgeNumber ?? null,
        employeeCode: worker.code,
        employmentStatus: null,
        department: null,
        shift: null,
        lastObservedAt: null,
        linkStatus: this.sourceLinkStatus(worker)
      },
      assignments: [],
      history: [],
      sourcePreview: [],
      assignmentStatus,
      defaultSubStageId: worker.defaultSubStageId ?? null
    };
  }

  private sourceLinkStatus(worker: WorkerPageItem): WorkerSourceLinkStatus {
    return worker.attendanceUserId || worker.badgeNumber ? 'linked' : 'unlinked';
  }

  private toEmploymentStatus(value: string | undefined, isActive: boolean | undefined): WorkerLocalEmploymentStatus {
    if (value === 'LeftEmployment') return 'left-employment';
    if (value === 'Suspended' || !isActive) return 'inactive';
    return 'active';
  }

  private toApiEmploymentStatus(status: WorkerLocalEmploymentStatus): WorkerEmploymentStatusUpdate['employmentStatus'] {
    return status === 'active' ? 'Active' : status === 'left-employment' ? 'LeftEmployment' : 'Suspended';
  }

  private toWorkerPageItem(worker: WorkerManagementProfile): WorkerPageItem {
    return {
      id: worker.id,
      code: worker.source.employeeCode ?? worker.source.badgeNumber ?? worker.id,
      fullName: worker.local.displayName,
      state: worker.local.employmentStatus === 'active' ? 'على رأس العمل' : 'خارج الخدمة',
      employmentStatus: this.toApiEmploymentStatus(worker.local.employmentStatus),
      isActive: worker.local.employmentStatus === 'active',
      ...(worker.local.photoUrl ? { photoReference: worker.local.photoUrl, hasPhoto: true } : { hasPhoto: false }),
      ...(worker.source.badgeNumber ? { badgeNumber: worker.source.badgeNumber } : {}),
      ...(worker.defaultSubStageId ? { defaultSubStageId: worker.defaultSubStageId } : {})
    };
  }

  private localCorrelation(): string | undefined {
    return this.manufacturingRealtime?.registerLocalOperation('employees');
  }
}
