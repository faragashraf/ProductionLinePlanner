import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { buildApiUrl } from '../config/api.config';
import { FactoryMapApiService } from './factory-map-api.service';
import { PermissionService } from './permission.service';

describe('FactoryMapApiService', () => {
  let service: FactoryMapApiService;
  let http: HttpTestingController;
  let permissions: jasmine.SpyObj<PermissionService>;

  beforeEach(() => {
    permissions = jasmine.createSpyObj<PermissionService>('PermissionService', ['hasPermission']);
    permissions.hasPermission.and.returnValue(true);
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [{ provide: PermissionService, useValue: permissions }]
    });
    service = TestBed.inject(FactoryMapApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds structural staffing coverage from one batch summary request without stage worker requests', () => {
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [{ id: 'factory-1', name: 'مصنع 1' }] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1' }] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'main-1', departmentId: 'department-1', name: 'تجهيز' }] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'فحص', capacity: 2 }] } });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/staffing-coverage')).flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 2, requiredWorkersCount: 2, hasAuthoritativeRequiredWorkerCount: true, assignmentCoveragePercent: 100, staffingStatus: 'Staffed' }] });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/attendance-summary')).flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 2, presentAssignedWorkersCount: 1, absentAssignedWorkersCount: 1, attendanceDataStatus: 'Complete', attendanceStatus: 'PartiallyPresent' }] });

    expect(result.hasUsableBackendData).toBeTrue();
    expect(result.layout.lines[0].name).toBe('خط 1');
    expect(result.layout.lines[0].stages[0].subStages[0].name).toBe('فحص');
    expect(result.layout.lines[0].stages[0].subStages[0].workersCurrent).toBe(2);
    expect(result.layout.lines[0].stages[0].subStages[0].workersRequired).toBe(2);
    expect(result.layout.lines[0].stages[0].subStages[0].attendanceStatus).toBe('PartiallyPresent');
    expect(result.layout.lines[0].stages[0].subStages[0].presentAssignedWorkers).toBe(1);
    expect(result.layout.lines[0].readinessPercent).toBe(100);
    expect(result.layout.presentAssignedWorkers).toBe(1);
    expect(result.layout.attendanceSummaryText).toBe('1 من 2');
    http.expectNone((request) => /\/api\/factory-structure\/sub-stages\/[^/]+\/workers$/.test(request.url));
  });

  it('returns an empty real-data layout when the factory API has no records', () => {
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [] } });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/staffing-coverage')).flush({ success: true, data: [] });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/attendance-summary')).flush({ success: true, data: [] });

    expect(result.hasBackendData).toBeFalse();
    expect(result.layout.lines).toEqual([]);
    expect(result.fallbackReason).toBe('incomplete');
  });

  it('keeps a stage with an undefined requirement informational instead of rendering a misleading division by zero', () => {
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [{ id: 'factory-1', name: 'مصنع 1' }] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1' }] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'main-1', departmentId: 'department-1', name: 'تجهيز' }] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'فحص', capacity: 0 }] } });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/staffing-coverage')).flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 1, requiredWorkersCount: null, hasAuthoritativeRequiredWorkerCount: false, assignmentCoveragePercent: null, staffingStatus: 'RequirementNotDefined' }] });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/attendance-summary')).flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 1, presentAssignedWorkersCount: 0, absentAssignedWorkersCount: 1, attendanceDataStatus: 'Complete', attendanceStatus: 'AllAbsent' }] });

    const stage = result.layout.lines[0].stages[0].subStages[0];
    expect(stage.workersCurrent).toBe(1);
    expect(stage.workerRequirementDefined).toBeFalse();
    expect(stage.status).toBe('info');
    expect(result.layout.workerRequirementDefined).toBeFalse();
    expect(result.layout.attendanceSummaryText).toBe('0 من 1');
  });

  it('keeps the batch coverage request active beyond the former short timeout', fakeAsync(() => {
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [{ id: 'factory-1', name: 'مصنع 1' }] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1' }] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'main-1', departmentId: 'department-1', name: 'تجهيز' }] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'فحص', capacity: 1 }] } });
    const coverageRequest = http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/staffing-coverage'));
    const attendanceRequest = http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/attendance-summary'));

    tick(1600);
    expect(result).toBeUndefined();

    coverageRequest.flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 1, requiredWorkersCount: 1, hasAuthoritativeRequiredWorkerCount: true, assignmentCoveragePercent: 100, staffingStatus: 'Staffed' }] });
    attendanceRequest.flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 1, presentAssignedWorkersCount: 1, absentAssignedWorkersCount: 0, attendanceDataStatus: 'Complete', attendanceStatus: 'FullyPresent' }] });
    expect(result.hasUsableBackendData).toBeTrue();
  }));

  it('does not request attendance summaries when the caller lacks attendance.view', () => {
    permissions.hasPermission.and.returnValue(false);
    let result: any;
    service.loadFactoryMapData().subscribe((value) => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [{ id: 'factory-1', name: 'مصنع 1' }] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1' }] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'main-1', departmentId: 'department-1', name: 'تجهيز' }] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'فحص' }] } });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/staffing-coverage')).flush({ success: true, data: [{ subStageId: 'sub-1', assignedWorkersCount: 1, requiredWorkersCount: 1, hasAuthoritativeRequiredWorkerCount: true, assignmentCoveragePercent: 100, staffingStatus: 'Staffed' }] });

    expect(result.layout.lines[0].stages[0].subStages[0].attendanceStatus).toBe('NotAuthorized');
    http.expectNone(buildApiUrl('/api/factory-structure/sub-stages/attendance-summary'));
  });

  it('uses authoritative distinct hierarchy counts when the same worker participates in multiple stages', () => {
    let result: any;
    service.loadFactoryMapData().subscribe(value => result = value);

    http.expectOne(buildApiUrl('/api/factories?pageSize=200')).flush({ success: true, data: { items: [{ id: 'factory-1', name: 'مصنع 1' }] } });
    http.expectOne(buildApiUrl('/api/production-lines?pageSize=200')).flush({ success: true, data: { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1' }] } });
    http.expectOne(buildApiUrl('/api/main-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'main-1', departmentId: 'department-1', name: 'تجهيز' }] } });
    http.expectOne(buildApiUrl('/api/sub-stages?isActive=true&pageSize=200')).flush({ success: true, data: { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'فحص' }, { id: 'sub-2', mainStageId: 'main-1', name: 'تجميع' }] } });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/staffing-coverage')).flush({ success: true, data: [
      { subStageId: 'sub-1', assignedWorkersCount: 1, requiredWorkersCount: 1, hasAuthoritativeRequiredWorkerCount: true, assignmentCoveragePercent: 100, staffingStatus: 'Staffed', mainStageDistinctWorkersCount: 1, productionLineDistinctWorkersCount: 1, factoryDistinctWorkersCount: 1 },
      { subStageId: 'sub-2', assignedWorkersCount: 1, requiredWorkersCount: 1, hasAuthoritativeRequiredWorkerCount: true, assignmentCoveragePercent: 100, staffingStatus: 'Staffed', mainStageDistinctWorkersCount: 1, productionLineDistinctWorkersCount: 1, factoryDistinctWorkersCount: 1 }
    ] });
    http.expectOne(buildApiUrl('/api/factory-structure/sub-stages/attendance-summary')).flush({ success: true, data: [
      { subStageId: 'sub-1', assignedWorkersCount: 1, presentAssignedWorkersCount: 1, absentAssignedWorkersCount: 0, attendanceDataStatus: 'Complete', attendanceStatus: 'FullyPresent', mainStageDistinctPresentWorkersCount: 1, mainStageDistinctAbsentWorkersCount: 0, productionLineDistinctPresentWorkersCount: 1, productionLineDistinctAbsentWorkersCount: 0, factoryDistinctPresentWorkersCount: 1, factoryDistinctAbsentWorkersCount: 0 },
      { subStageId: 'sub-2', assignedWorkersCount: 1, presentAssignedWorkersCount: 1, absentAssignedWorkersCount: 0, attendanceDataStatus: 'Complete', attendanceStatus: 'FullyPresent', mainStageDistinctPresentWorkersCount: 1, mainStageDistinctAbsentWorkersCount: 0, productionLineDistinctPresentWorkersCount: 1, productionLineDistinctAbsentWorkersCount: 0, factoryDistinctPresentWorkersCount: 1, factoryDistinctAbsentWorkersCount: 0 }
    ] });

    expect(result.layout.workersCurrent).toBe(1);
    expect(result.layout.presentAssignedWorkers).toBe(1);
    expect(result.layout.lines[0].workersCurrent).toBe(1);
    expect(result.layout.lines[0].stages[0].workersCurrent).toBe(1);
    expect(result.layout.readinessPercent).toBe(100);
    expect(result.layout.lines[0].stages[0].subStages.map((stage: any) => stage.workersCurrent)).toEqual([1, 1]);
  });
});
