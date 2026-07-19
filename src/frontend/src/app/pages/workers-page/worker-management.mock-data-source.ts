import { Injectable } from '@angular/core';
import { NEVER, Observable, map, mergeMap, of, throwError, timer } from 'rxjs';
import { WORKER_MANAGEMENT_FIXTURES } from './worker-management.fixtures';
import {
  WorkerAssignmentStatus,
  WorkerManagementFilterOptions,
  WorkerManagementListItem,
  WorkerManagementMockScenario,
  WorkerManagementPage,
  WorkerManagementProfile,
  WorkerManagementQuery
} from './worker-management.models';
import { WorkerManagementDataSource } from './worker-management.data-source';

export const WORKER_MANAGEMENT_MOCK_SCENARIO_STORAGE_KEY = 'plp.worker-management.mock-scenario';

@Injectable()
export class WorkerManagementMockDataSource implements WorkerManagementDataSource {
  loadPage(query: WorkerManagementQuery): Observable<WorkerManagementPage> {
    const scenario = this.scenario;
    if (scenario === 'loading') return NEVER;
    if (scenario === 'error') {
      return timer(120).pipe(mergeMap(() => throwError(() => new Error('تعذر تحميل مساحة إدارة العاملين التجريبية.'))));
    }

    const profiles = scenario === 'empty' ? [] : WORKER_MANAGEMENT_FIXTURES;
    return timer(120).pipe(map(() => this.createPage(profiles, query)));
  }

  loadProfile(workerId: string): Observable<WorkerManagementProfile> {
    const profile = WORKER_MANAGEMENT_FIXTURES.find(item => item.id === workerId);
    if (!profile) return throwError(() => new Error('تعذر العثور على ملف العامل المطلوب.'));
    return timer(80).pipe(map(() => this.clone(profile)));
  }

  private createPage(profiles: readonly WorkerManagementProfile[], query: WorkerManagementQuery): WorkerManagementPage {
    const options = this.filterOptions(WORKER_MANAGEMENT_FIXTURES);
    const filtered = profiles
      .map(profile => this.toListItem(profile))
      .filter(item => this.matches(item, query));
    const pageSize = Math.max(1, query.pageSize);
    const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
    const page = Math.min(Math.max(1, query.page), totalPages);
    const offset = (page - 1) * pageSize;
    return {
      items: filtered.slice(offset, offset + pageSize),
      totalCount: filtered.length,
      page,
      pageSize,
      totalPages,
      filterOptions: options
    };
  }

  private toListItem(profile: WorkerManagementProfile): WorkerManagementListItem {
    const assignments = profile.assignments;
    const assignmentStatus: WorkerAssignmentStatus = assignments.length === 0
      ? 'unassigned'
      : assignments.some(item => item.kind === 'temporary') && assignments.some(item => item.kind === 'permanent')
        ? 'mixed'
        : 'assigned';
    const primaryAssignment = assignments.find(item => item.kind === 'temporary') ?? assignments[0];
    return {
      id: profile.id,
      localName: profile.local.displayName,
      sourceName: profile.source.sourceName,
      photoUrl: profile.local.photoUrl,
      badgeNumber: profile.source.badgeNumber,
      employeeCode: profile.source.employeeCode,
      assignmentLabel: assignmentStatus === 'unassigned'
        ? 'غير مسكن'
        : assignmentStatus === 'mixed'
          ? 'تسكين دائم مع نقل مؤقت'
          : primaryAssignment.periodLabel,
      factoryLineLabel: primaryAssignment ? `${primaryAssignment.factoryName} / ${primaryAssignment.productionLineName}` : 'لا يوجد تسكين حالي',
      sourceLinkStatus: profile.source.linkStatus,
      localProfileStatus: profile.local.profileStatus,
      assignmentStatus,
      localEmploymentStatus: profile.local.employmentStatus,
      factoryId: primaryAssignment?.factoryId ?? null,
      productionLineId: primaryAssignment?.productionLineId ?? null,
      hasIdentityConflict: profile.source.linkStatus === 'conflict'
    };
  }

  private matches(item: WorkerManagementListItem, query: WorkerManagementQuery): boolean {
    const search = query.search.trim().toLocaleLowerCase('ar');
    const searchable = [item.localName, item.sourceName, item.badgeNumber, item.employeeCode]
      .filter((value): value is string => Boolean(value))
      .join(' ')
      .toLocaleLowerCase('ar');
    return (!search || searchable.includes(search))
      && (!query.localProfileStatus || item.localProfileStatus === query.localProfileStatus)
      && (!query.sourceLinkStatus || item.sourceLinkStatus === query.sourceLinkStatus)
      && (!query.factoryId || item.factoryId === query.factoryId)
      && (!query.productionLineId || item.productionLineId === query.productionLineId)
      && (!query.assignmentStatus || item.assignmentStatus === query.assignmentStatus)
      && (!query.localEmploymentStatus || item.localEmploymentStatus === query.localEmploymentStatus);
  }

  private filterOptions(profiles: readonly WorkerManagementProfile[]): WorkerManagementFilterOptions {
    const assignments = profiles.flatMap(profile => profile.assignments);
    const factories = new Map(assignments.map(item => [item.factoryId, item.factoryName]));
    const productionLines = new Map(assignments.map(item => [item.productionLineId, item.productionLineName]));
    return {
      factories: [...factories].map(([value, label]) => ({ value, label })),
      productionLines: [...productionLines].map(([value, label]) => ({ value, label }))
    };
  }

  private clone<T>(value: T): T {
    return JSON.parse(JSON.stringify(value)) as T;
  }

  private get scenario(): WorkerManagementMockScenario {
    const value = typeof sessionStorage === 'undefined' ? null : sessionStorage.getItem(WORKER_MANAGEMENT_MOCK_SCENARIO_STORAGE_KEY);
    return value === 'empty' || value === 'error' || value === 'loading' ? value : 'default';
  }
}
