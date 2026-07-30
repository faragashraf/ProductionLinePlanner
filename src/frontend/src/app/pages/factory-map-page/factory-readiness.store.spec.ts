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

    expect(api.loadStages).toHaveBeenCalledOnceWith('line-1', 'model-1');
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
    store.setWorkerFilter('checkedOut');
    expect(store.visibleWorkers().map(worker => worker.workerId)).toEqual(['worker-1']);
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

    expect(api.loadStages).toHaveBeenCalledOnceWith('line-1', 'model-1');
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

  it('requires a model selection before loading stages for a multi-model line', () => {
    const factory = store.snapshot()!.factories[0];
    const department = factory.departments[0];
    const line = department.productionLines[0];
    line.models.push({ id: 'model-2', name: 'موديل ب', code: 'M2', stageCount: 8 });
    api.loadStages.calls.reset();

    store.openFactory(factory);
    store.openDepartment(department);
    store.openLine(line);
    expect(api.loadStages).not.toHaveBeenCalled();

    store.selectModel('model-2');
    expect(api.loadStages).toHaveBeenCalledOnceWith('line-1', 'model-2');
    expect(store.selectedModel()?.id).toBe('model-2');
  });

  it('keeps a multi-model line lazy after reconnect until a model is selected', () => {
    const updated = snapshot();
    const line = updated.factories[0].departments[0].productionLines[0];
    line.models.push({ id: 'model-2', name: 'موديل ب', code: 'M2', stageCount: 8 });
    api.loadSnapshot.and.returnValue(of(updated));

    store.loadSnapshot();
    store.openFactory(updated.factories[0]);
    store.openDepartment(updated.factories[0].departments[0]);
    store.openLine(line);
    api.loadStages.calls.reset();

    store.loadSnapshot(true);

    expect(api.loadStages).not.toHaveBeenCalled();
    expect(store.stages()).toBeNull();
  });

  it('shows every loaded stage when no stage issue filter is selected', () => {
    openFilterStages(store, api);

    expect(store.selectedStageFilters()).toEqual([]);
    expect(store.visibleStages().map(stage => stage.id)).toEqual([
      'stage-absent', 'stage-late', 'stage-both', 'stage-empty', 'stage-ready', 'stage-unknown'
    ]);
    expect(store.visibleStageCount()).toBe(6);
    expect(store.totalStageCount()).toBe(6);
  });

  it('filters stages that have absent workers without another API load', () => {
    openFilterStages(store, api);
    api.loadStages.calls.reset();

    store.setStageFilters(['HasAbsentWorkers']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-absent', 'stage-both']);
    expect(api.loadStages).not.toHaveBeenCalled();
  });

  it('filters stages that have late workers', () => {
    openFilterStages(store, api);

    store.setStageFilters(['HasLateWorkers']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-late', 'stage-both']);
  });

  it('uses OR across selected filter types and returns each stage once', () => {
    openFilterStages(store, api);

    store.setStageFilters(['HasAbsentWorkers', 'HasLateWorkers']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-absent', 'stage-late', 'stage-both']);
  });

  it('does not confuse no assignments with an assigned absent worker', () => {
    openFilterStages(store, api);

    store.setStageFilters(['NoAssignments']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-empty']);
    expect(store.visibleStages()[0].metrics.absentCount).toBe(0);
  });

  it('only treats trusted assigned stages at one hundred percent as fully ready', () => {
    openFilterStages(store, api);

    store.setStageFilters(['FullyReady']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-late', 'stage-ready']);
    expect(store.visibleStages().some(stage => stage.id === 'stage-empty')).toBeFalse();
    expect(store.visibleStages().some(stage => stage.id === 'stage-unknown')).toBeFalse();
  });

  it('matches unknown attendance by count or stage status', () => {
    openFilterStages(store, api);

    store.setStageFilters(['HasUnknownAttendance']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-unknown']);
  });

  it('clears stage filters and restores every stage and summary count', () => {
    openFilterStages(store, api);
    store.setStageFilters(['HasAbsentWorkers']);

    store.clearStageFilters();

    expect(store.selectedStageFilters()).toEqual([]);
    expect(store.visibleStageCount()).toBe(6);
    expect(store.totalStageCount()).toBe(6);
  });

  it('computes option counts from the current model stage array', () => {
    openFilterStages(store, api);

    expect(filterCount(store, 'HasAbsentWorkers')).toBe(2);
    expect(filterCount(store, 'HasLateWorkers')).toBe(2);
    expect(filterCount(store, 'HasUnknownAttendance')).toBe(1);
    expect(filterCount(store, 'NoAssignments')).toBe(1);
    expect(filterCount(store, 'NotFullyReady')).toBe(2);
    expect(filterCount(store, 'FullyReady')).toBe(2);
    expect(filterCount(store, 'HasCheckedOutWorkers')).toBe(1);
  });

  it('keeps stage filters after opening a stage and returning to its model stages', () => {
    openFilterStages(store, api);
    store.setStageFilters(['HasAbsentWorkers']);

    store.openStage(store.visibleStages()[0]);
    store.goTo('line');

    expect(store.selectedStageFilters()).toEqual(['HasAbsentWorkers']);
    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-absent', 'stage-both']);
  });

  it('recalculates counts and results for a newly selected model while keeping general filter types', () => {
    const factory = store.snapshot()!.factories[0];
    const department = factory.departments[0];
    const line = department.productionLines[0];
    line.models.push({ id: 'model-2', name: 'موديل ب', code: 'M2', stageCount: 1 });
    api.loadStages.and.callFake((_lineId, modelId) => of(modelId === 'model-2' ? readyOnlyStages() : filterStages()));
    store.openFactory(factory);
    store.openDepartment(department);
    store.openLine(line);
    store.selectModel('model-1');
    store.setStageFilters(['HasAbsentWorkers']);
    expect(store.visibleStageCount()).toBe(2);

    store.selectModel('model-2');

    expect(store.selectedStageFilters()).toEqual(['HasAbsentWorkers']);
    expect(store.totalStageCount()).toBe(1);
    expect(store.visibleStageCount()).toBe(0);
    expect(filterCount(store, 'HasAbsentWorkers')).toBe(0);
    expect(filterCount(store, 'FullyReady')).toBe(1);
  });

  it('sorts a copied stage array by the model StageOrder and leaves the API result untouched', () => {
    const source = orderingStages('model-1');
    api.loadStages.and.returnValue(of(source));

    openCurrentLine(store);

    expect(source.stages.map(stage => stage.id)).toEqual([
      'stage-order-30', 'stage-order-10', 'stage-order-20', 'stage-invalid'
    ]);
    expect(store.visibleStages().map(stage => stage.id)).toEqual([
      'stage-order-10', 'stage-order-20', 'stage-order-30', 'stage-invalid'
    ]);
  });

  it('keeps domain order after filtering instead of reprioritizing by issue type', () => {
    api.loadStages.and.returnValue(of(orderingStages('model-1')));
    openCurrentLine(store);

    store.setStageFilters(['HasAbsentWorkers', 'HasLateWorkers']);

    expect(store.visibleStages().map(stage => stage.id)).toEqual(['stage-order-20', 'stage-order-30']);
  });

  it('uses name then id as stable ties and places null or invalid orders last', () => {
    const source = stages();
    source.stages = [
      readinessStage('tie-b', 'Alpha', metrics(), 4),
      readinessStage('null-b', 'Zulu', metrics(), null),
      readinessStage('tie-a', 'Alpha', metrics(), 4),
      readinessStage('invalid', 'Beta', metrics(), 0),
      readinessStage('first', 'Omega', metrics(), 1),
      readinessStage('null-a', 'Alpha', metrics(), null)
    ];
    api.loadStages.and.returnValue(of(source));

    openCurrentLine(store);

    expect(store.visibleStages().map(stage => stage.id)).toEqual([
      'first', 'tie-a', 'tie-b', 'null-a', 'invalid', 'null-b'
    ]);
  });

  it('recomputes domain order from the newly selected model', () => {
    const factory = store.snapshot()!.factories[0];
    const line = factory.departments[0].productionLines[0];
    line.models.push({ id: 'model-2', name: 'موديل ب', code: 'M2', stageCount: 4 });
    api.loadStages.and.callFake((_lineId, modelId) => of(orderingStages(modelId ?? 'model-1')));
    store.openFactory(factory);
    store.openDepartment(factory.departments[0]);
    store.openLine(line);

    store.selectModel('model-2');

    expect(store.stages()!.selectedProductModelId).toBe('model-2');
    expect(store.visibleStages().map(stage => stage.id)).toEqual([
      'stage-order-10', 'stage-order-20', 'stage-order-30', 'stage-invalid'
    ]);
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

function openFilterStages(
  store: FactoryReadinessStore,
  api: jasmine.SpyObj<OperationalReadinessApiService>
): void {
  api.loadStages.and.returnValue(of(filterStages()));
  const factory = store.snapshot()!.factories[0];
  const department = factory.departments[0];
  store.openFactory(factory);
  store.openDepartment(department);
  store.openLine(department.productionLines[0]);
}

function openCurrentLine(store: FactoryReadinessStore): void {
  const factory = store.snapshot()!.factories[0];
  store.openFactory(factory);
  store.openDepartment(factory.departments[0]);
  store.openLine(factory.departments[0].productionLines[0]);
}

function filterCount(store: FactoryReadinessStore, value: string): number {
  return store.stageFilterOptions().find(option => option.value === value)?.count ?? -1;
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
    workdayPolicy: { workdayBoundaryTime: '05:00:00', dayStartTime: '08:00:00', gracePeriodMinutes: 15, freshnessThresholdMinutes: 5 },
    factories: [{ id: 'factory-1', name: 'المصنع', code: 'F', metrics: metrics(), departments: [{
      id: 'department-1', factoryId: 'factory-1', name: 'الخياطة', code: 'D', metrics: metrics(), productionLines: [{
        id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط 1', code: 'L', metrics: metrics(), modelNames: ['موديل أ'],
        models: [{ id: 'model-1', name: 'موديل أ', code: 'M1', stageCount: 1 }]
      }]
    }] }]
  };
}

function stages(): OperationalReadinessStages {
  return {
    operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshSync(), factoryId: 'factory-1', factoryName: 'المصنع',
    departmentId: 'department-1', departmentName: 'الخياطة', productionLineId: 'line-1', productionLineName: 'خط 1', selectedProductModelId: 'model-1', selectedProductModelName: 'موديل أ', requiresModelSelection: false, availableModels: [{ id: 'model-1', name: 'موديل أ', code: 'M1', stageCount: 1 }], stages: [{
      id: 'stage-1', factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-1', mainStageId: 'main-1', name: 'حياكة', code: 'S1', mainStageName: 'خياطة', stageOrder: 1, metrics: metrics(), modelNames: ['موديل أ']
    }]
  };
}

function filterStages(): OperationalReadinessStages {
  const result = stages();
  const unknown = metrics(0, 2, 0, 0, 0, null, 'Unknown');
  unknown.unknownCount = 2;
  result.stages = [
    readinessStage('stage-absent', 'مرحلة بها غياب', metrics(2, 3, 0, 1, 0, 66.7, 'Critical'), 1),
    readinessStage('stage-late', 'مرحلة بها تأخير', metrics(3, 3, 1, 0, 0, 100, 'Ready'), 2),
    readinessStage('stage-both', 'مرحلة بها غياب وتأخير', metrics(2, 3, 1, 1, 1, 66.7, 'Critical'), 3),
    readinessStage('stage-empty', 'مرحلة بدون تسكين', metrics(0, 0, 0, 0, 0, null, 'NoAssignments'), 4),
    readinessStage('stage-ready', 'مرحلة مكتملة', metrics(2, 2, 0, 0, 0, 100, 'Ready'), 5),
    readinessStage('stage-unknown', 'مرحلة غير مؤكدة', unknown, 6)
  ];
  return result;
}

function readyOnlyStages(): OperationalReadinessStages {
  const result = stages();
  result.selectedProductModelId = 'model-2';
  result.selectedProductModelName = 'موديل ب';
  result.stages = [readinessStage('stage-model-2-ready', 'مرحلة الموديل الثاني', metrics(1, 1, 0, 0, 0, 100, 'Ready'), 1)];
  return result;
}

function orderingStages(modelId: string): OperationalReadinessStages {
  const result = stages();
  result.selectedProductModelId = modelId;
  result.stages = [
    readinessStage('stage-order-30', 'غياب', metrics(0, 1, 0, 1, 0, 0, 'Critical'), 30),
    readinessStage('stage-order-10', 'مكتملة', metrics(1, 1, 0, 0, 0, 100, 'Ready'), 10),
    readinessStage('stage-order-20', 'متأخرة', metrics(1, 1, 1, 0, 0, 100, 'Ready'), 20),
    readinessStage('stage-invalid', 'بلا ترتيب', metrics(0, 0, 0, 0, 0, null, 'NoAssignments'), null)
  ];
  return result;
}

function readinessStage(
  id: string,
  name: string,
  stageMetrics: OperationalReadinessMetrics,
  stageOrder: number | null
) {
  return {
    id,
    factoryId: 'factory-1',
    departmentId: 'department-1',
    productionLineId: 'line-1',
    mainStageId: `main-${id}`,
    name,
    code: id.toUpperCase(),
    mainStageName: 'خياطة',
    stageOrder,
    metrics: stageMetrics,
    modelNames: ['موديل أ']
  };
}

function workers(): OperationalReadinessWorkers {
  return {
    operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshSync(), factoryId: 'factory-1', factoryName: 'المصنع', departmentId: 'department-1', departmentName: 'الخياطة', productionLineId: 'line-1', productionLineName: 'خط 1', stageId: 'stage-1', stageName: 'حياكة',
    workers: [
      { workerId: 'worker-1', productionLineId: 'line-1', stageId: 'stage-1', employeeCode: 'W1', fullName: 'عامل حاضر', attendanceState: 'Present', attendanceLabel: 'حاضر', isOperationallyPresent: true, checkInAtUtc: '2026-07-29T05:00:00Z', checkOutAtUtc: '2026-07-29T09:00:00Z', lateByMinutes: null },
      { workerId: 'worker-2', productionLineId: 'line-1', stageId: 'stage-1', employeeCode: 'W2', fullName: 'عامل متأخر', attendanceState: 'Late', attendanceLabel: 'متأخر', isOperationallyPresent: true, checkInAtUtc: '2026-07-29T05:20:00Z', checkOutAtUtc: null, lateByMinutes: 5 },
      { workerId: 'worker-3', productionLineId: 'line-1', stageId: 'stage-1', employeeCode: 'W3', fullName: 'عامل غائب', attendanceState: 'Absent', attendanceLabel: 'غائب', isOperationallyPresent: false, checkInAtUtc: null, checkOutAtUtc: null, lateByMinutes: null }
    ]
  };
}
