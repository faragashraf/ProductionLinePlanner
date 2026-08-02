import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, Optional } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, switchMap, throwError } from 'rxjs';
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
  WorkerPhotoFilter,
  WorkerSourceLinkStatus,
  WorkerDepartmentOption,
  WorkerDepartmentAssignmentResult,
  WorkerAttendanceSummary,
  WorkerProfileAccess,
  WorkerProfileDataState,
  WorkerAttendanceHistoryPage,
  WorkerAttendanceHistoryQuery
} from './worker-management.models';
import {
  WorkerManagementDataSource,
  WorkerManagementLocalUpdate
} from './worker-management.data-source';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { AttendanceWorkforceApiService, WorkerAttendanceProfileSummary } from '../../core/services/attendance-workforce-api.service';

interface OptionalProfileValue<T> {
  state: WorkerProfileDataState;
  value: T | null;
}

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
    @Optional() private readonly masterDataApi?: ManufacturingMasterDataApiService,
    @Optional() private readonly attendanceApi?: AttendanceWorkforceApiService
  ) {}

  loadPage(query: WorkerManagementQuery): Observable<WorkerManagementPage> {
    return this.workersApi.loadWorkers({
      page: query.page,
      pageSize: query.pageSize,
      search: query.search,
      hasPhoto: this.toPhotoFilterBoolean(query.photoFilter),
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

  loadProfile(workerId: string, access: WorkerProfileAccess): Observable<WorkerManagementProfile> {
    return forkJoin({
      worker: this.workersApi.getWorker(workerId),
      salary: this.loadSalary(workerId, access.compensation),
      attendance: this.loadAttendance(workerId, access.attendance)
    }).pipe(map(({ worker, salary, attendance }) => this.toProfile(worker, access, salary, attendance)));
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
      map(updated => this.mergeWorker(worker, updated))
    );
  }

  uploadPhoto(worker: WorkerManagementProfile, photo: File): Observable<WorkerManagementProfile> {
    return this.workersApi.uploadWorkerPhoto(worker.id, photo, this.localCorrelation()).pipe(map(change => ({
      ...worker,
      local: { ...worker.local, photoUrl: change.photo.photoReference }
    })));
  }

  deletePhoto(worker: WorkerManagementProfile): Observable<WorkerManagementProfile> {
    return this.workersApi.deleteWorkerPhoto(worker.id, this.localCorrelation()).pipe(map(() => ({
      ...worker,
      local: { ...worker.local, photoUrl: null }
    })));
  }

  private toListItem(worker: WorkerPageItem): WorkerManagementListItem {
    const permanentAssignments = worker.permanentAssignments ?? [];
    const assignmentStatus = this.assignmentStatus(permanentAssignments.length);
    const primaryAssignment = permanentAssignments[0];
    const sourceLinkStatus = this.sourceLinkStatus(worker);
    return {
      id: worker.id ?? '',
      localName: worker.fullName,
      sourceName: null,
      photoUrl: worker.hasPhoto ? worker.photoReference ?? null : null,
      badgeNumber: worker.badgeNumber ?? null,
      employeeCode: worker.code,
      assignmentLabel: primaryAssignment
        ? `${primaryAssignment.mainStageName} / ${primaryAssignment.subStageName}${permanentAssignments.length > 1 ? ` +${permanentAssignments.length - 1}` : ''}`
        : 'غير مسكن حاليًا',
      factoryLineLabel: primaryAssignment
        ? `${primaryAssignment.factoryName} / ${primaryAssignment.productionLineName}`
        : 'لا يوجد تسكين دائم نشط',
      sourceLinkStatus,
      localProfileStatus: 'complete',
      assignmentStatus,
      localEmploymentStatus: this.toEmploymentStatus(worker.employmentStatus, worker.isActive),
      factoryId: primaryAssignment?.factoryId ?? null,
      productionLineId: primaryAssignment?.productionLineId ?? null,
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

  loadAttendanceHistory(workerId: string, query: WorkerAttendanceHistoryQuery): Observable<WorkerAttendanceHistoryPage> {
    if (!this.attendanceApi) return throwError(() => new Error('تعذر تحميل سجل الحضور والانصراف.'));
    return this.attendanceApi.getWorkerHistory(workerId, { ...query, sortDirection: 'desc' }).pipe(map(page => ({
      items: page.items,
      page: page.page,
      pageSize: page.pageSize,
      totalCount: page.totalCount,
      totalPages: page.totalPages
    })));
  }

  private toProfile(
    worker: WorkerPageItem,
    access: WorkerProfileAccess,
    salary: OptionalProfileValue<{ amount: number; currencyCode: string; effectiveFrom: string }>,
    attendance: OptionalProfileValue<WorkerAttendanceProfileSummary>
  ): WorkerManagementProfile {
    const assignments = access.assignments ? this.mapAssignments(worker) : [];
    const assignmentStatus = this.assignmentStatus(assignments.length);
    return {
      id: worker.id ?? '',
      local: {
        displayName: worker.fullName,
        photoUrl: worker.hasPhoto ? worker.photoReference ?? null : null,
        phone: worker.phone ?? null,
        salary: salary.value,
        profileStatus: 'complete',
        employmentStatus: this.toEmploymentStatus(worker.employmentStatus, worker.isActive),
        employmentEndDate: worker.employmentEndDate ?? null
      },
      source: {
        sourceName: null,
        attendanceUserId: worker.attendanceUserId ?? null,
        attendanceDepartmentId: worker.attendanceDepartmentId ?? null,
        badgeNumber: worker.badgeNumber ?? null,
        employeeCode: worker.code,
        employmentStatus: worker.employmentStatus ?? null,
        department: worker.department ?? null,
        shift: null,
        lastObservedAt: worker.lastExternalSyncAt ?? null,
        linkStatus: this.sourceLinkStatus(worker)
      },
      assignments,
      assignmentStatus,
      defaultSubStageId: assignments[0]?.stageNames.length ? worker.defaultSubStageId ?? null : null,
      attendance: attendance.value ? this.mapAttendance(attendance.value) : null,
      organizationalDepartmentId: worker.organizationalDepartmentId ?? null,
      organizationalDepartmentName: worker.organizationalDepartmentName ?? null,
      organizationalFactoryName: worker.organizationalFactoryName ?? null,
      organizationalDepartmentConcurrencyToken: worker.organizationalDepartmentConcurrencyToken ?? '',
      system: {
        createdAtUtc: worker.createdAtUtc ?? null,
        updatedAtUtc: worker.updatedAtUtc ?? null
      },
      dataStates: {
        assignments: access.assignments ? assignments.length ? 'loaded' : 'empty' : 'forbidden',
        attendance: attendance.state,
        salary: salary.state
      }
    };
  }

  private mergeWorker(existing: WorkerManagementProfile, worker: WorkerPageItem): WorkerManagementProfile {
    const canUseAssignments = existing.dataStates.assignments !== 'forbidden';
    const assignments = canUseAssignments ? this.mapAssignments(worker) : existing.assignments;
    return {
      ...existing,
      local: {
        ...existing.local,
        displayName: worker.fullName,
        phone: worker.phone ?? null,
        photoUrl: worker.hasPhoto ? worker.photoReference ?? null : existing.local.photoUrl,
        employmentStatus: this.toEmploymentStatus(worker.employmentStatus, worker.isActive),
        employmentEndDate: worker.employmentEndDate ?? null
      },
      source: {
        ...existing.source,
        attendanceUserId: worker.attendanceUserId ?? null,
        attendanceDepartmentId: worker.attendanceDepartmentId ?? null,
        badgeNumber: worker.badgeNumber ?? null,
        employeeCode: worker.code,
        employmentStatus: worker.employmentStatus ?? null,
        department: worker.department ?? null,
        lastObservedAt: worker.lastExternalSyncAt ?? null,
        linkStatus: this.sourceLinkStatus(worker)
      },
      assignments,
      assignmentStatus: this.assignmentStatus(assignments.length),
      defaultSubStageId: worker.defaultSubStageId ?? null,
      organizationalDepartmentId: worker.organizationalDepartmentId ?? existing.organizationalDepartmentId ?? null,
      organizationalDepartmentName: worker.organizationalDepartmentName ?? existing.organizationalDepartmentName ?? null,
      organizationalFactoryName: worker.organizationalFactoryName ?? existing.organizationalFactoryName ?? null,
      organizationalDepartmentConcurrencyToken: worker.organizationalDepartmentConcurrencyToken ?? existing.organizationalDepartmentConcurrencyToken ?? '',
      system: {
        createdAtUtc: worker.createdAtUtc ?? existing.system.createdAtUtc,
        updatedAtUtc: worker.updatedAtUtc ?? existing.system.updatedAtUtc
      },
      dataStates: {
        ...existing.dataStates,
        assignments: canUseAssignments ? assignments.length ? 'loaded' : 'empty' : 'forbidden'
      }
    };
  }

  private mapAssignments(worker: WorkerPageItem): WorkerManagementProfile['assignments'] {
    return (worker.permanentAssignments ?? []).map(assignment => ({
      id: assignment.id,
      kind: 'permanent',
      factoryId: assignment.factoryId,
      factoryName: assignment.factoryName,
      productionLineId: assignment.productionLineId,
      productionLineName: assignment.productionLineName,
      stageNames: [assignment.mainStageName, assignment.subStageName].filter(Boolean),
      periodLabel: assignment.assignedAtUtc
        ? `بدأ في ${new Intl.DateTimeFormat('ar-EG', { dateStyle: 'medium', timeZone: 'Africa/Cairo' }).format(new Date(assignment.assignedAtUtc))}`
        : 'تسكين دائم نشط'
    }));
  }

  private mapAttendance(value: WorkerAttendanceProfileSummary): WorkerAttendanceSummary {
    return {
      productionDate: value.productionDate,
      todayStatus: value.todayStatus,
      attendanceDataAvailableForDate: value.attendanceDataAvailableForDate,
      firstCheckInUtc: value.firstCheckInUtc,
      lastCheckOutUtc: value.lastCheckOutUtc,
      lastKnownMovementUtc: value.lastKnownMovementUtc
    };
  }

  private loadSalary(workerId: string, permitted: boolean): Observable<OptionalProfileValue<{ amount: number; currencyCode: string; effectiveFrom: string }>> {
    if (!permitted) return of({ state: 'forbidden', value: null });
    return this.workersApi.getCurrentSalary(workerId).pipe(
      map(value => ({
        state: 'loaded' as const,
        value: { amount: value.amount, currencyCode: value.currencyCode, effectiveFrom: value.effectiveFrom }
      })),
      catchError(error => of({
        state: this.errorStatus(error) === 404 ? 'empty' as const : this.errorStatus(error) === 403 ? 'forbidden' as const : 'error' as const,
        value: null
      }))
    );
  }

  private loadAttendance(workerId: string, permitted: boolean): Observable<OptionalProfileValue<WorkerAttendanceProfileSummary>> {
    if (!permitted) return of({ state: 'forbidden', value: null });
    if (!this.attendanceApi) return of({ state: 'error', value: null });
    return this.attendanceApi.getProfileSummary(workerId).pipe(
      map(value => ({ state: 'loaded' as const, value })),
      catchError(error => of({
        state: this.errorStatus(error) === 403 ? 'forbidden' as const : 'error' as const,
        value: null
      }))
    );
  }

  private errorStatus(error: unknown): number {
    return error instanceof HttpErrorResponse ? error.status : typeof error === 'object' && error !== null && 'status' in error
      ? Number((error as { status?: unknown }).status) || 0
      : 0;
  }

  private assignmentStatus(count: number): WorkerAssignmentStatus {
    return count > 1 ? 'multiple' : count === 1 ? 'assigned' : 'unassigned';
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
      ...(worker.source.attendanceUserId ? { attendanceUserId: worker.source.attendanceUserId } : {}),
      ...(worker.source.attendanceDepartmentId !== null ? { attendanceDepartmentId: worker.source.attendanceDepartmentId } : {}),
      ...(worker.organizationalDepartmentId ? { organizationalDepartmentId: worker.organizationalDepartmentId } : {}),
      ...(worker.organizationalDepartmentName ? { organizationalDepartmentName: worker.organizationalDepartmentName } : {}),
      ...(worker.organizationalFactoryName ? { organizationalFactoryName: worker.organizationalFactoryName } : {}),
      ...(worker.organizationalDepartmentConcurrencyToken ? { organizationalDepartmentConcurrencyToken: worker.organizationalDepartmentConcurrencyToken } : {}),
      ...(worker.defaultSubStageId ? { defaultSubStageId: worker.defaultSubStageId } : {}),
      ...(worker.local.employmentEndDate ? { employmentEndDate: worker.local.employmentEndDate } : {}),
      ...(worker.system.createdAtUtc ? { createdAtUtc: worker.system.createdAtUtc } : {}),
      ...(worker.system.updatedAtUtc ? { updatedAtUtc: worker.system.updatedAtUtc } : {})
    };
  }

  private localCorrelation(): string | undefined {
    return this.manufacturingRealtime?.registerLocalOperation('employees');
  }

  private toPhotoFilterBoolean(filter?: WorkerPhotoFilter): boolean | undefined {
    if (filter === 'with-photo') return true;
    if (filter === 'without-photo') return false;
    return undefined;
  }
}
