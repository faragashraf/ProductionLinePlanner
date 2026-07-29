import { signal } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { ManufacturingDataChanged, RealtimeConnectionStatus } from '../../core/models/realtime-notification.models';
import { ManufacturingRealtimeService, ManufacturingRealtimeWatch } from '../../core/services/manufacturing-realtime.service';
import { OperationalReadinessDelta } from '../../shared/models/operational-readiness.model';
import { FactoryReadinessStore } from './factory-readiness.store';
import { FactoryMapPageComponent } from './factory-map-page.component';

describe('FactoryMapPageComponent', () => {
  let store: jasmine.SpyObj<FactoryReadinessStore> & { realtimeDegraded: ReturnType<typeof signal<boolean>> };
  let status: BehaviorSubject<RealtimeConnectionStatus>;
  let realtime: jasmine.SpyObj<ManufacturingRealtimeService>;
  let watch: ManufacturingRealtimeWatch | undefined;
  let stop: jasmine.Spy;

  beforeEach(() => {
    store = Object.assign(
      jasmine.createSpyObj<FactoryReadinessStore>('store', ['loadSnapshot', 'applyDelta']),
      { realtimeDegraded: signal(false) }
    );
    status = new BehaviorSubject<RealtimeConnectionStatus>('connected');
    stop = jasmine.createSpy('stop');
    realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('realtime', ['watchScreen'], { connectionStatus$: status.asObservable() });
    realtime.watchScreen.and.callFake(value => { watch = value; return stop; });
  });

  it('loads the trusted snapshot and watches the shared factory-readiness group without coalescing deltas', () => {
    const component = new FactoryMapPageComponent(store, realtime);

    component.ngOnInit();

    expect(store.loadSnapshot).toHaveBeenCalledOnceWith();
    expect(watch?.screen).toBe('factory-readiness');
    expect(watch?.coalesceMs).toBe(0);
    expect(store.realtimeDegraded()).toBeFalse();
  });

  it('applies a small readiness delta and reloads a snapshot for reconnect or older server messages', () => {
    const component = new FactoryMapPageComponent(store, realtime);
    component.ngOnInit();
    const delta = sampleDelta();

    watch?.refresh({ ...sampleChange(), operationalReadiness: delta });
    watch?.refresh(undefined);

    expect(store.applyDelta).toHaveBeenCalledOnceWith(delta);
    expect(store.loadSnapshot).toHaveBeenCalledWith(true);
  });

  it('surfaces realtime degradation and stops the screen watch on destroy', () => {
    const component = new FactoryMapPageComponent(store, realtime);
    component.ngOnInit();

    status.next('reconnecting');
    expect(store.realtimeDegraded()).toBeTrue();
    status.next('connected');
    expect(store.realtimeDegraded()).toBeFalse();

    component.ngOnDestroy();
    expect(stop).toHaveBeenCalled();
  });
});

function sampleChange(): ManufacturingDataChanged {
  return {
    eventId: 'event-1', entityType: 'AttendanceRecord', changeType: 'Updated', entityId: 'attendance-1',
    occurredAtUtc: '2026-07-29T07:00:00Z', actorUserId: null, correlationId: null, factoryId: 'factory-1',
    departmentId: 'department-1', productionLineId: 'line-1', mainStageId: null, productModelId: null,
    subStageId: 'stage-1', productionDate: '2026-07-29', workerId: 'worker-1'
  };
}

function sampleDelta(): OperationalReadinessDelta {
  return {
    eventId: 'event-1', operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z',
    attendanceSync: { status: 'Fresh', isTrusted: true, lastAttemptAtUtc: '2026-07-29T07:00:00Z', lastSuccessfulAtUtc: '2026-07-29T07:00:00Z', lastErrorCode: null, ageMinutes: 0 },
    requiresSnapshotReload: false, nodes: [], workers: []
  };
}
