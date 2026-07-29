import { of } from 'rxjs';
import { OperationalReadinessApiService } from '../../core/services/operational-readiness-api.service';
import {
  AttendanceSyncFreshness,
  OperationalReadinessDelta,
  OperationalReadinessMetrics,
  OperationalReadinessSnapshot,
  OperationalReadinessStages,
  OperationalReadinessWorkers
} from '../../shared/models/operational-readiness.model';
import { FactoryReadinessStore } from './factory-readiness.store';

describe('FactoryReadinessStore', () => {
  let api: jasmine.SpyObj<OperationalReadinessApiService>;
  let store: FactoryReadinessStore;

  beforeEach(() => {
    api = jasmine.createSpyObj<OperationalReadinessApiService>('api', ['loadSnapshot', 'loadStages', 'loadWorkers']);
    api.loadSnapshot.and.returnValue(of(snapshot()));
    api.loadStages.and.returnValue(of(stages()));
    api.loadWorkers.and.returnValue(of(workers()));
    store = new FactoryReadinessStore(api);
    store.loadSnapshot();
  });

  it('drills down lazily and does not request workers before a stage is opened', () => {
    const factory = store.snapshot()!.factories[0];
    const department = factory.departments[0];
    const line = department.productionLines[0];

    store.openFactory(factory);
    store.openDepartment(department);
    store.openLine(line);

    expect(api.loadStages).toHaveBeenCalledOnceWith('line-1');
    expect(api.loadWorkers).not.toHaveBeenCalled();
    store.openStage(store.stages()!.stages[0]);
    expect(api.loadWorkers).toHaveBeenCalledOnceWith('line-1', 'stage-1');
  });

  it('orders worker problems first and filters the requested attendance state', () => {
    openWorkers(store);

    expect(store.visibleWorkers().map(worker => worker.attendanceState)).toEqual(['Absent', 'Late', 'Present']);
    store.setWorkerFilter('late');
    expect(store.visibleWorkers().map(worker => worker.workerId)).toEqual(['worker-2']);
    store.setWorkerFilter('absent');
    expect(store.visibleWorkers().map(worker => worker.workerId)).toEqual(['worker-3']);
  });

  it('replaces absolute path metrics once when the same realtime event is delivered twice', () => {
    const updated = metrics(7, 10, 1, 2, 0, 70);
    const delta: OperationalReadinessDelta = {
      eventId: 'punch-1', operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:01:00Z',
      attendanceSync: freshSync(), requiresSnapshotReload: false, workers: [],
      nodes: [
        { id: 'factory-1', parentId: null, nodeType: 'Factory', name: 'المصنع', code: 'F', metrics: updated, modelNames: [] },
        { id: 'department-1', parentId: 'factory-1', nodeType: 'Department', name: 'الخياطة', code: 'D', metrics: updated, modelNames: [] },
        { id: 'line-1', parentId: 'department-1', nodeType: 'ProductionLine', name: 'خط 1', code: 'L', metrics: updated, modelNames: ['موديل أ'] }
      ]
    };

    store.applyDelta(delta);
    store.applyDelta(delta);

    const factory = store.snapshot()!.factories[0];
    expect(factory.metrics.currentlyPresentCount).toBe(7);
    expect(factory.departments[0].metrics.currentlyPresentCount).toBe(7);
    expect(factory.departments[0].productionLines[0].metrics.currentlyPresentCount).toBe(7);
    expect(api.loadSnapshot).toHaveBeenCalledTimes(1);
  });

  it('reloads a trusted snapshot when sync trust changes instead of treating unknown workers as absent', () => {
    const stale: AttendanceSyncFreshness = { ...freshSync(), status: 'Stale', isTrusted: false, ageMinutes: 15 };

    store.applyDelta({
      eventId: 'sync-stale', operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:15:00Z',
      attendanceSync: stale, requiresSnapshotReload: false, nodes: [], workers: []
    });

    expect(api.loadSnapshot).toHaveBeenCalledTimes(2);
  });

  it('reloads the open lazy path after reconnect snapshot reconciliation', () => {
    openWorkers(store);
    api.loadStages.calls.reset();
    api.loadWorkers.calls.reset();

    store.loadSnapshot(true);

    expect(api.loadStages).toHaveBeenCalledOnceWith('line-1');
    expect(api.loadWorkers).toHaveBeenCalledOnceWith('line-1', 'stage-1');
    expect(store.selectedStage()?.id).toBe('stage-1');
  });

  it('preserves no-assignment semantics as unknown percentage rather than 100%', () => {
    const empty = snapshot();
    empty.factories[0].metrics = metrics(0, 0, 0, 0, 0, null, 'NoAssignments');
    api.loadSnapshot.and.returnValue(of(empty));

    store.loadSnapshot();

    expect(store.snapshot()!.factories[0].metrics.operationalReadinessPercentage).toBeNull();
    expect(store.snapshot()!.factories[0].metrics.status).toBe('NoAssignments');
  });
});

function openWorkers(store: FactoryReadinessStore): void {
  const factory = store.snapshot()!.factories[0];
  const department = factory.departments[0];
  store.openFactory(factory);
  store.openDepartment(department);
  store.openLine(department.productionLines[0]);
  store.openStage(store.stages()!.stages[0]);
}

function freshSync(): AttendanceSyncFreshness {
  return { status: 'Fresh', isTrusted: true, lastAttemptAtUtc: '2026-07-29T07:00:00Z', lastSuccessfulAtUtc: '2026-07-29T07:00:00Z', lastErrorCode: null, ageMinutes: 0 };
}

function metrics(present = 6, assigned = 10, late = 1, absent = 3, checkedOut = 1, percentage: number | null = 60, status: OperationalReadinessMetrics['status'] = 'Warning'): OperationalReadinessMetrics {
  return { assignedWorkerCount: assigned, currentlyPresentCount: present, lateCount: late, absentCount: absent, checkedOutCount: checkedOut, unknownCount: 0, operationalReadinessPercentage: percentage, contributionToParentShortage: assigned - present, childCount: 1, status };
}

function snapshot(): OperationalReadinessSnapshot {
  return {
    operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshSync(),
    workdayPolicy: { dayStartTime: '08:00:00', gracePeriodMinutes: 15, freshnessThresholdMinutes: 5 },
    factories: [{ id: 'factory-1', name: 'المصنع', code: 'F', metrics: metrics(), departments: [{
      id: 'department-1', factoryId: 'factory-1', name: 'الخياطة', code: 'D', metrics: metrics(), productionLines: [{
        id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1', code: 'L', metrics: metrics(), modelNames: ['موديل أ']
      }]
    }] }]
  };
}

function stages(): OperationalReadinessStages {
  return {
    operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshSync(), factoryId: 'factory-1', factoryName: 'المصنع',
    departmentId: 'department-1', departmentName: 'الخياطة', productionLineId: 'line-1', productionLineName: 'خط 1', stages: [{
      id: 'stage-1', factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-1', mainStageId: 'main-1', name: 'حياكة', code: 'S1', mainStageName: 'خياطة', metrics: metrics(), modelNames: ['موديل أ']
    }]
  };
}

function workers(): OperationalReadinessWorkers {
  return {
    operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshSync(), factoryId: 'factory-1', factoryName: 'المصنع', departmentId: 'department-1', departmentName: 'الخياطة', productionLineId: 'line-1', productionLineName: 'خط 1', stageId: 'stage-1', stageName: 'حياكة',
    workers: [
      { workerId: 'worker-1', productionLineId: 'line-1', stageId: 'stage-1', employeeCode: 'W1', fullName: 'عامل حاضر', attendanceState: 'Present', attendanceLabel: 'حاضر', isOperationallyPresent: true, checkInAtUtc: '2026-07-29T05:00:00Z', checkOutAtUtc: null, lateByMinutes: null },
      { workerId: 'worker-2', productionLineId: 'line-1', stageId: 'stage-1', employeeCode: 'W2', fullName: 'عامل متأخر', attendanceState: 'Late', attendanceLabel: 'متأخر', isOperationallyPresent: true, checkInAtUtc: '2026-07-29T05:20:00Z', checkOutAtUtc: null, lateByMinutes: 5 },
      { workerId: 'worker-3', productionLineId: 'line-1', stageId: 'stage-1', employeeCode: 'W3', fullName: 'عامل غائب', attendanceState: 'Absent', attendanceLabel: 'غائب', isOperationallyPresent: false, checkInAtUtc: null, checkOutAtUtc: null, lateByMinutes: null }
    ]
  };
}
